using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tunner.Platform.Policies;
using Xunit;
using static Tunner.Identity.Persistence.Tests.AuthenticationTestHost;

namespace Tunner.Identity.Persistence.Tests;

public sealed partial class PasskeyTests
{
    private sealed class TotpAndPasskeyAssurance(TotpTests.Profile totp) : IAssurancePolicySource
    {
        public AssurancePolicyVersion RequireEffective(AssurancePolicyScope scope, DateTimeOffset executedAt, string? expectedVersion = null)
        {
            if (scope != PasskeyApplication.EnrollmentScope && scope != PasskeyApplication.RevocationScope) return totp.RequireEffective(scope, executedAt, expectedVersion);
            var version = scope == PasskeyApplication.EnrollmentScope ? "fixture-enrollment-v1" : "fixture-revocation-v1";
            if (expectedVersion is not null && expectedVersion != version) throw new AssurancePolicyVersionConflictException();
            return new(new(version, "ACTIVE", scope, new(executedAt.AddHours(-1), true, executedAt.AddHours(1), false), null, ["SYNTHETIC-TEST-ONLY"]), "fixture-password", TimeSpan.FromMinutes(2), new Dictionary<string, string> { ["pwd"] = "fixture-password" });
        }
    }
    [Fact]
    public async Task NativePasskeyPrimaryRequiresTotpBeforeSessionAndBothLostResponsesRecoverExactOriginalResult()
    {
        await using var db = await fixture.CreateDatabaseAsync(); var profile = new TotpTests.Profile();
        await using var host = await StartPasskeys(db, configure: services =>
        {
            services.AddSingleton<ITotpPolicySource>(profile); services.AddSingleton<ITotpSeedStore>(profile); services.AddSingleton<ITotpMutationEligibilitySource>(profile);
            services.AddSingleton<ITotpMfaRequirementSource>(profile); services.AddSingleton<ITotpPresentationSource>(profile); services.AddSingleton<IStepUpPresentationSource>(profile);
            services.AddSingleton<IAssurancePolicySource>(new TotpAndPasskeyAssurance(profile));
        });
        host.Clock.SynchronizeToSystem(); await host.SeedAsync(db); using var key = new PasskeyAuthenticator(); var passkey = await Enroll(host, await Login(host), key);
        var totp = await TotpTests.Enroll(host, profile, passkey.Pending.Cookies); profile.RequireMfa = true;
        var primary = await AssertionOptions(host); var ceremonyId = primary.Ceremony.GetProperty("ceremonyId").GetGuid();
        using (var lost = await host.Post("/authentication/passkeys/sign-in", new { requestId = primary.Bootstrap.Key, ceremonyId, credential = key.Assert(primary.Ceremony.GetProperty("publicKey"), Origin) }, primary.Bootstrap.Cookies, primary.Bootstrap.Csrf))
        { Assert.Equal(HttpStatusCode.OK, lost.StatusCode); Assert.Equal("MFA_REQUIRED", (await lost.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetString()); }
        await using (var read = db.Context())
        {
            Assert.Equal(1, await read.Set<AuthenticationSessionRow>().CountAsync()); var row = await read.Set<PasskeyCeremonyRow>().SingleAsync(x => x.CeremonyId == ceremonyId); Assert.Equal("COMPLETED", row.State); Assert.Null(row.ResultSessionId);
        }
        // Primary response is deliberately discarded; POST result restores only its protected pending-MFA continuation.
        using var original = await host.Post("/authentication/passkeys/result", new { requestId = primary.Bootstrap.Key, ceremonyId }, primary.Bootstrap.Cookies, primary.Bootstrap.Csrf);
        Assert.Equal(HttpStatusCode.OK, original.StatusCode); Assert.Equal("MFA_REQUIRED", (await original.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetString());
        var cookies = Merge(primary.Bootstrap.Cookies, original); using var options = await host.Get("/authentication/totp/options", cookies); Assert.Equal(HttpStatusCode.OK, options.StatusCode);
        var mfa = await options.Content.ReadFromJsonAsync<JsonElement>(); cookies = Merge(cookies, options); var csrf = mfa.GetProperty("csrfToken").GetString();
        using (var lostFinal = await host.Post("/authentication/totp/complete", new { requestId = primary.Bootstrap.Key, authenticatorReference = totp.Reference, code = TotpTests.NativeCode(profile.Seed(totp.Reference), 1) }, cookies, csrf)) Assert.Equal(HttpStatusCode.OK, lostFinal.StatusCode);
        using var recovered = await host.Post("/authentication/totp/result", new { requestId = primary.Bootstrap.Key }, cookies, csrf); Assert.Equal(HttpStatusCode.OK, recovered.StatusCode); cookies = Merge(cookies, recovered);
        using var live = await host.Get("/authentication/session", cookies); Assert.Equal(HttpStatusCode.OK, live.StatusCode);
        var sessionCsrf = (await live.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("csrfToken").GetString(); cookies = Merge(cookies, live);
        using var finalPrimary = await host.Post("/authentication/passkeys/result", new { requestId = primary.Bootstrap.Key, ceremonyId }, cookies, sessionCsrf); Assert.Equal(HttpStatusCode.OK, finalPrimary.StatusCode);
        await using var check = db.Context(); Assert.Equal(2, await check.Set<AuthenticationSessionRow>().CountAsync()); Assert.Equal("passkey+totp", (await check.Set<AuthenticationSessionRow>().SingleAsync(x => x.PasskeyCredentialReference != null)).AuthenticationMethods);
    }
    [Fact]
    public async Task MfaFinalAccessPasskeyPolicyTransitionCannotCommitSessionReplayOrCompletedResult()
    {
        await using var db = await fixture.CreateDatabaseAsync(); var profile = new TotpTests.Profile(); var passkeyPolicy = new Policies();
        await using var host = await StartPasskeys(db, policies: passkeyPolicy, configure: services =>
        {
            services.AddSingleton<ITotpPolicySource>(profile); services.AddSingleton<ITotpSeedStore>(profile); services.AddSingleton<ITotpMutationEligibilitySource>(profile);
            services.AddSingleton<ITotpMfaRequirementSource>(profile); services.AddSingleton<ITotpPresentationSource>(profile); services.AddSingleton<IStepUpPresentationSource>(profile);
            services.AddSingleton<IAssurancePolicySource>(new TotpAndPasskeyAssurance(profile));
        });
        host.Clock.SynchronizeToSystem(); await host.SeedAsync(db); using var key = new PasskeyAuthenticator(); var passkey = await Enroll(host, await Login(host), key);
        var totp = await TotpTests.Enroll(host, profile, passkey.Pending.Cookies); profile.RequireMfa = true; var primary = await AssertionOptions(host);
        using var primaryResult = await host.Post("/authentication/passkeys/sign-in", new { requestId = primary.Bootstrap.Key, ceremonyId = primary.Ceremony.GetProperty("ceremonyId").GetGuid(), credential = key.Assert(primary.Ceremony.GetProperty("publicKey"), Origin) }, primary.Bootstrap.Cookies, primary.Bootstrap.Csrf);
        Assert.Equal(HttpStatusCode.OK, primaryResult.StatusCode); var cookies = Merge(primary.Bootstrap.Cookies, primaryResult);
        using var options = await host.Get("/authentication/totp/options", cookies); Assert.Equal(HttpStatusCode.OK, options.StatusCode); var view = await options.Content.ReadFromJsonAsync<JsonElement>(); cookies = Merge(cookies, options);
        var calls = 0; host.Access.BeforeDecision = request => { if (request.Operation == "SIGN_IN" && ++calls == 2) passkeyPolicy.Version = "fixture-passkey-v2"; };
        using var result = await host.Post("/authentication/totp/complete", new { requestId = primary.Bootstrap.Key, authenticatorReference = totp.Reference, code = TotpTests.NativeCode(profile.Seed(totp.Reference), 1) }, cookies, view.GetProperty("csrfToken").GetString());
        Assert.True(calls >= 2, "FINAL_ACCESS_TRANSITION_NOT_EXERCISED");
        await using var read = db.Context(); Assert.Equal(1, await read.Set<AuthenticationSessionRow>().CountAsync()); Assert.Equal(1, await read.Set<TotpUsedCodeRow>().CountAsync());
        Assert.Equal("PENDING", (await read.Set<TotpPendingAuthenticationRow>().SingleAsync()).State); Assert.Equal(HttpStatusCode.Unauthorized, result.StatusCode);
    }
}
