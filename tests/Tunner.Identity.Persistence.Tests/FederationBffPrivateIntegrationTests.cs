using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Tunner.Accounts;
using Tunner.Database.Outbox;
using Tunner.Platform.Configuration;
using Tunner.Platform.Policies;
using Tunner.Platform.Secrets;
using Tunner.User.Bff;
using Xunit;
using static Tunner.Identity.Persistence.Tests.AuthenticationTestHost;

namespace Tunner.Identity.Persistence.Tests;

// Actual shared HTTPS BFF + official native handler + finite Identity transaction. No provider egress or public test dispatch.
internal sealed class FederationBffPrivateIntegrationTests(IdentityPostgresFixture fixture)
{
    private const string Issuer = "https://accounts.google.com";
    private const string Scheme = "fixture-google-wire";
    private const string Correlation = "__Host-fixture.Google.Correlation.";
    private const string Nonce = "__Host-fixture.Google.Nonce.";

    private sealed class Inputs : IRegistrationPasswordBlocklist, IRegistrationNotificationDispatcher, ISecretStore
    {
        internal int Notifications;
        public ValueTask<bool> IsBlockedAsync(string password, CancellationToken cancellationToken) => ValueTask.FromResult(false);
        public Task QueueAsync(RegistrationNotification notification, CancellationToken cancellationToken)
        { Notifications++; return Task.CompletedTask; }
        public Task<SecretValues> ReadAsync(EngineeringSecretReference reference, CancellationToken cancellationToken = default)
        {
            if (reference.StoreReference != "synthetic" || reference.ResourceReference != "synthetic/google")
                throw new InvalidOperationException("FINITE_SECRET_REFERENCE_INVALID");
            return Task.FromResult(new SecretValues(new Dictionary<string, string>(StringComparer.Ordinal)
                { ["secret"] = FederationOidcNativeProtocolTests.SyntheticProvider.Secret }));
        }
        public override string ToString() => nameof(Inputs);
    }

    private static readonly EffectivePolicyInterval Interval = new(new(2020, 1, 1, 0, 0, 0, TimeSpan.Zero), true,
        new DateTimeOffset(2040, 1, 1, 0, 0, 0, TimeSpan.Zero), false);

    private static EngineeringSecretReference SecretReference()
    {
        const string prefix = EngineeringEnvironmentConfiguration.SectionName + ":";
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [prefix + "Class"] = "EPHEMERAL_TEST", [prefix + "Reference"] = "synthetic-federation-wire",
            [prefix + "Endpoints:provider"] = Issuer, [prefix + "SecretReferences:google:StoreReference"] = "synthetic",
            [prefix + "SecretReferences:google:ResourceReference"] = "synthetic/google"
        }).Build();
        return EngineeringEnvironmentConfiguration.Load(configuration).SecretReferences["google"];
    }

    public async Task ActualHttpsNativeRegistrationRequiresExactPairedContextAndNoticesBeforeSharedOnboarding()
    {
        await using var db = await fixture.CreateFiniteDatabaseAsync(CancellationToken.None);
        using var provider = new FederationOidcNativeProtocolTests.SyntheticProvider(Issuer) { ContactEvidence = true };
        using var backchannel = new HttpClient(provider, disposeHandler: false);
        var inputs = new Inputs(); var age = new FixtureAccountAgePolicies();
        var policy = new RegistrationPolicy("fixture-registration-v2", "PDR-0011", "fixture-processing", "fixture-retention",
            "fixture-terms", "fixture-privacy", "fixture-proof", "fixture-existing", "PASSWORD_AND_VERIFIED_EMAIL", 15, 128, 10_000,
            6, TimeSpan.FromMinutes(5), 2, TimeSpan.FromSeconds(30), 2, TimeSpan.FromMinutes(1), 1000, 1000, 1000, 1000,
            TimeSpan.FromMinutes(30), TimeSpan.FromHours(1));
        var values = new FederationProfile("fixture-google-wire-v1", FederationProviderKind.Google, Origin, Scheme,
            "fixture-google-wire-namespace", Issuer, FederationOidcNativeProtocolTests.SyntheticProvider.ClientId, [],
            "fixture-google-wire-issuer-map", 128, 128, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(3), TimeSpan.FromMinutes(3),
            4, "/login", ["/login"], "fixture-sign-in-admission", "fixture-federation-primary", "fixture-assurance-map", "fixture-session-use",
            "fixture-processing", "fixture-retention") { Registration = new("fixture-google-contact-v1", policy.Version, "fixture-google-verified-email") };
        var profile = new FederationProfileVersion(new(values.Version, "ACTIVE", values.Provider, Interval, null, ["SYNTHETIC-TEST-ONLY"]), values);
        var registration = new FederationOidcRegistration(profile, new(Issuer + "/.well-known/openid-configuration"), SecretReference(), "secret",
            TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(2), TimeSpan.Zero, 16384, 32768, 8, 128, 24576, 16384, Correlation, Nonce);
        await using var host = await Start(db, services =>
        {
            services.AddLogging(logging => logging.ClearProviders());
            services.AddSingleton<IFederationProfileSource>(new FederationProfileCatalog([profile]));
            services.AddSingleton<ITotpMfaRequirementSource>(new TotpTests.Profile());
            services.AddSingleton<IRegistrationPolicySource>(new RegistrationPolicyCatalog([
                new(new(policy.Version, "ACTIVE", "REGISTRATION", Interval, null, ["SYNTHETIC-TEST-ONLY"]), policy)]));
            services.AddSingleton<IRegistrationPasswordBlocklist>(inputs); services.AddSingleton<IRegistrationNotificationDispatcher>(inputs);
            services.AddSingleton<IRegistrationPresentationSource, RegistrationBffTests.Presentation>();
            services.AddSingleton<IAccountAgePolicySource>(age); services.AddSingleton<IAccountConsentEvidenceSource>(age);
            services.AddSingleton(new RegistrationNetworkPartitionKey(RandomNumberGenerator.GetBytes(32)));
            services.AddSingleton<ISecretStore>(inputs); services.AddTunnerFederationOidc(registration);
            services.Configure<OpenIdConnectOptions>(Scheme, options => options.Backchannel = backchannel);
        });
        host.Clock.SynchronizeToSystem();
        var boot = await host.Options();
        var request = new { requestId = boot.Key, provider = "Google", rememberMe = false };
        using var start = await host.Post("/authentication/federation/start", request, boot.Cookies, boot.Csrf);
        Assert.True(start.StatusCode == HttpStatusCode.OK); var cookies = Merge(boot.Cookies, start);
        var started = await start.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(started.GetProperty("challengePath").GetString() == "/authentication/federation/google/challenge");
        await NativeRoundTrip(host, provider, cookies);
        using var options = await host.Get("/registration/options", cookies);
        Assert.True(options.StatusCode == HttpStatusCode.OK); cookies = Merge(cookies, options);
        var body = await options.Content.ReadFromJsonAsync<JsonElement>(); var descriptor = body.GetProperty("federationIntake");
        Assert.True(descriptor.GetProperty("mode").GetString() == "FEDERATED" && descriptor.GetProperty("provider").GetString() == "Google" &&
            descriptor.GetProperty("verifiedEmail").GetString() == FederationOidcNativeProtocolTests.SyntheticProvider.Email && descriptor.GetProperty("canContinue").GetBoolean());
        var notice = new FederationRegistrationAcknowledgement(descriptor.GetProperty("attemptId").GetGuid(), descriptor.GetProperty("federationRequestId").GetGuid(),
            descriptor.GetProperty("registrationKey").GetGuid(), descriptor.GetProperty("expectedVersion").GetInt64(),
            body.GetProperty("termsVersion").GetString()!, body.GetProperty("privacyVersion").GetString()!, true);
        Assert.True(notice.FederationRequestId == boot.Key && notice.RegistrationKey == body.GetProperty("registrationKey").GetGuid());
        var csrf = body.GetProperty("csrfToken").GetString()!;
        using (var missingCsrf = await host.Post("/authentication/federation/complete", notice, cookies, null))
            Assert.True(missingCsrf.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Forbidden);
        using (var foreign = await host.Get("/registration/options"))
        {
            Assert.True(foreign.StatusCode == HttpStatusCode.OK);
            var pair = foreign.Headers.GetValues("Set-Cookie").Select(value => value.Split(';')[0])
                .Single(value => value.StartsWith(RegistrationEndpoints.BrowserCookieName + "=", StringComparison.Ordinal));
            var wrongPair = string.Join("; ", cookies.Split(';').Select(value => value.Trim())
                .Where(value => !value.StartsWith(RegistrationEndpoints.BrowserCookieName + "=", StringComparison.Ordinal)).Append(pair));
            using var rejected = await host.Post("/authentication/federation/complete", notice, wrongPair, csrf);
            Assert.True(rejected.StatusCode == HttpStatusCode.Conflict);
        }
        using (var stale = await host.Post("/authentication/federation/complete", notice with { ExpectedVersion = notice.ExpectedVersion + 1 }, cookies, csrf))
            Assert.True(stale.StatusCode == HttpStatusCode.Conflict);
        using (var unacknowledged = await host.Post("/authentication/federation/complete", notice with { AcknowledgedNotices = false }, cookies, csrf))
            Assert.True(unacknowledged.StatusCode == HttpStatusCode.Conflict);
        await using (var empty = db.Context()) Assert.True(!await empty.Accounts.AnyAsync() && !await empty.Set<RegistrationOperationRow>().AnyAsync());
        using var complete = await host.Post("/authentication/federation/complete", notice, cookies, csrf);
        Assert.True(complete.StatusCode == HttpStatusCode.OK && NoSessionCookie(complete)); cookies = Merge(cookies, complete);
        var completed = await complete.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(!completed.GetProperty("authenticated").GetBoolean());
        var operation = completed.GetProperty("registration").Deserialize<RegistrationResult>(JsonSerializerOptions.Web);
        Assert.True(operation is { ErrorCode: null, NextAction: "PERSONAL_INFORMATION", PersonalInformation: not null });
        using (var original = await host.Post("/authentication/federation/result", new { }, cookies, csrf))
        {
            Assert.True(original.StatusCode == HttpStatusCode.OK && NoSessionCookie(original));
            var restored = await original.Content.ReadFromJsonAsync<JsonElement>();
            Assert.True(!restored.GetProperty("authenticated").GetBoolean() && restored.GetProperty("registration").GetProperty("operationId").GetGuid() == operation!.OperationId);
        }
        using (var progress = await host.Get("/registration/progress", cookies))
        {
            Assert.True(progress.StatusCode == HttpStatusCode.Accepted && NoSessionCookie(progress));
            var current = await progress.Content.ReadFromJsonAsync<RegistrationResult>();
            Assert.True(current is { Status: "REQUIRES_ACTION", NextAction: "PERSONAL_INFORMATION" } && current.OperationId == operation!.OperationId);
        }
        await using (var pending = db.Context())
        {
            Assert.True(await pending.Accounts.AllAsync(value => value.State == AccountState.VerificationPending) &&
                await pending.Users.AllAsync(value => value.PasswordHash == null && value.EmailConfirmed) && !await pending.Set<AuthenticationSessionRow>().AnyAsync());
        }
        var personal = new CompletePersonalInformationCommand { OperationId = operation!.OperationId, IdempotencyKey = Guid.NewGuid(),
            ExpectedVersion = operation.Version, FullName = "Synthetic Person", DisplayName = "Synthetic", DateOfBirth = new(2015, 4, 6) };
        using (var activated = await host.Post("/registration/personal-information", personal, cookies, csrf))
        {
            Assert.True(activated.StatusCode == HttpStatusCode.OK && NoSessionCookie(activated));
            var result = await activated.Content.ReadFromJsonAsync<RegistrationResult>();
            Assert.True(result is { Status: "SUCCEEDED", NextAction: "COMPLETE", AgeCapabilities: { AgeClassification: "UNDER_18", AgeAllowsAccountLifecycle: true } });
        }
        // Registration itself issues no grant. Only a new ordinary linked SignIn may establish the actual native BFF cookie/session.
        await using (var registered = db.Context()) Assert.True(!await registered.Set<AuthenticationSessionRow>().AnyAsync());
        var linked = await host.Options(cookies); cookies = linked.Cookies; var linkedRequest = Guid.NewGuid();
        using (var linkedStart = await host.Post("/authentication/federation/start", new { requestId = linkedRequest, provider = "Google", rememberMe = false }, cookies, linked.Csrf))
        { Assert.True(linkedStart.StatusCode == HttpStatusCode.OK); cookies = Merge(cookies, linkedStart); }
        await NativeRoundTrip(host, provider, cookies);
        using (var signed = await host.Post("/authentication/federation/complete", new { }, cookies, linked.Csrf))
        {
            Assert.True(signed.StatusCode == HttpStatusCode.OK && !NoSessionCookie(signed)); cookies = Merge(cookies, signed);
            Assert.True((await signed.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("authenticated").GetBoolean());
        }
        using (var live = await host.Get("/authentication/session", cookies)) Assert.True(live.StatusCode == HttpStatusCode.OK);
        await using var final = db.Context();
        Assert.True(await final.Accounts.CountAsync() == 1 && await final.Accounts.AllAsync(value => value.State == AccountState.Active) &&
            await final.ExternalIdentityLinks.CountAsync() == 1 && await final.Set<RegistrationOperationRow>().CountAsync() == 1 &&
            await final.Set<AuthenticationSessionRow>().CountAsync() == 1 && inputs.Notifications == 0 && provider.TokenRequests == 2);
        Assert.True(await final.Set<OutboxMessage>().CountAsync(value => value.EventType == "AccountActivated") == 1);
    }

    private static bool NoSessionCookie(HttpResponseMessage response) => !response.Headers.TryGetValues("Set-Cookie", out var values) ||
        !values.Any(value => value.StartsWith(AuthenticationIdentity.CookieName + "=", StringComparison.Ordinal));

    private static async Task NativeRoundTrip(AuthenticationTestHost host, FederationOidcNativeProtocolTests.SyntheticProvider provider, string cookies)
    {
        using var challenge = await host.GoogleFederationChallenge(cookies);
        Assert.True(challenge.StatusCode == HttpStatusCode.Redirect && challenge.Headers.Location is { Scheme: "https", Host: "accounts.google.com" });
        var query = QueryHelpers.ParseQuery(challenge.Headers.Location!.Query);
        Assert.True(query["response_type"] == "code" && query["response_mode"] == "form_post" && query["code_challenge_method"] == "S256");
        var code = provider.Admit(query["nonce"].ToString(), query["code_challenge"].ToString());
        var protocol = challenge.Headers.GetValues("Set-Cookie").Select(value => value.Split(';')[0]).ToArray();
        Assert.True(protocol.Length == 2 && protocol.All(value => value.StartsWith(Correlation, StringComparison.Ordinal) || value.StartsWith(Nonce, StringComparison.Ordinal)));
        using var callback = await host.GoogleFederationCallback(query["state"].ToString(), code, string.Join("; ", protocol));
        Assert.True(callback.StatusCode == HttpStatusCode.SeeOther && callback.Headers.Location?.OriginalString == "/login" && NoSessionCookie(callback) &&
            string.IsNullOrEmpty(await callback.Content.ReadAsStringAsync()) && provider.PkceAndConfidentialClientObserved);
    }
}
