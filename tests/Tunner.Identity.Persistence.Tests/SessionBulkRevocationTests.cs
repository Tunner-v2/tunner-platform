using System.Collections.Concurrent;
using System.Data.Common;
using System.Globalization;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Tunner.Database.Outbox;
using Tunner.Platform.Policies;
using Xunit;
using static Tunner.Identity.Persistence.Tests.AuthenticationTestHost;

namespace Tunner.Identity.Persistence.Tests;

public sealed class SessionBulkRevocationTests(SessionListingPostgresFixture fixture) : IClassFixture<SessionListingPostgresFixture>
{
    // All values and policy evidence below are synthetic local-test authority only.
    internal sealed class Discovery : ISessionBulkRevocationDiscoverySource
    {
        internal int Maximum { get; set; } = 20;
        internal string Version { get; set; } = "fixture-discovery-v1";
        internal static readonly EffectivePolicyInterval Interval = new(new(2026, 10, 20, 0, 0, 0, TimeSpan.Zero), true,
            new DateTimeOffset(2026, 10, 23, 0, 0, 0, TimeSpan.Zero), false);
        public SessionBulkRevocationDiscoveryBudget RequireEffective(DateTimeOffset at) => new(new(Version, "ACTIVE",
            SessionBulkRevocationDiscoveryBudget.Scope, Interval, null, ["SYNTHETIC-TEST-ONLY"]), Maximum);
    }
    private sealed class Assurance : IAssurancePolicySource
    {
        internal bool BulkAllowed { get; set; } = true;
        internal string Version { get; set; } = "fixture-bulk-proof-v1";
        public AssurancePolicyVersion RequireEffective(AssurancePolicyScope scope, DateTimeOffset at, string? expectedVersion = null) =>
            new AssurancePolicyCatalog([new(new(Version, "ACTIVE", BulkAllowed ? SessionBulkRevocationApplication.Scope : SessionRevocationBinding.Scope,
                Discovery.Interval, null, ["SYNTHETIC-TEST-ONLY"]), "fixture-proof", TimeSpan.FromMinutes(5),
                new Dictionary<string, string> { ["pwd"] = "fixture-proof" })]).RequireEffective(scope, at, expectedVersion);
    }
    private sealed class Scope : IAsyncDisposable
    {
        private readonly AsyncServiceScope scope;
        internal IdentityPersistenceDbContext Database { get; }
        internal AuthenticationApplication Authentication { get; }
        internal StepUpApplication StepUp { get; }
        internal SessionBulkRevocationApplication Owner { get; }
        internal Scope(AuthenticationTestHost host, Discovery discovery, IAssurancePolicySource assurance)
        {
            scope = host.Services.CreateAsyncScope(); var provider = scope.ServiceProvider;
            Database = provider.GetRequiredService<IdentityPersistenceDbContext>(); Authentication = provider.GetRequiredService<AuthenticationApplication>();
            StepUp = new(Database, Authentication, host.Policies, assurance, provider.GetRequiredService<AuthenticationPasswordWork>(),
                provider.GetRequiredService<IDataProtectionProvider>(), host.Clock);
            Owner = new(Authentication, StepUp, discovery, assurance);
        }
        public ValueTask DisposeAsync() => scope.DisposeAsync();
    }
    private static async Task<(StepUpCaller Actor, string Key)> Login(AuthenticationTestHost host, string email = "person@example.test", string origin = Origin)
    {
        await using var services = host.Services.CreateAsyncScope();
        var result = await services.ServiceProvider.GetRequiredService<AuthenticationApplication>().SignInAsync(
            new(AuthenticationIdentity.Digest(Guid.NewGuid().ToString()), new('B', 64), origin, null), Command(Guid.NewGuid(), email: email), CancellationToken.None);
        Assert.Null(result.ErrorCode); var grant = Assert.IsType<AuthenticationGrant>(result.Grant);
        return (StepUpCaller.FromPrincipal(grant.Ticket.Principal, origin, new('B', 64)), grant.Key);
    }
    private static async Task<SessionBulkRevocationPreparation> Prepare(AuthenticationTestHost host, Discovery discovery, Assurance assurance, StepUpCaller caller)
    {
        await using var scope = new Scope(host, discovery, assurance);
        return await scope.Owner.PrepareAsync(caller, Guid.NewGuid(), "fixture-" + Guid.NewGuid().ToString("N"));
    }
    private static async Task<string> Issue(AuthenticationTestHost host, Discovery discovery, Assurance assurance, StepUpCaller actor, SessionBulkRevocationManifest manifest)
    {
        await using var scope = new Scope(host, discovery, assurance);
        var issued = await scope.StepUp.IssuePasswordAsync(actor, manifest.Operation(actor), manifest.AssuranceVersion, Password);
        Assert.Null(issued.ErrorCode); return Assert.IsType<StepUpProofGrant>(issued.Grant).Key;
    }
    private static async Task<SessionBulkRevocationResult> Revoke(AuthenticationTestHost host, Discovery discovery, Assurance assurance,
        StepUpCaller actor, SessionBulkRevocationManifest manifest, string key, CancellationToken ct = default)
    { await using var scope = new Scope(host, discovery, assurance); return await scope.Owner.RevokeAsync(actor, manifest, key, ct); }
    private static async Task<AuthenticationSessionRow> Row(IdentityTestDatabase db, Guid id)
    { await using var context = db.Context(); return await context.Set<AuthenticationSessionRow>().AsNoTracking().SingleAsync(x => x.SessionId == id); }

    [Fact]
    public async Task CompleteServerDiscoveryIncludesEveryEligibleOwnedSessionAndNoForeignOriginOrAccount()
    {
        await using var db = await fixture.CreateDatabaseAsync(); await using var host = await Start(db); await host.SeedAsync(db); await host.SeedAsync(db, foreignEmail: true);
        var a = await Login(host); var b = await Login(host); var actor = await Login(host);
        await Login(host, origin: "https://other.example.test"); await Login(host, "foreign@example.test");
        var prepared = await Prepare(host, new(), new(), actor.Actor); Assert.Null(prepared.ErrorCode); var manifest = Assert.IsType<SessionBulkRevocationManifest>(prepared.Manifest);
        Assert.Equal(new[] { a.Actor.SessionId, b.Actor.SessionId }.OrderBy(x => x.ToString("N"), StringComparer.Ordinal), manifest.Targets.Select(x => x.SessionReference));
        Assert.All(manifest.Targets, x => Assert.True(x.ExpectedVersion > 0));
        Assert.Equal(0, manifest.Operation(actor.Actor).ExpectedVersion);
    }

    [Fact]
    public async Task EmptyPreparationIsNonmutatingAbsenceWithoutProofOrReceipt()
    {
        await using var db = await fixture.CreateDatabaseAsync(); await using var host = await Start(db); await host.SeedAsync(db); var actor = await Login(host);
        var before = await Row(db, actor.Actor.SessionId); await using var check = db.Context(); var events = await check.Set<OutboxMessage>().CountAsync();
        var prepared = await Prepare(host, new(), new(), actor.Actor); Assert.Equal("NO_OTHER_SESSIONS", prepared.ErrorCode); Assert.Null(prepared.Manifest);
        var after = await Row(db, actor.Actor.SessionId); Assert.True(after.Version == before.Version && after.LastSeenAt == before.LastSeenAt);
        Assert.Equal(0, await check.Set<StepUpProofRow>().CountAsync()); Assert.Equal(events, await check.Set<OutboxMessage>().CountAsync());
    }

    [Fact]
    public async Task ExpiredActorPreparationPreservesTerminalizationWithoutRevokingTargetsOrIssuingProof()
    {
        await using var db = await fixture.CreateDatabaseAsync(); await using var host = await Start(db); await host.SeedAsync(db);
        var actor = await Login(host); var beforeActor = await Row(db, actor.Actor.SessionId);
        host.Clock.Advance(host.Policies.Policy.AbsoluteLifetime + TimeSpan.FromSeconds(1));
        var target = await Login(host); var beforeTarget = await Row(db, target.Actor.SessionId);
        var result = await Prepare(host, new(), new(), actor.Actor);
        Assert.Equal("AUTHENTICATION_FAILED", result.ErrorCode); Assert.Null(result.Manifest);
        var ended = await Row(db, actor.Actor.SessionId);
        Assert.True(ended.State == "EXPIRED" && ended.Version == beforeActor.Version + 1 && ended.RevokedAt == host.Clock.GetUtcNow());
        Assert.True(ended.ProtectedKey.Length == 0 && ended.ProtectedTicket.Length == 0 && ended.LastSeenAt == beforeActor.LastSeenAt);
        var unchanged = await Row(db, target.Actor.SessionId);
        Assert.True(unchanged.State == beforeTarget.State && unchanged.Version == beforeTarget.Version && unchanged.LastSeenAt == beforeTarget.LastSeenAt);
        await using var check = db.Context();
        var expiry = Assert.Single(await check.Set<OutboxMessage>().Where(x => x.EventType == "TunnerSessionExpired").ToArrayAsync());
        Assert.True(expiry.Subject == actor.Actor.SessionId.ToString("N") && expiry.ResourceVersion == ended.Version.ToString(CultureInfo.InvariantCulture) &&
            expiry.OperationId == ended.OperationId && expiry.CorrelationId == ended.CorrelationId && expiry.OccurredAtUtc == ended.RevokedAt);
        Assert.Equal(0, await check.Set<OutboxMessage>().CountAsync(x => x.EventType == "TunnerSessionRevoked"));
        Assert.Equal(0, await check.Set<StepUpProofRow>().CountAsync());
    }

    [Fact]
    public async Task DiscoveryOverflowRejectsInsteadOfFreezingATruncatedSet()
    {
        await using var db = await fixture.CreateDatabaseAsync(); await using var host = await Start(db); await host.SeedAsync(db);
        await Login(host); await Login(host); await Login(host); var actor = await Login(host); var before = await Row(db, actor.Actor.SessionId);
        var prepared = await Prepare(host, new() { Maximum = 2 }, new(), actor.Actor);
        Assert.Equal("DISCOVERY_CAPACITY_EXCEEDED", prepared.ErrorCode); Assert.Null(prepared.Manifest);
        Assert.Equal(before.Version, (await Row(db, actor.Actor.SessionId)).Version);
        await using var check = db.Context(); Assert.Equal(4, await check.Set<AuthenticationSessionRow>().CountAsync(x => x.State == "ACTIVE"));
        Assert.Equal(0, await check.Set<StepUpProofRow>().CountAsync());
    }

    [Fact]
    public async Task BulkPermissionAndAssuranceAreNotInheritedFromSingleSessionPermission()
    {
        await using var db = await fixture.CreateDatabaseAsync(); await using var host = await Start(db); await host.SeedAsync(db); await Login(host); var actor = await Login(host);
        host.Access.DeniedOperation = SessionBulkRevocationApplication.Scope.Operation;
        Assert.Equal("AUTHENTICATION_FAILED", (await Prepare(host, new(), new(), actor.Actor)).ErrorCode);
        host.Access.DeniedOperation = null;
        await Assert.ThrowsAnyAsync<InvalidOperationException>(() => Prepare(host, new(), new() { BulkAllowed = false }, actor.Actor));
    }

    [Fact]
    public async Task AlreadyDeniedSessionsAreExcludedWithoutChangingTheirRecordedRows()
    {
        await using var db = await fixture.CreateDatabaseAsync(); await using var host = await Start(db); await host.SeedAsync(db);
        var expired = await Login(host); host.Clock.Advance(host.Policies.Policy.AbsoluteLifetime + TimeSpan.FromSeconds(1));
        var denied = await Login(host);
        await using (var update = db.Context())
        {
            var user = await update.Users.SingleAsync(); user.SecurityStamp = Guid.NewGuid().ToString("N"); user.ConcurrencyStamp = Guid.NewGuid().ToString("N"); await update.SaveChangesAsync();
        }
        var eligible = await Login(host); var actor = await Login(host);
        var before = new[] { await Row(db, expired.Actor.SessionId), await Row(db, denied.Actor.SessionId) };
        var manifest = Assert.IsType<SessionBulkRevocationManifest>((await Prepare(host, new(), new(), actor.Actor)).Manifest);
        Assert.Equal(eligible.Actor.SessionId, Assert.Single(manifest.Targets).SessionReference);
        foreach (var original in before)
        {
            var unchanged = await Row(db, original.SessionId);
            Assert.True(unchanged.Version == original.Version && unchanged.State == original.State && unchanged.LastSeenAt == original.LastSeenAt);
        }
    }

    [Fact]
    public async Task OneProofRevokesTheCompleteOriginalSetAndExactReceiptsReconcileOnce()
    {
        await using var db = await fixture.CreateDatabaseAsync(); await using var host = await Start(db); await host.SeedAsync(db);
        await Login(host); await Login(host); var actor = await Login(host); var discovery = new Discovery(); var assurance = new Assurance();
        var manifest = Assert.IsType<SessionBulkRevocationManifest>((await Prepare(host, discovery, assurance, actor.Actor)).Manifest);
        var before = new List<AuthenticationSessionRow>(); foreach (var item in manifest.Targets) before.Add(await Row(db, item.SessionReference));
        var key = await Issue(host, discovery, assurance, actor.Actor, manifest);
        var applied = await Revoke(host, discovery, assurance, actor.Actor, manifest, key); Assert.True(applied.Applied); Assert.Equal(2, applied.Receipts!.Count);
        Assert.Equal(manifest.Targets.Select(x => x.SessionReference), applied.Receipts.Select(x => x.SessionReference));
        var duplicate = await Revoke(host, discovery, assurance, actor.Actor, manifest, key); Assert.True(duplicate.Applied); Assert.Equal(applied.Receipts, duplicate.Receipts);
        await using (var read = new Scope(host, discovery, assurance)) Assert.True((await read.Owner.ReadResultAsync(actor.Actor, manifest)).Applied);
        foreach (var target in before)
        {
            var after = await Row(db, target.SessionId); Assert.True(after.State == "REVOKED" && after.Version == target.Version + 1 && after.LastSeenAt == target.LastSeenAt);
            Assert.True(after.ProtectedKey.Length == 0 && after.ProtectedTicket.Length == 0);
        }
        Assert.Equal("ACTIVE", (await Row(db, actor.Actor.SessionId)).State);
        await using var check = db.Context(); Assert.Equal(1, await check.Set<StepUpProofRow>().CountAsync(x => x.State == "CONSUMED"));
        Assert.Equal(2, await check.Set<OutboxMessage>().CountAsync(x => x.EventType == "TunnerSessionRevoked"));
        Assert.Equal(1, await check.Set<OutboxMessage>().CountAsync(x => x.EventType == "StepUpProofConsumed"));
        foreach (var item in await check.Set<OutboxMessage>().ToArrayAsync()) Assert.True(!item.Payload.Contains(key, StringComparison.Ordinal) && !item.Payload.Contains(Password, StringComparison.Ordinal));
    }

    [Fact]
    public async Task ForeignActorGenericEntryAndDeniedBulkPermissionCannotConsumeTheManifestProof()
    {
        await using var db = await fixture.CreateDatabaseAsync(); await using var host = await Start(db); await host.SeedAsync(db);
        await Login(host); var actor = await Login(host); var discovery = new Discovery(); var assurance = new Assurance();
        var manifest = Assert.IsType<SessionBulkRevocationManifest>((await Prepare(host, discovery, assurance, actor.Actor)).Manifest);
        var key = await Issue(host, discovery, assurance, actor.Actor, manifest); var other = await Login(host);
        await Assert.ThrowsAsync<ArgumentException>(() => Revoke(host, discovery, assurance, other.Actor, manifest, key));
        await using (var generic = new Scope(host, discovery, assurance))
            await Assert.ThrowsAsync<InvalidOperationException>(() => generic.StepUp.ExecuteAsync(actor.Actor, manifest.Operation(actor.Actor), key,
                _ => throw new InvalidOperationException("Generic bulk execution must not be entered.")));
        host.Access.DeniedOperation = SessionBulkRevocationApplication.Scope.Operation;
        Assert.False((await Revoke(host, discovery, assurance, actor.Actor, manifest, key)).Applied);
        await using var check = db.Context(); Assert.Equal("ACTIVE", (await check.Set<StepUpProofRow>().SingleAsync()).State);
        Assert.Equal(0, await check.Set<OutboxMessage>().CountAsync(x => x.EventType == "TunnerSessionRevoked"));
    }

    [Fact]
    public async Task AStaleLastTargetRollsBackEveryEarlierTargetAndProof()
    {
        await using var db = await fixture.CreateDatabaseAsync(); await using var host = await Start(db); await host.SeedAsync(db);
        var a = await Login(host); var b = await Login(host); var actor = await Login(host); var discovery = new Discovery(); var assurance = new Assurance();
        var manifest = Assert.IsType<SessionBulkRevocationManifest>((await Prepare(host, discovery, assurance, actor.Actor)).Manifest);
        var key = await Issue(host, discovery, assurance, actor.Actor, manifest); var last = manifest.Targets[^1].SessionReference;
        await using (var touch = new Scope(host, discovery, assurance)) Assert.NotNull(await touch.Authentication.RetrieveAsync(last == a.Actor.SessionId ? a.Key : b.Key, Origin, CancellationToken.None));
        Assert.False((await Revoke(host, discovery, assurance, actor.Actor, manifest, key)).Applied);
        await using var check = db.Context(); Assert.Equal(3, await check.Set<AuthenticationSessionRow>().CountAsync(x => x.State == "ACTIVE"));
        Assert.Equal("ACTIVE", (await check.Set<StepUpProofRow>().SingleAsync()).State);
        Assert.Equal(0, await check.Set<OutboxMessage>().CountAsync(x => x.EventType == "TunnerSessionRevoked"));
    }

    [Theory]
    [InlineData("authentication")]
    [InlineData("discovery")]
    [InlineData("assurance")]
    public async Task ChangedFinalAuthorityRollsBackAllEffects(string changed)
    {
        await using var db = await fixture.CreateDatabaseAsync(); await using var host = await Start(db); await host.SeedAsync(db);
        await Login(host); await Login(host); var actor = await Login(host); var discovery = new Discovery(); var assurance = new Assurance();
        var manifest = Assert.IsType<SessionBulkRevocationManifest>((await Prepare(host, discovery, assurance, actor.Actor)).Manifest); var key = await Issue(host, discovery, assurance, actor.Actor, manifest);
        var calls = 0; host.Access.BeforeDecision = request =>
        {
            if (request.Operation != SessionBulkRevocationApplication.Scope.Operation || ++calls != 2) return;
            if (changed == "authentication") host.Policies.Policy = host.Policies.Policy with { Version = "fixture-auth-v2" };
            if (changed == "discovery") discovery.Version = "fixture-discovery-v2";
            if (changed == "assurance") assurance.Version = "fixture-bulk-proof-v2";
        };
        var error = await Record.ExceptionAsync(async () => Assert.False((await Revoke(host, discovery, assurance, actor.Actor, manifest, key)).Applied));
        if (changed == "assurance") Assert.IsType<AssurancePolicyVersionConflictException>(error); else Assert.Null(error);
        host.Access.BeforeDecision = null; await using var check = db.Context();
        Assert.Equal(3, await check.Set<AuthenticationSessionRow>().CountAsync(x => x.State == "ACTIVE"));
        Assert.Equal("ACTIVE", (await check.Set<StepUpProofRow>().SingleAsync()).State);
        Assert.Equal(0, await check.Set<OutboxMessage>().CountAsync(x => x.EventType == "TunnerSessionRevoked"));
    }

    [Fact]
    public async Task TargetExpiryAfterPreparationRollsBackTheWholeManifest()
    {
        await using var db = await fixture.CreateDatabaseAsync(); await using var host = await Start(db); await host.SeedAsync(db);
        await Login(host); host.Clock.Advance(TimeSpan.FromMinutes(4)); await Login(host); var actor = await Login(host);
        var discovery = new Discovery(); var assurance = new Assurance(); var manifest = Assert.IsType<SessionBulkRevocationManifest>((await Prepare(host, discovery, assurance, actor.Actor)).Manifest);
        var key = await Issue(host, discovery, assurance, actor.Actor, manifest); host.Clock.Advance(TimeSpan.FromSeconds(61));
        Assert.False((await Revoke(host, discovery, assurance, actor.Actor, manifest, key)).Applied);
        await using var check = db.Context(); Assert.Equal(0, await check.Set<OutboxMessage>().CountAsync(x => x.EventType == "TunnerSessionRevoked"));
        Assert.Equal("ACTIVE", (await check.Set<StepUpProofRow>().SingleAsync()).State);
    }

    [Fact]
    public async Task LaterCreatedSessionIsOutsideTheCompleteOriginalManifestAndRemainsActive()
    {
        await using var db = await fixture.CreateDatabaseAsync(); await using var host = await Start(db); await host.SeedAsync(db);
        await Login(host); await Login(host); var actor = await Login(host); var discovery = new Discovery(); var assurance = new Assurance();
        var manifest = Assert.IsType<SessionBulkRevocationManifest>((await Prepare(host, discovery, assurance, actor.Actor)).Manifest);
        var later = await Login(host); var key = await Issue(host, discovery, assurance, actor.Actor, manifest);
        var result = await Revoke(host, discovery, assurance, actor.Actor, manifest, key); Assert.True(result.Applied); Assert.Equal(2, result.Receipts!.Count);
        Assert.DoesNotContain(result.Receipts, x => x.SessionReference == later.Actor.SessionId); Assert.Equal("ACTIVE", (await Row(db, later.Actor.SessionId)).State);
    }

    [Fact]
    public async Task PartialTargetEvidenceAndConsumedProofNeverBecomeWholeSetSuccess()
    {
        await using var db = await fixture.CreateDatabaseAsync(); await using var host = await Start(db); await host.SeedAsync(db);
        await Login(host); await Login(host); var actor = await Login(host); var discovery = new Discovery(); var assurance = new Assurance();
        var manifest = Assert.IsType<SessionBulkRevocationManifest>((await Prepare(host, discovery, assurance, actor.Actor)).Manifest); var key = await Issue(host, discovery, assurance, actor.Actor, manifest);
        var operation = manifest.Operation(actor.Actor); var at = host.Clock.GetUtcNow();
        await using (var adverse = db.Context())
        {
            var proof = await adverse.Set<StepUpProofRow>().SingleAsync(); proof.State = "CONSUMED"; proof.EndedAt = at; proof.ProtectedKey = ""; proof.Version++;
            var id = manifest.Targets[0].SessionReference; var target = await adverse.Set<AuthenticationSessionRow>().SingleAsync(x => x.SessionId == id);
            target.State = "REVOKED"; target.RevokedAt = at; target.ProtectedKey = ""; target.ProtectedTicket = ""; target.Version++; target.OperationId = operation.OperationId; target.CorrelationId = operation.CorrelationId;
            foreach (var evidence in new[] { (Type: "TunnerSessionRevoked", Subject: id.ToString("N"), Version: target.Version), (Type: "StepUpProofConsumed", Subject: proof.ProofId.ToString("N"), Version: proof.Version) })
                adverse.Add(new OutboxMessage(Guid.NewGuid(), "1.0", evidence.Type, "1", at, "tunner/identity", evidence.Subject, "RESTRICTED_SECURITY",
                    JsonSerializer.Serialize(new { reference = evidence.Subject }), evidence.Version.ToString(CultureInfo.InvariantCulture), operation.OperationId, operation.CorrelationId));
            await adverse.SaveChangesAsync();
        }
        await using (var read = new Scope(host, discovery, assurance)) Assert.False((await read.Owner.ReadResultAsync(actor.Actor, manifest)).Applied);
        Assert.False((await Revoke(host, discovery, assurance, actor.Actor, manifest, key)).Applied);
        Assert.Equal("ACTIVE", (await Row(db, manifest.Targets[1].SessionReference)).State);
    }

    [Fact]
    public async Task ConcurrentDuplicatesShareOneConsumedProofAndAllReceipts()
    {
        await using var db = await fixture.CreateDatabaseAsync(); await using var host = await Start(db); await host.SeedAsync(db);
        await Login(host); await Login(host); var actor = await Login(host); var discovery = new Discovery(); var assurance = new Assurance();
        var manifest = Assert.IsType<SessionBulkRevocationManifest>((await Prepare(host, discovery, assurance, actor.Actor)).Manifest); var key = await Issue(host, discovery, assurance, actor.Actor, manifest);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var results = await Task.WhenAll(Revoke(host, discovery, assurance, actor.Actor, manifest, key, timeout.Token), Revoke(host, discovery, assurance, actor.Actor, manifest, key, timeout.Token));
        Assert.All(results, x => Assert.True(x.Applied && x.Receipts!.Count == 2));
        await using var check = db.Context(); Assert.Equal(1, await check.Set<OutboxMessage>().CountAsync(x => x.EventType == "StepUpProofConsumed"));
        Assert.Equal(2, await check.Set<OutboxMessage>().CountAsync(x => x.EventType == "TunnerSessionRevoked"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OverlappingAndOpposingSetsTakeAllCanonicalLocksBeforeAccount(bool opposing)
    {
        await using var db = await fixture.CreateDatabaseAsync(); var gate = new LockBarrier();
        await using var host = await Start(db, s => s.AddDbContext<IdentityPersistenceDbContext>(o => o.AddInterceptors(gate))); await host.SeedAsync(db);
        await Login(host); await Login(host); var a = await Login(host); var discovery = new Discovery(); var assurance = new Assurance();
        var b = opposing ? await Login(host) : default;
        var am = Assert.IsType<SessionBulkRevocationManifest>((await Prepare(host, discovery, assurance, a.Actor)).Manifest); var ak = await Issue(host, discovery, assurance, a.Actor, am);
        if (!opposing) b = await Login(host);
        var bm = Assert.IsType<SessionBulkRevocationManifest>((await Prepare(host, discovery, assurance, b.Actor)).Manifest); var bk = await Issue(host, discovery, assurance, b.Actor, bm);
        await using var first = new Scope(host, discovery, assurance); await using var second = new Scope(host, discovery, assurance);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20)); gate.Arm(first.Database, am.Targets.Select(x => x.SessionReference).Append(a.Actor.SessionId));
        var left = first.Owner.RevokeAsync(a.Actor, am, ak, timeout.Token);
        try
        {
            await gate.Reached.Task.WaitAsync(timeout.Token); var right = second.Owner.RevokeAsync(b.Actor, bm, bk, timeout.Token);
            await gate.Contended.Task.WaitAsync(timeout.Token); gate.Release.TrySetResult(); var results = await Task.WhenAll(left, right);
            if (opposing) Assert.True(results.Count(x => x.Applied) <= 1); else Assert.Single(results, x => x.Applied);
            gate.AssertCanonical();
            await using var check = db.Context(); foreach (var manifest in new[] { am, bm })
            {
                var op = manifest.Operation(manifest.ActorSessionId == a.Actor.SessionId ? a.Actor : b.Actor);
                var effects = await check.Set<OutboxMessage>().CountAsync(x => x.EventType == "TunnerSessionRevoked" && x.OperationId == op.OperationId);
                Assert.True(effects == 0 || effects == manifest.Targets.Count);
            }
        }
        finally { gate.Release.TrySetResult(); }
    }

    private sealed class LockBarrier : DbCommandInterceptor
    {
        private Guid owner; private string[] expected = []; private int entered; private readonly ConcurrentQueue<string> acquired = new();
        internal TaskCompletionSource Reached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Contended { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal void Arm(DbContext context, IEnumerable<Guid> ids) { owner = context.ContextId.InstanceId; expected = ids.Select(x => "auth-session:" + x.ToString("N")).Distinct().Order(StringComparer.Ordinal).ToArray(); }
        private static string? Session(DbCommand command) => command.CommandText.Contains("pg_advisory_xact_lock", StringComparison.Ordinal)
            ? command.Parameters.Cast<DbParameter>().Select(x => x.Value as string).FirstOrDefault(x => x?.StartsWith("auth-session:", StringComparison.Ordinal) == true) : null;
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData data, InterceptionResult<int> result, CancellationToken ct = default)
        { if (owner != Guid.Empty && data.Context?.ContextId.InstanceId != owner && Session(command) is { } id && expected.Contains(id)) Contended.TrySetResult(); return ValueTask.FromResult(result); }
        public override ValueTask<int> NonQueryExecutedAsync(DbCommand command, CommandExecutedEventData data, int result, CancellationToken ct = default)
        { if (data.Context?.ContextId.InstanceId == owner && Session(command) is { } id) acquired.Enqueue(id); return ValueTask.FromResult(result); }
        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData data, InterceptionResult<DbDataReader> result, CancellationToken ct = default)
        {
            if (data.Context?.ContextId.InstanceId == owner && command.CommandText.Contains("FROM accounts.accounts", StringComparison.Ordinal) && command.CommandText.Contains("FOR NO KEY UPDATE", StringComparison.Ordinal) && Interlocked.CompareExchange(ref entered, 1, 0) == 0)
            { AssertCanonical(); Reached.TrySetResult(); await Release.Task.WaitAsync(ct); }
            return result;
        }
        internal void AssertCanonical() => Assert.Equal(expected, acquired.Distinct().ToArray());
    }
}

public sealed class SessionBulkRevocationContractTests
{
    [Fact]
    public void ManifestDefensivelyCopiesCanonicalMembershipAndRejectsEmptyDuplicateOrSelfTargets()
    {
        var session = Guid.NewGuid(); var caller = StepUpCaller.FromPrincipal(new ClaimsPrincipal(new ClaimsIdentity([
            new(ClaimTypes.NameIdentifier, "fixture-account"), new(AuthenticationIdentity.SessionClaim, session.ToString("N")), new(AuthenticationIdentity.OriginClaim, Origin)], AuthenticationIdentity.Scheme)), Origin, new('B', 64));
        var discovery = new SessionBulkRevocationTests.Discovery(); var budget = discovery.RequireEffective(new TestClock().GetUtcNow());
        var original = new List<SessionBulkRevocationTarget> { new(Guid.NewGuid(), 3), new(Guid.NewGuid(), 7) };
        var manifest = SessionBulkRevocationManifest.FromDiscovery(caller, Guid.NewGuid(), "fixture", original, new TestClock().GetUtcNow(), "fixture-auth", "fixture-proof", budget);
        var digest = manifest.Operation(caller).ContextDigest; var ids = original.Select(x => x.SessionReference).OrderBy(x => x.ToString("N"), StringComparer.Ordinal).ToArray(); original.Clear();
        Assert.Equal(ids, manifest.Targets.Select(x => x.SessionReference)); Assert.Equal(digest, manifest.Operation(caller).ContextDigest);
        Assert.Throws<NotSupportedException>(() => ((IList<SessionBulkRevocationTarget>)manifest.Targets).Clear());
        Assert.Throws<ArgumentException>(() => SessionBulkRevocationManifest.FromDiscovery(caller, Guid.NewGuid(), "fixture", [], new TestClock().GetUtcNow(), "auth", "proof", budget));
        var duplicate = new SessionBulkRevocationTarget(Guid.NewGuid(), 1);
        Assert.Throws<ArgumentException>(() => SessionBulkRevocationManifest.FromDiscovery(caller, Guid.NewGuid(), "fixture", [duplicate, duplicate], new TestClock().GetUtcNow(), "auth", "proof", budget));
        Assert.Throws<ArgumentException>(() => SessionBulkRevocationManifest.FromDiscovery(caller, Guid.NewGuid(), "fixture", [new(session, 1)], new TestClock().GetUtcNow(), "auth", "proof", budget));
    }
}
