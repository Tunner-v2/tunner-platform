using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Tunner.Database.Outbox;
using Tunner.Platform.Configuration;
using Tunner.Platform.Policies;
using Tunner.Platform.Secrets;
using Tunner.User.Bff;
using Xunit;
using static Tunner.Identity.Persistence.Tests.AuthenticationTestHost;

namespace Tunner.Identity.Persistence.Tests;

// Uses the same SessionListingPostgresFixture, HTTPS host and bulk preparation as the existing 37 cases.
public sealed partial class SessionBulkRevocationHttpTests
{
    private static readonly EffectivePolicyInterval NativeInterval = new(new(2020, 1, 1, 0, 0, 0, TimeSpan.Zero), true,
        new DateTimeOffset(2040, 1, 1, 0, 0, 0, TimeSpan.Zero), false);
    private sealed class NativePublication(TotpTests.Profile? totp = null) : IStepUpPresentationSource
    {
        public StepUpPresentation RequirePublished(AssurancePolicyVersion policy)
        {
            var existing = totp?.RequirePublished(policy) ?? new StepUpBrowserTests.Presentation().RequirePublished(policy);
            string[] federation = ["description", "source", "pending", "unknown", "check", "continue", "cancel", "Google"];
            return new("en", existing.Content.Values.Concat(federation.Select(key => new RegistrationPublishedText("stepUp.federation." + key,
                "fixture", "fixture-v1", "SYNTHETIC-TEST-ONLY", "en", "PUBLISHED", "Fixture " + key)))
                .GroupBy(x => x.Key, StringComparer.Ordinal).Select(x => x.Last()));
        }
    }
    private sealed class NativeSecrets : ISecretStore
    {
        public Task<SecretValues> ReadAsync(EngineeringSecretReference reference, CancellationToken cancellationToken = default)
        {
            if (reference.StoreReference != "synthetic" || reference.ResourceReference != "synthetic/google")
                throw new InvalidOperationException("The synthetic native provider reference is required.");
            return Task.FromResult(new SecretValues(new Dictionary<string, string>
                { ["secret"] = FederationOidcNativeProtocolTests.SyntheticProvider.Secret }));
        }
    }
    private sealed record NativeFrame(Guid RequestId, Guid AttemptId, Guid CeremonyId, Guid SourceReference, string Cookies,
        string Csrf, JsonElement Options, Original Original);
    private static FederationProfileVersion NativeFederationProfile()
    {
        var values = new FederationProfile("fixture-bulk-google-v1", FederationProviderKind.Google, Origin, "fixture-bulk-google-wire",
            "fixture-bulk-google-namespace", "https://accounts.google.com", FederationOidcNativeProtocolTests.SyntheticProvider.ClientId,
            [], "fixture-bulk-google-issuer-map", 128, 128, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(3), TimeSpan.FromMinutes(3),
            4, "/login", ["/login"], "fixture-admission", "fixture-federation-primary", "fixture-map", "fixture-session-use",
            "fixture-processing", "fixture-retention")
        {
            FreshAuthentication = new("fixture-bulk-fresh-v1", "SYNTHETIC-TEST-ONLY", "fixture-bulk-fresh-map",
                TimeSpan.FromSeconds(90), "/login", [new(["pwd", "mfa"], "fixture-password")])
        };
        return new(new(values.Version, "ACTIVE", values.Provider, NativeInterval, null, ["SYNTHETIC-TEST-ONLY"]), values);
    }
    private static void ConfigureNativeFederation(IServiceCollection services, Profile profile,
        FederationProfileVersion federation, HttpClient backchannel)
    {
        const string prefix = EngineeringEnvironmentConfiguration.SectionName + ":";
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [prefix + "Class"] = "EPHEMERAL_TEST", [prefix + "Reference"] = "synthetic-bulk-native-proof",
            [prefix + "Endpoints:provider"] = federation.Values.Issuer,
            [prefix + "SecretReferences:google:StoreReference"] = "synthetic",
            [prefix + "SecretReferences:google:ResourceReference"] = "synthetic/google"
        }).Build();
        var secret = EngineeringEnvironmentConfiguration.Load(configuration).SecretReferences["google"];
        services.AddSingleton<IFederationProfileSource>(new FederationProfileCatalog([federation]));
        services.AddSingleton<ITotpMfaRequirementSource>(new TotpTests.Profile());
        services.AddSingleton<ISecretStore>(new NativeSecrets());
        services.AddTunnerFederationOidc(new(federation, new(federation.Values.Issuer + "/.well-known/openid-configuration"), secret,
            "secret", TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(2), TimeSpan.Zero, 16384, 32768, 8, 128, 24576, 16384,
            "__Host-fixture.Bulk.Google.Correlation.", "__Host-fixture.Bulk.Google.Nonce."));
        services.Configure<OpenIdConnectOptions>(federation.Values.TransportScheme, options => options.Backchannel = backchannel);
        profile.Methods.Add("federation:Google", "fixture-password"); profile.NativePresentation = new NativePublication();
    }
    private async Task<Scenario> CreateNative(string method, TotpTests.Profile totp, FederationProfileVersion federation, HttpClient backchannel)
    {
        Task<AuthenticationTestHost> Starter(IdentityTestDatabase database, Action<IServiceCollection> configure) => method switch
        {
            "passkey" => PasskeyTests.StartRecoveryFixtureAsync(database, configure),
            "totp" => TotpTests.StartTotp(database, totp, configure),
            _ => Start(database, configure)
        };
        return await Create(start: Starter, synchronizeClock: true, configure: (services, profile) =>
        {
            profile.Interval = NativeInterval;
            if (method == "passkey")
            {
                profile.Methods.Add(PasskeyPolicy.MethodKey(false, false), "fixture-password");
                var passkey = (IPasskeyPolicySource)services.Last(x => x.ServiceType == typeof(IPasskeyPolicySource)).ImplementationInstance!;
                var policy = passkey.RequireEffective(DateTimeOffset.UtcNow);
                profile.ScopeVersions.Add(PasskeyApplication.EnrollmentScope, policy.EnrollmentAssurancePolicyVersion);
                profile.ScopeVersions.Add(PasskeyApplication.RevocationScope, policy.RevocationAssurancePolicyVersion);
            }
            else if (method == "totp")
            {
                profile.AssuranceVersion = totp.AssuranceVersion;
                profile.Methods.Add("totp", "fixture-password"); profile.NativePresentation = new NativePublication(totp);
            }
            else ConfigureNativeFederation(services, profile, federation, backchannel);
        });
    }
    private static async Task<Guid> EnrollNativeSource(Scenario test, string method, PasskeyAuthenticator key, TotpTests.Profile totp,
        FederationProfileVersion federation)
    {
        if (method == "passkey") return (await PasskeyTests.SeedRecoveryCredentialAsync(test.Host, key)).Reference;
        if (method == "totp") return (await TotpTests.Enroll(test.Host, totp, await TotpTests.Login(test.Host))).Reference;
        await using var setup = test.Database.Context();
        var actor = await setup.Set<AuthenticationSessionRow>().SingleAsync(x => x.SessionId == test.Actor.SessionId);
        var reference = Guid.NewGuid();
        // Existing qualified source-link fixture only. Provider proof still runs the official native OIDC handler below.
        setup.ExternalIdentityLinks.Add(new ExternalIdentityLinkRow
        {
            LinkId = reference, AccountId = actor.AccountId, Provider = federation.Values.StableNamespaceId, Subject = "opaque-subject", Version = 1,
            EvidenceReference = "SYNTHETIC-TEST-ONLY", LinkState = "ACTIVE", QualifiedClientId = federation.Values.ClientId,
            QualificationProfileVersion = federation.Values.Version, IssuerMappingEvidenceReference = federation.Values.IssuerMappingEvidenceReference,
            QualificationEvidenceReference = "SYNTHETIC-TEST-ONLY"
        });
        await setup.SaveChangesAsync(); return reference;
    }
    private static async Task<NativeFrame> BeginBulkNative(Scenario test, string method, Guid source)
    {
        var original = await Activate(test, await Preview(test));
        using var options = await test.Host.Get("/authentication/step-up/options", original.Cookies);
        Assert.Equal(HttpStatusCode.OK, options.StatusCode); var view = await options.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(original.OperationReference, view.GetProperty("operationReference").GetString());
        var request = view.GetProperty("requestId").GetGuid(); var attempt = Guid.NewGuid(); var csrf = view.GetProperty("csrfToken").GetString()!;
        var cookies = Merge(original.Cookies, options);
        if (method == "totp")
        {
            Assert.True(view.GetProperty("totpAvailable").GetBoolean());
            return new(request, attempt, Guid.Empty, source, cookies, csrf, default, original);
        }
        if (method == "federation")
        {
            Assert.Contains(view.GetProperty("federation").GetProperty("choices").EnumerateArray(), x => x.GetProperty("linkReference").GetGuid() == source);
            using var start = await test.Host.Post("/authentication/step-up/federation/start", new { requestId = request, provider = "Google", linkReference = source }, cookies, csrf);
            Assert.Equal(HttpStatusCode.OK, start.StatusCode); var started = await start.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(original.OperationReference, started.GetProperty("operationReference").GetString());
            return new(request, started.GetProperty("federation").GetProperty("attemptId").GetGuid(), Guid.Empty, source, Merge(cookies, start), csrf, default, original);
        }
        Assert.True(view.GetProperty("passkeyAvailable").GetBoolean());
        using var native = await test.Host.Post("/authentication/step-up/passkey/options", new { requestId = request, attemptId = attempt }, cookies, csrf);
        Assert.Equal(HttpStatusCode.OK, native.StatusCode); var ceremony = await native.Content.ReadFromJsonAsync<JsonElement>();
        return new(request, attempt, ceremony.GetProperty("ceremonyId").GetGuid(), source, Merge(cookies, native), csrf, ceremony.GetProperty("publicKey").Clone(), original);
    }
    private static async Task NativeFederationRoundTrip(Scenario test, NativeFrame frame, FederationOidcNativeProtocolTests.SyntheticProvider provider)
    {
        using var challenge = await test.Host.GoogleFederationChallenge(frame.Cookies);
        Assert.Equal(HttpStatusCode.Redirect, challenge.StatusCode); Assert.Equal("accounts.google.com", challenge.Headers.Location!.Host);
        var query = QueryHelpers.ParseQuery(challenge.Headers.Location.Query);
        Assert.Equal("login", query["prompt"].ToString()); Assert.Equal("S256", query["code_challenge_method"].ToString());
        using (var claims = JsonDocument.Parse(query["claims"].ToString()))
        {
            Assert.True(claims.RootElement.GetProperty("id_token").GetProperty("auth_time").GetProperty("essential").GetBoolean());
            Assert.True(claims.RootElement.GetProperty("id_token").GetProperty("amr").GetProperty("essential").GetBoolean());
        }
        var code = provider.Admit(query["nonce"].ToString(), query["code_challenge"].ToString());
        var protocol = string.Join("; ", challenge.Headers.GetValues("Set-Cookie").Select(x => x.Split(';')[0]));
        using var callback = await test.Host.GoogleFederationCallback(query["state"].ToString(), code, protocol);
        Assert.Equal(HttpStatusCode.SeeOther, callback.StatusCode); Assert.Equal("/login", callback.Headers.Location?.OriginalString);
        Assert.True(provider.PkceAndConfidentialClientObserved); Assert.Equal(1, provider.TokenRequests);
        Assert.DoesNotContain(callback.Headers.TryGetValues("Set-Cookie", out var headers) ? headers : [],
            x => x.StartsWith(AuthenticationIdentity.CookieName + "=", StringComparison.Ordinal));
    }
    private static Task<HttpResponseMessage> ContinueNative(Scenario test, string method, NativeFrame frame, Guid? request = null, bool wrongAttempt = false)
        => test.Host.Post("/authentication/step-up/" + method + "/continue", method switch
        {
            "passkey" => (object)new { requestId = request ?? frame.RequestId, attemptId = wrongAttempt ? Guid.NewGuid() : frame.AttemptId, frame.CeremonyId },
            "totp" => new { requestId = request ?? frame.RequestId, attemptId = wrongAttempt ? Guid.NewGuid() : frame.AttemptId, authenticatorReference = frame.SourceReference },
            _ => new { requestId = request ?? frame.RequestId }
        }, frame.Cookies, frame.Csrf);
    [Theory]
    [InlineData("passkey", false)]
    [InlineData("totp", false)]
    [InlineData("federation", false)]
    [InlineData("passkey", true)]
    [InlineData("totp", true)]
    [InlineData("federation", true)]
    public async Task NativeBulkProofContinuationsKeepExactManifestAndCannotBorrowIndependentOwner(string method, bool switchOwner)
    {
        var totp = new TotpTests.Profile(); var federation = NativeFederationProfile();
        using var provider = new FederationOidcNativeProtocolTests.SyntheticProvider(federation.Values.Issuer) { FreshEvidence = true };
        using var backchannel = new HttpClient(provider, disposeHandler: false); using var key = new PasskeyAuthenticator();
        await using var test = await CreateNative(method, totp, federation, backchannel);
        var source = await EnrollNativeSource(test, method, key, totp, federation); var frame = await BeginBulkNative(test, method, source);
        var later = await Login(test.Host, test.Database);
        using (var wrongView = await ContinueNative(test, method, frame, Guid.NewGuid())) Assert.Equal(HttpStatusCode.Conflict, wrongView.StatusCode);
        // Fail the real owner only after a native verifier has committed an ACTIVE proof.
        var failures = 0;
        test.Host.Access.BeforeDecision = request =>
        {
            if (request.Operation != SessionBulkRevocationApplication.Scope.Operation) return;
            using var read = test.Database.Context();
            if (read.Set<StepUpProofRow>().Any(x => x.Operation == SessionBulkRevocationApplication.Scope.Operation && x.State == "ACTIVE") &&
                Interlocked.CompareExchange(ref failures, 1, 0) == 0) throw new InvalidOperationException("SYNTHETIC_OWNER_DEPENDENCY_FAILURE");
        };
        HttpResponseMessage verified;
        if (method == "passkey") verified = await test.Host.Post("/authentication/step-up/passkey", new { frame.RequestId, frame.AttemptId, frame.CeremonyId,
            credential = key.Assert(frame.Options, Origin) }, frame.Cookies, frame.Csrf);
        else if (method == "totp") verified = await test.Host.Post("/authentication/step-up/totp", new { frame.RequestId, frame.AttemptId,
            authenticatorReference = source, code = TotpTests.NativeCode(totp.Seed(source), 1) }, frame.Cookies, frame.Csrf);
        else
        {
            await NativeFederationRoundTrip(test, frame, provider);
            using var result = await test.Host.Post("/authentication/step-up/federation/result", new { frame.RequestId }, frame.Cookies, frame.Csrf);
            Assert.Equal(HttpStatusCode.OK, result.StatusCode);
            Assert.Equal("VERIFIED", (await result.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("federation").GetProperty("status").GetString());
            verified = await ContinueNative(test, method, frame);
        }
        using (verified)
        {
            Assert.Equal(HttpStatusCode.ServiceUnavailable, verified.StatusCode); Assert.Equal(1, failures);
            frame = frame with { Cookies = Merge(frame.Cookies, verified) };
        }
        test.Host.Access.BeforeDecision = null;
        await using (var active = test.Database.Context())
        {
            var proof = await active.Set<StepUpProofRow>().SingleAsync(x => x.Operation == SessionBulkRevocationApplication.Scope.Operation);
            Assert.Equal("ACTIVE", proof.State); Assert.Equal(method == "passkey" ? PasskeyPolicy.MethodKey(false, false) : method == "federation" ? "federation:Google" : "totp", proof.AuthenticationMethod);
            Assert.Equal(frame.Original.OperationReference, proof.CorrelationId);
            Assert.Equal(0, await active.Set<OutboxMessage>().CountAsync(x => x.EventType == "TunnerSessionRevoked"));
        }
        using (var recovery = await test.Host.Get("/authentication/step-up/options", frame.Cookies))
        {
            Assert.Equal(HttpStatusCode.OK, recovery.StatusCode); var recovered = await recovery.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(frame.Original.OperationReference, recovered.GetProperty("operationReference").GetString());
            var selected = recovered.GetProperty(method == "passkey" ? "passkeyRecovery" : method == "totp" ? "totpRecovery" : "federation");
            if (method == "federation") selected = selected.GetProperty("recovery");
            Assert.Equal(method == "totp" ? "PROOF_ACTIVE" : "PROOF_ISSUED", selected.GetProperty("status").GetString());
            frame = frame with { Cookies = Merge(frame.Cookies, recovery), Csrf = recovered.GetProperty("csrfToken").GetString()! };
        }
        if (method != "federation") using (var wrongAttempt = await ContinueNative(test, method, frame, wrongAttempt: true)) Assert.Equal(HttpStatusCode.Conflict, wrongAttempt.StatusCode);
        if (switchOwner)
        {
            test.Fallback.Resource = "fixture-new-resource";
            using var independent = await test.Host.Get("/authentication/step-up/options", frame.Cookies);
            Assert.Equal("fixture-correlation", (await independent.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("operationReference").GetString());
            using var denied = await ContinueNative(test, method, frame); Assert.Equal(HttpStatusCode.Conflict, denied.StatusCode);
            using var original = await test.Host.Post(Root + "/result", new { requestId = frame.Original.RequestId }, frame.Cookies, frame.Csrf);
            Assert.Equal(HttpStatusCode.Conflict, original.StatusCode);
        }
        else
        {
            for (var i = 0; i < 2; i++)
            {
                using var continued = await ContinueNative(test, method, frame); Assert.Equal(HttpStatusCode.OK, continued.StatusCode);
                Assert.Equal(frame.Original.OperationReference, (await continued.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("operationReference").GetString());
            }
            using var result = await test.Host.Post(Root + "/result", new { requestId = frame.Original.RequestId }, frame.Cookies, frame.Csrf);
            Assert.Equal(HttpStatusCode.OK, result.StatusCode); var receipts = await result.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(frame.Original.View.GetProperty("targets").EnumerateArray().Select(x => x.GetProperty("reference").GetGuid()).Order(),
                receipts.GetProperty("targets").EnumerateArray().Select(x => x.GetProperty("reference").GetGuid()).Order());
            using var independent = await test.Host.Get("/authentication/step-up/options", frame.Cookies);
            var ordinary = await independent.Content.ReadFromJsonAsync<JsonElement>(); Assert.Equal("fixture-correlation", ordinary.GetProperty("operationReference").GetString());
            using var applied = await test.Host.Post("/authentication/step-up/password", new { requestId = ordinary.GetProperty("requestId").GetGuid(), password = Password },
                Merge(frame.Cookies, independent), ordinary.GetProperty("csrfToken").GetString()); Assert.Equal(HttpStatusCode.OK, applied.StatusCode);
        }
        await using var check = test.Database.Context();
        var final = await check.Set<StepUpProofRow>().SingleAsync(x => x.Operation == SessionBulkRevocationApplication.Scope.Operation);
        Assert.Equal(switchOwner ? "ACTIVE" : "CONSUMED", final.State);
        Assert.Equal(switchOwner ? 0 : frame.Original.View.GetProperty("targets").GetArrayLength(),
            await check.Set<OutboxMessage>().CountAsync(x => x.EventType == "TunnerSessionRevoked"));
        Assert.Equal("ACTIVE", (await check.Set<AuthenticationSessionRow>().SingleAsync(x => x.SessionId == later.SessionId)).State);
        Assert.Equal("ACTIVE", (await check.Set<AuthenticationSessionRow>().SingleAsync(x => x.SessionId == test.Actor.SessionId)).State);
        if (method == "passkey") Assert.Equal(1, await check.Set<PasskeyCredentialRow>().Select(x => x.UsageVersion).SingleAsync());
        if (method == "totp") Assert.Equal(2, await check.Set<TotpUsedCodeRow>().CountAsync());
        if (method == "federation") Assert.Equal(1, provider.TokenRequests);
    }
}
