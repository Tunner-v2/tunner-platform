using System.Collections.Concurrent;
using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Tunner.Accounts;
using Tunner.Database.Outbox;
using Tunner.Platform.Policies;
using Xunit;
using static Tunner.Identity.Persistence.Tests.AuthenticationTestHost;

namespace Tunner.Identity.Persistence.Tests;

public sealed class SessionRevocationTests(SessionListingPostgresFixture fixture) : IClassFixture<SessionListingPostgresFixture>
{
    // Explicit synthetic authority. None of these values activate a production policy.
    private static readonly string Network = new('B', 64);
    private sealed class OwnerScope : IAsyncDisposable
    {
        private readonly AsyncServiceScope scope;
        internal IdentityPersistenceDbContext Database { get; }
        internal AuthenticationApplication Authentication { get; }
        internal StepUpApplication StepUp { get; }
        internal SessionRevocationApplication Owner { get; }
        internal IServiceProvider Services => scope.ServiceProvider;
        internal OwnerScope(AuthenticationTestHost host, IAssurancePolicySource? source = null)
        {
            scope = host.Services.CreateAsyncScope(); var provider = scope.ServiceProvider;
            Database = provider.GetRequiredService<IdentityPersistenceDbContext>();
            Authentication = provider.GetRequiredService<AuthenticationApplication>();
            StepUp = new(Database, Authentication, host.Policies, source ?? Policies(host),
                provider.GetRequiredService<AuthenticationPasswordWork>(), provider.GetRequiredService<IDataProtectionProvider>(), host.Clock);
            Owner = new(Authentication, StepUp);
        }
        public ValueTask DisposeAsync() => scope.DisposeAsync();
    }
    private static AssurancePolicyCatalog Policies(AuthenticationTestHost host, AssurancePolicyScope? scope = null) => new([
        new(new("fixture-session-revoke-v1", "ACTIVE", scope ?? SessionRevocationBinding.Scope,
            new EffectivePolicyInterval(host.Clock.GetUtcNow().AddHours(-1), true, host.Clock.GetUtcNow().AddHours(1), false),
            null, ["SYNTHETIC-TEST-ONLY"]), "fixture-proof", TimeSpan.FromMinutes(1),
            new Dictionary<string, string> { ["pwd"] = "fixture-proof" })]);
    private static async Task<AuthenticationSessionRow> Row(IdentityTestDatabase db, Guid id)
    {
        await using var context = db.Context();
        return await context.Set<AuthenticationSessionRow>().AsNoTracking().SingleAsync(x => x.SessionId == id);
    }
    private static async Task<SessionRevocationRequest> Request(IdentityTestDatabase db, Guid id) =>
        new(Guid.NewGuid(), id, (await Row(db, id)).Version, "fixture-" + Guid.NewGuid().ToString("N"));
    private static async Task<string> Issue(AuthenticationTestHost host, StepUpCaller caller, SessionRevocationRequest request)
    {
        await using var scope = new OwnerScope(host);
        var result = await scope.StepUp.IssuePasswordAsync(caller, SessionRevocationBinding.Operation(caller, request), "fixture-session-revoke-v1", Password);
        Assert.Null(result.ErrorCode); return Assert.IsType<StepUpProofGrant>(result.Grant).Key;
    }
    private static async Task<SessionRevocationResult> Revoke(AuthenticationTestHost host, StepUpCaller caller, SessionRevocationRequest request, string key, CancellationToken ct = default)
    {
        await using var scope = new OwnerScope(host); return await scope.Owner.RevokeAsync(caller, request, key, ct);
    }
    private static async Task<(AuthenticationCaller Caller, PasswordSignInCommand Command, AuthenticationGrant Grant, StepUpCaller Actor)> Login(AuthenticationTestHost host)
    {
        var caller = new AuthenticationCaller(AuthenticationIdentity.Digest(Guid.NewGuid().ToString()), Network, Origin, null);
        var command = Command(Guid.NewGuid()); await using var scope = new OwnerScope(host);
        var result = await scope.Authentication.SignInAsync(caller, command, CancellationToken.None);
        Assert.Null(result.ErrorCode); var grant = Assert.IsType<AuthenticationGrant>(result.Grant);
        return (caller, command, grant, StepUpCaller.FromPrincipal(grant.Ticket.Principal, Origin, Network));
    }
    private static async Task<StepUpCaller> IssuedActor(AuthenticationTestHost host, AuthenticationSessionRow row)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var protector = scope.ServiceProvider.GetRequiredService<IDataProtectionProvider>().CreateProtector("Tunner.User.Session.Ticket.v1");
        var ticket = TicketSerializer.Default.Deserialize(Convert.FromBase64String(protector.Unprotect(row.ProtectedTicket)));
        Assert.NotNull(ticket); return StepUpCaller.FromPrincipal(ticket.Principal, Origin, Network);
    }

    [Fact]
    public async Task ExactOtherSessionRevocationReconcilesWithoutRepeatingEffectOrProofConsumption()
    {
        await using var db = await fixture.CreateDatabaseAsync(); await using var host = await Start(db); await host.SeedAsync(db);
        var target = await Login(host); var actor = await SessionListingTests.SignIn(host); var request = await Request(db, target.Actor.SessionId);
        await using (var prepare = new OwnerScope(host)) Assert.NotNull(await prepare.Owner.PrepareAsync(actor, request));
        var before = await Row(db, target.Actor.SessionId); var key = await Issue(host, actor, request);
        var result = await Revoke(host, actor, request, key); Assert.True(result.Applied); Assert.Null(result.ErrorCode);
        Assert.Equal(result.Receipt, (await Revoke(host, actor, request, key)).Receipt);
        await using (var read = new OwnerScope(host)) Assert.Equal(result.Receipt, (await read.Owner.ReadResultAsync(actor, request)).Receipt);
        var ended = await Row(db, target.Actor.SessionId);
        Assert.True(ended.State == "REVOKED" && ended.Version == before.Version + 1 && ended.LastSeenAt == before.LastSeenAt);
        Assert.True(ended.ProtectedKey.Length == 0 && ended.ProtectedTicket.Length == 0);
        await using (var retrieval = new OwnerScope(host)) Assert.Null(await retrieval.Authentication.RetrieveAsync(target.Grant.Key, Origin, CancellationToken.None));
        await using var context = db.Context(); var operation = SessionRevocationBinding.Operation(actor, request);
        Assert.Equal(1, await context.Set<OutboxMessage>().CountAsync(x => x.EventType == "TunnerSessionRevoked" && x.OperationId == operation.OperationId));
        Assert.Equal(1, await context.Set<OutboxMessage>().CountAsync(x => x.EventType == "StepUpProofConsumed" && x.OperationId == operation.OperationId));
        var proof = await context.Set<StepUpProofRow>().SingleAsync(x => x.OperationId == operation.OperationId);
        Assert.True(proof.State == "CONSUMED" && proof.Version == 2 && proof.ProtectedKey.Length == 0);
        foreach (var item in await context.Set<OutboxMessage>().Where(x => x.OperationId == operation.OperationId).ToArrayAsync())
            Assert.True(!item.Payload.Contains(key, StringComparison.Ordinal) && !item.Payload.Contains(Password, StringComparison.Ordinal) && !item.Payload.Contains("person@example.test", StringComparison.Ordinal));
        Assert.Equal(nameof(SessionRevocationResult), result.ToString());
    }

    [Theory]
    [InlineData("foreign")]
    [InlineData("origin")]
    [InlineData("version")]
    public async Task ForeignOrStaleTargetsCannotBePreparedOrChanged(string kind)
    {
        await using var db = await fixture.CreateDatabaseAsync(); await using var host = await Start(db); await host.SeedAsync(db);
        if (kind == "foreign") await host.SeedAsync(db, foreignEmail: true);
        var target = await SessionListingTests.SignIn(host, kind == "foreign" ? "foreign@example.test" : "person@example.test",
            kind == "origin" ? "https://other.example.test" : Origin);
        var actor = await SessionListingTests.SignIn(host); var request = await Request(db, target.SessionId);
        if (kind == "version") request = request with { ExpectedVersion = request.ExpectedVersion + 1 };
        var before = await Row(db, target.SessionId); await using var scope = new OwnerScope(host);
        Assert.Null(await scope.Owner.PrepareAsync(actor, request));
        var key = await Issue(host, actor, request); Assert.False((await Revoke(host, actor, request, key)).Applied);
        Assert.False((await scope.Owner.ReadResultAsync(actor, request)).Applied);
        var after = await Row(db, target.SessionId); Assert.True(after.Version == before.Version && after.State == before.State && after.LastSeenAt == before.LastSeenAt);
    }

    [Fact]
    public async Task PrepareAndResultReadRequireCurrentActorAndExactOperationAccess()
    {
        await using var db = await fixture.CreateDatabaseAsync(); await using var host = await Start(db); await host.SeedAsync(db);
        var target = await SessionListingTests.SignIn(host); var actor = await Login(host); var request = await Request(db, target.SessionId);
        host.Access.DeniedOperation = "SESSION_REVOKE";
        await using (var scope = new OwnerScope(host))
        {
            Assert.Null(await scope.Owner.PrepareAsync(actor.Actor, request));
            Assert.Equal("AUTHENTICATION_FAILED", (await scope.Owner.ReadResultAsync(actor.Actor, request)).ErrorCode);
        }
        host.Access.DeniedOperation = null;
        await using (var scope = new OwnerScope(host)) await scope.Authentication.RemoveAsync(actor.Grant.Key, Origin, CancellationToken.None);
        await using (var scope = new OwnerScope(host)) Assert.Null(await scope.Owner.PrepareAsync(actor.Actor, request));
        Assert.Equal("ACTIVE", (await Row(db, target.SessionId)).State);
    }

    [Fact]
    public async Task ExpiredOrStampInvalidTargetDoesNotConsumeProofOrRecordRevocation()
    {
        await using var db = await fixture.CreateDatabaseAsync(); await using var host = await Start(db); await host.SeedAsync(db);
        var expired = await SessionListingTests.SignIn(host); host.Clock.Advance(host.Policies.Policy.AbsoluteLifetime + TimeSpan.FromSeconds(1));
        var denied = await SessionListingTests.SignIn(host);
        await using (var update = db.Context())
        {
            var user = await update.Users.SingleAsync(); user.SecurityStamp = Guid.NewGuid().ToString("N"); user.ConcurrencyStamp = Guid.NewGuid().ToString("N"); await update.SaveChangesAsync();
        }
        var actor = await SessionListingTests.SignIn(host);
        foreach (var target in new[] { expired, denied })
        {
            var request = await Request(db, target.SessionId); var key = await Issue(host, actor, request); var before = await Row(db, target.SessionId);
            Assert.False((await Revoke(host, actor, request, key)).Applied);
            var after = await Row(db, target.SessionId); Assert.True(after.State == before.State && after.Version == before.Version);
        }
        await using var check = db.Context(); Assert.Equal(0, await check.Set<StepUpProofRow>().CountAsync(x => x.State == "CONSUMED"));
    }

    [Fact]
    public async Task ConsumedProofWithoutExactTargetEffectIsNeverAnOwnerReceipt()
    {
        await using var db = await fixture.CreateDatabaseAsync(); await using var host = await Start(db); await host.SeedAsync(db);
        var target = await SessionListingTests.SignIn(host); var actor = await SessionListingTests.SignIn(host); var request = await Request(db, target.SessionId);
        await Issue(host, actor, request);
        await using (var update = db.Context())
        {
            var proof = await update.Set<StepUpProofRow>().SingleAsync(); proof.State = "CONSUMED"; proof.EndedAt = host.Clock.GetUtcNow(); proof.ProtectedKey = ""; proof.Version++;
            await update.SaveChangesAsync();
        }
        await using var read = new OwnerScope(host); Assert.False((await read.Owner.ReadResultAsync(actor, request)).Applied);
        Assert.Equal("ACTIVE", (await Row(db, target.SessionId)).State);
    }

    [Fact]
    public async Task ChangedFinalPolicyRollsBackTargetAndProofAtomically()
    {
        await using var db = await fixture.CreateDatabaseAsync(); await using var host = await Start(db); await host.SeedAsync(db);
        var target = await SessionListingTests.SignIn(host); var actor = await SessionListingTests.SignIn(host); var request = await Request(db, target.SessionId);
        var key = await Issue(host, actor, request); var calls = 0;
        host.Access.BeforeDecision = input =>
        {
            if (input.Operation == "SESSION_REVOKE" && ++calls == 2) host.Policies.Policy = host.Policies.Policy with { Version = "fixture-auth-v2" };
        };
        Assert.False((await Revoke(host, actor, request, key)).Applied); host.Access.BeforeDecision = null;
        var row = await Row(db, target.SessionId); Assert.True(row.State == "ACTIVE" && row.Version == request.ExpectedVersion);
        await using var check = db.Context(); Assert.Equal("ACTIVE", (await check.Set<StepUpProofRow>().SingleAsync()).State);
        Assert.Equal(0, await check.Set<OutboxMessage>().CountAsync(x => x.EventType == "TunnerSessionRevoked"));
    }

    [Fact]
    public async Task DuplicateConcurrentRequestsReturnOneCausalEffect()
    {
        await using var db = await fixture.CreateDatabaseAsync(); await using var host = await Start(db); await host.SeedAsync(db);
        var target = await SessionListingTests.SignIn(host); var actor = await SessionListingTests.SignIn(host); var request = await Request(db, target.SessionId); var key = await Issue(host, actor, request);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var results = await Task.WhenAll(Revoke(host, actor, request, key, timeout.Token), Revoke(host, actor, request, key, timeout.Token));
        Assert.All(results, x => Assert.True(x.Applied)); Assert.Equal(results[0].Receipt, results[1].Receipt);
        Assert.Equal(request.ExpectedVersion + 1, (await Row(db, target.SessionId)).Version);
    }

    [Fact]
    public async Task DifferentActorsCompetingForOneTargetApplyOnlyOneExactOperation()
    {
        await using var db = await fixture.CreateDatabaseAsync(); await using var host = await Start(db); await host.SeedAsync(db);
        var target = await SessionListingTests.SignIn(host); var a = await SessionListingTests.SignIn(host); var b = await SessionListingTests.SignIn(host);
        var ar = await Request(db, target.SessionId); var br = await Request(db, target.SessionId); var ak = await Issue(host, a, ar); var bk = await Issue(host, b, br);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var results = await Task.WhenAll(Revoke(host, a, ar, ak, timeout.Token), Revoke(host, b, br, bk, timeout.Token));
        Assert.Single(results, x => x.Applied); Assert.Equal(ar.ExpectedVersion + 1, (await Row(db, target.SessionId)).Version);
    }

    [Fact]
    public async Task OpposingRevocationsAcquireBothSessionsBeforeAccountWithoutDeadlock()
    {
        await using var db = await fixture.CreateDatabaseAsync(); var gate = new SessionLocksBarrier();
        await using var host = await Start(db, s => s.AddDbContext<IdentityPersistenceDbContext>(o => o.AddInterceptors(gate))); await host.SeedAsync(db);
        var a = await SessionListingTests.SignIn(host); var b = await SessionListingTests.SignIn(host);
        var ar = await Request(db, b.SessionId); var ak = await Issue(host, a, ar); var br = await Request(db, a.SessionId); var bk = await Issue(host, b, br);
        await using var first = new OwnerScope(host); await using var second = new OwnerScope(host);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20)); gate.Arm(first.Database, a.SessionId, b.SessionId);
        var left = first.Owner.RevokeAsync(a, ar, ak, timeout.Token);
        try
        {
            await gate.AccountReached.Task.WaitAsync(timeout.Token);
            var right = second.Owner.RevokeAsync(b, br, bk, timeout.Token);
            await gate.ContenderAttempted.Task.WaitAsync(timeout.Token); gate.Release.TrySetResult();
            var results = await Task.WhenAll(left, right); Assert.Single(results, x => x.Applied);
            gate.AssertCanonical();
        }
        finally { gate.Release.TrySetResult(); }
    }

    [Fact]
    public async Task PasswordRetryPrelocksPriorAndPreviousBeforeAccountWhileRevokerContends()
    {
        await using var db = await fixture.CreateDatabaseAsync(); var gate = new SessionLocksBarrier(); var profile = new TotpTests.Profile();
        await using var host = await TotpTests.StartTotp(db, profile, s => s.AddDbContext<IdentityPersistenceDbContext>(o => o.AddInterceptors(gate))); await host.SeedAsync(db);
        var previous = await Login(host); var prior = await Login(host); var actor = await SessionListingTests.SignIn(host);
        var request = await Request(db, prior.Actor.SessionId); var key = await Issue(host, actor, request);
        await using var retry = new OwnerScope(host); await using var revoker = new OwnerScope(host);
        var caller = prior.Caller with { PreviousSessionId = previous.Actor.SessionId };
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20)); gate.Arm(retry.Database, previous.Actor.SessionId, prior.Actor.SessionId);
        var original = retry.Authentication.SignInAsync(caller, prior.Command, timeout.Token);
        try
        {
            await gate.AccountReached.Task.WaitAsync(timeout.Token);
            var revoke = revoker.Owner.RevokeAsync(actor, request, key, timeout.Token);
            await gate.ContenderAttempted.Task.WaitAsync(timeout.Token); gate.Release.TrySetResult();
            Assert.NotNull((await original).Grant); Assert.False((await revoke).Applied); gate.AssertCanonical();
        }
        finally { gate.Release.TrySetResult(); }
    }

    [Fact]
    public async Task FederationRetryPrelocksPreviousAndResultCanonicallyBeforeAccount()
    {
        await using var db = await fixture.CreateDatabaseAsync(); var gate = new SessionLocksBarrier();
        await using var host = await Start(db, s => s.AddDbContext<IdentityPersistenceDbContext>(o => o.AddInterceptors(gate))); await host.SeedAsync(db);
        var a = await SessionListingTests.SignIn(host); var b = await SessionListingTests.SignIn(host); var actor = await SessionListingTests.SignIn(host);
        var ids = new[] { a.SessionId, b.SessionId }.OrderBy(x => x.ToString("N"), StringComparer.Ordinal).ToArray();
        var resultId = ids[0]; var previousId = ids[1]; var request = await Request(db, resultId); var key = await Issue(host, actor, request);
        var now = host.Clock.GetUtcNow();
        // Synthetic invalid-link result-recovery call exercises the actual two-session lock seam.
        // It must fail closed after the locks; this is not a verified federation authentication.
        var values = new FederationProfile("fixture-v1", FederationProviderKind.Google, Origin, "fixture-google", "fixture-google",
            "https://accounts.google.com", "fixture-client", [], "fixture-mapping", 256, 256,
            TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(2), 1,
            "/signed-in", ["/signed-in"], "fixture-admission", "fixture-assurance", "fixture-assurance-map", "fixture-use", "fixture-processing", "fixture-retention");
        var profile = new FederationProfileVersion(new(values.Version, "ACTIVE", values.Provider,
            new EffectivePolicyInterval(now.AddHours(-1), true, now.AddHours(1), false), null, ["SYNTHETIC-TEST-ONLY"]), values);
        var caller = new AuthenticationCaller(new('A', 64), Network, Origin, previousId);
        var attempt = new FederationAttemptRow { AttemptId = Guid.NewGuid(), RequestId = Guid.NewGuid(), CorrelationId = "fixture",
            PreviousSessionId = previousId, PreviousCookieDigest = "fixture-cookie", LinkVersion = 1,
            VerifiedAt = now, VerificationExpiresAt = now.AddMinutes(1) };
        var browser = new FederationCurrentBrowser(caller, Guid.NewGuid(), now.AddMinutes(1), host.Policies.Policy.Version,
            "fixture-cookie", previousId, "fixture-cookie");
        var linked = new FederationLinkedSignInRequest(attempt, browser,
            new ExternalIdentityLinkRow { LinkId = Guid.NewGuid(), AccountId = actor.AccountId, Provider = "fixture-google", Subject = "fixture-subject" }, profile);
        await using var retry = new OwnerScope(host); await using var revoker = new OwnerScope(host);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await using var tx = await retry.Database.Database.BeginTransactionAsync(timeout.Token); gate.Arm(retry.Database, previousId, resultId);
        var original = retry.Authentication.ReadOriginalFederatedSignInAsync(linked, resultId, null, timeout.Token);
        try
        {
            await gate.AccountReached.Task.WaitAsync(timeout.Token);
            var revoke = revoker.Owner.RevokeAsync(actor, request, key, timeout.Token);
            await gate.ContenderAttempted.Task.WaitAsync(timeout.Token); gate.Release.TrySetResult();
            Assert.Equal("AUTHENTICATION_FAILED", (await original).ErrorCode);
            await tx.RollbackAsync(timeout.Token); Assert.True((await revoke).Applied); gate.AssertCanonical();
        }
        finally { gate.Release.TrySetResult(); }
    }

    [Fact]
    public async Task CredentialRevocationActorEffectSerializesWithExactSessionRevoker()
    {
        await using var db = await fixture.CreateDatabaseAsync(); var gate = new SessionLocksBarrier();
        await using var host = await PasskeyTests.StartRecoveryFixtureAsync(db, services =>
        {
            var source = (IPasskeyPolicySource)services.Last(x => x.ServiceType == typeof(IPasskeyPolicySource)).ImplementationInstance!;
            var policy = source.RequireEffective(new TestClock().GetUtcNow());
            services.RemoveAll<IPasskeyPolicySource>();
            services.AddSingleton<IPasskeyPolicySource>(new FixedPasskeyPolicy(new(policy.Version, policy.RelyingPartyId, policy.Origin,
                policy.CeremonyLifetime, policy.AuthenticatorTimeout, policy.ResultRecoveryLifetime, PasskeyRevocationSessionEffect.RevokeCredentialSessions,
                "required", policy.AllowedAlgorithms, policy.ChallengeBytes, policy.MaximumCredentialJsonBytes, policy.MaximumActiveCredentials,
                policy.MaximumPendingCeremonies, policy.EnrollmentAssurancePolicyVersion, policy.RevocationAssurancePolicyVersion,
                policy.ProcessingReference, policy.RetentionReference, "required", "none", policy.AssuranceMappings)));
            services.AddDbContext<IdentityPersistenceDbContext>(o => o.AddInterceptors(gate));
        });
        await host.SeedAsync(db); var revoker = await SessionListingTests.SignIn(host);
        using var native = new PasskeyAuthenticator(); var enrolled = await PasskeyTests.SeedRecoveryCredentialAsync(host, native);
        await PasskeyTests.SeedRecoveryCounterAsync(host, native);
        AuthenticationSessionRow target; long credentialVersion;
        await using (var read = db.Context())
        {
            target = await read.Set<AuthenticationSessionRow>().AsNoTracking().SingleAsync(x => x.AuthenticationMethods == "passkey");
            credentialVersion = (await read.Set<PasskeyCredentialRow>().SingleAsync(x => x.CredentialReference == enrolled.Reference)).Version;
        }
        var actor = await IssuedActor(host, target); UserStepUpOperationContext context; string oldKey;
        var assurance = host.Services.GetRequiredService<IAssurancePolicySource>();
        await using (var start = new OwnerScope(host, assurance))
        {
            var app = start.Services.GetRequiredService<PasskeyApplication>(); var id = Guid.NewGuid();
            var pending = await app.StartMutationAsync(actor, id, enrolled.Reference, credentialVersion, CancellationToken.None); Assert.Null(pending.ErrorCode);
            context = (await app.ResolveMutationAsync(actor, pending.Ceremony!.CeremonyId, id, CancellationToken.None))!; Assert.NotNull(context);
        }
        await using (var issue = new OwnerScope(host, assurance))
        {
            var proof = await issue.StepUp.IssuePasswordAsync(actor, context.Binding, context.ExpectedPolicyVersion, Password); Assert.NotNull(proof.Grant); oldKey = proof.Grant.Key;
        }
        var request = await Request(db, actor.SessionId); var key = await Issue(host, revoker, request);
        await using var old = new OwnerScope(host, assurance); await using var current = new OwnerScope(host);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20)); gate.Arm(old.Database, actor.SessionId);
        var mutation = old.StepUp.ExecuteOutcomeAsync(actor, context.Binding, oldKey,
            ct => old.Services.GetRequiredService<PasskeyApplication>().ApplyMutationAsync(actor, context, new DefaultHttpContext(), ct), timeout.Token);
        try
        {
            await gate.AccountReached.Task.WaitAsync(timeout.Token); var revoke = current.Owner.RevokeAsync(revoker, request, key, timeout.Token);
            await gate.ContenderAttempted.Task.WaitAsync(timeout.Token); gate.Release.TrySetResult();
            using var result = await mutation; Assert.True(result.Applied && result.SignInRequired); Assert.False((await revoke).Applied); gate.AssertCanonical();
            Assert.Equal("PASSKEY_REVOKED", (await Row(db, actor.SessionId)).TerminationCause);
            Assert.Equal("ACTIVE", (await Row(db, revoker.SessionId)).State);
        }
        finally { gate.Release.TrySetResult(); }
    }

    private sealed class FixedPasskeyPolicy(PasskeyPolicy policy) : IPasskeyPolicySource
    { public PasskeyPolicy RequireEffective(DateTimeOffset effectiveAt) => policy; }

    [Theory]
    [InlineData("DISABLE")]
    [InlineData("RESET")]
    public async Task FactorRevocationActorEffectSerializesWithExactSessionRevoker(string kind)
    {
        await using var db = await fixture.CreateDatabaseAsync(); var gate = new SessionLocksBarrier(); var profile = new TotpTests.Profile();
        profile.Policy = profile.Policy with { DisableSessionEffect = TotpSessionEffect.RevokeFactorSessions, ResetSessionEffect = TotpSessionEffect.RevokeFactorSessions };
        await using var host = await TotpTests.StartTotp(db, profile, s => s.AddDbContext<IdentityPersistenceDbContext>(o => o.AddInterceptors(gate))); await host.SeedAsync(db);
        var revoker = await SessionListingTests.SignIn(host); var enrolled = await TotpTests.Enroll(host, profile, await TotpTests.Login(host));
        profile.RequireMfa = true; var boot = await host.Options();
        using var primary = await host.Post("/authentication/sign-in", Command(boot.Key), boot.Cookies, boot.Csrf); var cookies = Merge(boot.Cookies, primary);
        using var options = await host.Get("/authentication/totp/options", cookies); var view = await options.Content.ReadFromJsonAsync<JsonElement>(); cookies = Merge(cookies, options);
        using var mfa = await host.Post("/authentication/totp/complete", new { requestId = boot.Key, authenticatorReference = enrolled.Reference,
            code = TotpTests.NativeCode(profile.Seed(enrolled.Reference), 1) }, cookies, view.GetProperty("csrfToken").GetString()); Assert.Equal(HttpStatusCode.OK, mfa.StatusCode);
        AuthenticationSessionRow target;
        await using (var read = db.Context()) target = await read.Set<AuthenticationSessionRow>().AsNoTracking().SingleAsync(x => x.AuthenticationMethods == "pwd+totp");
        var actor = await IssuedActor(host, target); UserStepUpOperationContext context; string oldKey;
        await using (var start = new OwnerScope(host, profile))
        {
            var app = start.Services.GetRequiredService<TotpApplication>(); var id = Guid.NewGuid();
            var pending = await app.StartAsync(actor, kind, new TotpStartCommand { RequestId = id, AuthenticatorReference = enrolled.Reference, ExpectedVersion = 2 }, CancellationToken.None);
            Assert.Null(pending.ErrorCode); context = (await app.ResolveAsync(actor, pending.Operation!.EnrollmentId, id, CancellationToken.None))!; Assert.NotNull(context);
        }
        await using (var issue = new OwnerScope(host, profile))
        {
            var proof = await issue.StepUp.IssuePasswordAsync(actor, context.Binding, context.ExpectedPolicyVersion, Password); Assert.NotNull(proof.Grant); oldKey = proof.Grant.Key;
        }
        var request = await Request(db, actor.SessionId); var key = await Issue(host, revoker, request);
        await using var old = new OwnerScope(host, profile); await using var current = new OwnerScope(host);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20)); gate.Arm(old.Database, actor.SessionId);
        var mutation = old.StepUp.ExecuteOutcomeAsync(actor, context.Binding, oldKey,
            ct => old.Services.GetRequiredService<TotpApplication>().ApplyAsync(actor, context, ct), timeout.Token);
        try
        {
            await gate.AccountReached.Task.WaitAsync(timeout.Token); var revoke = current.Owner.RevokeAsync(revoker, request, key, timeout.Token);
            await gate.ContenderAttempted.Task.WaitAsync(timeout.Token); gate.Release.TrySetResult();
            using var result = await mutation; Assert.True(result.Applied && result.SignInRequired); Assert.False((await revoke).Applied); gate.AssertCanonical();
            Assert.Equal("TOTP_DISABLED", (await Row(db, actor.SessionId)).TerminationCause);
            Assert.Equal("ACTIVE", (await Row(db, revoker.SessionId)).State);
        }
        finally { gate.Release.TrySetResult(); }
    }

    [Fact]
    public async Task OrdinaryOwnerStillConsumesItsProofThroughTheExistingCore()
    {
        await using var db = await fixture.CreateDatabaseAsync(); await using var host = await Start(db); await host.SeedAsync(db);
        var actor = await SessionListingTests.SignIn(host); var scope = new AssurancePolicyScope("fixture.ordinary-operation", "fixture.private");
        var operation = new StepUpOperation("OP-" + Guid.NewGuid().ToString("N"), "fixture-correlation", scope, "fixture-resource", 1, new('A', 64));
        await using var issue = new OwnerScope(host, Policies(host, scope));
        var proof = await issue.StepUp.IssuePasswordAsync(actor, operation, "fixture-session-revoke-v1", Password); Assert.NotNull(proof.Grant);
        var calls = 0; await using var execute = new OwnerScope(host, Policies(host, scope));
        using var result = await execute.StepUp.ExecuteAsync(actor, operation, proof.Grant.Key, _ => { calls++; return Task.FromResult(true); });
        Assert.True(result.Applied); Assert.Equal(1, calls);
        using var replay = await execute.StepUp.ExecuteAsync(actor, operation, proof.Grant.Key, _ => throw new InvalidOperationException("An ordinary replay executed."));
        Assert.False(replay.Applied);
    }

    private sealed class SessionLocksBarrier : DbCommandInterceptor
    {
        private Guid owner; private string[] expected = []; private int entered;
        private readonly ConcurrentQueue<string> acquired = new();
        internal TaskCompletionSource AccountReached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource ContenderAttempted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal void Arm(DbContext context, params Guid[] sessions)
        { owner = context.ContextId.InstanceId; expected = sessions.Select(x => "auth-session:" + x.ToString("N")).Distinct().Order(StringComparer.Ordinal).ToArray(); }
        private static string? Session(DbCommand command) => command.CommandText.Contains("pg_advisory_xact_lock", StringComparison.Ordinal)
            ? command.Parameters.Cast<DbParameter>().Select(p => p.Value as string).FirstOrDefault(p => p?.StartsWith("auth-session:", StringComparison.Ordinal) == true) : null;
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData data, InterceptionResult<int> result, CancellationToken ct = default)
        {
            var value = Session(command);
            if (owner != Guid.Empty && data.Context?.ContextId.InstanceId != owner && value is not null && expected.Contains(value)) ContenderAttempted.TrySetResult();
            return ValueTask.FromResult(result);
        }
        public override ValueTask<int> NonQueryExecutedAsync(DbCommand command, CommandExecutedEventData data, int result, CancellationToken ct = default)
        {
            if (data.Context?.ContextId.InstanceId == owner && Session(command) is { } value) acquired.Enqueue(value);
            return ValueTask.FromResult(result);
        }
        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData data,
            InterceptionResult<DbDataReader> result, CancellationToken ct = default)
        {
            if (data.Context?.ContextId.InstanceId == owner && command.CommandText.Contains("FROM accounts.accounts", StringComparison.Ordinal) &&
                command.CommandText.Contains("FOR NO KEY UPDATE", StringComparison.Ordinal) && Interlocked.CompareExchange(ref entered, 1, 0) == 0)
            {
                AssertCanonical(); AccountReached.TrySetResult(); await Release.Task.WaitAsync(ct);
            }
            return result;
        }
        internal void AssertCanonical() => Assert.Equal(expected, acquired.Distinct().ToArray());
    }
}

public sealed class SessionRevocationContractTests
{
    [Fact]
    public void SelfTargetAndInvalidVersionsCannotBecomeOwnerBindings()
    {
        var principal = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity([
            new(System.Security.Claims.ClaimTypes.NameIdentifier, "fixture-account"), new(AuthenticationIdentity.SessionClaim, Guid.NewGuid().ToString("N")),
            new(AuthenticationIdentity.OriginClaim, Origin)], AuthenticationIdentity.Scheme));
        var caller = StepUpCaller.FromPrincipal(principal, Origin, new('B', 64));
        Assert.Throws<ArgumentException>(() => SessionRevocationBinding.Operation(caller, new(Guid.NewGuid(), caller.SessionId, 1, "fixture")));
        Assert.Throws<ArgumentException>(() => SessionRevocationBinding.Operation(caller, new(Guid.NewGuid(), Guid.NewGuid(), long.MaxValue, "fixture")));
        Assert.Throws<ArgumentException>(() => SessionRevocationBinding.Operation(caller, new(Guid.Empty, Guid.NewGuid(), 1, "fixture")));
    }
}
