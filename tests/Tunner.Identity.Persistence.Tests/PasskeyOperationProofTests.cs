using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tunner.Database.Outbox;
using Xunit;
using static Tunner.Identity.Persistence.Tests.AuthenticationTestHost;

namespace Tunner.Identity.Persistence.Tests;

public sealed partial class PasskeyTests
{
    private static readonly string[] NativeCompletedFields = ["attemptId", "ceremonyId", "operationReference", "requestId", "signInRequired", "status"];
    private sealed record NativeAttempt(Pending Owner, Guid RequestId, Guid AttemptId, Guid CeremonyId, string Reference, string Cookies, string Csrf, JsonElement Options);
    private static async Task<NativeAttempt> BeginNativeProof(AuthenticationTestHost host, Pending pending)
    {
        using var options = await host.Get("/authentication/step-up/options", pending.Cookies); options.EnsureSuccessStatusCode();
        var view = await options.Content.ReadFromJsonAsync<JsonElement>(); Assert.True(view.GetProperty("passkeyAvailable").GetBoolean()); Assert.True(view.GetProperty("maximumCredentialJsonBytes").GetInt32() > 0);
        var request = view.GetProperty("requestId").GetGuid(); var attempt = Guid.NewGuid(); var reference = view.GetProperty("operationReference").GetString()!;
        Assert.Equal(pending.CeremonyId.ToString("N"), reference); var csrf = view.GetProperty("csrfToken").GetString()!; var cookies = Merge(pending.Cookies, options);
        using var native = await host.Post("/authentication/step-up/passkey/options", new { requestId = request, attemptId = attempt }, cookies, csrf);
        Assert.True(native.IsSuccessStatusCode, await native.Content.ReadAsStringAsync()); var row = await native.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(reference, row.GetProperty("operationReference").GetString()); Assert.Equal(request, row.GetProperty("requestId").GetGuid()); Assert.Equal(attempt, row.GetProperty("attemptId").GetGuid());
        return new(pending, request, attempt, row.GetProperty("ceremonyId").GetGuid(), reference, Merge(cookies, native), csrf, row.GetProperty("publicKey").Clone());
    }
    private static Task<HttpResponseMessage> CompleteNativeProof(AuthenticationTestHost host, NativeAttempt native, JsonElement credential) =>
        host.Post("/authentication/step-up/passkey", new { requestId = native.RequestId, attemptId = native.AttemptId, ceremonyId = native.CeremonyId, credential }, native.Cookies, native.Csrf);
    private static async Task<Pending> AuthorizeNative(AuthenticationTestHost host, Pending pending, PasskeyAuthenticator key)
    {
        var native = await BeginNativeProof(host, pending); using var applied = await CompleteNativeProof(host, native, key.Assert(native.Options, Origin));
        Assert.True(applied.IsSuccessStatusCode, await applied.Content.ReadAsStringAsync()); var result = await applied.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("COMPLETED", result.GetProperty("status").GetString()); Assert.Equal(native.Reference, result.GetProperty("operationReference").GetString());
        Assert.Equal(native.RequestId, result.GetProperty("requestId").GetGuid()); Assert.Equal(native.AttemptId, result.GetProperty("attemptId").GetGuid()); Assert.Equal(native.CeremonyId, result.GetProperty("ceremonyId").GetGuid());
        Assert.Equal(NativeCompletedFields, result.EnumerateObject().Select(x => x.Name).OrderBy(x => x, StringComparer.Ordinal));
        return pending with { Cookies = Merge(native.Cookies, applied), Csrf = native.Csrf, SignInRequired = result.GetProperty("signInRequired").GetBoolean() };
    }
    [Fact]
    public async Task NativeOperationProofUsesSharedRowsAndDoesNotElevateOrReplaceSession()
    {
        await using var db = await fixture.CreateDatabaseAsync(); await using var host = await StartPasskeys(db, passkeyProof: true); await host.SeedAsync(db, minor: true);
        using var first = new PasskeyAuthenticator(); var enrolled = await Enroll(host, await Login(host), first);
        await using var read = db.Context(); var before = await read.Set<AuthenticationSessionRow>().AsNoTracking().SingleAsync();
        var pending = await AuthorizeNative(host, await Begin(host, enrolled.Pending.Cookies), first);
        var proof = await read.Set<StepUpProofRow>().AsNoTracking().SingleAsync(x => x.AuthenticationMethod.StartsWith("passkey:"));
        Assert.Equal("CONSUMED", proof.State); Assert.Empty(proof.ProtectedKey); Assert.Equal(enrolled.Reference, proof.PasskeyCredentialReference);
        Assert.Equal(PasskeyPolicy.MethodKey(false, false), proof.AuthenticationMethod); Assert.Equal("fixture-passkey-v1", proof.PasskeyPolicyVersion);
        var after = await read.Set<AuthenticationSessionRow>().AsNoTracking().SingleAsync();
        Assert.Equal(before.SessionId, after.SessionId); Assert.Equal(before.KeyDigest, after.KeyDigest); Assert.Equal(before.Assurance, after.Assurance);
        Assert.Equal(before.AuthenticationMethods, after.AuthenticationMethods); Assert.Equal(before.AuthenticatedAt, after.AuthenticatedAt); Assert.Equal(before.ExpiresAt, after.ExpiresAt);
        var source = await read.Set<PasskeyCredentialRow>().AsNoTracking().SingleAsync(); Assert.Equal(1, source.Version); Assert.Equal(1, source.UsageVersion);
        using var second = new PasskeyAuthenticator(); var options = await EnrollmentOptions(host, pending);
        using var completed = await host.Post("/account/passkeys/enrollment/complete", new { requestId = pending.RequestId, ceremonyId = pending.CeremonyId, credential = second.Attest(options.Options, Origin) }, pending.Cookies, pending.Csrf);
        Assert.True(completed.IsSuccessStatusCode, await completed.Content.ReadAsStringAsync()); Assert.Equal(2, await read.Set<PasskeyCredentialRow>().CountAsync());
    }
    [Theory]
    [InlineData(PasskeyRevocationSessionEffect.PreserveExistingSessions)]
    [InlineData(PasskeyRevocationSessionEffect.RevokeCredentialSessions)]
    public async Task NativeSelfKeyProofRevokesExactLifecycleVersionWithBothExplicitSessionEffects(PasskeyRevocationSessionEffect effect)
    {
        await using var db = await fixture.CreateDatabaseAsync(); await using var host = await StartPasskeys(db, new Policies { Effect = effect }, passkeyProof: true); await host.SeedAsync(db);
        using var key = new PasskeyAuthenticator(); var enrolled = await Enroll(host, await Login(host), key); var cookies = await SignInPasskey(host, key);
        var pending = await AuthorizeNative(host, await Begin(host, cookies, enrolled.Reference, 1), key);
        Assert.Equal(effect == PasskeyRevocationSessionEffect.RevokeCredentialSessions, pending.SignInRequired);
        await using var read = db.Context(); var credential = await read.Set<PasskeyCredentialRow>().SingleAsync(); Assert.Equal("REVOKED", credential.State); Assert.Equal(2, credential.Version); Assert.Equal(2, credential.UsageVersion);
        var proof = await read.Set<StepUpProofRow>().SingleAsync(x => x.AuthenticationMethod.StartsWith("passkey:")); Assert.Equal("CONSUMED", proof.State); Assert.Equal(enrolled.Reference, proof.PasskeyCredentialReference);
        using var session = await host.Get("/authentication/session", cookies); Assert.Equal(pending.SignInRequired ? HttpStatusCode.Unauthorized : HttpStatusCode.OK, session.StatusCode);
        Assert.Equal(1, await read.Set<OutboxMessage>().CountAsync(x => x.EventType == "AuthenticatorRevoked"));
    }
    [Theory]
    [InlineData("origin")][InlineData("rp")][InlineData("challenge")][InlineData("signature")][InlineData("uv")][InlineData("handle")]
    public async Task NativeOperationProofRejectsInvalidCeremonyWithoutIssuingProofOrOwnerMutation(string changed)
    {
        await using var db = await fixture.CreateDatabaseAsync(); await using var host = await StartPasskeys(db, passkeyProof: true); await host.SeedAsync(db);
        using var key = new PasskeyAuthenticator(); var enrolled = await Enroll(host, await Login(host), key); var native = await BeginNativeProof(host, await Begin(host, enrolled.Pending.Cookies, enrolled.Reference, 1));
        var response = key.Assert(native.Options, changed == "origin" ? "https://other.example.test" : Origin, wrongRp: changed == "rp" ? "other.example.test" : null,
            challenge: changed == "challenge" ? "AAAAAAAAAAAAAAAAAAAAAA" : null, badSignature: changed == "signature", uv: changed != "uv", wrongUserHandle: changed == "handle" ? "Zm9yZWlnbg" : null);
        using var failed = await CompleteNativeProof(host, native, response); Assert.Equal(HttpStatusCode.Unauthorized, failed.StatusCode);
        Assert.Equal("REJECTED", (await failed.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("ceremony_status").GetString());
        using var replay = await CompleteNativeProof(host, native, key.Assert(native.Options, Origin)); Assert.False(replay.IsSuccessStatusCode);
        await using var read = db.Context(); Assert.Equal("ACTIVE", await read.Set<PasskeyCredentialRow>().Select(x => x.State).SingleAsync());
        Assert.Empty(await read.Set<StepUpProofRow>().Where(x => x.AuthenticationMethod.StartsWith("passkey:")).ToArrayAsync()); Assert.Equal(0, await read.Set<PasskeyCredentialRow>().Select(x => x.UsageVersion).SingleAsync());
    }
    [Fact]
    public async Task NativeProofRemainsBoundToOriginalOperationRequestAndSession()
    {
        await using var db = await fixture.CreateDatabaseAsync(); await using var host = await StartPasskeys(db, passkeyProof: true); await host.SeedAsync(db);
        using var key = new PasskeyAuthenticator(); var enrolled = await Enroll(host, await Login(host), key); var native = await BeginNativeProof(host, await Begin(host, enrolled.Pending.Cookies));
        var response = key.Assert(native.Options, Origin);
        using var wrong = await host.Post("/authentication/step-up/passkey", new { requestId = Guid.NewGuid(), native.AttemptId, native.CeremonyId, credential = response }, native.Cookies, native.Csrf); Assert.Equal(HttpStatusCode.Conflict, wrong.StatusCode);
        var other = await Begin(host, native.Cookies); using var changed = await host.Get("/authentication/step-up/options", other.Cookies); var view = await changed.Content.ReadFromJsonAsync<JsonElement>();
        using var switched = await host.Post("/authentication/step-up/passkey", new { requestId = native.RequestId, native.AttemptId, native.CeremonyId, credential = response }, Merge(other.Cookies, changed), view.GetProperty("csrfToken").GetString()); Assert.Equal(HttpStatusCode.Conflict, switched.StatusCode);
        await using var read = db.Context(); Assert.Empty(await read.Set<StepUpProofRow>().Where(x => x.AuthenticationMethod.StartsWith("passkey:")).ToArrayAsync());
    }
    [Fact]
    public async Task IssuedNativeProofRecoversReadOnlyThenExplicitlyContinuesAfterOwnerFailure()
    {
        await using var db = await fixture.CreateDatabaseAsync(); var eligibility = new Eligibility { Stale = true }; await using var host = await StartPasskeys(db, eligibility: eligibility, passkeyProof: true); await host.SeedAsync(db);
        using var key = new PasskeyAuthenticator(); var enrolled = await Enroll(host, await Login(host), key); var native = await BeginNativeProof(host, await Begin(host, enrolled.Pending.Cookies, enrolled.Reference, 1));
        using var failed = await CompleteNativeProof(host, native, key.Assert(native.Options, Origin)); Assert.Equal(HttpStatusCode.ServiceUnavailable, failed.StatusCode);
        using var options = await host.Get("/authentication/step-up/options", native.Cookies); var view = await options.Content.ReadFromJsonAsync<JsonElement>(); var recovery = view.GetProperty("passkeyRecovery");
        Assert.Equal(native.AttemptId, recovery.GetProperty("attemptId").GetGuid()); Assert.Equal(native.CeremonyId, recovery.GetProperty("ceremonyId").GetGuid()); Assert.Equal("PROOF_ISSUED", recovery.GetProperty("status").GetString());
        eligibility.Stale = false;
        using var continued = await host.Post("/authentication/step-up/passkey/continue", new { native.RequestId, native.AttemptId, native.CeremonyId }, Merge(native.Cookies, options), view.GetProperty("csrfToken").GetString());
        Assert.True(continued.IsSuccessStatusCode, await continued.Content.ReadAsStringAsync());
        using var duplicate = await host.Post("/authentication/step-up/passkey/continue", new { native.RequestId, native.AttemptId, native.CeremonyId }, native.Cookies, native.Csrf); Assert.Equal(HttpStatusCode.OK, duplicate.StatusCode);
        await using var read = db.Context(); Assert.Equal(1, await read.Set<OutboxMessage>().CountAsync(x => x.EventType == "AuthenticatorRevoked")); Assert.Equal(1, await read.Set<PasskeyCredentialRow>().Select(x => x.UsageVersion).SingleAsync());
    }
    [Fact]
    public async Task PasswordlessActorOnlySeesActualConfiguredProofMethodsAndPasswordFact()
    {
        await using var db = await fixture.CreateDatabaseAsync(); await using var host = await StartPasskeys(db, passkeyProof: true); await host.SeedAsync(db);
        using var key = new PasskeyAuthenticator(); var enrolled = await Enroll(host, await Login(host), key); var cookies = await SignInPasskey(host, key);
        await using (var seed = db.Context()) { var user = await seed.Users.SingleAsync(); user.PasswordHash = null; await seed.SaveChangesAsync(); }
        using var methods = await host.Get("/account/passkeys", cookies); Assert.False((await methods.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("passwordAvailable").GetBoolean());
        var pending = await Begin(host, cookies); using var options = await host.Get("/authentication/step-up/options", pending.Cookies); var view = await options.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(view.GetProperty("passwordAvailable").GetBoolean()); Assert.True(view.GetProperty("passkeyAvailable").GetBoolean());
        await AuthorizeNative(host, pending with { Cookies = Merge(pending.Cookies, options) }, key);
    }
    [Fact]
    public async Task ConsumedPasskeySourceRevocationBlocksLaterEnrollmentActivation()
    {
        await using var db = await fixture.CreateDatabaseAsync(); await using var host = await StartPasskeys(db, passkeyProof: true); await host.SeedAsync(db);
        using var source = new PasskeyAuthenticator(); var enrolled = await Enroll(host, await Login(host), source);
        var pending = await AuthorizeNative(host, await Begin(host, enrolled.Pending.Cookies), source); var creation = await EnrollmentOptions(host, pending);
        await Authorize(host, await Begin(host, pending.Cookies, enrolled.Reference, 1));
        using var second = new PasskeyAuthenticator(); using var denied = await host.Post("/account/passkeys/enrollment/complete", new { pending.RequestId, pending.CeremonyId, credential = second.Attest(creation.Options, Origin) }, pending.Cookies, pending.Csrf);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode); await using var read = db.Context(); Assert.Equal(1, await read.Set<PasskeyCredentialRow>().CountAsync());
    }
    [Fact]
    public async Task CompletedNativeReadRequiresItsOriginalAttemptCeremonyAndConsumedProof()
    {
        await using var db = await fixture.CreateDatabaseAsync(); await using var host = await StartPasskeys(db, passkeyProof: true); await host.SeedAsync(db);
        using var key = new PasskeyAuthenticator(); var enrolled = await Enroll(host, await Login(host), key); var native = await BeginNativeProof(host, await Begin(host, enrolled.Pending.Cookies));
        var credential = key.Assert(native.Options, Origin); using var completed = await CompleteNativeProof(host, native, credential); completed.EnsureSuccessStatusCode();
        using var wrongAttempt = await host.Post("/authentication/step-up/passkey/continue", new { native.RequestId, attemptId = Guid.NewGuid(), native.CeremonyId }, native.Cookies, native.Csrf); Assert.Equal(HttpStatusCode.Conflict, wrongAttempt.StatusCode);
        using var wrongCeremony = await host.Post("/authentication/step-up/passkey/continue", new { native.RequestId, native.AttemptId, ceremonyId = Guid.NewGuid() }, native.Cookies, native.Csrf); Assert.Equal(HttpStatusCode.Conflict, wrongCeremony.StatusCode);
        using var changed = await CompleteNativeProof(host, native, key.Assert(native.Options, Origin)); Assert.Equal(HttpStatusCode.Conflict, changed.StatusCode);
        using var exact = await host.Post("/authentication/step-up/passkey/continue", new { native.RequestId, native.AttemptId, native.CeremonyId }, native.Cookies, native.Csrf); exact.EnsureSuccessStatusCode(); var result = await exact.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("COMPLETED", result.GetProperty("status").GetString()); Assert.Equal(native.RequestId, result.GetProperty("requestId").GetGuid()); Assert.Equal(native.AttemptId, result.GetProperty("attemptId").GetGuid()); Assert.Equal(native.CeremonyId, result.GetProperty("ceremonyId").GetGuid()); Assert.Equal(native.Reference, result.GetProperty("operationReference").GetString()); Assert.False(exact.Headers.Contains("Set-Cookie"));
        await using var read = db.Context(); Assert.Equal(1, await read.Set<StepUpProofRow>().CountAsync(x => x.AuthenticationMethod.StartsWith("passkey:") && x.State == "CONSUMED")); Assert.Equal(1, await read.Set<PasskeyCredentialRow>().Select(x => x.UsageVersion).SingleAsync());
    }

    private sealed class IncompletePublishedPresentation : IPasskeyPresentationSource, IStepUpPresentationSource
    {
        internal string? Missing { get; set; }
        private RegistrationPublishedText[] Content(string file)
        {
            using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, file)));
            return document.RootElement.GetProperty("content").EnumerateObject().Where(x => x.Name != Missing).Select(x => new RegistrationPublishedText(x.Name, "fixture", "fixture-v1", "fixture-publication-only", "en", "PUBLISHED", x.Value.GetString()!)).ToArray();
        }
        public PasskeyPresentation RequirePublished(PasskeyPolicy policy) => new("en", Content("passkeys-content.proposal.json"));
        public StepUpPresentation RequirePublished(AssurancePolicyVersion policy) => new("en", Content("step-up-content.proposal.json"));
    }
    [Theory]
    [InlineData(false)][InlineData(true)]
    public async Task MissingConsumedPublishedCopyIsDependencyUnavailableRatherThanBrowserValidation(bool sharedProof)
    {
        await using var db = await fixture.CreateDatabaseAsync(); var source = new IncompletePublishedPresentation();
        await using var host = await StartPasskeys(db, configure: services => { services.AddSingleton<IPasskeyPresentationSource>(source); services.AddSingleton<IStepUpPresentationSource>(source); }); await host.SeedAsync(db);
        var pending = await Begin(host, await Login(host)); source.Missing = sharedProof ? "stepUp.passkey.unknown" : "passkeys.methods.password";
        using var response = await host.Get(sharedProof ? "/authentication/step-up/options" : "/account/passkeys/options", pending.Cookies); Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<JsonElement>(); Assert.Equal("DEPENDENCY_UNAVAILABLE", error.GetProperty("error_code").GetString()); Assert.False(error.TryGetProperty("content", out _));
    }

}
