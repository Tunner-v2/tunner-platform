using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Tunner.Database.Outbox;
using Tunner.Platform.Policies;
using Xunit;
using static Tunner.Identity.Persistence.Tests.AuthenticationTestHost;

namespace Tunner.Identity.Persistence.Tests;

public sealed partial class PasskeyTests(IdentityPostgresFixture fixture) : IClassFixture<IdentityPostgresFixture>
{
    // Recovery's finite cohort reuses the existing real native browser enrollment/sign-in fixtures.
    internal static Task<AuthenticationTestHost> StartRecoveryFixtureAsync(IdentityTestDatabase db, Action<IServiceCollection> configure) => StartPasskeys(db, configure: configure);
    internal static async Task<(Guid Reference, string Cookies)> SeedRecoveryCredentialAsync(AuthenticationTestHost host, PasskeyAuthenticator key,
        RecoveryCodeFirstCaseCheckpoint? checkpoint = null)
    { var enrolled = await Enroll(host, await Login(host, checkpoint), key, checkpoint); return (enrolled.Reference, enrolled.Pending.Cookies); }
    internal static Task<string> SeedRecoveryCounterAsync(AuthenticationTestHost host, PasskeyAuthenticator key) => SignInPasskey(host, key);

    private sealed class Policies : IPasskeyPolicySource
    {
        internal string Version { get; set; } = "fixture-passkey-v1";
        internal PasskeyRevocationSessionEffect Effect { get; set; } = PasskeyRevocationSessionEffect.PreserveExistingSessions;
        internal PasskeyPolicy Value => new(Version, "authentication.example.test", Origin, TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(2), Effect, "required", new[] { -7 }, 32, 32768, 4, 4,
            "fixture-enrollment-v1", "fixture-revocation-v1", "fixture-processing", "fixture-retention", "required", "none",
            new Dictionary<string, string> { [PasskeyPolicy.AssuranceKey(false, false)] = "fixture-passkey-assurance",
                [PasskeyPolicy.AssuranceKey(true, false)] = "fixture-syncable-unbacked-assurance", [PasskeyPolicy.AssuranceKey(true, true)] = "fixture-syncable-backed-assurance" });
        public PasskeyPolicy RequireEffective(DateTimeOffset effectiveAt) => Value;
    }
    private sealed class Eligibility : IPasskeyRevocationEligibilitySource
    {
        internal bool Allowed { get; set; } = true;
        internal bool Stale { get; set; }
        internal TimeSpan Validity { get; set; } = TimeSpan.FromHours(1);
        public ValueTask<PasskeyRevocationEligibilityDecision> RequireAsync(PasskeyRevocationEligibilityRequest request, CancellationToken cancellationToken) => ValueTask.FromResult(
            new PasskeyRevocationEligibilityDecision(request.AccountId, request.AccountVersion, request.CredentialReference, request.CredentialVersion,
                Stale ? new string('0', 64) : request.AlternativeMethodsDigest, Allowed, "fixture-path-policy", "fixture-path-evidence",
                new(request.EffectiveAt.AddHours(-1), true, request.EffectiveAt + Validity, false)));
    }
    private sealed class Presentations : IPasskeyPresentationSource, IStepUpPresentationSource
    {
        public PasskeyPresentation RequirePublished(PasskeyPolicy policy)
        {
            using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "passkeys-content.proposal.json")));
            return new("en", document.RootElement.GetProperty("content").EnumerateObject().Select(x => new RegistrationPublishedText(x.Name, "fixture", "fixture-v1", "fixture-only-publication", "en", "PUBLISHED", x.Value.GetString()!)));
        }
        public StepUpPresentation RequirePublished(AssurancePolicyVersion policy)
        {
            using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "step-up-content.proposal.json")));
            return new("en", document.RootElement.GetProperty("content").EnumerateObject().Select(x => new RegistrationPublishedText(x.Name, "fixture", "fixture-v1", "fixture-publication", "en", "PUBLISHED", x.Value.GetString()!)));
        }
    }
    private static async Task<AuthenticationTestHost> StartPasskeys(IdentityTestDatabase db, Policies? policies = null, Eligibility? eligibility = null, Action<IServiceCollection>? configure = null, bool passkeyProof = false)
    {
        var now = new TestClock().GetUtcNow();
        AssurancePolicyVersion Policy(string version, AssurancePolicyScope scope) => new(new(version, "ACTIVE", scope, new(now.AddHours(-1), true, now.AddHours(1), false), null, ["synthetic-test-only"]),
            "fixture-password", TimeSpan.FromSeconds(45), new Dictionary<string, string> { ["pwd"] = "fixture-password",
                [PasskeyPolicy.MethodKey(false, false)] = passkeyProof ? "fixture-password" : "fixture-insufficient",
                [PasskeyPolicy.MethodKey(true, false)] = passkeyProof ? "fixture-password" : "fixture-insufficient",
                [PasskeyPolicy.MethodKey(true, true)] = passkeyProof ? "fixture-password" : "fixture-insufficient" });
        return await Start(db, services =>
        {
            services.AddSingleton<IPasskeyPolicySource>(policies ?? new()); services.AddSingleton<IPasskeyRevocationEligibilitySource>(eligibility ?? new());
            services.AddSingleton<IPasskeyPresentationSource, Presentations>(); services.AddSingleton<IStepUpPresentationSource, Presentations>();
            services.AddSingleton<IAssurancePolicySource>(new AssurancePolicyCatalog([
                Policy("fixture-enrollment-v1", PasskeyApplication.EnrollmentScope), Policy("fixture-revocation-v1", PasskeyApplication.RevocationScope)]));
            configure?.Invoke(services);
        });
    }
    private static async Task<string> Login(AuthenticationTestHost host, RecoveryCodeFirstCaseCheckpoint? checkpoint = null)
    {
        if (checkpoint is not null) checkpoint.Phase = RecoveryCodeFirstCasePhase.LOGIN;
        checkpoint?.Mark(RecoveryCodeFirstCaseStage.OPTIONS_GET);
        var boot = await host.Options();
        checkpoint?.Mark(RecoveryCodeFirstCaseStage.SIGN_IN_POST);
        using var response = await host.Post("/authentication/sign-in", Command(boot.Key), boot.Cookies, boot.Csrf);
        checkpoint?.Mark(RecoveryCodeFirstCaseStage.SIGN_IN_STATUS);
        response.EnsureSuccessStatusCode(); return Merge(boot.Cookies, response);
    }
    private sealed record Pending(Guid RequestId, Guid CeremonyId, string Cookies, string Csrf, string? ResultHandle = null, bool SignInRequired = false);
    private static async Task<Pending> Begin(AuthenticationTestHost host, string cookies, Guid? credential = null, long expectedVersion = 0)
    {
        using var options = await host.Get("/account/passkeys/options", cookies); options.EnsureSuccessStatusCode();
        var bootstrap = await options.Content.ReadFromJsonAsync<JsonElement>(); cookies = Merge(cookies, options);
        var id = bootstrap.GetProperty("requestId").GetGuid(); var csrf = bootstrap.GetProperty("csrfToken").GetString()!;
        using var response = credential is null ? await host.Post("/account/passkeys/enrollment/start", new { requestId = id }, cookies, csrf) :
            await host.Post("/account/passkeys/revocation/start", new { requestId = id, credentialReference = credential, expectedVersion }, cookies, csrf);
        response.EnsureSuccessStatusCode(); var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        return new(id, json.GetProperty("ceremonyId").GetGuid(), Merge(cookies, response), csrf, json.GetProperty("resultHandle").GetString());
    }
    private static async Task<Pending> Authorize(AuthenticationTestHost host, Pending pending, RecoveryCodeFirstCaseCheckpoint? checkpoint = null)
    {
        if (checkpoint is not null) checkpoint.Phase = RecoveryCodeFirstCasePhase.GENERATE;
        checkpoint?.Mark(RecoveryCodeFirstCaseStage.PROOF_OPTIONS_GET);
        using var options = await host.Get("/authentication/step-up/options", pending.Cookies);
        checkpoint?.Mark(RecoveryCodeFirstCaseStage.PROOF_OPTIONS_STATUS);
        options.EnsureSuccessStatusCode();
        checkpoint?.Mark(RecoveryCodeFirstCaseStage.PROOF_OPTIONS_BODY);
        var json = await options.Content.ReadFromJsonAsync<JsonElement>();
        checkpoint?.Mark(RecoveryCodeFirstCaseStage.PROOF_BINDING);
        Assert.Equal(pending.CeremonyId.ToString("N"), json.GetProperty("operationReference").GetString()); var cookies = Merge(pending.Cookies, options);
        var csrf = json.GetProperty("csrfToken").GetString()!;
        checkpoint?.Mark(RecoveryCodeFirstCaseStage.PASSWORD_PROOF_POST);
        using var applied = await host.Post("/authentication/step-up/password", new { requestId = json.GetProperty("requestId").GetGuid(), password = Password }, cookies, csrf);
        checkpoint?.Mark(RecoveryCodeFirstCaseStage.PROOF_STATUS);
        Assert.True(applied.IsSuccessStatusCode, await applied.Content.ReadAsStringAsync());
        checkpoint?.Mark(RecoveryCodeFirstCaseStage.RESULT_COOKIE);
        return pending with { Cookies = Merge(cookies, applied), Csrf = csrf, SignInRequired = (await applied.Content.ReadFromJsonAsync<JsonElement>()).TryGetProperty("signInRequired", out var ended) && ended.GetBoolean() };
    }
    private static async Task<(Pending Pending, JsonElement Options)> EnrollmentOptions(AuthenticationTestHost host, Pending pending)
    {
        using var response = await host.Post("/account/passkeys/enrollment/options", new { requestId = pending.RequestId, ceremonyId = pending.CeremonyId }, pending.Cookies, pending.Csrf); response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(); Assert.Equal(pending.RequestId, json.GetProperty("requestId").GetGuid());
        Assert.Equal(pending.CeremonyId, json.GetProperty("ceremonyId").GetGuid()); return (pending, json.GetProperty("publicKey").Clone());
    }
    private static async Task<(Pending Pending, Guid Reference)> Enroll(AuthenticationTestHost host, string cookies, PasskeyAuthenticator authenticator,
        RecoveryCodeFirstCaseCheckpoint? checkpoint = null)
    {
        if (checkpoint is not null) checkpoint.Phase = RecoveryCodeFirstCasePhase.GENERATE;
        checkpoint?.Mark(RecoveryCodeFirstCaseStage.START_POST);
        var started = await Begin(host, cookies);
        var pending = await Authorize(host, started, checkpoint);
        if (checkpoint is not null) checkpoint.Phase = RecoveryCodeFirstCasePhase.SAFE_READ;
        checkpoint?.Mark(RecoveryCodeFirstCaseStage.PROOF_OPTIONS_GET);
        var ceremony = await EnrollmentOptions(host, pending);
        checkpoint?.Mark(RecoveryCodeFirstCaseStage.RESULT_POST);
        using var completed = await host.Post("/account/passkeys/enrollment/complete", new { requestId = pending.RequestId, ceremonyId = pending.CeremonyId,
            credential = authenticator.Attest(ceremony.Options, Origin) }, pending.Cookies, pending.Csrf);
        checkpoint?.Mark(RecoveryCodeFirstCaseStage.RESULT_STATUS);
        Assert.True(completed.IsSuccessStatusCode, await completed.Content.ReadAsStringAsync());
        checkpoint?.Mark(RecoveryCodeFirstCaseStage.RESULT_BODY);
        var json = await completed.Content.ReadFromJsonAsync<JsonElement>();
        return (pending with { Cookies = Merge(pending.Cookies, completed) }, json.GetProperty("credentialReference").GetGuid());
    }
    private static async Task<(Bootstrap Bootstrap, JsonElement Ceremony)> AssertionOptions(AuthenticationTestHost host)
    {
        var boot = await host.Options(); using var response = await host.Post("/authentication/passkeys/options", new { requestId = boot.Key, rememberMe = false }, boot.Cookies, boot.Csrf);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync()); return (boot with { Cookies = Merge(boot.Cookies, response) }, await response.Content.ReadFromJsonAsync<JsonElement>());
    }
    [Fact]
    public async Task NativeEnrollmentRemainsInactiveUntilVerifiedAndSessionUsesConfiguredVerifiedAssurance()
    {
        await using var db = await fixture.CreateDatabaseAsync(); await using var host = await StartPasskeys(db); await host.SeedAsync(db, minor: true);
        var cookies = await Login(host); var pending = await Begin(host, cookies);
        await using (var read = db.Context()) { Assert.Empty(await read.Set<PasskeyCredentialRow>().ToArrayAsync()); Assert.Equal(0, await read.Database.SqlQueryRaw<int>("SELECT count(*)::int AS \"Value\" FROM identity.\"AspNetUserPasskeys\"").SingleAsync()); }
        using var authenticator = new PasskeyAuthenticator(); pending = await Authorize(host, pending); var ceremony = await EnrollmentOptions(host, pending);
        var credential = authenticator.Attest(ceremony.Options, Origin);
        using var completed = await host.Post("/account/passkeys/enrollment/complete", new { requestId = pending.RequestId, ceremonyId = pending.CeremonyId, credential }, pending.Cookies, pending.Csrf);
        Assert.True(completed.IsSuccessStatusCode, await completed.Content.ReadAsStringAsync());
        var assertion = await AssertionOptions(host); var proof = authenticator.Assert(assertion.Ceremony.GetProperty("publicKey"), Origin);
        var command = new { requestId = assertion.Bootstrap.Key, ceremonyId = assertion.Ceremony.GetProperty("ceremonyId").GetGuid(), credential = proof };
        using var signed = await host.Post("/authentication/passkeys/sign-in", command, assertion.Bootstrap.Cookies, assertion.Bootstrap.Csrf);
        Assert.True(signed.IsSuccessStatusCode, await signed.Content.ReadAsStringAsync()); var sessionCookies = Merge(assertion.Bootstrap.Cookies, signed);
        using var live = await host.Get("/authentication/session", sessionCookies); Assert.Equal(HttpStatusCode.OK, live.StatusCode);
        using var retried = await host.Post("/authentication/passkeys/sign-in", command, assertion.Bootstrap.Cookies, assertion.Bootstrap.Csrf); Assert.Equal(HttpStatusCode.OK, retried.StatusCode);
        await using var context = db.Context(); var sessions = await context.Set<AuthenticationSessionRow>().Where(x => x.AuthenticationMethods == "passkey").ToArrayAsync();
        Assert.Single(sessions); Assert.Equal("fixture-passkey-assurance", sessions[0].Assurance); Assert.NotNull(sessions[0].PasskeyCredentialReference);
        Assert.Equal(1, await context.Set<PasskeyCredentialRow>().Select(x => x.Version).SingleAsync());
        Assert.Equal(1, await context.Set<PasskeyCredentialRow>().Select(x => x.UsageVersion).SingleAsync());
        var events = await context.Set<OutboxMessage>().Where(x => x.EventType == "AuthenticatorAdded" || x.EventType == "PasskeyAuthenticated").ToArrayAsync(); Assert.Equal(2, events.Length);
        foreach (var item in events) { Assert.DoesNotContain(Password, item.Payload); Assert.DoesNotContain("person@example.test", item.Payload); Assert.DoesNotContain("clientDataJSON", item.Payload); }
    }
    [Theory]
    [InlineData("origin")][InlineData("rp")][InlineData("challenge")][InlineData("type")][InlineData("signature")][InlineData("uv")][InlineData("backup")][InlineData("up")][InlineData("userHandle")]
    public async Task NativeAssertionRejectsInvalidSignedCeremoniesAndConsumesOriginalChallenge(string failure)
    {
        await using var db = await fixture.CreateDatabaseAsync(); await using var host = await StartPasskeys(db); await host.SeedAsync(db);
        using var authenticator = new PasskeyAuthenticator(); await Enroll(host, await Login(host), authenticator);
        var assertion = await AssertionOptions(host); var options = assertion.Ceremony.GetProperty("publicKey");
        var credential = authenticator.Assert(options, failure == "origin" ? "https://other.example.test" : Origin,
            wrongRp: failure == "rp" ? "other.example.test" : null, challenge: failure == "challenge" ? "AAAAAAAAAAAAAAAAAAAAAA" : null,
            type: failure == "type" ? "webauthn.create" : "webauthn.get", uv: failure != "uv", badSignature: failure == "signature", backup: failure == "backup", up: failure != "up", wrongUserHandle: failure == "userHandle" ? "Zm9yZWlnbi11c2Vy" : null);
        var id = assertion.Ceremony.GetProperty("ceremonyId").GetGuid();
        using var response = await host.Post("/authentication/passkeys/sign-in", new { requestId = assertion.Bootstrap.Key, ceremonyId = id, credential }, assertion.Bootstrap.Cookies, assertion.Bootstrap.Csrf);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        using var replay = await host.Post("/authentication/passkeys/sign-in", new { requestId = assertion.Bootstrap.Key, ceremonyId = id, credential = authenticator.Assert(options, Origin) }, assertion.Bootstrap.Cookies, assertion.Bootstrap.Csrf);
        Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);
        await using var context = db.Context(); var row = await context.Set<PasskeyCeremonyRow>().SingleAsync(x => x.CeremonyId == id);
        Assert.Equal("REJECTED", row.State); Assert.Empty(row.ProtectedState); Assert.Empty(await context.Set<AuthenticationSessionRow>().Where(x => x.AuthenticationMethods == "passkey").ToArrayAsync());
    }
    [Fact]
    public async Task InvalidEnrollmentCannotActivateCredentialAndChangedPolicyCannotCompleteOldCeremony()
    {
        await using var db = await fixture.CreateDatabaseAsync(); var policies = new Policies(); await using var host = await StartPasskeys(db, policies); await host.SeedAsync(db);
        using var authenticator = new PasskeyAuthenticator(); var pending = await Authorize(host, await Begin(host, await Login(host))); var ceremony = await EnrollmentOptions(host, pending);
        policies.Version = "fixture-passkey-v2";
        using var stale = await host.Post("/account/passkeys/enrollment/complete", new { requestId = pending.RequestId, ceremonyId = pending.CeremonyId,
            credential = authenticator.Attest(ceremony.Options, Origin) }, pending.Cookies, pending.Csrf); Assert.Equal(HttpStatusCode.Forbidden, stale.StatusCode);
        await using var context = db.Context(); Assert.Empty(await context.Set<PasskeyCredentialRow>().ToArrayAsync());
    }
    [Fact]
    public async Task RevocationUsesExactSharedProofAndVersionAndCannotBeBypassedOrDuplicated()
    {
        await using var db = await fixture.CreateDatabaseAsync(); var eligibility = new Eligibility(); await using var host = await StartPasskeys(db, eligibility: eligibility); await host.SeedAsync(db);
        using var authenticator = new PasskeyAuthenticator(); var enrolled = await Enroll(host, await Login(host), authenticator);
        var pending = await Begin(host, enrolled.Pending.Cookies, enrolled.Reference, 1);
        using (var before = await host.Post("/account/passkeys/result", new { requestId = pending.RequestId, ceremonyId = pending.CeremonyId }, pending.Cookies, pending.Csrf))
            Assert.Equal("PENDING", (await before.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetString());
        pending = await Authorize(host, pending);
        using var reconciled = await host.Post("/account/passkeys/result", new { requestId = pending.RequestId, ceremonyId = pending.CeremonyId }, pending.Cookies, pending.Csrf);
        Assert.Equal("COMPLETED", (await reconciled.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetString());
        await using var context = db.Context(); var row = await context.Set<PasskeyCredentialRow>().SingleAsync(); Assert.Equal("REVOKED", row.State); Assert.Equal(2, row.Version);
        Assert.Equal(1, await context.Set<OutboxMessage>().CountAsync(x => x.EventType == "AuthenticatorRevoked"));
        var assertion = await AssertionOptions(host);
        using var denied = await host.Post("/authentication/passkeys/sign-in", new { requestId = assertion.Bootstrap.Key, ceremonyId = assertion.Ceremony.GetProperty("ceremonyId").GetGuid(),
            credential = authenticator.Assert(assertion.Ceremony.GetProperty("publicKey"), Origin) }, assertion.Bootstrap.Cookies, assertion.Bootstrap.Csrf);
        Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
    }
    [Fact]
    public async Task ForeignSessionAndStaleAlternativeMethodAuthorityCannotAuthorizeRevocation()
    {
        await using var db = await fixture.CreateDatabaseAsync(); var eligibility = new Eligibility { Stale = true }; await using var host = await StartPasskeys(db, eligibility: eligibility); await host.SeedAsync(db);
        using var authenticator = new PasskeyAuthenticator(); var enrolled = await Enroll(host, await Login(host), authenticator);
        var pending = await Begin(host, enrolled.Pending.Cookies, enrolled.Reference, 1);
        using var options = await host.Get("/authentication/step-up/options", pending.Cookies); var json = await options.Content.ReadFromJsonAsync<JsonElement>();
        using var rejected = await host.Post("/authentication/step-up/password", new { requestId = json.GetProperty("requestId").GetGuid(), password = Password }, Merge(pending.Cookies, options), json.GetProperty("csrfToken").GetString());
        Assert.Equal(HttpStatusCode.ServiceUnavailable, rejected.StatusCode);
        var other = await Login(host); using var boot = await host.Get("/account/passkeys/options", other); var bootstrap = await boot.Content.ReadFromJsonAsync<JsonElement>();
        using var foreign = await host.Post("/account/passkeys/result", new { requestId = pending.RequestId, ceremonyId = pending.CeremonyId }, Merge(other, boot), bootstrap.GetProperty("csrfToken").GetString());
        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        await using var context = db.Context(); Assert.Equal("ACTIVE", await context.Set<PasskeyCredentialRow>().Select(x => x.State).SingleAsync());
        Assert.Equal("ACTIVE", await context.Set<StepUpProofRow>().Where(x => x.Operation == "REVOKE_PASSKEY").Select(x => x.State).SingleAsync());
    }
    private static async Task<string> SignInPasskey(AuthenticationTestHost host, PasskeyAuthenticator authenticator, bool backupEligible = false, bool backedUp = false, uint? counterOverride = null)
    {
        var assertion = await AssertionOptions(host);
        using var signed = await host.Post("/authentication/passkeys/sign-in", new { requestId = assertion.Bootstrap.Key, ceremonyId = assertion.Ceremony.GetProperty("ceremonyId").GetGuid(),
            credential = authenticator.Assert(assertion.Ceremony.GetProperty("publicKey"), Origin, backupEligible: backupEligible, backedUp: backedUp, counterOverride: counterOverride) }, assertion.Bootstrap.Cookies, assertion.Bootstrap.Csrf);
        Assert.True(signed.IsSuccessStatusCode, await signed.Content.ReadAsStringAsync()); return Merge(assertion.Bootstrap.Cookies, signed);
    }
    [Theory]
    [InlineData(PasskeyRevocationSessionEffect.PreserveExistingSessions)]
    [InlineData(PasskeyRevocationSessionEffect.RevokeCredentialSessions)]
    public async Task ApprovedSessionEffectIsExplicitAndTerminalOriginalResultSurvivesLostResponseHeaders(PasskeyRevocationSessionEffect effect)
    {
        await using var db = await fixture.CreateDatabaseAsync(); var policies = new Policies { Effect = effect }; await using var host = await StartPasskeys(db, policies); await host.SeedAsync(db);
        using var authenticator = new PasskeyAuthenticator(); var enrolled = await Enroll(host, await Login(host), authenticator);
        var first = await SignInPasskey(host, authenticator); var second = await SignInPasskey(host, authenticator);
        var pending = await Begin(host, first, enrolled.Reference, 1); var original = pending; pending = await Authorize(host, pending);
        Assert.Equal(effect == PasskeyRevocationSessionEffect.RevokeCredentialSessions, pending.SignInRequired);
        using var firstLive = await host.Get("/authentication/session", first); using var secondLive = await host.Get("/authentication/session", second);
        var expected = effect == PasskeyRevocationSessionEffect.RevokeCredentialSessions ? HttpStatusCode.Unauthorized : HttpStatusCode.OK;
        Assert.Equal(expected, firstLive.StatusCode); Assert.Equal(expected, secondLive.StatusCode);
        using var passwordLive = await host.Get("/authentication/session", enrolled.Pending.Cookies); Assert.Equal(HttpStatusCode.OK, passwordLive.StatusCode);
        if (effect == PasskeyRevocationSessionEffect.RevokeCredentialSessions)
        {
            // No terminal response headers/body are retained. The handle was already issued with the exact original start.
            var anonymous = await host.Options(original.Cookies);
            using var recovered = await host.Post("/account/passkeys/terminal-result", new { requestId = original.RequestId, ceremonyId = original.CeremonyId, resultHandle = original.ResultHandle }, anonymous.Cookies, anonymous.Csrf);
            Assert.True(recovered.IsSuccessStatusCode, await recovered.Content.ReadAsStringAsync());
            var result = await recovered.Content.ReadFromJsonAsync<JsonElement>(); Assert.Equal("COMPLETED", result.GetProperty("status").GetString()); Assert.True(result.GetProperty("signInRequired").GetBoolean());
            using var wrong = await host.Post("/account/passkeys/terminal-result", new { requestId = Guid.NewGuid(), ceremonyId = original.CeremonyId, resultHandle = original.ResultHandle }, anonymous.Cookies, anonymous.Csrf);
            Assert.Equal(HttpStatusCode.NotFound, wrong.StatusCode);
        }
        await using var context = db.Context(); var credential = await context.Set<PasskeyCredentialRow>().SingleAsync(); Assert.Equal(effect.ToString(), credential.RevocationSessionEffect);
        Assert.Equal(1, await context.Set<OutboxMessage>().CountAsync(x => x.EventType == "AuthenticatorRevoked"));
    }
    [Fact]
    public async Task ConcurrentNativeCompletionAndAssertionReconcileWithoutDuplicateCredentialOrSession()
    {
        await using var db = await fixture.CreateDatabaseAsync(); await using var host = await StartPasskeys(db); await host.SeedAsync(db);
        using var authenticator = new PasskeyAuthenticator(); var pending = await Authorize(host, await Begin(host, await Login(host))); var options = await EnrollmentOptions(host, pending);
        var command = new { requestId = pending.RequestId, ceremonyId = pending.CeremonyId, credential = authenticator.Attest(options.Options, Origin) };
        var completed = await Task.WhenAll(host.Post("/account/passkeys/enrollment/complete", command, pending.Cookies, pending.Csrf), host.Post("/account/passkeys/enrollment/complete", command, pending.Cookies, pending.Csrf));
        try { Assert.All(completed, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode)); } finally { foreach (var r in completed) r.Dispose(); }
        var assertion = await AssertionOptions(host); var signedCommand = new { requestId = assertion.Bootstrap.Key, ceremonyId = assertion.Ceremony.GetProperty("ceremonyId").GetGuid(), credential = authenticator.Assert(assertion.Ceremony.GetProperty("publicKey"), Origin) };
        var signed = await Task.WhenAll(host.Post("/authentication/passkeys/sign-in", signedCommand, assertion.Bootstrap.Cookies, assertion.Bootstrap.Csrf), host.Post("/authentication/passkeys/sign-in", signedCommand, assertion.Bootstrap.Cookies, assertion.Bootstrap.Csrf));
        try { Assert.All(signed, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode)); } finally { foreach (var r in signed) r.Dispose(); }
        await using var context = db.Context(); Assert.Equal(1, await context.Set<PasskeyCredentialRow>().CountAsync()); Assert.Equal(1, await context.Set<AuthenticationSessionRow>().CountAsync(x => x.AuthenticationMethods == "passkey"));
        Assert.Equal(1, await context.Set<OutboxMessage>().CountAsync(x => x.EventType == "AuthenticatorAdded"));
    }
    [Theory]
    [InlineData("origin")][InlineData("rp")][InlineData("challenge")][InlineData("uv")]
    public async Task NativeInvalidEnrollmentLeavesNoFrameworkCredential(string changed)
    {
        await using var db = await fixture.CreateDatabaseAsync(); await using var host = await StartPasskeys(db); await host.SeedAsync(db);
        using var authenticator = new PasskeyAuthenticator(); var pending = await Authorize(host, await Begin(host, await Login(host))); var options = await EnrollmentOptions(host, pending);
        var credential = authenticator.Attest(options.Options, changed == "origin" ? "https://other.example.test" : Origin, wrongRp: changed == "rp" ? "other.example.test" : null,
            challenge: changed == "challenge" ? "AAAAAAAAAAAAAAAAAAAAAA" : null, uv: changed != "uv");
        using var response = await host.Post("/account/passkeys/enrollment/complete", new { requestId = pending.RequestId, ceremonyId = pending.CeremonyId, credential }, pending.Cookies, pending.Csrf);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        await using var context = db.Context(); Assert.Empty(await context.Set<PasskeyCredentialRow>().ToArrayAsync());
        Assert.Equal(0, await context.Database.SqlQueryRaw<int>("SELECT count(*)::int AS \"Value\" FROM identity.\"AspNetUserPasskeys\"").SingleAsync());
    }
    [Fact]
    public async Task CurrentSignedBackupFactsMapConfiguredAssuranceAndNativeCounterRegressionIsRejected()
    {
        await using var db = await fixture.CreateDatabaseAsync(); await using var host = await StartPasskeys(db); await host.SeedAsync(db);
        using var authenticator = new PasskeyAuthenticator(); var pending = await Authorize(host, await Begin(host, await Login(host))); var options = await EnrollmentOptions(host, pending);
        using var enrolled = await host.Post("/account/passkeys/enrollment/complete", new { requestId = pending.RequestId, ceremonyId = pending.CeremonyId,
            credential = authenticator.Attest(options.Options, Origin, backupEligible: true) }, pending.Cookies, pending.Csrf); enrolled.EnsureSuccessStatusCode();
        await SignInPasskey(host, authenticator, backupEligible: true, backedUp: true);
        var assertion = await AssertionOptions(host);
        using var regressed = await host.Post("/authentication/passkeys/sign-in", new { requestId = assertion.Bootstrap.Key, ceremonyId = assertion.Ceremony.GetProperty("ceremonyId").GetGuid(),
            credential = authenticator.Assert(assertion.Ceremony.GetProperty("publicKey"), Origin, backupEligible: true, backedUp: true, counterOverride: 1) }, assertion.Bootstrap.Cookies, assertion.Bootstrap.Csrf);
        Assert.Equal(HttpStatusCode.Unauthorized, regressed.StatusCode);
        await using var context = db.Context(); var session = await context.Set<AuthenticationSessionRow>().SingleAsync(x => x.AuthenticationMethods == "passkey");
        Assert.Equal("fixture-syncable-backed-assurance", session.Assurance); Assert.Equal(PasskeyPolicy.AssuranceKey(true, true), session.PasskeyAssuranceKey);
        await using var scope = host.Services.CreateAsyncScope(); var userManager = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<IdentityUserAdapter>>();
        var user = await userManager.FindByPasskeyIdAsync(authenticator.CredentialId); Assert.NotNull(user); var stored = await userManager.GetPasskeyAsync(user, authenticator.CredentialId);
        Assert.NotNull(stored); Assert.Equal(1U, stored.SignCount); Assert.True(stored.IsBackedUp); Assert.Empty(stored.AttestationObject); Assert.Empty(stored.ClientDataJson);
    }
    [Fact]
    public async Task ZeroCounterAuthenticatorUsesOneTimeChallengeAndCompatiblePolicyVersionDoesNotEndIssuedSession()
    {
        await using var db = await fixture.CreateDatabaseAsync(); var policies = new Policies(); await using var host = await StartPasskeys(db, policies); await host.SeedAsync(db);
        using var authenticator = new PasskeyAuthenticator(); await Enroll(host, await Login(host), authenticator);
        var first = await SignInPasskey(host, authenticator, counterOverride: 0); await SignInPasskey(host, authenticator, counterOverride: 0);
        policies.Version = "fixture-passkey-compatible-v2"; using var live = await host.Get("/authentication/session", first); Assert.Equal(HttpStatusCode.OK, live.StatusCode);
    }
    [Theory]
    [InlineData("expiry")][InlineData("restriction")]
    public async Task LateIssuanceFailureRollsBackNativeCounterButCommitsAbuseAndOriginalChallengeRejection(string failure)
    {
        await using var db = await fixture.CreateDatabaseAsync(); await using var host = await StartPasskeys(db); await host.SeedAsync(db);
        using var authenticator = new PasskeyAuthenticator(); await Enroll(host, await Login(host), authenticator); var assertion = await AssertionOptions(host);
        host.Access.BeforeDecision = request => { if (request.Operation == "SIGN_IN") { if (failure == "expiry") host.Clock.Advance(TimeSpan.FromMinutes(2)); else host.Access.Allowed = false; } };
        var id = assertion.Ceremony.GetProperty("ceremonyId").GetGuid();
        using var denied = await host.Post("/authentication/passkeys/sign-in", new { requestId = assertion.Bootstrap.Key, ceremonyId = id,
            credential = authenticator.Assert(assertion.Ceremony.GetProperty("publicKey"), Origin) }, assertion.Bootstrap.Cookies, assertion.Bootstrap.Csrf);
        Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
        await using var context = db.Context(); Assert.Empty(await context.Set<AuthenticationSessionRow>().Where(x => x.AuthenticationMethods == "passkey").ToArrayAsync());
        Assert.True(await context.Set<PasskeyCeremonyRow>().Where(x => x.CeremonyId == id).Select(x => x.State).SingleAsync() is "REJECTED" or "EXPIRED");
        Assert.True(await context.Set<AuthenticationAbuseRow>().Where(x => x.Partition.StartsWith("passkey:")).SumAsync(x => x.Count) >= 3);
        await using var scope = host.Services.CreateAsyncScope(); var manager = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<IdentityUserAdapter>>();
        var user = await manager.FindByPasskeyIdAsync(authenticator.CredentialId); Assert.NotNull(user); Assert.Equal(0U, (await manager.GetPasskeyAsync(user, authenticator.CredentialId))!.SignCount);
    }
    [Fact]
    public async Task TrustedExpiryWorkerClearsNativeStateIdempotently()
    {
        await using var db = await fixture.CreateDatabaseAsync(); await using var host = await StartPasskeys(db); await host.SeedAsync(db);
        var pending = await Authorize(host, await Begin(host, await Login(host))); host.Clock.Advance(TimeSpan.FromMinutes(1));
        await using var scope = host.Services.CreateAsyncScope(); var worker = scope.ServiceProvider.GetRequiredService<IPasskeyCeremonyMaintenance>(); Assert.Equal(1, await worker.ExpireCeremoniesAsync()); Assert.Equal(0, await worker.ExpireCeremoniesAsync());
        await using var context = db.Context(); var row = await context.Set<PasskeyCeremonyRow>().SingleAsync(); Assert.Equal("EXPIRED", row.State); Assert.Empty(row.ProtectedState); Assert.Empty(row.OptionsJson);
    }

    [Fact]
    public async Task ExactAssertionResultSurvivesNewCookieAndKnownRejectedAttemptCanStartFreshChallenge()
    {
        await using var db = await fixture.CreateDatabaseAsync(); await using var host = await StartPasskeys(db); await host.SeedAsync(db);
        using var authenticator = new PasskeyAuthenticator(); await Enroll(host, await Login(host), authenticator);
        var original = await AssertionOptions(host); var originalId = original.Ceremony.GetProperty("ceremonyId").GetGuid();
        using var denied = await host.Post("/authentication/passkeys/sign-in", new { requestId = original.Bootstrap.Key, ceremonyId = originalId,
            credential = authenticator.Assert(original.Ceremony.GetProperty("publicKey"), Origin, badSignature: true) }, original.Bootstrap.Cookies, original.Bootstrap.Csrf); Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
        var requestId = Guid.NewGuid(); using var options = await host.Post("/authentication/passkeys/options", new { requestId, rememberMe = false }, original.Bootstrap.Cookies, original.Bootstrap.Csrf); options.EnsureSuccessStatusCode();
        var ceremony = await options.Content.ReadFromJsonAsync<JsonElement>(); var ceremonyId = ceremony.GetProperty("ceremonyId").GetGuid();
        using var signed = await host.Post("/authentication/passkeys/sign-in", new { requestId, ceremonyId, credential = authenticator.Assert(ceremony.GetProperty("publicKey"), Origin) }, original.Bootstrap.Cookies, original.Bootstrap.Csrf);
        signed.EnsureSuccessStatusCode(); var cookies = Merge(original.Bootstrap.Cookies, signed);
        using var session = await host.Get("/authentication/session", cookies); var csrf = (await session.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("csrfToken").GetString()!;
        using var result = await host.Post("/authentication/passkeys/result", new { requestId, ceremonyId }, cookies, csrf); Assert.Equal(HttpStatusCode.OK, result.StatusCode); Assert.False(result.Headers.Contains("Set-Cookie"));
        using var other = await host.Post("/authentication/passkeys/result", new { requestId = original.Bootstrap.Key, ceremonyId = originalId }, cookies, csrf);
        Assert.Equal(HttpStatusCode.Conflict, other.StatusCode); Assert.Equal("VERSION_CONFLICT", (await other.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error_code").GetString());
        using var passwordResult = await host.Post("/authentication/result", new { idempotencyKey = original.Bootstrap.Key, email = "person@example.test", rememberMe = false }, cookies, csrf); Assert.Equal(HttpStatusCode.NotFound, passwordResult.StatusCode);
    }
    [Fact]
    public async Task CancellingOriginalOperationDoesNotClearOrAuthorizeAnotherTabsSelectedOperation()
    {
        await using var db = await fixture.CreateDatabaseAsync(); await using var host = await StartPasskeys(db); await host.SeedAsync(db);
        var first = await Begin(host, await Login(host)); var second = await Begin(host, first.Cookies);
        using var cancelled = await host.Post("/account/passkeys/cancel", new { requestId = first.RequestId, ceremonyId = first.CeremonyId }, second.Cookies, second.Csrf); cancelled.EnsureSuccessStatusCode();
        Assert.DoesNotContain(cancelled.Headers.TryGetValues("Set-Cookie", out var values) ? values : [], value => value.StartsWith(PasskeyStepUpOwner.CookieName, StringComparison.Ordinal));
        second = await Authorize(host, second with { Cookies = Merge(second.Cookies, cancelled) });
        using var wrong = await host.Post("/account/passkeys/enrollment/options", new { requestId = first.RequestId, ceremonyId = second.CeremonyId }, second.Cookies, second.Csrf); Assert.Equal(HttpStatusCode.Forbidden, wrong.StatusCode);
        var options = await EnrollmentOptions(host, second); Assert.Equal(JsonValueKind.Object, options.Options.ValueKind);
        await using var context = db.Context(); Assert.Equal("REJECTED", await context.Set<PasskeyCeremonyRow>().Where(x => x.CeremonyId == first.CeremonyId).Select(x => x.State).SingleAsync());
        Assert.Empty(await context.Set<PasskeyCredentialRow>().ToArrayAsync());
    }

    private sealed class AdvanceAfterTerminalPersistence : SaveChangesInterceptor
    {
        internal Action? Advance { get; set; }
        internal bool Fired { get; private set; }
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            if (!Fired && Advance is { } action && eventData.Context?.ChangeTracker.Entries<AuthenticationSessionRow>().Any(x => x.Entity.State == "REVOKED" && x.Entity.TerminationCause == "PASSKEY_REVOKED") == true)
            { Fired = true; action(); }
            return ValueTask.FromResult(result);
        }
    }
    [Fact]
    public async Task EligibilityExpiryDuringTerminalPersistenceRollsBackCredentialSessionProofAndOutbox()
    {
        await using var db = await fixture.CreateDatabaseAsync(); var interceptor = new AdvanceAfterTerminalPersistence(); var eligibility = new Eligibility { Validity = TimeSpan.FromSeconds(1) };
        await using var host = await StartPasskeys(db, new Policies { Effect = PasskeyRevocationSessionEffect.RevokeCredentialSessions }, eligibility,
            services => services.AddDbContext<IdentityPersistenceDbContext>(options => options.AddInterceptors(interceptor))); await host.SeedAsync(db);
        using var authenticator = new PasskeyAuthenticator(); var enrolled = await Enroll(host, await Login(host), authenticator); var cookies = await SignInPasskey(host, authenticator);
        var pending = await Begin(host, cookies, enrolled.Reference, 1); interceptor.Advance = () => host.Clock.Advance(TimeSpan.FromSeconds(2));
        using var options = await host.Get("/authentication/step-up/options", pending.Cookies); var view = await options.Content.ReadFromJsonAsync<JsonElement>();
        using var rejected = await host.Post("/authentication/step-up/password", new { requestId = view.GetProperty("requestId").GetGuid(), password = Password }, Merge(pending.Cookies, options), view.GetProperty("csrfToken").GetString());
        Assert.Equal(HttpStatusCode.Conflict, rejected.StatusCode); Assert.True(interceptor.Fired);
        using var stillLive = await host.Get("/authentication/session", cookies); Assert.Equal(HttpStatusCode.OK, stillLive.StatusCode);
        await using var context = db.Context(); Assert.Equal("ACTIVE", await context.Set<PasskeyCredentialRow>().Select(x => x.State).SingleAsync());
        Assert.Equal("PENDING", await context.Set<PasskeyCeremonyRow>().Where(x => x.CeremonyId == pending.CeremonyId).Select(x => x.State).SingleAsync());
        Assert.Equal("ACTIVE", await context.Set<StepUpProofRow>().Where(x => x.Operation == "REVOKE_PASSKEY").Select(x => x.State).SingleAsync());
        Assert.Equal(0, await context.Set<OutboxMessage>().CountAsync(x => x.EventType == "AuthenticatorRevoked"));
        await using var scope = host.Services.CreateAsyncScope(); var manager = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<IdentityUserAdapter>>(); Assert.NotNull(await manager.FindByPasskeyIdAsync(authenticator.CredentialId));
    }

    [Theory]
    [InlineData("eligibility", PasskeyRevocationSessionEffect.RevokeCredentialSessions)]
    [InlineData("access", PasskeyRevocationSessionEffect.RevokeCredentialSessions)]
    [InlineData("proof", PasskeyRevocationSessionEffect.RevokeCredentialSessions)]
    [InlineData("eligibility", PasskeyRevocationSessionEffect.PreserveExistingSessions)]
    [InlineData("access", PasskeyRevocationSessionEffect.PreserveExistingSessions)]
    [InlineData("proof", PasskeyRevocationSessionEffect.PreserveExistingSessions)]
    public async Task FinalOperationAuthorizationChecksActualIntervalsAfterAllAuthorityAwaits(string expired, PasskeyRevocationSessionEffect effect)
    {
        await using var db = await fixture.CreateDatabaseAsync(); var eligibility = new Eligibility { Validity = expired == "eligibility" ? TimeSpan.FromSeconds(1) : TimeSpan.FromHours(1) };
        await using var host = await StartPasskeys(db, new Policies { Effect = effect }, eligibility); await host.SeedAsync(db);
        using var authenticator = new PasskeyAuthenticator(); var enrolled = await Enroll(host, await Login(host), authenticator); var cookies = await SignInPasskey(host, authenticator);
        var pending = await Begin(host, cookies, enrolled.Reference, 1); var calls = 0;
        if (expired == "access") host.Access.DecisionLifetime = TimeSpan.FromSeconds(1);
        host.Access.BeforeDecision = request => { if (request.Operation == "REVOKE_PASSKEY" && ++calls == 4) host.Clock.Advance(expired == "proof" ? TimeSpan.FromSeconds(46) : TimeSpan.FromSeconds(2)); };
        using var options = await host.Get("/authentication/step-up/options", pending.Cookies); var view = await options.Content.ReadFromJsonAsync<JsonElement>();
        using var rejected = await host.Post("/authentication/step-up/password", new { requestId = view.GetProperty("requestId").GetGuid(), password = Password }, Merge(pending.Cookies, options), view.GetProperty("csrfToken").GetString());
        Assert.Equal(HttpStatusCode.Forbidden, rejected.StatusCode); Assert.Equal(4, calls);
        await using var context = db.Context(); Assert.Equal("ACTIVE", await context.Set<PasskeyCredentialRow>().Select(x => x.State).SingleAsync());
        Assert.Equal("ACTIVE", await context.Set<AuthenticationSessionRow>().Where(x => x.AuthenticationMethods == "passkey").Select(x => x.State).SingleAsync());
        Assert.Equal("ACTIVE", await context.Set<StepUpProofRow>().Where(x => x.Operation == "REVOKE_PASSKEY").Select(x => x.State).SingleAsync());
        Assert.Equal(0, await context.Set<OutboxMessage>().CountAsync(x => x.EventType == "AuthenticatorRevoked"));
    }

}
