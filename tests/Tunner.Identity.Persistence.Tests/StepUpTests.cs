using System.Security.Claims;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Tunner.Accounts;
using Tunner.Database.Outbox;
using Tunner.Platform.Policies;
using Xunit;
using static Tunner.Identity.Persistence.Tests.AuthenticationTestHost;

namespace Tunner.Identity.Persistence.Tests;

public sealed class StepUpTests(IdentityPostgresFixture fixture) : IClassFixture<IdentityPostgresFixture>
{
    // All operations, values, lifetimes and approvals below are synthetic test-only authority.
    private static readonly AssurancePolicyScope PolicyScope = new("fixture.change-value", "fixture.user-bff");
    private static readonly string Digest = new('A', 64);
    private static StepUpOperation Operation() => new("OP-" + Guid.NewGuid().ToString("N"), "fixture-correlation", PolicyScope, "fixture-resource", 1, Digest);
    private static AssurancePolicyVersion Policy(AuthenticationTestHost host, string version = "fixture-step-v1", string status = "ACTIVE", string assurance = "fixture-password") =>
        new(new(version, status, PolicyScope, new EffectivePolicyInterval(host.Clock.GetUtcNow().AddHours(-1), true, host.Clock.GetUtcNow().AddHours(1), false),
            null, ["synthetic-test-only"]), assurance, TimeSpan.FromSeconds(30), new Dictionary<string, string> { ["pwd"] = "fixture-password" });
    private static AssurancePolicyCatalog Source(AuthenticationTestHost host) => new AssurancePolicyCatalog([Policy(host)]);
    private sealed class ProofScope : IAsyncDisposable
    {
        private readonly AsyncServiceScope scope;
        internal IdentityPersistenceDbContext Database { get; }
        internal StepUpApplication Application { get; }
        internal ProofScope(AuthenticationTestHost host, IAssurancePolicySource source)
        {
            scope = host.Services.CreateAsyncScope();
            Database = scope.ServiceProvider.GetRequiredService<IdentityPersistenceDbContext>();
            Application = new(Database, scope.ServiceProvider.GetRequiredService<AuthenticationApplication>(), host.Policies, source,
                scope.ServiceProvider.GetRequiredService<AuthenticationPasswordWork>(), scope.ServiceProvider.GetRequiredService<IDataProtectionProvider>(), host.Clock);
        }
        public ValueTask DisposeAsync() => scope.DisposeAsync();
    }
    private static async Task<StepUpCaller> SignIn(AuthenticationTestHost host)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var attempt = await scope.ServiceProvider.GetRequiredService<AuthenticationApplication>().SignInAsync(
            new(AuthenticationIdentity.Digest(Guid.NewGuid().ToString()), Digest, Origin, null), Command(Guid.NewGuid()), CancellationToken.None);
        Assert.Null(attempt.ErrorCode); Assert.NotNull(attempt.Grant);
        return StepUpCaller.FromPrincipal(attempt.Grant.Ticket.Principal, Origin, Digest);
    }
    private static async Task<StepUpProofGrant> Issue(AuthenticationTestHost host, StepUpCaller caller, StepUpOperation operation)
    {
        await using var scope = new ProofScope(host, Source(host));
        var result = await scope.Application.IssuePasswordAsync(caller, operation, "fixture-step-v1", Password);
        Assert.Null(result.ErrorCode); return Assert.IsType<StepUpProofGrant>(result.Grant);
    }
    private static async Task Target(IdentityTestDatabase db)
    {
        await using var context = db.Context();
        await context.Database.ExecuteSqlRawAsync("CREATE TABLE identity.step_up_test_target (id integer PRIMARY KEY, version bigint NOT NULL, value integer NOT NULL); INSERT INTO identity.step_up_test_target VALUES (1,1,0);");
    }
    private static Task<int> Change(IdentityPersistenceDbContext context, CancellationToken ct) =>
        context.Database.ExecuteSqlRawAsync("UPDATE identity.step_up_test_target SET value=value+1, version=version+1 WHERE id=1 AND version=1", ct);
    private static async Task<int> Value(IdentityTestDatabase db)
    {
        await using var context = db.Context();
        return await context.Database.SqlQueryRaw<int>("SELECT value AS \"Value\" FROM identity.step_up_test_target WHERE id=1").SingleAsync();
    }

    [Fact]
    public async Task FreshProofIsConsumedOnceWithoutUpgradingSessionAndEventsContainOnlySafeReferences()
    {
        await using var db = await fixture.CreateDatabaseAsync(); await using var host = await Start(db); await host.SeedAsync(db, minor: true);
        await Target(db); var caller = await SignIn(host); var op = Operation();
        await using var read = db.Context(); var before = await read.Set<AuthenticationSessionRow>().AsNoTracking().SingleAsync();
        var grant = await Issue(host, caller, op);
        Assert.Equal(TimeSpan.FromSeconds(30), grant.ExpiresAt - host.Clock.GetUtcNow());
        await using (var scope = new ProofScope(host, Source(host)))
            Assert.True((await scope.Application.ExecuteAsync(caller, op, grant.Key, async ct => await Change(scope.Database, ct) == 1)).Applied);
        await using (var scope = new ProofScope(host, Source(host)))
            Assert.Equal("STEP_UP_REQUIRED", (await scope.Application.ExecuteAsync(caller, op, grant.Key, _ => throw new InvalidOperationException("Replay executed."))).ErrorCode);
        Assert.Equal(1, await Value(db));
        var row = await read.Set<StepUpProofRow>().AsNoTracking().SingleAsync();
        Assert.Equal("CONSUMED", row.State); Assert.Equal(2, row.Version); Assert.Empty(row.ProtectedKey);
        Assert.NotEqual(grant.Key, row.KeyDigest); Assert.Equal(AuthenticationIdentity.Digest(grant.Key), row.KeyDigest);
        var after = await read.Set<AuthenticationSessionRow>().AsNoTracking().SingleAsync();
        Assert.Equal(before.Assurance, after.Assurance); Assert.Equal(before.AuthenticatedAt, after.AuthenticatedAt);
        Assert.Equal(before.ExpiresAt, after.ExpiresAt); Assert.Equal(before.KeyDigest, after.KeyDigest); Assert.Equal(before.ProtectedTicket, after.ProtectedTicket);
        var events = await read.Set<OutboxMessage>().Where(x => x.EventType.StartsWith("StepUp")).ToArrayAsync();
        Assert.Equal(2, events.Length);
        foreach (var evidence in events)
        {
            Assert.Equal("RESTRICTED_SECURITY", evidence.DataClassification);
            Assert.DoesNotContain(grant.Key, evidence.Payload); Assert.DoesNotContain(Password, evidence.Payload);
            Assert.DoesNotContain("person@example.test", evidence.Payload); Assert.Equal(op.OperationId, evidence.OperationId);
        }
        Assert.DoesNotContain(grant.Key, grant.ToString()); Assert.DoesNotContain(caller.AccountId, caller.ToString());
        Assert.DoesNotContain(op.ResourceId, op.ToString());
    }

    [Fact]
    public async Task ConcurrentIssuanceReturnsOneOriginalProofAndEndedOperationCannotBeReissued()
    {
        await using var db = await fixture.CreateDatabaseAsync(); await using var host = await Start(db); await host.SeedAsync(db);
        var caller = await SignIn(host); var op = Operation();
        var grants = await Task.WhenAll(Issue(host, caller, op), Issue(host, caller, op));
        Assert.Equal(grants[0].Key, grants[1].Key); Assert.Equal(grants[0].ExpiresAt, grants[1].ExpiresAt);
        await using var context = db.Context(); Assert.Equal(1, await context.Set<StepUpProofRow>().CountAsync());
        Assert.Equal(1, await context.Set<OutboxMessage>().CountAsync(x => x.EventType == "StepUpProofIssued"));
        await using (var scope = new ProofScope(host, Source(host)))
            Assert.True((await scope.Application.ExecuteAsync(caller, op, grants[0].Key, _ => Task.FromResult(true))).Applied);
        await using var retry = new ProofScope(host, Source(host));
        Assert.Equal("VERSION_CONFLICT", (await retry.Application.IssuePasswordAsync(caller, op, "fixture-step-v1", Password)).ErrorCode);
    }

    [Fact]
    public async Task ConcurrentConsumptionExecutesExactlyOneMutation()
    {
        await using var db = await fixture.CreateDatabaseAsync(); await using var host = await Start(db); await host.SeedAsync(db);
        await Target(db); var caller = await SignIn(host); var op = Operation(); var grant = await Issue(host, caller, op);
        async Task<StepUpExecutionResult> Execute()
        {
            await using var scope = new ProofScope(host, Source(host));
            return await scope.Application.ExecuteAsync(caller, op, grant.Key, async ct => await Change(scope.Database, ct) == 1);
        }
        var results = await Task.WhenAll(Execute(), Execute());
        Assert.Single(results, x => x.Applied); Assert.Single(results, x => x.ErrorCode == "STEP_UP_REQUIRED");
        Assert.Equal(1, await Value(db)); await using var check = db.Context();
        Assert.Equal(1, await check.Set<OutboxMessage>().CountAsync(x => x.EventType == "StepUpProofConsumed"));
    }

    [Theory]
    [InlineData("operation-id")][InlineData("correlation")][InlineData("operation")][InlineData("context")]
    [InlineData("resource")][InlineData("version")][InlineData("digest")][InlineData("session")][InlineData("key")]
    public async Task ProofCannotAuthorizeDifferentBindings(string change)
    {
        await using var db = await fixture.CreateDatabaseAsync(); await using var host = await Start(db); await host.SeedAsync(db);
        var caller = await SignIn(host); var op = Operation(); var grant = await Issue(host, caller, op);
        var different = new StepUpOperation(change == "operation-id" ? "OP-other" : op.OperationId, change == "correlation" ? "other-correlation" : op.CorrelationId,
            new(change == "operation" ? "other-operation" : op.Scope.Operation, change == "context" ? "other-context" : op.Scope.Context),
            change == "resource" ? "other-resource" : op.ResourceId, change == "version" ? 2 : op.ExpectedVersion, change == "digest" ? new('B', 64) : op.ContextDigest);
        if (change == "session") caller = await SignIn(host);
        await using var scope = new ProofScope(host, Source(host));
        var result = await scope.Application.ExecuteAsync(caller, different, change == "key" ? new('C',64) : grant.Key, _ => throw new InvalidOperationException("Wrong binding executed."));
        Assert.Equal("STEP_UP_REQUIRED", result.ErrorCode);
        await using var check = db.Context(); Assert.Equal("ACTIVE", (await check.Set<StepUpProofRow>().SingleAsync()).State);
    }

    [Theory]
    [InlineData("wrong")][InlineData("empty")][InlineData("long")][InlineData("insufficient")]
    public async Task FreshCredentialsAndExplicitMethodAssuranceAreRequired(string change)
    {
        await using var db = await fixture.CreateDatabaseAsync(); await using var host = await Start(db); await host.SeedAsync(db);
        var caller = await SignIn(host);
        var source = new AssurancePolicyCatalog([Policy(host, assurance: change == "insufficient" ? "fixture-stronger" : "fixture-password")]);
        await using var scope = new ProofScope(host, source);
        var password = change switch { "wrong" => "invalid", "empty" => "", "long" => new string('x',129), _ => Password };
        var result = await scope.Application.IssuePasswordAsync(caller, Operation(), "fixture-step-v1", password);
        Assert.Null(result.Grant);
        Assert.Equal(change is "empty" or "long" ? "VALIDATION_ERROR" : change == "insufficient" ? "STEP_UP_REQUIRED" : "AUTHENTICATION_FAILED", result.ErrorCode);
        await using var check = db.Context(); Assert.Empty(await check.Set<StepUpProofRow>().ToArrayAsync());
        if (change == "wrong") Assert.Equal(1, await check.Set<OutboxMessage>().CountAsync(x => x.EventType == "StepUpAuthenticationRejected"));
    }

    [Theory]
    [InlineData("stamp")][InlineData("contact")][InlineData("closed")][InlineData("restriction")][InlineData("session")]
    public async Task RevocationOrChangedAccountAuthorityPreventsAnAlreadyIssuedProof(string change)
    {
        await using var db = await fixture.CreateDatabaseAsync(); await using var host = await Start(db); await host.SeedAsync(db);
        var caller = await SignIn(host); var op = Operation(); var grant = await Issue(host, caller, op);
        await using (var update = db.Context())
        {
            if (change == "stamp") (await update.Users.SingleAsync()).SecurityStamp = Guid.NewGuid().ToString();
            if (change == "contact") { var c = await update.VerifiedContacts.SingleAsync(); c.State = ContactVerificationState.Revoked; c.Version++; }
            if (change == "closed") { var a = await update.Accounts.SingleAsync(); a.State = AccountState.Closed; a.Version++; }
            if (change == "restriction") host.Access.Allowed = false;
            if (change == "session") { var s = await update.Set<AuthenticationSessionRow>().SingleAsync(); s.State = "REVOKED"; s.Version++; }
            await update.SaveChangesAsync();
        }
        await using var scope = new ProofScope(host, Source(host));
        Assert.Equal("AUTHENTICATION_FAILED", (await scope.Application.ExecuteAsync(caller, op, grant.Key, _ => throw new InvalidOperationException("Revoked action executed."))).ErrorCode);
        await using var check = db.Context(); Assert.Equal("ACTIVE", (await check.Set<StepUpProofRow>().SingleAsync()).State);
    }

    [Fact]
    public async Task TargetConflictAndThrownMutationRollBackBothProofAndTargetAndAllowSafeRetry()
    {
        await using var db = await fixture.CreateDatabaseAsync(); await using var host = await Start(db); await host.SeedAsync(db);
        await Target(db); var caller = await SignIn(host); var op = Operation(); var grant = await Issue(host, caller, op);
        await using (var scope = new ProofScope(host, Source(host)))
        {
            var result = await scope.Application.ExecuteAsync(caller, op, grant.Key, async ct => { await Change(scope.Database, ct); return false; });
            Assert.Equal("VERSION_CONFLICT", result.ErrorCode);
        }
        Assert.Equal(0, await Value(db));
        await using (var scope = new ProofScope(host, Source(host)))
            await Assert.ThrowsAsync<InvalidOperationException>(() => scope.Application.ExecuteAsync(caller, op, grant.Key, async ct =>
            { await Change(scope.Database, ct); await scope.Database.SaveChangesAsync(ct); throw new InvalidOperationException("Synthetic local action failure."); }));
        Assert.Equal(0, await Value(db));
        await using (var scope = new ProofScope(host, Source(host)))
            Assert.True((await scope.Application.ExecuteAsync(caller, op, grant.Key, async ct => await Change(scope.Database, ct) == 1)).Applied);
        Assert.Equal(1, await Value(db));
    }

    [Theory]
    [InlineData(false)][InlineData(true)]
    public async Task ExpiryBeforeOrDuringMutationNeverAuthorizesAndCleanupIsIdempotent(bool during)
    {
        await using var db = await fixture.CreateDatabaseAsync(); await using var host = await Start(db); await host.SeedAsync(db);
        await Target(db); var caller = await SignIn(host); var op = Operation(); var grant = await Issue(host, caller, op);
        if (!during) host.Clock.Advance(TimeSpan.FromSeconds(30));
        await using (var scope = new ProofScope(host, Source(host)))
        {
            var result = await scope.Application.ExecuteAsync(caller, op, grant.Key, async ct =>
            { Assert.True(during); await Change(scope.Database, ct); host.Clock.Advance(TimeSpan.FromSeconds(30)); return true; });
            Assert.Equal("STEP_UP_REQUIRED", result.ErrorCode);
        }
        Assert.Equal(0, await Value(db));
        await using (var scope = new ProofScope(host, Source(host)))
        { Assert.Equal(1, await scope.Application.ExpireAsync()); Assert.Equal(0, await scope.Application.ExpireAsync()); }
        await using var check = db.Context(); var row = await check.Set<StepUpProofRow>().SingleAsync();
        Assert.Equal("EXPIRED", row.State); Assert.Empty(row.ProtectedKey); Assert.Equal(grant.ExpiresAt, row.ExpiresAt);
        Assert.Equal(1, await check.Set<OutboxMessage>().CountAsync(x => x.EventType == "StepUpProofExpired"));
    }

    [Theory]
    [InlineData("missing")][InlineData("draft")][InlineData("ambiguous")][InlineData("stale")][InlineData("auth-unavailable")]
    public async Task UnavailableOrChangedPolicyFailsClosedWithoutMutation(string change)
    {
        await using var db = await fixture.CreateDatabaseAsync(); await using var host = await Start(db); await host.SeedAsync(db);
        var caller = await SignIn(host); var op = Operation(); var grant = await Issue(host, caller, op);
        IAssurancePolicySource source = new AssurancePolicyCatalog(change switch {
            "missing" => [], "draft" => [Policy(host, status:"DRAFT")],
            "ambiguous" => [Policy(host), Policy(host, "fixture-step-v2")],
            "stale" => [Policy(host,"fixture-step-v2")], _ => [Policy(host)] });
        if (change == "auth-unavailable") host.Policies.Available = false;
        await using var scope = new ProofScope(host, source);
        await Assert.ThrowsAnyAsync<InvalidOperationException>(() => scope.Application.ExecuteAsync(caller, op, grant.Key, _ => throw new InvalidOperationException("Policy failure executed.")));
        await using var check = db.Context(); Assert.Equal("ACTIVE", (await check.Set<StepUpProofRow>().SingleAsync()).State);
    }

    // Test-only SQL attacks use fixed assignments/model-owned identifiers; account data remains parameterized.
    private static async Task<int> Attack(IdentityPersistenceDbContext context, string statement, string? account = null)
    {
        await context.Database.OpenConnectionAsync();
        await using var command = new NpgsqlCommand(statement, (NpgsqlConnection)context.Database.GetDbConnection());
        if (account is not null) command.Parameters.AddWithValue("account", account);
        return await command.ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task SqlCannotEditBindingsExtendExpiryOrReviveEndedProofAndCompositeForeignKeyBindsAccount()
    {
        await using var db = await fixture.CreateDatabaseAsync(); await using var host = await Start(db); await host.SeedAsync(db);
        var caller = await SignIn(host); var op = Operation(); var grant = await Issue(host, caller, op);
        await using var check = db.Context();
        foreach (var assignment in new[] { "expected_version=2", "expires_at=expires_at+interval '1 hour'", "key_digest=repeat('B',64)", "protected_key='tampered'",
            "state='CONSUMED', protected_key='', ended_at=NULL, version=version+1", "state='EXPIRED', protected_key='', ended_at=authenticated_at, version=version+1" })
        {
            var error = await Assert.ThrowsAsync<PostgresException>(() => Attack(check, "UPDATE identity.step_up_proofs SET " + assignment));
            Assert.Equal("23514", error.SqlState);
        }
        var wrongAccount = await host.SeedAsync(db, confirmed: false);
        var columns = string.Join(",", check.Model.FindEntityType(typeof(StepUpProofRow))!.GetProperties().Select(p => p.GetColumnName()));
        var selections = string.Join(",", check.Model.FindEntityType(typeof(StepUpProofRow))!.GetProperties().Select(p => p.Name switch
        { "ProofId" => "gen_random_uuid()", "AccountId" => "@account", "KeyDigest" => "repeat('D',64)", "OperationId" => "'OP-wrong-account'", _ => p.GetColumnName() }));
        var foreignKey = await Assert.ThrowsAsync<PostgresException>(() => Attack(check, "INSERT INTO identity.step_up_proofs (" + columns + ") SELECT " + selections + " FROM identity.step_up_proofs", wrongAccount));
        Assert.Equal("23503", foreignKey.SqlState);
        await using (var scope = new ProofScope(host, Source(host)))
            Assert.True((await scope.Application.ExecuteAsync(caller, op, grant.Key, _ => Task.FromResult(true))).Applied);
        var revive = await Assert.ThrowsAsync<PostgresException>(() => check.Database.ExecuteSqlRawAsync("UPDATE identity.step_up_proofs SET state='ACTIVE', protected_key='revived', ended_at=NULL, version=version+1"));
        Assert.Equal("23514", revive.SqlState);
    }

    [Fact]
    public async Task AdminUnauthenticatedWrongOriginOrInvalidContextCannotConstructTrustedCaller()
    {
        await using var db = await fixture.CreateDatabaseAsync(); await using var host = await Start(db); await host.SeedAsync(db);
        var caller = await SignIn(host);
        Claim[] claims = [new(ClaimTypes.NameIdentifier, caller.AccountId), new(AuthenticationIdentity.SessionClaim, caller.SessionId.ToString()),
            new(AuthenticationIdentity.OriginClaim, Origin)];
        Assert.Throws<ArgumentException>(() => StepUpCaller.FromPrincipal(new(new ClaimsIdentity(claims, "Admin")), Origin, Digest));
        Assert.Throws<ArgumentException>(() => StepUpCaller.FromPrincipal(new(new ClaimsIdentity(claims)), Origin, Digest));
        Assert.Throws<ArgumentException>(() => StepUpCaller.FromPrincipal(new(new ClaimsIdentity(claims, AuthenticationIdentity.Scheme)), "https://other.example.test", Digest));
        Assert.Throws<ArgumentException>(() => StepUpCaller.FromPrincipal(new(new ClaimsIdentity(claims, AuthenticationIdentity.Scheme)), Origin, "untrusted"));
        Assert.Throws<ArgumentException>(() => new StepUpOperation("op","correlation",PolicyScope,"resource",1,"bad"));
    }


    [Theory]
    [InlineData("session")][InlineData("policy")]
    public async Task ProofExpiryIsClippedToCurrentSessionAndPolicyInterval(string bound)
    {
        await using var db = await fixture.CreateDatabaseAsync(); await using var host = await Start(db); await host.SeedAsync(db);
        if (bound == "session") host.Policies.Policy = host.Policies.Policy with { AbsoluteLifetime = TimeSpan.FromSeconds(20), IdleLifetime = TimeSpan.FromSeconds(20) };
        var caller = await SignIn(host); var now = host.Clock.GetUtcNow(); var op = Operation();
        var configured = bound == "policy"
            ? new AssurancePolicyVersion(new("fixture-step-v1", "ACTIVE", PolicyScope, new EffectivePolicyInterval(now.AddHours(-1), true, now.AddSeconds(10), false),
                null, ["synthetic-test-only"]), "fixture-password", TimeSpan.FromSeconds(30), new Dictionary<string,string> { ["pwd"] = "fixture-password" })
            : Policy(host);
        await using var scope = new ProofScope(host, new AssurancePolicyCatalog([configured]));
        var result = await scope.Application.IssuePasswordAsync(caller, op, "fixture-step-v1", Password);
        Assert.Null(result.ErrorCode); Assert.NotNull(result.Grant);
        Assert.Equal(now.AddSeconds(bound == "policy" ? 10 : 20), result.Grant.ExpiresAt);
        host.Clock.Advance(TimeSpan.FromSeconds(20));
        if (bound == "session")
            Assert.Equal("AUTHENTICATION_FAILED", (await scope.Application.ExecuteAsync(caller, op, result.Grant.Key, _ => throw new InvalidOperationException("Expired session executed."))).ErrorCode);
        else
            await Assert.ThrowsAnyAsync<InvalidOperationException>(() => scope.Application.ExecuteAsync(caller, op, result.Grant.Key, _ => throw new InvalidOperationException("Expired policy executed.")));
    }

    [Fact]
    public async Task StepUpAbuseLimitsAreIndependentButReuseCurrentAuthenticationPolicyValues()
    {
        await using var db = await fixture.CreateDatabaseAsync(); await using var host = await Start(db); await host.SeedAsync(db);
        host.Policies.Policy = host.Policies.Policy with { IdentifierAttemptLimit = 1 };
        var caller = await SignIn(host); var op = Operation();
        await using var scope = new ProofScope(host, Source(host));
        Assert.Equal("AUTHENTICATION_FAILED", (await scope.Application.IssuePasswordAsync(caller, op, "fixture-step-v1", "wrong")).ErrorCode);
        Assert.Equal("RATE_OR_ABUSE_LIMITED", (await scope.Application.IssuePasswordAsync(caller, op, "fixture-step-v1", Password)).ErrorCode);
        await using var check = db.Context(); Assert.Empty(await check.Set<StepUpProofRow>().ToArrayAsync());
        Assert.Equal("ACTIVE", (await check.Set<AuthenticationSessionRow>().SingleAsync()).State);
    }

    [Fact]
    public async Task ExactOperationRestrictionDoesNotBecomeGeneralSessionRevocation()
    {
        await using var db = await fixture.CreateDatabaseAsync(); await using var host = await Start(db); await host.SeedAsync(db);
        var caller = await SignIn(host); var op = Operation(); var grant = await Issue(host, caller, op);
        host.Access.DeniedOperation = op.Scope.Operation;
        await using var scope = new ProofScope(host, Source(host));
        Assert.Equal("AUTHENTICATION_FAILED", (await scope.Application.ExecuteAsync(caller, op, grant.Key, _ => throw new InvalidOperationException("Denied operation executed."))).ErrorCode);
        await using var check = db.Context(); Assert.Equal("ACTIVE", (await check.Set<AuthenticationSessionRow>().SingleAsync()).State);
        Assert.Equal("ACTIVE", (await check.Set<StepUpProofRow>().SingleAsync()).State);
    }

    [Fact]
    public async Task PolicyFailureAfterLocalMutationRollsBackInsteadOfConsumingProof()
    {
        await using var db = await fixture.CreateDatabaseAsync(); await using var host = await Start(db); await host.SeedAsync(db);
        await Target(db); var caller = await SignIn(host); var op = Operation(); var grant = await Issue(host, caller, op);
        await using var scope = new ProofScope(host, Source(host));
        await Assert.ThrowsAnyAsync<InvalidOperationException>(() => scope.Application.ExecuteAsync(caller, op, grant.Key, async ct =>
        { await Change(scope.Database, ct); host.Policies.Available = false; return true; }));
        Assert.Equal(0, await Value(db)); await using var check = db.Context();
        Assert.Equal("ACTIVE", (await check.Set<StepUpProofRow>().SingleAsync()).State);
    }

    [Fact]
    public async Task VerifiedContactCannotBeRevokedConcurrentlyInsideTheAuthorizedMutation()
    {
        await using var db = await fixture.CreateDatabaseAsync(); await using var host = await Start(db); await host.SeedAsync(db);
        var caller = await SignIn(host); var op = Operation(); var grant = await Issue(host, caller, op);
        await using var scope = new ProofScope(host, Source(host));
        Assert.True((await scope.Application.ExecuteAsync(caller, op, grant.Key, async ct =>
        {
            await using var competing = new NpgsqlConnection(db.ConnectionString); await competing.OpenAsync(ct);
            await using var command = new NpgsqlCommand("SET lock_timeout='250ms'; UPDATE accounts.verified_contacts SET state='REVOKED', version=version+1", competing);
            var blocked = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync(ct));
            Assert.Equal("55P03", blocked.SqlState); return true;
        })).Applied);
        await using var check = db.Context(); Assert.Equal(ContactVerificationState.Verified, (await check.VerifiedContacts.SingleAsync()).State);
    }

    [Fact]
    public async Task MigrationRoundTripRestoresProofConstraintsAndHasNoPendingModelChanges()
    {
        await using var db = await fixture.CreateDatabaseAsync(); await using var context = db.Context();
        var migrations = (await context.Database.GetAppliedMigrationsAsync()).ToArray();
        var declared = context.Database.GetMigrations().ToArray();
        Assert.Contains("20261109033631_P2OperationStepUpProofs", migrations);
        Assert.Contains("20261109164622_P2PasskeyCeremonies", migrations);
        Assert.Equal(declared, migrations); // The actual declared latest migration, including passkeys, must be applied.
        var proofIndex = Array.FindIndex(migrations, x => x.EndsWith("_P2OperationStepUpProofs", StringComparison.Ordinal));
        Assert.True(proofIndex > 0);
        await context.GetService<IMigrator>().MigrateAsync(migrations[proofIndex - 1]);
        await context.Database.MigrateAsync();
        Assert.False(context.Database.HasPendingModelChanges());
        Assert.Equal(declared, (await context.Database.GetAppliedMigrationsAsync()).ToArray());
        Assert.Equal(1, await context.Database.SqlQueryRaw<int>("SELECT count(*)::integer AS \"Value\" FROM pg_trigger WHERE tgname='step_up_proof_guard'").SingleAsync());
        Assert.Equal(5, await context.Database.SqlQueryRaw<int>("SELECT count(*)::integer AS \"Value\" FROM pg_constraint WHERE conrelid='identity.step_up_proofs'::regclass AND contype='c' AND conname IN ('ck_step_up_state','ck_step_up_bounds','ck_step_up_digests','ck_step_up_end','ck_step_up_consumed')").SingleAsync());
    }
}
