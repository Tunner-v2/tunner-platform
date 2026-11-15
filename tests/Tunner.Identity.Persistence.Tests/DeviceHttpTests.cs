using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Http;
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

public sealed class DeviceHttpTests(SessionListingPostgresFixture fixture) : IClassFixture<SessionListingPostgresFixture>
{
    private const string Root = "/account/devices";
    private static readonly string[] FederationMethods = ["mfa", "pwd"];
    private sealed class Profile : IDeviceHttpBudgetSource, IDevicePresentationSource, IAssurancePolicySource, IStepUpPresentationSource
    {
        internal string BudgetVersion { get; set; } = "synthetic-device-http-v1";
        internal string PublicationVersion { get; set; } = "synthetic-device-copy-v1";
        internal bool Published { get; set; } = true;
        internal int MaximumCookie { get; set; } = 4096;
        internal string RequiredAssurance { get; set; } = "fixture-password";
        internal Dictionary<string, string> Methods { get; } = new() { ["pwd"] = "fixture-password" };
        internal Dictionary<AssurancePolicyScope, string> ScopeVersions { get; } = new();
        internal IStepUpPresentationSource? NativePresentation { get; set; }
        internal TimeSpan ProofLifetime { get; set; } = TimeSpan.FromMinutes(1);
        private static EffectivePolicyInterval Interval => new(new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), true, new(2027, 1, 1, 0, 0, 0, TimeSpan.Zero), false);
        public DeviceHttpBudget RequireCurrent(DateTimeOffset at) => new(new(BudgetVersion, "ACTIVE", DeviceHttpBudget.Scope, Interval, null, ["SYNTHETIC-ONLY"]),
            1, 4096, MaximumCookie, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(3));
        public DevicePresentation RequirePublished(AuthenticationPolicy authenticationPolicy, string devicePolicyVersion) => new("en",
            DevicePresentation.RequiredKeys.Select(key => new RegistrationPublishedText(key, "synthetic-device", PublicationVersion, "SYNTHETIC-ONLY", "en", Published ? "PUBLISHED" : "DRAFT", "Fixture " + key)));
        public AssurancePolicyVersion RequireEffective(AssurancePolicyScope scope, DateTimeOffset at, string? expectedVersion = null) =>
            new AssurancePolicyCatalog([new(new(ScopeVersions.GetValueOrDefault(scope, scope == DeviceApplication.RevokeScope ? "synthetic-device-proof" : "fixture-step-v1"), "ACTIVE", scope,
                Interval, null, ["SYNTHETIC-ONLY"]), RequiredAssurance, ProofLifetime, Methods)])
                .RequireEffective(scope, at, expectedVersion);
        StepUpPresentation IStepUpPresentationSource.RequirePublished(AssurancePolicyVersion policy) => NativePresentation?.RequirePublished(policy) ?? new StepUpBrowserTests.Presentation().RequirePublished(policy);
    }
    private sealed class CompanionSessions : ISessionListingHttpBudgetSource, ISessionRevocationPresentationSource,
        ISessionBulkRevocationDiscoverySource, ISessionBulkRevocationHttpBudgetSource, ISessionBulkRevocationPresentationSource
    {
        private static PolicyVersionCandidate<string> Metadata(string scope) => new("synthetic-companion-" + scope, "ACTIVE", scope,
            new(new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), true, new(2027, 1, 1, 0, 0, 0, TimeSpan.Zero), false), null, ["SYNTHETIC-ONLY"]);
        public SessionListingHttpBudget RequireEffective(DateTimeOffset at) => new(Metadata(SessionListingHttpBudget.Scope), 20, 4096);
        SessionBulkRevocationDiscoveryBudget ISessionBulkRevocationDiscoverySource.RequireEffective(DateTimeOffset at) => new(Metadata(SessionBulkRevocationDiscoveryBudget.Scope), 20);
        SessionBulkRevocationHttpBudget ISessionBulkRevocationHttpBudgetSource.RequireEffective(DateTimeOffset at) => new(Metadata(SessionBulkRevocationHttpBudget.Scope), 4096, 4096);
        private static IEnumerable<RegistrationPublishedText> Text(IEnumerable<string> keys) => keys.Select(key => new RegistrationPublishedText(key,
            "synthetic", "synthetic-v1", "SYNTHETIC-ONLY", "en", "PUBLISHED", "Fixture " + key));
        SessionRevocationPresentation ISessionRevocationPresentationSource.RequirePublished(AuthenticationPolicy policy) => new("en", Text(SessionRevocationPresentation.RequiredKeys));
        SessionBulkRevocationPresentation ISessionBulkRevocationPresentationSource.RequirePublished(AuthenticationPolicy policy) => new("en", Text(SessionBulkRevocationPresentation.RequiredKeys));
        internal static void Configure(IServiceCollection services)
        {
            var profile = new CompanionSessions(); services.AddSingleton<ISessionListingHttpBudgetSource>(profile);
            services.AddSingleton<ISessionRevocationPresentationSource>(profile); services.AddSingleton<ISessionBulkRevocationDiscoverySource>(profile);
            services.AddSingleton<ISessionBulkRevocationHttpBudgetSource>(profile); services.AddSingleton<ISessionBulkRevocationPresentationSource>(profile);
            services.AddTunnerSessionRevocation(); services.AddTunnerSessionBulkRevocation();
        }
    }
    private sealed record Browser(string Cookies, string Csrf, Guid SessionId);
    private sealed record Draft(Guid RequestId, string Cookies, string Csrf);
    private sealed record Original(Guid RequestId, string OperationReference, string Cookies, string Csrf, JsonElement Target, JsonElement View);
    private sealed class Scenario(IdentityTestDatabase db, AuthenticationTestHost host, Profile profile, DeviceTrustTests.Qualification qualification,
        StepUpBrowserTests.Owner fallback, Browser actor) : IAsyncDisposable
    {
        internal IdentityTestDatabase Database { get; } = db;
        internal AuthenticationTestHost Host { get; } = host;
        internal Profile Profile { get; } = profile;
        internal DeviceTrustTests.Qualification Qualification { get; } = qualification;
        internal StepUpBrowserTests.Owner Fallback { get; } = fallback;
        internal Browser Actor { get; } = actor;
        public async ValueTask DisposeAsync() { await Host.DisposeAsync(); await Database.DisposeAsync(); }
    }
    private async Task<Scenario> Create(bool enabled = true,
        Func<IdentityTestDatabase, Action<IServiceCollection>, Task<AuthenticationTestHost>>? start = null,
        Action<IServiceCollection, Profile>? configure = null)
    {
        var db = await fixture.CreateDatabaseAsync(); var profile = new Profile(); var qualification = new DeviceTrustTests.Qualification();
        await using (var setup = db.Context()) await setup.Database.ExecuteSqlRawAsync("CREATE TABLE identity.step_up_browser_target (id integer PRIMARY KEY, version bigint NOT NULL, value integer NOT NULL, session_id uuid NULL, operation_id text NULL); INSERT INTO identity.step_up_browser_target VALUES(1,1,0,NULL,NULL);");
        var fallback = new StepUpBrowserTests.Owner(db);
        var host = await (start ?? ((database, setup) => Start(database, setup)))(db, services =>
        {
            services.AddSingleton<IAssurancePolicySource>(profile); services.AddSingleton<IStepUpPresentationSource>(profile);
            services.AddSingleton<IUserStepUpOperationSource>(fallback);
            services.AddSingleton<IDeviceEvidencePolicySource>(qualification); services.AddSingleton<IDeviceSafeCharacteristicSource>(qualification);
            services.AddSingleton<IDeviceTrustEligibilitySource>(qualification);
            if (enabled)
            {
                services.AddSingleton<IDeviceHttpBudgetSource>(profile); services.AddSingleton<IDevicePresentationSource>(profile); services.AddTunnerDevices();
            }
            configure?.Invoke(services, profile);
        });
        try { await host.SeedAsync(db); return new(db, host, profile, qualification, fallback, await Login(host, db)); }
        catch { await host.DisposeAsync(); await db.DisposeAsync(); throw; }
    }
    private static async Task<Browser> Login(AuthenticationTestHost host, IdentityTestDatabase db, string? existing = null, string email = "person@example.test")
    {
        var boot = await host.Options(existing); using var signed = await host.Post("/authentication/sign-in", Command(boot.Key, email: email), boot.Cookies, boot.Csrf);
        Assert.Equal(HttpStatusCode.OK, signed.StatusCode); var cookies = Merge(boot.Cookies, signed);
        using var current = await host.Get("/authentication/session", cookies); Assert.Equal(HttpStatusCode.OK, current.StatusCode);
        var json = await current.Content.ReadFromJsonAsync<JsonElement>(); await using var read = db.Context();
        var id = await read.Set<AuthenticationSessionRow>().Where(x => x.IdempotencyKey == boot.Key).Select(x => x.SessionId).SingleAsync();
        return new(Merge(cookies, current), json.GetProperty("csrfToken").GetString()!, id);
    }
    private static async Task<Draft> Options(Scenario test, Browser? actor = null)
    {
        actor ??= test.Actor; using var response = await test.Host.Get(Root + "/options", actor.Cookies); Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Headers(response); var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(DevicePresentation.RequiredKeys.Count, json.GetProperty("content").EnumerateObject().Count());
        return new(json.GetProperty("requestId").GetGuid(), Merge(actor.Cookies, response), actor.Csrf);
    }
    private static async Task<JsonElement> Device(Scenario test, Browser? actor = null)
    {
        actor ??= test.Actor; using var response = await test.Host.Post(Root + "/list", new { }, actor.Cookies, actor.Csrf);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode); Headers(response);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("devices")[0].Clone();
    }
    private static object Confirm(Draft draft, JsonElement device, bool confirmed = true) => new {
        requestId = draft.RequestId, reference = device.GetProperty("reference").GetGuid(), expectedVersion = device.GetProperty("version").GetString(),
        expectedTrustVersion = device.GetProperty("trustVersion").GetString(), confirmed };
    private static async Task<Original> Prepare(Scenario test, Draft? draft = null, JsonElement? device = null)
    {
        draft ??= await Options(test); device ??= await Device(test);
        using var response = await test.Host.Post(Root + "/prepare", Confirm(draft, device.Value), draft.Cookies, draft.Csrf);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode); var view = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("PENDING", view.GetProperty("status").GetString()); Assert.Equal("PRESERVE_EXISTING_SESSIONS", view.GetProperty("sessionEffect").GetString());
        return new(draft.RequestId, view.GetProperty("operationReference").GetString()!, Merge(draft.Cookies, response), draft.Csrf, device.Value, view);
    }
    private static async Task<(Guid RequestId, string Cookies, string Csrf)> ProofOptions(Scenario test, Original original)
    {
        using var response = await test.Host.Get("/authentication/step-up/options", original.Cookies); Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(); Assert.Equal(original.OperationReference, json.GetProperty("operationReference").GetString());
        return (json.GetProperty("requestId").GetGuid(), Merge(original.Cookies, response), json.GetProperty("csrfToken").GetString()!);
    }
    private static void Headers(HttpResponseMessage response) => Assert.True(response.Headers.CacheControl?.NoStore == true &&
        response.Headers.GetValues("Referrer-Policy").Single() == "no-referrer" && response.Headers.GetValues("X-Content-Type-Options").Single() == "nosniff");
    private static string Only(string cookies, string name) => cookies.Split(';', StringSplitOptions.TrimEntries).Single(x => x.StartsWith(name + "=", StringComparison.Ordinal));
    private static string Replace(string cookies, string name, string selected) => string.Join("; ", cookies.Split(';', StringSplitOptions.TrimEntries)
        .Where(x => !x.StartsWith(name + "=", StringComparison.Ordinal)).Append(selected));
    private static async Task NoRemoval(Scenario test, int operations)
    {
        await using var db = test.Database.Context(); Assert.Equal(operations, await db.Set<DeviceOperationRow>().CountAsync());
        Assert.Equal(0, await db.Set<DeviceTrustRow>().CountAsync(x => x.State == "REVOKED"));
        Assert.Equal(0, await db.Set<OutboxMessage>().CountAsync(x => x.EventType == "DeviceTrustRevoked" || x.EventType == "StepUpProofConsumed"));
    }

    [Fact]
    public async Task RealHttpsListDetailsConfirmAndSharedPasswordProofRemoveTrustWithoutSigningOut()
    {
        await using var test = await Create(); var device = await Device(test);
        using (var details = await test.Host.Post(Root + "/details", new { reference = device.GetProperty("reference").GetGuid() }, test.Actor.Cookies, test.Actor.Csrf))
        { Assert.Equal(HttpStatusCode.OK, details.StatusCode); Assert.Equal(device.GetRawText(), (await details.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("device").GetRawText()); }
        var original = await Prepare(test, device: device); var proof = await ProofOptions(test, original);
        using (var result = await test.Host.Post("/authentication/step-up/password", new { requestId = proof.RequestId, password = Password }, proof.Cookies, proof.Csrf))
        { Assert.Equal(HttpStatusCode.OK, result.StatusCode); var json = await result.Content.ReadFromJsonAsync<JsonElement>(); Assert.Equal("COMPLETED", json.GetProperty("status").GetString()); Assert.False(json.GetProperty("signInRequired").GetBoolean()); }
        using var read = await test.Host.Post(Root + "/result", new { requestId = original.RequestId }, proof.Cookies, proof.Csrf); Assert.Equal(HttpStatusCode.OK, read.StatusCode); Headers(read);
        var receipt = await read.Content.ReadFromJsonAsync<JsonElement>(); Assert.Equal("REVOKED", receipt.GetProperty("status").GetString());
        Assert.Equal(original.OperationReference, receipt.GetProperty("operationReference").GetString());
        Assert.Equal("1", receipt.GetProperty("target").GetProperty("priorTrustVersion").GetString()); Assert.Equal("2", receipt.GetProperty("target").GetProperty("trustVersion").GetString());
        using var current = await test.Host.Get("/authentication/session", proof.Cookies); Assert.Equal(HttpStatusCode.OK, current.StatusCode);
        using var context = await test.Host.Get(Root + "/context", proof.Cookies); Assert.Equal("COMPLETED", (await context.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetString());
        using var duplicate = await test.Host.Post("/authentication/step-up/password", new { requestId = proof.RequestId, password = Password }, proof.Cookies, proof.Csrf); Assert.Equal(HttpStatusCode.OK, duplicate.StatusCode);
        await using var persisted = test.Database.Context(); Assert.Equal(1, await persisted.Set<OutboxMessage>().CountAsync(x => x.EventType == "DeviceTrustRevoked"));
        Assert.Equal(1, await persisted.Set<StepUpProofRow>().CountAsync(x => x.State == "CONSUMED"));
        Assert.Equal(0, await persisted.Set<OutboxMessage>().CountAsync(x => x.EventType == "TunnerSessionRevoked"));
        var text = device.GetRawText() + await read.Content.ReadAsStringAsync();
        foreach (var forbidden in new[] { "CorrelationDigest", "PolicySnapshot", "ProcessingReference", "RetentionReference", "AccountId", "BrowserDigest", Password, "person@example.test" })
            Assert.DoesNotContain(forbidden, text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DraftAndKeepTrustedCreateNoOperationOrProofSelectionAndDoNotMaskFallback()
    {
        await using var test = await Create(); var draft = await Options(test); var device = await Device(test);
        Assert.DoesNotContain(DeviceBrowserEndpoints.OriginalCookie + "=", draft.Cookies, StringComparison.Ordinal);
        using var rejected = await test.Host.Post(Root + "/prepare", Confirm(draft, device, false), draft.Cookies, draft.Csrf); Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        using var absent = await test.Host.Get(Root + "/context", draft.Cookies); Assert.Equal(HttpStatusCode.NotFound, absent.StatusCode);
        using var proof = await test.Host.Get("/authentication/step-up/options", draft.Cookies); Assert.Equal(HttpStatusCode.OK, proof.StatusCode);
        Assert.Equal("fixture-correlation", (await proof.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("operationReference").GetString());
        await NoRemoval(test, 0);
    }

    [Fact]
    public async Task ExactDraftRetryRecoversOriginalAfterLostPrepareResponseWithoutDeadlineRenewalOrResnapshot()
    {
        await using var test = await Create(); var draft = await Options(test); var device = await Device(test); var original = await Prepare(test, draft, device);
        test.Host.Clock.Advance(TimeSpan.FromSeconds(5));
        using var retry = await test.Host.Post(Root + "/prepare", Confirm(draft, device), draft.Cookies, draft.Csrf); Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        var repeated = await retry.Content.ReadFromJsonAsync<JsonElement>(); Assert.Equal(original.View.GetRawText(), repeated.GetRawText());
        await NoRemoval(test, 1);
        using var rebound = await test.Host.Post(Root + "/prepare", new { requestId = draft.RequestId, reference = device.GetProperty("reference").GetGuid(), expectedVersion = "2", expectedTrustVersion = "1", confirmed = true }, draft.Cookies, draft.Csrf);
        Assert.Equal(HttpStatusCode.Conflict, rebound.StatusCode);
    }

    [Theory]
    [InlineData("csrf")]
    [InlineData("origin")]
    [InlineData("anonymous")]
    public async Task PrivateMutationsRequireActualAuthenticationSameOriginAndAntiforgery(string kind)
    {
        await using var test = await Create(); var draft = await Options(test); var device = await Device(test);
        using var result = await test.Host.Post(Root + "/prepare", Confirm(draft, device), kind == "anonymous" ? null : draft.Cookies,
            kind == "csrf" ? null : draft.Csrf, kind == "origin" ? "https://other.example.test" : Origin);
        Assert.True(result.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized); await NoRemoval(test, 0);
    }

    [Theory]
    [InlineData("{\"continuation\":null}")]
    [InlineData("{\"continuation\":\"\"}")]
    [InlineData("{\"continuation\":42}")]
    [InlineData("{\"accountId\":\"foreign\"}")]
    public async Task ListingRejectsAmbiguousOrUnexpectedInputs(string body)
    {
        await using var test = await Create(); using var response = await test.Host.RawPost(Root + "/list", body, test.Actor.Cookies, test.Actor.Csrf);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode); await NoRemoval(test, 0);
    }

    [Fact]
    public async Task ProtectedCursorIsBoundToActorPolicyAndOriginalLifetime()
    {
        await using var test = await Create(); _ = await Login(test.Host, test.Database); var other = await Login(test.Host, test.Database);
        using var first = await test.Host.Post(Root + "/list", new { }, test.Actor.Cookies, test.Actor.Csrf); var json = await first.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(json.GetProperty("hasMore").GetBoolean()); var continuation = json.GetProperty("continuation").GetString(); Assert.NotNull(continuation);
        using var next = await test.Host.Post(Root + "/list", new { continuation }, test.Actor.Cookies, test.Actor.Csrf); Assert.Equal(HttpStatusCode.OK, next.StatusCode);
        using var foreign = await test.Host.Post(Root + "/list", new { continuation }, other.Cookies, other.Csrf); Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        test.Profile.BudgetVersion = "synthetic-new-budget";
        using var changed = await test.Host.Post(Root + "/list", new { continuation }, test.Actor.Cookies, test.Actor.Csrf); Assert.Equal(HttpStatusCode.Conflict, changed.StatusCode);
        test.Profile.BudgetVersion = "synthetic-device-http-v1"; test.Host.Clock.Advance(TimeSpan.FromSeconds(31));
        using var expired = await test.Host.Post(Root + "/list", new { continuation }, test.Actor.Cookies, test.Actor.Csrf); Assert.Equal(HttpStatusCode.NotFound, expired.StatusCode);
    }

    [Fact]
    public async Task ForeignDeviceAndOriginalCookiesCannotBeDisclosedOrSelected()
    {
        await using var test = await Create(); var original = await Prepare(test); await test.Host.SeedAsync(test.Database, foreignEmail: true);
        var foreign = await Login(test.Host, test.Database, email: "foreign@example.test");
        using var details = await test.Host.Post(Root + "/details", new { reference = original.Target.GetProperty("reference").GetGuid() }, foreign.Cookies, foreign.Csrf);
        Assert.Equal(HttpStatusCode.NotFound, details.StatusCode);
        var stolen = Replace(foreign.Cookies, DeviceBrowserEndpoints.OriginalCookie, Only(original.Cookies, DeviceBrowserEndpoints.OriginalCookie));
        using var context = await test.Host.Get(Root + "/context", stolen); Assert.Equal(HttpStatusCode.NotFound, context.StatusCode);
        using var result = await test.Host.Post(Root + "/result", new { requestId = original.RequestId }, stolen, foreign.Csrf); Assert.Equal(HttpStatusCode.NotFound, result.StatusCode);
        await NoRemoval(test, 1);
    }

    [Fact]
    public async Task UncertainOriginalCannotBeReplacedAndExpiryDoesNotRenewSelection()
    {
        await using var test = await Create(); var original = await Prepare(test); var device = original.Target;
        using var options = await test.Host.Get(Root + "/options", original.Cookies); var draftJson = await options.Content.ReadFromJsonAsync<JsonElement>();
        var cookies = Merge(original.Cookies, options);
        using var rebound = await test.Host.Post(Root + "/prepare", new { requestId = Guid.NewGuid(), reference = device.GetProperty("reference").GetGuid(), expectedVersion = "1", expectedTrustVersion = "1", confirmed = true }, cookies, original.Csrf);
        Assert.Equal(HttpStatusCode.Conflict, rebound.StatusCode);
        test.Host.Clock.Advance(TimeSpan.FromSeconds(31));
        using var context = await test.Host.Get(Root + "/context", cookies); var view = await context.Content.ReadFromJsonAsync<JsonElement>(); Assert.Equal("CHANGED", view.GetProperty("status").GetString());
        Assert.Equal(original.View.GetProperty("expiresAt").GetString(), view.GetProperty("expiresAt").GetString());
        using var retry = await test.Host.Post(Root + "/prepare", new { requestId = original.RequestId, reference = device.GetProperty("reference").GetGuid(), expectedVersion = "1", expectedTrustVersion = "1", confirmed = true }, cookies, original.Csrf);
        Assert.Equal("CHANGED", (await retry.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetString());
        await NoRemoval(test, 1);
    }

    [Theory]
    [InlineData("publication")]
    [InlineData("cookie-budget")]
    public async Task UnqualifiedPublicationOrCookieCapacityCannotSelectAnOperation(string kind)
    {
        await using var test = await Create(); if (kind == "publication") test.Profile.Published = false; else test.Profile.MaximumCookie = 64;
        using var response = await test.Host.Get(Root + "/options", test.Actor.Cookies); Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        await NoRemoval(test, 0); Assert.False(response.Headers.TryGetValues("Set-Cookie", out var cookies) && cookies.Any(x => x.StartsWith(DeviceBrowserEndpoints.OriginalCookie, StringComparison.Ordinal)));
    }

    [Theory]
    [InlineData("transport")]
    [InlineData("publication")]
    public async Task FinalBrowserQualificationChangeRollsBackDeviceMutationAndProof(string kind)
    {
        await using var test = await Create(); var original = await Prepare(test); var proof = await ProofOptions(test, original);
        var accessor = test.Host.Services.GetRequiredService<IHttpContextAccessor>();
        test.Host.Access.BeforeDecision = _ =>
        {
            if (accessor.HttpContext?.RequestServices.GetRequiredService<IdentityPersistenceDbContext>().ChangeTracker.Entries<DeviceOperationRow>().Any(x => x.Entity.State == "COMPLETED") == true)
            { if (kind == "transport") test.Profile.BudgetVersion = "synthetic-changed"; else test.Profile.PublicationVersion = "synthetic-changed"; }
        };
        using var response = await test.Host.Post("/authentication/step-up/password", new { requestId = proof.RequestId, password = Password }, proof.Cookies, proof.Csrf);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode); test.Host.Access.BeforeDecision = null;
        await NoRemoval(test, 1); await using var db = test.Database.Context(); Assert.Equal("PENDING", (await db.Set<DeviceOperationRow>().SingleAsync()).State);
        Assert.Equal("ACTIVE", (await db.Set<StepUpProofRow>().SingleAsync()).State);
    }

    [Fact]
    public async Task UnchangedFallbackCompletesNormallyWhenDeviceIsNotSelected()
    {
        await using var test = await Create(); var draft = await Options(test);
        using var options = await test.Host.Get("/authentication/step-up/options", draft.Cookies); var frame = await options.Content.ReadFromJsonAsync<JsonElement>();
        using var completed = await test.Host.Post("/authentication/step-up/password", new { requestId = frame.GetProperty("requestId").GetGuid(), password = Password },
            Merge(draft.Cookies, options), frame.GetProperty("csrfToken").GetString());
        Assert.Equal(HttpStatusCode.OK, completed.StatusCode); await using var read = test.Database.Context();
        Assert.Equal(1, await read.Database.SqlQueryRaw<int>("SELECT value AS \"Value\" FROM identity.step_up_browser_target WHERE id=1").SingleAsync());
        Assert.Equal(0, await read.Set<DeviceOperationRow>().CountAsync()); Assert.Equal(0, await read.Set<DeviceTrustRow>().CountAsync(x => x.State == "REVOKED"));
    }

    [Fact]
    public async Task OptionalDeviceTransportInstallsNoRoutesOrCollectionWhenAbsent()
    {
        await using var test = await Create(enabled: false); using var response = await test.Host.Get(Root + "/options", test.Actor.Cookies);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode); await using var db = test.Database.Context(); Assert.Equal(0, await db.Set<DeviceRecordRow>().CountAsync());
    }

    [Fact]
    public async Task ProtectedProofViewCannotBeBorrowedByAnotherActorsOriginal()
    {
        await using var test = await Create(); var original = await Prepare(test); var first = await ProofOptions(test, original);
        var other = await Login(test.Host, test.Database); var otherOriginal = await Prepare(test, await Options(test, other), await Device(test, other));
        var frame = await ProofOptions(test, otherOriginal); var substituted = Replace(frame.Cookies, StepUpBrowserEndpoints.CookieName, Only(first.Cookies, StepUpBrowserEndpoints.CookieName));
        using var denied = await test.Host.Post("/authentication/step-up/password", new { requestId = first.RequestId, password = Password }, substituted, frame.Csrf);
        Assert.Equal(HttpStatusCode.Conflict, denied.StatusCode); await NoRemoval(test, 2);
    }

    [Theory]
    [InlineData("passkey", false)]
    [InlineData("totp", false)]
    [InlineData("passkey", true)]
    [InlineData("totp", true)]
    public async Task NativeProofContinuationKeepsExactDeviceOriginalAndPreservesIndependentOwner(string method, bool switchOwner)
    {
        var totp = new TotpTests.Profile(); using var key = new PasskeyAuthenticator();
        Task<AuthenticationTestHost> Starter(IdentityTestDatabase db, Action<IServiceCollection> setup) => method == "passkey"
            ? PasskeyTests.StartRecoveryFixtureAsync(db, setup) : TotpTests.StartTotp(db, totp, setup);
        await using var test = await Create(start: Starter, configure: (services, profile) =>
        {
            if (method == "passkey")
            {
                profile.Methods.Add(PasskeyPolicy.MethodKey(false, false), profile.RequiredAssurance);
                profile.ScopeVersions.Add(PasskeyApplication.EnrollmentScope, "fixture-enrollment-v1");
                profile.ScopeVersions.Add(PasskeyApplication.RevocationScope, "fixture-revocation-v1");
            }
            else
            {
                profile.RequiredAssurance = totp.Policy.OperationProofAssurance; profile.Methods["pwd"] = profile.RequiredAssurance;
                profile.Methods.Add("totp", profile.RequiredAssurance); profile.NativePresentation = totp;
                foreach (var scope in new[] { TotpApplication.EnrollmentScope, TotpApplication.DisableScope, TotpApplication.ResetScope }) profile.ScopeVersions.Add(scope, totp.AssuranceVersion);
            }
        });
        var source = method == "passkey" ? (await PasskeyTests.SeedRecoveryCredentialAsync(test.Host, key)).Reference :
            (await TotpTests.Enroll(test.Host, totp, await TotpTests.Login(test.Host))).Reference;
        var original = await Prepare(test); var frame = await ProofOptions(test, original); var attemptId = Guid.NewGuid(); Guid ceremonyId = Guid.Empty; JsonElement credential = default;
        if (method == "passkey")
        {
            using var options = await test.Host.Post("/authentication/step-up/passkey/options", new { requestId = frame.RequestId, attemptId }, frame.Cookies, frame.Csrf);
            Assert.Equal(HttpStatusCode.OK, options.StatusCode); var native = await options.Content.ReadFromJsonAsync<JsonElement>();
            ceremonyId = native.GetProperty("ceremonyId").GetGuid(); credential = key.Assert(native.GetProperty("publicKey"), Origin); frame.Cookies = Merge(frame.Cookies, options);
        }
        var failure = 0;
        test.Host.Access.BeforeDecision = request =>
        {
            if (request.Operation != DeviceApplication.RevokeScope.Operation) return;
            using var read = test.Database.Context();
            if (read.Set<StepUpProofRow>().Any(x => x.Operation == DeviceApplication.RevokeScope.Operation && x.State == "ACTIVE") &&
                Interlocked.CompareExchange(ref failure, 1, 0) == 0) throw new InvalidOperationException("SYNTHETIC_DEVICE_OWNER_FAILURE_AFTER_NATIVE_PROOF");
        };
        using (var verified = method == "passkey"
            ? await test.Host.Post("/authentication/step-up/passkey", new { requestId = frame.RequestId, attemptId, ceremonyId, credential }, frame.Cookies, frame.Csrf)
            : await test.Host.Post("/authentication/step-up/totp", new { requestId = frame.RequestId, attemptId, authenticatorReference = source, code = TotpTests.NativeCode(totp.Seed(source), 1) }, frame.Cookies, frame.Csrf))
        { Assert.Equal(HttpStatusCode.ServiceUnavailable, verified.StatusCode); Assert.Equal(1, failure); frame.Cookies = Merge(frame.Cookies, verified); }
        test.Host.Access.BeforeDecision = null;
        using (var recovery = await test.Host.Get("/authentication/step-up/options", frame.Cookies))
        {
            Assert.Equal(HttpStatusCode.OK, recovery.StatusCode); var current = await recovery.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(original.OperationReference, current.GetProperty("operationReference").GetString());
            Assert.Equal(method == "passkey" ? "PROOF_ISSUED" : "PROOF_ACTIVE", current.GetProperty(method == "passkey" ? "passkeyRecovery" : "totpRecovery").GetProperty("status").GetString());
            frame.Cookies = Merge(frame.Cookies, recovery); frame.Csrf = current.GetProperty("csrfToken").GetString()!;
        }
        if (switchOwner) test.Fallback.Resource = "synthetic-independent-new-resource";
        for (var retry = 0; retry < 2; retry++)
        {
            using var continued = await test.Host.Post("/authentication/step-up/" + method + "/continue", method == "passkey"
                ? (object)new { requestId = frame.RequestId, attemptId, ceremonyId }
                : new { requestId = frame.RequestId, attemptId, authenticatorReference = source }, frame.Cookies, frame.Csrf);
            Assert.Equal(switchOwner ? HttpStatusCode.Conflict : HttpStatusCode.OK, continued.StatusCode);
        }
        using var result = await test.Host.Post(Root + "/result", new { requestId = original.RequestId }, frame.Cookies, frame.Csrf);
        Assert.Equal(switchOwner ? HttpStatusCode.Conflict : HttpStatusCode.OK, result.StatusCode);
        await using var persisted = test.Database.Context(); var proof = await persisted.Set<StepUpProofRow>().SingleAsync(x => x.Operation == DeviceApplication.RevokeScope.Operation);
        Assert.Equal(switchOwner ? "ACTIVE" : "CONSUMED", proof.State);
        Assert.Equal(switchOwner ? 0 : 1, await persisted.Set<OutboxMessage>().CountAsync(x => x.EventType == "DeviceTrustRevoked"));
        Assert.Equal("ACTIVE", (await persisted.Set<AuthenticationSessionRow>().SingleAsync(x => x.SessionId == test.Actor.SessionId)).State);
    }

    private sealed class FederationPublication : IStepUpPresentationSource
    {
        public StepUpPresentation RequirePublished(AssurancePolicyVersion policy)
        {
            var existing = new StepUpBrowserTests.Presentation().RequirePublished(policy);
            string[] suffixes = ["description", "source", "pending", "unknown", "check", "continue", "cancel", "Google"];
            return new("en", existing.Content.Values.Concat(suffixes.Select(key => new RegistrationPublishedText("stepUp.federation." + key,
                "synthetic-device", "synthetic-device-v1", "SYNTHETIC-ONLY", "en", "PUBLISHED", "Fixture " + key))));
        }
    }
    private sealed class FederationSecrets : ISecretStore
    {
        public Task<SecretValues> ReadAsync(EngineeringSecretReference reference, CancellationToken cancellationToken = default)
        {
            if (reference.StoreReference != "synthetic" || reference.ResourceReference != "synthetic/google") throw new InvalidOperationException("Explicit synthetic provider reference required.");
            return Task.FromResult(new SecretValues(new Dictionary<string, string> { ["secret"] = FederationOidcNativeProtocolTests.SyntheticProvider.Secret }));
        }
    }
    [Fact]
    public async Task NativeGoogleFederationResolvesDeviceInsideExistingTransactionAndPreservesCausalDuplicateAndFallbackOutcomes()
    {
        var values = new FederationProfile("synthetic-device-google-v1", FederationProviderKind.Google, Origin, "synthetic-device-google-wire",
            "synthetic-device-google-namespace", "https://accounts.google.com", FederationOidcNativeProtocolTests.SyntheticProvider.ClientId,
            [], "synthetic-device-google-issuer-map", 128, 128, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(3), TimeSpan.FromMinutes(3),
            4, "/login", ["/login"], "synthetic-admission", "synthetic-federation-primary", "synthetic-map", "synthetic-session-use", "synthetic-processing", "synthetic-retention")
        {
            FreshAuthentication = new("synthetic-device-fresh-v1", "SYNTHETIC-ONLY", "synthetic-device-fresh-map", TimeSpan.FromSeconds(90),
                "/login", [new(["pwd", "mfa"], "fixture-password")])
        };
        var federation = new FederationProfileVersion(new(values.Version, "ACTIVE", values.Provider,
            new(new(2020, 1, 1, 0, 0, 0, TimeSpan.Zero), true, new(2040, 1, 1, 0, 0, 0, TimeSpan.Zero), false), null, ["SYNTHETIC-ONLY"]), values);
        using var provider = new FederationOidcNativeProtocolTests.SyntheticProvider(values.Issuer) { FreshEvidence = true };
        using var backchannel = new HttpClient(provider, disposeHandler: false);
        async Task<AuthenticationTestHost> Starter(IdentityTestDatabase database, Action<IServiceCollection> setup)
        { var host = await Start(database, setup); host.Clock.SynchronizeToSystem(); return host; }
        await using var test = await Create(start: Starter, configure: (services, profile) =>
        {
            // The actual provider authenticated 30 seconds earlier. Preserve that source time and its 90-second freshness cap.
            profile.ProofLifetime = TimeSpan.FromMinutes(2); profile.Methods.Add("federation:Google", profile.RequiredAssurance);
            profile.NativePresentation = new FederationPublication();
            const string prefix = EngineeringEnvironmentConfiguration.SectionName + ":";
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
                [prefix + "Class"] = "EPHEMERAL_TEST", [prefix + "Reference"] = "synthetic-device-native-proof", [prefix + "Endpoints:provider"] = values.Issuer,
                [prefix + "SecretReferences:google:StoreReference"] = "synthetic", [prefix + "SecretReferences:google:ResourceReference"] = "synthetic/google" }).Build();
            var secretReference = EngineeringEnvironmentConfiguration.Load(configuration).SecretReferences["google"];
            services.AddSingleton<IFederationProfileSource>(new FederationProfileCatalog([federation]));
            services.AddSingleton<ITotpMfaRequirementSource>(new TotpTests.Profile()); services.AddSingleton<ISecretStore>(new FederationSecrets());
            services.AddTunnerFederationOidc(new(federation, new(values.Issuer + "/.well-known/openid-configuration"), secretReference, "secret",
                TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(2), TimeSpan.Zero, 16384, 32768, 8, 128, 24576, 16384,
                "__Host-fixture.Device.Google.Correlation.", "__Host-fixture.Device.Google.Nonce."));
            services.Configure<OpenIdConnectOptions>(values.TransportScheme, options => options.Backchannel = backchannel);
        });
        var link = Guid.NewGuid();
        await using (var setup = test.Database.Context())
        {
            var actor = await setup.Set<AuthenticationSessionRow>().SingleAsync(x => x.SessionId == test.Actor.SessionId);
            setup.ExternalIdentityLinks.Add(new ExternalIdentityLinkRow { LinkId = link, AccountId = actor.AccountId, Provider = values.StableNamespaceId,
                Subject = "opaque-subject", Version = 1, EvidenceReference = "SYNTHETIC-ONLY", LinkState = "ACTIVE", QualifiedClientId = values.ClientId,
                QualificationProfileVersion = values.Version, IssuerMappingEvidenceReference = values.IssuerMappingEvidenceReference, QualificationEvidenceReference = "SYNTHETIC-ONLY" });
            await setup.SaveChangesAsync();
        }
        var original = await Prepare(test); var frame = await ProofOptions(test, original);
        using var started = await test.Host.Post("/authentication/step-up/federation/start", new { requestId = frame.RequestId, provider = "Google", linkReference = link }, frame.Cookies, frame.Csrf);
        Assert.True(started.StatusCode == HttpStatusCode.OK, await started.Content.ReadAsStringAsync());
        var native = await started.Content.ReadFromJsonAsync<JsonElement>(); var attemptId = native.GetProperty("federation").GetProperty("attemptId").GetGuid();
        frame.Cookies = Merge(frame.Cookies, started);
        using var challenge = await test.Host.GoogleFederationChallenge(frame.Cookies); Assert.Equal(HttpStatusCode.Redirect, challenge.StatusCode);
        Assert.Equal("accounts.google.com", challenge.Headers.Location!.Host); var query = QueryHelpers.ParseQuery(challenge.Headers.Location.Query);
        Assert.False(query.ContainsKey("prompt")); Assert.Equal("S256", query["code_challenge_method"].ToString());
        using (var claims = JsonDocument.Parse(query["claims"].ToString()))
        {
            Assert.True(claims.RootElement.GetProperty("id_token").GetProperty("auth_time").GetProperty("essential").GetBoolean());
            Assert.True(claims.RootElement.GetProperty("id_token").GetProperty("amr").GetProperty("essential").GetBoolean());
        }
        var code = provider.Admit(query["nonce"].ToString(), query["code_challenge"].ToString());
        var protocol = string.Join("; ", challenge.Headers.GetValues("Set-Cookie").Select(x => x.Split(';')[0]));
        using var callback = await test.Host.GoogleFederationCallback(query["state"].ToString(), code, protocol);
        Assert.Equal(HttpStatusCode.SeeOther, callback.StatusCode); Assert.Equal("/login", callback.Headers.Location?.OriginalString);
        Assert.True(provider.PkceAndConfidentialClientObserved);
        Assert.DoesNotContain(callback.Headers.TryGetValues("Set-Cookie", out var cookies) ? cookies : [], x => x.StartsWith(AuthenticationIdentity.CookieName + "=", StringComparison.Ordinal));
        for (var retry = 0; retry < 2; retry++)
        {
            using var completed = await test.Host.Post("/authentication/step-up/federation/continue", new { requestId = frame.RequestId }, frame.Cookies, frame.Csrf);
            Assert.True(completed.StatusCode == HttpStatusCode.OK, await completed.Content.ReadAsStringAsync());
            Assert.Equal(original.OperationReference, (await completed.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("operationReference").GetString());
            using var result = await test.Host.Post(Root + "/result", new { requestId = original.RequestId }, frame.Cookies, frame.Csrf);
            Assert.Equal(HttpStatusCode.OK, result.StatusCode); var receipt = await result.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal("REVOKED", receipt.GetProperty("status").GetString()); Assert.Equal(original.OperationReference, receipt.GetProperty("operationReference").GetString());
            Assert.Equal(original.Target.GetProperty("reference").GetGuid(), receipt.GetProperty("target").GetProperty("reference").GetGuid());
            Assert.Equal("2", receipt.GetProperty("target").GetProperty("trustVersion").GetString());
        }
        using var independent = await test.Host.Get("/authentication/step-up/options", frame.Cookies); Assert.Equal(HttpStatusCode.OK, independent.StatusCode);
        var ordinary = await independent.Content.ReadFromJsonAsync<JsonElement>(); Assert.Equal("fixture-correlation", ordinary.GetProperty("operationReference").GetString());
        using var applied = await test.Host.Post("/authentication/step-up/password", new { requestId = ordinary.GetProperty("requestId").GetGuid(), password = Password },
            Merge(frame.Cookies, independent), ordinary.GetProperty("csrfToken").GetString()); Assert.Equal(HttpStatusCode.OK, applied.StatusCode);
        using var active = await test.Host.Get("/authentication/session", frame.Cookies); Assert.Equal(HttpStatusCode.OK, active.StatusCode);
        await using var persisted = test.Database.Context(); var proof = await persisted.Set<StepUpProofRow>().SingleAsync(x => x.Operation == DeviceApplication.RevokeScope.Operation);
        var attempt = await persisted.Set<FederationAttemptRow>().SingleAsync(x => x.AttemptId == attemptId);
        Assert.Equal("CONSUMED", proof.State); Assert.Equal("federation:Google", proof.AuthenticationMethod); Assert.Equal(attemptId, proof.FederationAttemptId);
        Assert.Equal(provider.AuthenticatedAt, attempt.ProviderAuthenticatedAt); Assert.Equal(provider.AuthenticatedAt, proof.AuthenticatedAt);
        Assert.NotEqual(attempt.VerifiedAt, proof.AuthenticatedAt); Assert.True(proof.ExpiresAt <= attempt.VerificationExpiresAt); Assert.Equal(link, attempt.SelectedProofLinkId);
        Assert.Equal(FederationMethods, JsonSerializer.Deserialize<string[]>(attempt.ProviderMethodsJson!)!.Order(StringComparer.Ordinal));
        Assert.Equal(1, await persisted.Set<OutboxMessage>().CountAsync(x => x.EventType == "DeviceTrustRevoked"));
        Assert.Equal(0, await persisted.Set<OutboxMessage>().CountAsync(x => x.EventType == "TunnerSessionRevoked"));
        Assert.Equal(1, await persisted.Database.SqlQueryRaw<int>("SELECT value AS \"Value\" FROM identity.step_up_browser_target WHERE id=1").SingleAsync());
        Assert.Equal(1, provider.TokenRequests);
    }

    private static async Task<(Guid RequestId, Guid CeremonyId, string Cookies, string Csrf)> SelectPasskey(Scenario test, string cookies)
    {
        using var options = await test.Host.Get("/account/passkeys/options", cookies); Assert.Equal(HttpStatusCode.OK, options.StatusCode);
        var boot = await options.Content.ReadFromJsonAsync<JsonElement>(); var csrf = boot.GetProperty("csrfToken").GetString()!;
        var requestId = boot.GetProperty("requestId").GetGuid(); cookies = Merge(cookies, options);
        using var selected = await test.Host.Post("/account/passkeys/enrollment/start", new { requestId }, cookies, csrf);
        Assert.Equal(HttpStatusCode.OK, selected.StatusCode); var view = await selected.Content.ReadFromJsonAsync<JsonElement>();
        return (requestId, view.GetProperty("ceremonyId").GetGuid(), Merge(cookies, selected), csrf);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConfirmedDeviceRemainsForegroundOverPreselectedRealPasskeyAndNewPasskeyIntentCanSupersedeIt(bool completeDevice)
    {
        await using var test = await Create(start: PasskeyTests.StartRecoveryFixtureAsync, configure: (_, profile) =>
        {
            profile.ScopeVersions.Add(PasskeyApplication.EnrollmentScope, "fixture-enrollment-v1");
            profile.ScopeVersions.Add(PasskeyApplication.RevocationScope, "fixture-revocation-v1");
        });
        var previous = await SelectPasskey(test, test.Actor.Cookies);
        using (var selected = await test.Host.Get("/authentication/step-up/options", previous.Cookies))
            Assert.Equal(previous.CeremonyId.ToString("N"), (await selected.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("operationReference").GetString());
        var actor = test.Actor with { Cookies = previous.Cookies, Csrf = previous.Csrf };
        var original = await Prepare(test, await Options(test, actor), await Device(test, actor));
        Assert.Equal(Only(previous.Cookies, PasskeyStepUpOwner.CookieName), Only(original.Cookies, PasskeyStepUpOwner.CookieName));
        var frame = await ProofOptions(test, original);
        if (completeDevice)
        {
            using var complete = await test.Host.Post("/authentication/step-up/password", new { requestId = frame.RequestId, password = Password }, frame.Cookies, frame.Csrf);
            Assert.Equal(HttpStatusCode.OK, complete.StatusCode);
            using var result = await test.Host.Post(Root + "/result", new { requestId = original.RequestId }, frame.Cookies, frame.Csrf);
            Assert.Equal(HttpStatusCode.OK, result.StatusCode); Assert.Equal("REVOKED", (await result.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetString());
        }
        using (var oldResult = await test.Host.Post("/account/passkeys/result", new { requestId = previous.RequestId, ceremonyId = previous.CeremonyId }, frame.Cookies, frame.Csrf))
        { Assert.Equal(HttpStatusCode.OK, oldResult.StatusCode); Assert.Equal("PENDING", (await oldResult.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetString()); }
        var next = await SelectPasskey(test, frame.Cookies); Assert.NotEqual(previous.CeremonyId, next.CeremonyId);
        using var nextOptions = await test.Host.Get("/authentication/step-up/options", next.Cookies); Assert.Equal(HttpStatusCode.OK, nextOptions.StatusCode);
        var nextView = await nextOptions.Content.ReadFromJsonAsync<JsonElement>(); Assert.Equal(next.CeremonyId.ToString("N"), nextView.GetProperty("operationReference").GetString());
        var nextCookies = Merge(next.Cookies, nextOptions); var nextCsrf = nextView.GetProperty("csrfToken").GetString()!;
        var substituted = Replace(nextCookies, StepUpBrowserEndpoints.CookieName, Only(frame.Cookies, StepUpBrowserEndpoints.CookieName));
        using var stale = await test.Host.Post("/authentication/step-up/password", new { requestId = frame.RequestId, password = Password }, substituted, nextCsrf);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        using var authorized = await test.Host.Post("/authentication/step-up/password", new { requestId = nextView.GetProperty("requestId").GetGuid(), password = Password }, nextCookies, nextCsrf);
        Assert.Equal(HttpStatusCode.OK, authorized.StatusCode);
        using var context = await test.Host.Get(Root + "/context", nextCookies);
        Assert.Equal(completeDevice ? "COMPLETED" : "CHANGED", (await context.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetString());
        await using var persisted = test.Database.Context(); Assert.Equal(completeDevice ? 1 : 0, await persisted.Set<OutboxMessage>().CountAsync(x => x.EventType == "DeviceTrustRevoked"));
        Assert.Equal("PENDING", (await persisted.Set<PasskeyCeremonyRow>().SingleAsync(x => x.CeremonyId == previous.CeremonyId)).State);
        Assert.Equal("AUTHORIZED", (await persisted.Set<PasskeyCeremonyRow>().SingleAsync(x => x.CeremonyId == next.CeremonyId)).State);
    }
    private static async Task<Original> SelectSession(Scenario test, string family, string cookies, string csrf, Guid target)
    {
        if (family == "single")
        {
            await using var read = test.Database.Context(); var version = await read.Set<AuthenticationSessionRow>().Where(x => x.SessionId == target).Select(x => x.Version).SingleAsync();
            var requestId = Guid.NewGuid(); using var response = await test.Host.Post("/account/sessions/revocation/prepare",
                new { requestId, targetReference = target, expectedVersion = version.ToString(System.Globalization.CultureInfo.InvariantCulture) }, cookies, csrf);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode); var view = await response.Content.ReadFromJsonAsync<JsonElement>();
            return new(requestId, view.GetProperty("operationReference").GetString()!, Merge(cookies, response), csrf, default, view);
        }
        using var preview = await test.Host.Post("/account/sessions/bulk-revocation/preview", new { }, cookies, csrf); Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
        var draft = await preview.Content.ReadFromJsonAsync<JsonElement>(); cookies = Merge(cookies, preview); var id = draft.GetProperty("requestId").GetGuid();
        using var activated = await test.Host.Post("/account/sessions/bulk-revocation/activate", new { requestId = id }, cookies, csrf); Assert.Equal(HttpStatusCode.OK, activated.StatusCode);
        var active = await activated.Content.ReadFromJsonAsync<JsonElement>(); return new(id, active.GetProperty("operationReference").GetString()!, Merge(cookies, activated), csrf, default, active);
    }
    [Theory]
    [InlineData("single", true)]
    [InlineData("bulk", true)]
    [InlineData("single", false)]
    [InlineData("bulk", false)]
    public async Task DeviceAndSessionFamiliesKeepTheExplicitNewConfirmedForegroundInBothDirections(string family, bool deviceLast)
    {
        await using var test = await Create(configure: (services, _) => CompanionSessions.Configure(services));
        var target = await Login(test.Host, test.Database); _ = await Login(test.Host, test.Database);
        Original device; Original session; Original foreground;
        (Guid RequestId, string Cookies, string Csrf)? oldDeviceFrame = null;
        if (deviceLast)
        {
            session = await SelectSession(test, family, test.Actor.Cookies, test.Actor.Csrf, target.SessionId);
            var actor = test.Actor with { Cookies = session.Cookies };
            device = await Prepare(test, await Options(test, actor), await Device(test, actor)); foreground = device;
            Assert.Equal(Only(session.Cookies, SessionRevocationOriginalSelection.CookieName), Only(device.Cookies, SessionRevocationOriginalSelection.CookieName));
        }
        else
        {
            device = await Prepare(test); oldDeviceFrame = await ProofOptions(test, device);
            session = await SelectSession(test, family, oldDeviceFrame.Value.Cookies, oldDeviceFrame.Value.Csrf, target.SessionId); foreground = session;
        }
        var frame = await ProofOptions(test, foreground);
        if (oldDeviceFrame is { } old)
        {
            var substituted = Replace(frame.Cookies, StepUpBrowserEndpoints.CookieName, Only(old.Cookies, StepUpBrowserEndpoints.CookieName));
            using var denied = await test.Host.Post("/authentication/step-up/password", new { requestId = old.RequestId, password = Password }, substituted, frame.Csrf);
            Assert.Equal(HttpStatusCode.Conflict, denied.StatusCode);
        }
        using var completed = await test.Host.Post("/authentication/step-up/password", new { requestId = frame.RequestId, password = Password }, frame.Cookies, frame.Csrf);
        Assert.True(completed.StatusCode == HttpStatusCode.OK, await completed.Content.ReadAsStringAsync());
        Assert.Equal(foreground.OperationReference, (await completed.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("operationReference").GetString());
        using var deviceResult = await test.Host.Post(Root + "/result", new { requestId = device.RequestId }, frame.Cookies, frame.Csrf);
        using var sessionResult = await test.Host.Post("/account/sessions/" + (family == "single" ? "revocation" : "bulk-revocation") + "/result",
            new { requestId = session.RequestId }, frame.Cookies, frame.Csrf);
        Assert.Equal(deviceLast ? HttpStatusCode.OK : HttpStatusCode.Conflict, deviceResult.StatusCode);
        Assert.Equal(deviceLast ? HttpStatusCode.Conflict : HttpStatusCode.OK, sessionResult.StatusCode);
        await using var persisted = test.Database.Context(); Assert.Equal(deviceLast ? 1 : 0, await persisted.Set<OutboxMessage>().CountAsync(x => x.EventType == "DeviceTrustRevoked"));
        Assert.Equal(deviceLast ? 0 : family == "single" ? 1 : 2, await persisted.Set<OutboxMessage>().CountAsync(x => x.EventType == "TunnerSessionRevoked"));
        Assert.Equal("ACTIVE", (await persisted.Set<AuthenticationSessionRow>().SingleAsync(x => x.SessionId == test.Actor.SessionId)).State);
        if (!deviceLast)
        {
            using var oldContext = await test.Host.Get(Root + "/context", frame.Cookies); Assert.Equal("CHANGED", (await oldContext.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetString());
        }
    }

    [Fact]
    public async Task PackagedDevicePagesGuardRealSessionsRejectMalformedDetailsAndPreservePrivateApiRoutes()
    {
        Assert.True(File.Exists(Path.Combine(AppContext.BaseDirectory, "wwwroot", "registration", "devices.html")));
        await using var test = await Create(); var device = await Device(test); var reference = device.GetProperty("reference").GetGuid();
        var paths = new[] { Root, Root + "/" + reference.ToString("D") };
        foreach (var path in paths)
        {
            using var anonymous = await test.Host.Get(path); Assert.Equal(HttpStatusCode.Redirect, anonymous.StatusCode); Assert.Equal("/login", anonymous.Headers.Location?.OriginalString);
            using var current = await test.Host.Get(path, test.Actor.Cookies); Assert.Equal(HttpStatusCode.OK, current.StatusCode);
            Assert.Equal("text/html", current.Content.Headers.ContentType?.MediaType); Assert.Contains("id=\"root\"", await current.Content.ReadAsStringAsync(), StringComparison.Ordinal);
            Assert.Contains("frame-ancestors 'none'", current.Headers.GetValues("Content-Security-Policy").Single(), StringComparison.Ordinal);
        }
        foreach (var malformed in new[] { "not-a-device", reference.ToString("N"), "12345678-zzzz-1234-1234-123456789012" })
        { using var invalid = await test.Host.Get(Root + "/" + malformed, test.Actor.Cookies); Assert.Equal(HttpStatusCode.NotFound, invalid.StatusCode); }
        using (var options = await test.Host.Get(Root + "/options", test.Actor.Cookies))
        { Assert.Equal(HttpStatusCode.OK, options.StatusCode); Assert.Equal("application/json", options.Content.Headers.ContentType?.MediaType); }
        using (var context = await test.Host.Get(Root + "/context", test.Actor.Cookies))
        { Assert.Equal(HttpStatusCode.NotFound, context.StatusCode); Assert.NotEqual("text/html", context.Content.Headers.ContentType?.MediaType); }
        using var ended = await test.Host.Post("/authentication/sign-out", new { }, test.Actor.Cookies, test.Actor.Csrf); Assert.Equal(HttpStatusCode.OK, ended.StatusCode);
        foreach (var path in paths)
        { using var revoked = await test.Host.Get(path, test.Actor.Cookies); Assert.Equal(HttpStatusCode.Redirect, revoked.StatusCode); Assert.Equal("/login", revoked.Headers.Location?.OriginalString); }
    }
}
