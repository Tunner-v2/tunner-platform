using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tunner.Database;
using Tunner.Database.Audit;
using Tunner.Database.DurableWork;
using Tunner.Database.Outbox;
using Tunner.Platform.Policies;
using Xunit;
using static Tunner.Identity.Persistence.Tests.AuthenticationTestHost;

namespace Tunner.Identity.Persistence.Tests;

public sealed class AuthenticationAuditCompletionTests(SessionListingPostgresFixture fixture) : IClassFixture<SessionListingPostgresFixture>
{
    [Fact]
    public async Task FailedSharedPasswordProofRetainsActualSessionActorAfterSessionTermination()
    { await using var db = await fixture.CreateDatabaseAsync(); await PasskeyTests.VerifyAuthenticationPostCommit(db, "step-up"); }

    [Theory]
    [InlineData("session")]
    [InlineData("mfa")]
    public async Task VerifiedPasskeyAuditDistinguishesOriginalSessionFromPendingMfa(string outcome)
    { await using var db = await fixture.CreateDatabaseAsync(); await PasskeyTests.VerifyAuthenticationPostCommit(db, outcome); }

    [Theory]
    [InlineData("sign-in")]
    [InlineData("enrollment")]
    public async Task RejectedNativeCeremonyRetainsOnlyItsActualAuthenticatedActor(string kind)
    { await using var db = await fixture.CreateDatabaseAsync(); await PasskeyTests.VerifyAuthenticationPostCommit(db, kind); }
}

public sealed partial class PasskeyTests
{
    internal static async Task VerifyAuthenticationPostCommit(IdentityTestDatabase db, string scenario)
    {
        var totp = scenario == "mfa" ? new TotpTests.Profile() : null;
        await using var host = await StartPasskeys(db, configure: services =>
        {
            if (totp is null) return;
            services.AddSingleton<ITotpPolicySource>(totp); services.AddSingleton<ITotpSeedStore>(totp); services.AddSingleton<ITotpMutationEligibilitySource>(totp);
            services.AddSingleton<ITotpMfaRequirementSource>(totp); services.AddSingleton<ITotpPresentationSource>(totp); services.AddSingleton<IStepUpPresentationSource>(totp);
            services.AddSingleton<IAssurancePolicySource>(new TotpAndPasskeyAssurance(totp));
        });
        // The plain fixture's qualified assurance catalog is anchored to TestClock's fixed instant.
        // TOTP alone uses the existing execution-time assurance adapter and requires the native system clock.
        if (totp is not null) host.Clock.SynchronizeToSystem();
        var account = await host.SeedAsync(db); using var key = new PasskeyAuthenticator();
        var cookies = await Login(host); string eventType; Guid ceremonyId = Guid.Empty;
        if (scenario == "step-up")
        {
            var pending = await Begin(host, cookies);
            using var options = await host.Get("/authentication/step-up/options", pending.Cookies); Assert.Equal(HttpStatusCode.OK, options.StatusCode);
            var body = await options.Content.ReadFromJsonAsync<JsonElement>();
            using var failed = await host.Post("/authentication/step-up/password", new { requestId = body.GetProperty("requestId").GetGuid(), password = "fixture incorrect password" },
                Merge(pending.Cookies, options), body.GetProperty("csrfToken").GetString());
            Assert.Equal(HttpStatusCode.Unauthorized, failed.StatusCode); eventType = "StepUpAuthenticationRejected";
        }
        else if (scenario == "enrollment")
        {
            var pending = await Authorize(host, await Begin(host, cookies)); var enrollment = await EnrollmentOptions(host, pending); ceremonyId = pending.CeremonyId;
            using var failed = await host.Post("/account/passkeys/enrollment/complete", new { requestId = pending.RequestId, ceremonyId,
                credential = key.Attest(enrollment.Options, "https://foreign.example.test") }, pending.Cookies, pending.Csrf);
            Assert.Equal(HttpStatusCode.Unauthorized, failed.StatusCode); eventType = "PasskeyCeremonyRejected";
        }
        else
        {
            var enrolled = await Enroll(host, cookies, key);
            if (totp is not null) { _ = await TotpTests.Enroll(host, totp, enrolled.Pending.Cookies); totp.RequireMfa = true; }
            var assertion = await AssertionOptions(host); ceremonyId = assertion.Ceremony.GetProperty("ceremonyId").GetGuid();
            using var result = await host.Post("/authentication/passkeys/sign-in", new { requestId = assertion.Bootstrap.Key, ceremonyId,
                credential = key.Assert(assertion.Ceremony.GetProperty("publicKey"), Origin, badSignature: scenario == "sign-in") }, assertion.Bootstrap.Cookies, assertion.Bootstrap.Csrf);
            Assert.Equal(scenario == "sign-in" ? HttpStatusCode.Unauthorized : HttpStatusCode.OK, result.StatusCode);
            if (scenario == "mfa")
            {
                Assert.Equal("MFA_REQUIRED", (await result.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetString());
                await using var read = db.Context(); Assert.False(await read.Set<AuthenticationSessionRow>().AnyAsync(x => x.AuthenticationMethods == "passkey"));
            }
            eventType = scenario == "sign-in" ? "PasskeyCeremonyRejected" : "PasskeyAuthenticated";
            if (scenario == "session") cookies = Merge(assertion.Bootstrap.Cookies, result);
        }
        var identity = new AuthenticationAuditFactory<IdentityPersistenceDbContext>(db.Context);
        var platform = new AuthenticationAuditFactory<TunnerMigrationsDbContext>(() => new(new DbContextOptionsBuilder<TunnerMigrationsDbContext>().UseNpgsql(db.ConnectionString).Options));
        PendingOutboxMessage message;
        await using (var read = db.Context()) message = IdentityPostCommitProcessor.Project(await read.Set<OutboxMessage>().AsNoTracking().SingleAsync(x => x.EventType == eventType));
        var profile = new CompletionRejectionProfile(message.OccurredAtUtc);
        IIdentityPostCommitOwnerSource owner = scenario == "step-up" ? new AuthenticationRejectionPostCommitOwnerSource(identity, profile) : new IdentitySecurityPostCommitOwnerSource(identity);
        var original = Assert.IsType<IdentityPostCommitOwnerFacts>(await owner.ReadCommittedAsync(message, default));
        Assert.Equal(scenario == "sign-in" ? null : account, original.ActorAccountId);
        if (scenario is "session" or "mfa")
        {
            Assert.Equal("true", original.References["primaryFactorVerified"]);
            Assert.Equal(scenario == "session" ? "true" : "false", original.References["sessionIssued"]);
            if (scenario == "mfa") { Assert.Null(original.SessionReference); Assert.Contains("originalPendingMfaReference", original.References.Keys); }
        }
        else if (scenario == "sign-in") Assert.Null(original.SessionReference);
        else Assert.NotNull(original.SessionReference);
        // Real session retirement clears protected ticket/key and increments its version, without erasing historical identity.
        if (scenario is "session" or "step-up" or "enrollment")
        {
            using var session = await host.Get("/authentication/session", cookies); Assert.Equal(HttpStatusCode.OK, session.StatusCode);
            var view = await session.Content.ReadFromJsonAsync<JsonElement>();
            using var ended = await host.Post("/authentication/sign-out", new { }, Merge(cookies, session), view.GetProperty("csrfToken").GetString());
            Assert.Equal(HttpStatusCode.OK, ended.StatusCode);
            await using var read = db.Context(); var retired = await read.Set<AuthenticationSessionRow>().SingleAsync(x => x.SessionId == Guid.ParseExact(original.SessionReference!, "N"));
            Assert.Equal("REVOKED", retired.State); Assert.True(retired.Version > 1); Assert.Empty(retired.ProtectedTicket); Assert.Empty(retired.ProtectedKey);
        }
        host.Clock.Advance(TimeSpan.FromMinutes(3));
        var retained = Assert.IsType<IdentityPostCommitOwnerFacts>(await owner.ReadCommittedAsync(message, default));
        Assert.Equal(JsonSerializer.Serialize(original), JsonSerializer.Serialize(retained));
        Assert.Null(await owner.ReadCommittedAsync(message with { CorrelationId = "fixture-unrelated-correlation" }, default));
        if (scenario == "step-up") { profile.Available = false; Assert.Null(await owner.ReadCommittedAsync(message, default)); profile.Available = true; }
        if (scenario != "step-up")
        {
            await using var read = db.Context(); var ceremony = await read.Set<PasskeyCeremonyRow>().SingleAsync(x => x.CeremonyId == ceremonyId);
            Assert.Empty(ceremony.ProtectedState); Assert.Empty(ceremony.OptionsJson);
            Assert.True(ceremony.EndedAt <= message.OccurredAtUtc);
        }
        var contracts = new CompletionAuditContracts();
        var processor = new IdentityPostCommitProcessor(identity, platform, new EfConsumerInboxStore(platform), [owner], contracts,
            new("fixture-authentication-completion", "fixture-generation-v1", TimeSpan.FromMinutes(1)), host.Clock);
        Assert.Equal(IdentityPostCommitOutcome.Completed, await processor.ProcessAsync(message));
        Assert.Equal(IdentityPostCommitOutcome.Completed, await processor.ProcessAsync(message));
        await using var stored = platform.CreateDbContext(); var audit = Assert.Single(await stored.AuditEvents.ToListAsync()); audit.RequireIntegrity();
        using var json = JsonDocument.Parse(audit.Envelope);
        Assert.Equal(scenario == "sign-in" ? "fixture-anonymous" : account, json.RootElement.GetProperty("actor").GetProperty("principal_id").GetString());
        Assert.Equal(message.OperationId, audit.OperationId); Assert.Equal(message.CorrelationId, audit.CorrelationId);
        Assert.NotNull((await stored.ConsumerInboxEntries.SingleAsync()).CompletedAtUtc);
        foreach (var secret in new[] { Password, "fixture incorrect password", "person@example.test", "clientDataJSON", "ProtectedState", "CredentialJson" })
            Assert.DoesNotContain(secret, audit.Envelope + JsonSerializer.Serialize(retained), StringComparison.OrdinalIgnoreCase);
    }

    private sealed class CompletionRejectionProfile(DateTimeOffset at) : IAuthenticationRejectionAuditProfileSource
    {
        internal bool Available = true;
        public AuthenticationRejectionAuditProfile? RequireEffective(DateTimeOffset eventOccurredAt) => !Available ? null : new(
            new("fixture-step-up-rejection-audit", "ACTIVE", AuthenticationRejectionAuditProfile.Scope,
                new(at.AddMinutes(-1), true, at.AddMinutes(1), false), null, ["fixture-rejection-approval"]), "fixture-processing", "fixture-retention", "RESTRICTED_SECURITY");
    }
    private sealed class CompletionAuditContracts : IIdentityPostCommitContractSource
    {
        public ValueTask<IdentityPostCommitQualification?> RequireCurrentAsync(IdentityPostCommitFacts facts, DateTimeOffset at, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested(); var e = facts.Event;
            var audit = new AuditEvent(e.EventId, e.OccurredAtUtc, new(facts.Owner.ActorAccountId ?? "fixture-anonymous", "fixture-actor",
                facts.Owner.SessionReference is { } session ? [session] : []), "fixture-authentication-observation", e.OperationId!, e.CorrelationId!,
                [new(e.Subject, e.ResourceVersion)], "fixture-recorded", e.DataClassification, facts.Owner.RetentionReference, e.EventId.ToString("N"),
                policyVersionReferences: ["fixture-audit-contract"], evidenceReferences: ["fixture-audit-approval"]);
            return ValueTask.FromResult<IdentityPostCommitQualification?>(new(e.EventId, facts.Digest, "fixture-audit-contract", "fixture-audit-approval",
                new(at.AddMinutes(-1), true, at.AddMinutes(1), false), audit));
        }
    }
    private sealed class AuthenticationAuditFactory<T>(Func<T> create) : IDbContextFactory<T> where T : DbContext
    {
        public T CreateDbContext() => create();
        public Task<T> CreateDbContextAsync(CancellationToken cancellationToken = default)
        { cancellationToken.ThrowIfCancellationRequested(); return Task.FromResult(CreateDbContext()); }
    }
}
