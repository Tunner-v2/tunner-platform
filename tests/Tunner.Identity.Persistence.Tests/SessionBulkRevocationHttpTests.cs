using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tunner.Database.Outbox;
using Tunner.Platform.Policies;
using Xunit;
using static Tunner.Identity.Persistence.Tests.AuthenticationTestHost;

namespace Tunner.Identity.Persistence.Tests;

public sealed class SessionBulkRevocationHttpTests(SessionListingPostgresFixture fixture) : IClassFixture<SessionListingPostgresFixture>
{
    private const string Root = "/account/sessions/bulk-revocation";
    private sealed class Profile : IAssurancePolicySource, IStepUpPresentationSource, ISessionRevocationPresentationSource,
        ISessionBulkRevocationPresentationSource, ISessionBulkRevocationDiscoverySource, ISessionBulkRevocationHttpBudgetSource,
        ISessionListingHttpBudgetSource, ISessionListingPresentationSource
    {
        internal int MaximumCandidates { get; set; } = 20;
        internal int MaximumCookie { get; set; } = 4096;
        internal string DiscoveryVersion { get; set; } = "fixture-discovery-v1";
        internal string TransportVersion { get; set; } = "fixture-transport-v1";
        internal string AssuranceVersion { get; set; } = "fixture-step-v1";
        internal bool Published { get; set; } = true;
        private static PolicyVersionCandidate<string> Metadata(string version, string scope) => new(version, "ACTIVE", scope,
            SessionBulkRevocationTests.Discovery.Interval, null, ["SYNTHETIC-TEST-ONLY"]);
        public AssurancePolicyVersion RequireEffective(AssurancePolicyScope scope, DateTimeOffset at, string? expectedVersion = null) =>
            new AssurancePolicyCatalog([new(new(AssuranceVersion, "ACTIVE", scope, SessionBulkRevocationTests.Discovery.Interval,
                null, ["SYNTHETIC-TEST-ONLY"]), "fixture-password", TimeSpan.FromSeconds(30), new Dictionary<string, string> { ["pwd"] = "fixture-password" })])
                .RequireEffective(scope, at, expectedVersion);
        public StepUpPresentation RequirePublished(AssurancePolicyVersion policy) => new StepUpBrowserTests.Presentation().RequirePublished(policy);
        SessionBulkRevocationDiscoveryBudget ISessionBulkRevocationDiscoverySource.RequireEffective(DateTimeOffset at)
            => new(Metadata(DiscoveryVersion, SessionBulkRevocationDiscoveryBudget.Scope), MaximumCandidates);
        SessionBulkRevocationHttpBudget ISessionBulkRevocationHttpBudgetSource.RequireEffective(DateTimeOffset at)
            => new(Metadata(TransportVersion, SessionBulkRevocationHttpBudget.Scope), 4096, MaximumCookie);
        SessionListingHttpBudget ISessionListingHttpBudgetSource.RequireEffective(DateTimeOffset at)
            => new(Metadata("fixture-list-v1", SessionListingHttpBudget.Scope), 20, 4096);
        private RegistrationPublishedText[] Text(IEnumerable<string> keys) => keys.Select(key => new RegistrationPublishedText(key,
            "fixture", "fixture-v1", "SYNTHETIC-TEST-ONLY", "en", Published ? "PUBLISHED" : "DRAFT", "Fixture " + key)).ToArray();
        SessionBulkRevocationPresentation ISessionBulkRevocationPresentationSource.RequirePublished(AuthenticationPolicy policy)
            => new("en", Text(SessionBulkRevocationPresentation.RequiredKeys));
        SessionRevocationPresentation ISessionRevocationPresentationSource.RequirePublished(AuthenticationPolicy policy)
            => new("en", Text(SessionRevocationPresentation.RequiredKeys));
        SessionListingPresentation ISessionListingPresentationSource.RequirePublished(AuthenticationPolicy policy)
            => new("en", Text(SessionListingPresentation.RequiredKeys));
    }
    private sealed class DecoratedOwner(StepUpBrowserTests.Owner inner) : IUserStepUpOperationSource
    {
        public Task<UserStepUpOperationContext?> ResolveAsync(UserStepUpActor actor, CancellationToken ct) => inner.ResolveAsync(actor, ct);
        public Task<bool> ApplyAsync(UserStepUpActor actor, UserStepUpOperationContext operation, IdentityPersistenceDbContext database, CancellationToken ct)
        {
            Assert.False(SessionBulkRevocationApplication.Owns(operation.Binding.Scope));
            Assert.False(SessionRevocationApplication.Owns(operation.Binding.Scope));
            return inner.ApplyAsync(actor, operation, database, ct);
        }
    }
    private sealed record Browser(string Cookies, string Csrf, Guid SessionId);
    private sealed record Original(Guid RequestId, string OperationReference, string Cookies, string Csrf, JsonElement View);
    private sealed class Scenario(IdentityTestDatabase database, AuthenticationTestHost host, Profile profile,
        StepUpBrowserTests.Owner fallback, Browser actor, Browser[] targets) : IAsyncDisposable
    {
        internal IdentityTestDatabase Database { get; } = database;
        internal AuthenticationTestHost Host { get; } = host;
        internal Profile Profile { get; } = profile;
        internal StepUpBrowserTests.Owner Fallback { get; } = fallback;
        internal Browser Actor { get; } = actor;
        internal Browser[] Targets { get; } = targets;
        public async ValueTask DisposeAsync() { await Host.DisposeAsync(); await Database.DisposeAsync(); }
    }
    private async Task<Scenario> Create(bool enabled = true, int targetCount = 2, int maximumCookie = 4096)
    {
        var database = await fixture.CreateDatabaseAsync(); var profile = new Profile { MaximumCookie = maximumCookie };
        var fallback = new StepUpBrowserTests.Owner(database);
        await using (var setup = database.Context()) await setup.Database.ExecuteSqlRawAsync("CREATE TABLE identity.step_up_browser_target (id integer PRIMARY KEY, version bigint NOT NULL, value integer NOT NULL, session_id uuid NULL, operation_id text NULL); INSERT INTO identity.step_up_browser_target VALUES(1,1,0,NULL,NULL);");
        var host = await Start(database, services =>
        {
            services.AddSingleton<IAssurancePolicySource>(profile); services.AddSingleton<IStepUpPresentationSource>(profile);
            services.AddSingleton<ISessionListingHttpBudgetSource>(profile); services.AddSingleton<ISessionListingPresentationSource>(profile);
            services.AddSingleton<IUserStepUpOperationSource>(new DecoratedOwner(fallback));
            services.AddSingleton<ISessionRevocationPresentationSource>(profile); services.AddTunnerSessionRevocation();
            if (enabled)
            {
                services.AddSingleton<ISessionBulkRevocationDiscoverySource>(profile);
                services.AddSingleton<ISessionBulkRevocationHttpBudgetSource>(profile);
                services.AddSingleton<ISessionBulkRevocationPresentationSource>(profile); services.AddTunnerSessionBulkRevocation();
            }
        });
        try
        {
            await host.SeedAsync(database); var targets = new List<Browser>();
            for (var i = 0; i < targetCount; i++) targets.Add(await Login(host, database));
            return new(database, host, profile, fallback, await Login(host, database), targets.ToArray());
        }
        catch { await host.DisposeAsync(); await database.DisposeAsync(); throw; }
    }
    private static async Task<Browser> Login(AuthenticationTestHost host, IdentityTestDatabase database)
    {
        var boot = await host.Options(); using var signed = await host.Post("/authentication/sign-in", Command(boot.Key), boot.Cookies, boot.Csrf);
        Assert.Equal(HttpStatusCode.OK, signed.StatusCode); var cookies = Merge(boot.Cookies, signed);
        using var current = await host.Get("/authentication/session", cookies); Assert.Equal(HttpStatusCode.OK, current.StatusCode);
        var json = await current.Content.ReadFromJsonAsync<JsonElement>(); await using var read = database.Context();
        var id = await read.Set<AuthenticationSessionRow>().Where(x => x.IdempotencyKey == boot.Key).Select(x => x.SessionId).SingleAsync();
        return new(Merge(cookies, current), json.GetProperty("csrfToken").GetString()!, id);
    }
    private static async Task<Original> Preview(Scenario test, string? cookies = null)
    {
        using var response = await test.Host.Post(Root + "/preview", new { }, cookies ?? test.Actor.Cookies, test.Actor.Csrf);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode); Headers(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(); Assert.Equal("DRAFT", json.GetProperty("status").GetString());
        Assert.False(json.GetProperty("activated").GetBoolean());
        var headers = response.Headers.GetValues("Set-Cookie").ToArray();
        var original = Assert.Single(headers, x => x.StartsWith(SessionBulkRevocationBrowserEndpoints.CookieName + "=", StringComparison.Ordinal));
        Assert.True(Encoding.UTF8.GetByteCount(original) <= test.Profile.MaximumCookie);
        Assert.True(original.Contains("httponly", StringComparison.OrdinalIgnoreCase) && original.Contains("secure", StringComparison.OrdinalIgnoreCase) && original.Contains("samesite=strict", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(headers, x => x.StartsWith(SessionRevocationOriginalSelection.CookieName + "=", StringComparison.Ordinal));
        return new(json.GetProperty("requestId").GetGuid(), json.GetProperty("operationReference").GetString()!, Merge(cookies ?? test.Actor.Cookies, response), test.Actor.Csrf, json);
    }
    private static async Task<Original> Activate(Scenario test, Original original)
    {
        using var response = await test.Host.Post(Root + "/activate", new { requestId = original.RequestId }, original.Cookies, original.Csrf);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode); var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("PENDING", json.GetProperty("status").GetString()); Assert.True(json.GetProperty("activated").GetBoolean());
        Assert.Equal(original.View.GetProperty("targets").GetRawText(), json.GetProperty("targets").GetRawText());
        Assert.Equal(original.View.GetProperty("expiresAt").GetString(), json.GetProperty("expiresAt").GetString());
        foreach (var header in response.Headers.GetValues("Set-Cookie").Where(x => !x.StartsWith(StepUpBrowserEndpoints.CookieName + "=", StringComparison.Ordinal)))
            Assert.True(Encoding.UTF8.GetByteCount(header) <= test.Profile.MaximumCookie);
        return original with { Cookies = Merge(original.Cookies, response), View = json };
    }
    private static async Task<Original> Complete(Scenario test, Original original)
    {
        using var options = await test.Host.Get("/authentication/step-up/options", original.Cookies); Assert.Equal(HttpStatusCode.OK, options.StatusCode);
        var json = await options.Content.ReadFromJsonAsync<JsonElement>(); Assert.Equal(original.OperationReference, json.GetProperty("operationReference").GetString());
        var cookies = Merge(original.Cookies, options); var csrf = json.GetProperty("csrfToken").GetString()!;
        using var response = await test.Host.Post("/authentication/step-up/password", new { requestId = json.GetProperty("requestId").GetGuid(), password = Password }, cookies, csrf);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(original.OperationReference, (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("operationReference").GetString());
        return original with { Cookies = Merge(cookies, response), Csrf = csrf };
    }
    private static void Headers(HttpResponseMessage response) => Assert.True(response.Headers.CacheControl?.NoStore == true &&
        response.Headers.GetValues("Referrer-Policy").Single() == "no-referrer" && response.Headers.GetValues("X-Content-Type-Options").Single() == "nosniff");
    private static string Only(string cookies, string name) => cookies.Split(';', StringSplitOptions.TrimEntries).Single(x => x.StartsWith(name + "=", StringComparison.Ordinal));
    private static string Replace(string cookies, string name, string selected) => string.Join("; ", cookies.Split(';', StringSplitOptions.TrimEntries)
        .Where(x => !x.StartsWith(name + "=", StringComparison.Ordinal)).Append(selected));
    private static async Task NoEffects(Scenario test)
    {
        await using var check = test.Database.Context(); Assert.Equal(0, await check.Set<StepUpProofRow>().CountAsync());
        Assert.Equal(0, await check.Set<OutboxMessage>().CountAsync(x => x.EventType == "TunnerSessionRevoked"));
        Assert.All(await check.Set<AuthenticationSessionRow>().Where(x => test.Targets.Select(t => t.SessionId).Contains(x.SessionId)).ToArrayAsync(), x => Assert.Equal("ACTIVE", x.State));
    }
    [Fact]
    public async Task PreviewReloadAndCancelNeverSelectOrIssueProofAndPreserveIndependentOwner()
    {
        await using var test = await Create(); var draft = await Preview(test);
        using (var context = await test.Host.Get(Root + "/context", draft.Cookies))
        { Assert.Equal("DRAFT", (await context.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetString()); Assert.False(context.Headers.Contains("Set-Cookie")); }
        using (var generic = await test.Host.Get("/authentication/step-up/options", draft.Cookies))
            Assert.Equal("fixture-correlation", (await generic.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("operationReference").GetString());
        using var cancel = await test.Host.Post(Root + "/cancel", new { requestId = draft.RequestId }, draft.Cookies, draft.Csrf);
        Assert.Equal(HttpStatusCode.OK, cancel.StatusCode);
        using var missing = await test.Host.Get(Root + "/context", Merge(draft.Cookies, cancel)); Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        await NoEffects(test);
    }
    [Fact]
    public async Task ConfirmExecutesTheReviewedOriginalSetOnceAndLeavesLaterSessionsAndActorCurrent()
    {
        await using var test = await Create(); var draft = await Preview(test); var later = await Login(test.Host, test.Database);
        var active = await Activate(test, draft); active = await Complete(test, active);
        for (var i = 0; i < 2; i++)
        {
            using var response = await test.Host.Post(Root + "/result", new { requestId = active.RequestId }, active.Cookies, active.Csrf);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode); Headers(response); Assert.False(response.Headers.Contains("Set-Cookie"));
            var json = await response.Content.ReadFromJsonAsync<JsonElement>(); Assert.Equal("REVOKED", json.GetProperty("status").GetString());
            Assert.Equal(test.Targets.Select(x => x.SessionId).Order(), json.GetProperty("targets").EnumerateArray().Select(x => x.GetProperty("reference").GetGuid()).Order());
        }
        await using var check = test.Database.Context();
        Assert.Equal(1, await check.Set<StepUpProofRow>().CountAsync(x => x.State == "CONSUMED" && x.Operation == "SESSION_REVOKE_OTHERS"));
        Assert.Equal(test.Targets.Length, await check.Set<OutboxMessage>().CountAsync(x => x.EventType == "TunnerSessionRevoked"));
        Assert.All(await check.Set<AuthenticationSessionRow>().Where(x => x.SessionId == later.SessionId || x.SessionId == test.Actor.SessionId).ToArrayAsync(), x => Assert.Equal("ACTIVE", x.State));
        using var actor = await test.Host.Get("/authentication/session", active.Cookies); Assert.Equal(HttpStatusCode.OK, actor.StatusCode);
    }
    [Fact]
    public async Task RepeatedPreviewAndActivationKeepOriginalDataExpiryAndDoNotReissueSelection()
    {
        await using var test = await Create(); var draft = await Preview(test); test.Host.Clock.Advance(TimeSpan.FromSeconds(2));
        using (var replay = await test.Host.Post(Root + "/preview", new { requestId = draft.RequestId }, draft.Cookies, draft.Csrf))
        { Assert.Equal(draft.View.GetRawText(), (await replay.Content.ReadFromJsonAsync<JsonElement>()).GetRawText()); Assert.False(replay.Headers.Contains("Set-Cookie")); }
        var active = await Activate(test, draft);
        using var duplicate = await test.Host.Post(Root + "/activate", new { requestId = active.RequestId }, active.Cookies, active.Csrf);
        Assert.Equal(HttpStatusCode.OK, duplicate.StatusCode); Assert.False(duplicate.Headers.Contains("Set-Cookie")); await NoEffects(test);
    }
    [Theory]
    [InlineData("target")]
    [InlineData("assurance")]
    [InlineData("discovery")]
    [InlineData("transport")]
    public async Task ChangedOriginalCannotBeActivatedOrResnapshotted(string change)
    {
        await using var test = await Create(); var draft = await Preview(test);
        if (change == "target") { using var touch = await test.Host.Get("/authentication/session", test.Targets[0].Cookies); Assert.Equal(HttpStatusCode.OK, touch.StatusCode); }
        if (change == "assurance") test.Profile.AssuranceVersion = "fixture-step-v2";
        if (change == "discovery") test.Profile.DiscoveryVersion = "fixture-discovery-v2";
        if (change == "transport") test.Profile.TransportVersion = "fixture-transport-v2";
        using var context = await test.Host.Get(Root + "/context", draft.Cookies); Assert.Equal(HttpStatusCode.OK, context.StatusCode);
        var json = await context.Content.ReadFromJsonAsync<JsonElement>(); Assert.Equal("CHANGED", json.GetProperty("status").GetString());
        Assert.Equal(draft.View.GetProperty("targets").GetRawText(), json.GetProperty("targets").GetRawText());
        using var activate = await test.Host.Post(Root + "/activate", new { requestId = draft.RequestId }, draft.Cookies, draft.Csrf);
        Assert.Equal(HttpStatusCode.Conflict, activate.StatusCode); Assert.False(activate.Headers.Contains("Set-Cookie")); await NoEffects(test);
    }
    [Fact]
    public async Task ChangedTargetAfterActivationRollsBackTheWholeSetAndDoesNotConsumeProof()
    {
        await using var test = await Create(); var active = await Activate(test, await Preview(test));
        using var options = await test.Host.Get("/authentication/step-up/options", active.Cookies); var json = await options.Content.ReadFromJsonAsync<JsonElement>();
        using var touch = await test.Host.Get("/authentication/session", test.Targets[0].Cookies); Assert.Equal(HttpStatusCode.OK, touch.StatusCode);
        using var execute = await test.Host.Post("/authentication/step-up/password", new { requestId = json.GetProperty("requestId").GetGuid(), password = Password },
            Merge(active.Cookies, options), json.GetProperty("csrfToken").GetString());
        Assert.Equal(HttpStatusCode.Conflict, execute.StatusCode); await NoEffects(test);
    }
    [Fact]
    public async Task ActivatedUnconfirmedOriginalCannotBeCancelledOrOverwrittenByANewPreview()
    {
        await using var test = await Create(); var active = await Activate(test, await Preview(test));
        using (var cancel = await test.Host.Post(Root + "/cancel", new { requestId = active.RequestId }, active.Cookies, active.Csrf))
        { Assert.Equal(HttpStatusCode.Conflict, cancel.StatusCode); Assert.False(cancel.Headers.Contains("Set-Cookie")); }
        using (var replacement = await test.Host.Post(Root + "/preview", new { }, active.Cookies, active.Csrf))
        { Assert.Equal(HttpStatusCode.Conflict, replacement.StatusCode); Assert.False(replacement.Headers.Contains("Set-Cookie")); }
        using var result = await test.Host.Post(Root + "/result", new { requestId = active.RequestId }, active.Cookies, active.Csrf);
        Assert.Equal(HttpStatusCode.Conflict, result.StatusCode); await NoEffects(test);
    }
    [Theory]
    [InlineData("missing")]
    [InlineData("tampered")]
    [InlineData("expired")]
    [InlineData("missing-expired")]
    public async Task SurvivingBulkSelectionOrInvalidOriginalRejectsReplayAndFreshReplacementWithoutResnapshot(string loss)
    {
        await using var test = await Create(); var active = await Activate(test, await Preview(test));
        var later = await Login(test.Host, test.Database); var cookies = active.Cookies;
        if (loss.StartsWith("missing", StringComparison.Ordinal)) cookies = string.Join("; ", cookies.Split(';', StringSplitOptions.TrimEntries)
            .Where(x => !x.StartsWith(SessionBulkRevocationBrowserEndpoints.CookieName + "=", StringComparison.Ordinal)));
        if (loss == "tampered") cookies = Replace(cookies, SessionBulkRevocationBrowserEndpoints.CookieName,
            SessionBulkRevocationBrowserEndpoints.CookieName + "=tampered");
        if (loss.Contains("expired", StringComparison.Ordinal)) test.Host.Clock.Advance(test.Host.Policies.Policy.BrowserContextLifetime);
        using (var replay = await test.Host.Post(Root + "/preview", new { requestId = active.RequestId }, cookies, active.Csrf))
        { Assert.Equal(HttpStatusCode.NotFound, replay.StatusCode); Assert.False(replay.Headers.Contains("Set-Cookie")); }
        using (var fresh = await test.Host.Post(Root + "/preview", new { }, cookies, active.Csrf))
        { Assert.Equal(HttpStatusCode.Conflict, fresh.StatusCode); Assert.False(fresh.Headers.Contains("Set-Cookie")); }
        await NoEffects(test); await using var check = test.Database.Context();
        Assert.Equal("ACTIVE", (await check.Set<AuthenticationSessionRow>().SingleAsync(x => x.SessionId == later.SessionId)).State);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TotalCapabilityLossRejectsReplayWhileExplicitFreshPreviewGetsNewServerIdentity(bool activated)
    {
        await using var test = await Create(); var original = await Preview(test);
        if (activated) original = await Activate(test, original);
        var later = await Login(test.Host, test.Database);
        // Base actor cookies predate every original/selection capability.
        using (var replay = await test.Host.Post(Root + "/preview", new { requestId = original.RequestId }, test.Actor.Cookies, test.Actor.Csrf))
        { Assert.Equal(HttpStatusCode.NotFound, replay.StatusCode); Assert.False(replay.Headers.Contains("Set-Cookie")); }
        var fresh = await Preview(test);
        Assert.NotEqual(original.RequestId, fresh.RequestId); Assert.NotEqual(original.OperationReference, fresh.OperationReference);
        Assert.Contains(fresh.View.GetProperty("targets").EnumerateArray(), x => x.GetProperty("reference").GetGuid() == later.SessionId);
        await NoEffects(test);
    }
    [Theory]
    [InlineData(0, 20, 4096, "NO_OTHER_SESSIONS")]
    [InlineData(2, 1, 4096, "DISCOVERY_CAPACITY_EXCEEDED")]
    [InlineData(2, 20, 100, "TRANSPORT_CAPACITY_EXCEEDED")]
    public async Task EmptyOverflowAndActualProtectedTransportCapacityFailBeforeCookieOrProof(int targets, int discovery, int cookie, string code)
    {
        await using var test = await Create(targetCount: targets, maximumCookie: cookie); test.Profile.MaximumCandidates = discovery;
        using var response = await test.Host.Post(Root + "/preview", new { }, test.Actor.Cookies, test.Actor.Csrf);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode); Assert.Contains(code, await response.Content.ReadAsStringAsync());
        Assert.False(response.Headers.Contains("Set-Cookie")); await NoEffects(test);
    }
    [Fact]
    public async Task SelectionOutlivesExpiredProofViewButCannotUseThatViewOrRenewOriginal()
    {
        await using var test = await Create(); var active = await Activate(test, await Preview(test));
        using var options = await test.Host.Get("/authentication/step-up/options", active.Cookies); var json = await options.Content.ReadFromJsonAsync<JsonElement>();
        var cookies = Merge(active.Cookies, options); await using var scope = test.Host.Services.CreateAsyncScope();
        var protector = scope.ServiceProvider.GetRequiredService<IDataProtectionProvider>().CreateProtector("Tunner.User.StepUp.Browser.v1");
        var protectedValue = Only(cookies, StepUpBrowserEndpoints.CookieName).Split('=', 2)[1];
        var view = JsonSerializer.Deserialize<StepUpBrowserService.Browser>(protector.Unprotect(protectedValue))!;
        var shortView = protector.Protect(JsonSerializer.Serialize(view with { ExpiresAt = test.Host.Clock.GetUtcNow().AddSeconds(1) }));
        cookies = Replace(cookies, StepUpBrowserEndpoints.CookieName, StepUpBrowserEndpoints.CookieName + "=" + shortView); test.Host.Clock.Advance(TimeSpan.FromSeconds(2));
        using (var expired = await test.Host.Post("/authentication/step-up/password", new { requestId = view.RequestId, password = Password }, cookies, json.GetProperty("csrfToken").GetString()))
            Assert.Equal(HttpStatusCode.Forbidden, expired.StatusCode);
        using (var renewed = await test.Host.Get("/authentication/step-up/options", cookies))
        {
            Assert.Equal(HttpStatusCode.OK, renewed.StatusCode); Assert.Equal(active.OperationReference, (await renewed.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("operationReference").GetString());
            Assert.DoesNotContain(renewed.Headers.GetValues("Set-Cookie"), x => x.StartsWith(SessionBulkRevocationBrowserEndpoints.CookieName + "=", StringComparison.Ordinal));
        }
        using var context = await test.Host.Get(Root + "/context", cookies);
        Assert.Equal(active.View.GetProperty("expiresAt").GetString(), (await context.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("expiresAt").GetString());
        await NoEffects(test);
    }
    [Fact]
    public async Task SingleSelectionSupersedesBulkAndBulkReplayNeverReselectsOrMasksSingle()
    {
        await using var test = await Create(); var active = await Activate(test, await Preview(test));
        await using var read = test.Database.Context(); var target = await read.Set<AuthenticationSessionRow>().AsNoTracking().SingleAsync(x => x.SessionId == test.Targets[0].SessionId);
        using var single = await test.Host.Post("/account/sessions/revocation/prepare", new { requestId = Guid.NewGuid(), targetReference = target.SessionId,
            expectedVersion = target.Version.ToString(System.Globalization.CultureInfo.InvariantCulture) }, active.Cookies, active.Csrf);
        Assert.Equal(HttpStatusCode.OK, single.StatusCode); var selected = await single.Content.ReadFromJsonAsync<JsonElement>(); var cookies = Merge(active.Cookies, single);
        using (var replay = await test.Host.Post(Root + "/activate", new { requestId = active.RequestId }, cookies, active.Csrf))
        { Assert.Equal(HttpStatusCode.OK, replay.StatusCode); Assert.False(replay.Headers.Contains("Set-Cookie")); }
        using var options = await test.Host.Get("/authentication/step-up/options", cookies);
        Assert.Equal(selected.GetProperty("operationReference").GetString(), (await options.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("operationReference").GetString());
        using var result = await test.Host.Post(Root + "/result", new { requestId = active.RequestId }, cookies, active.Csrf);
        Assert.Equal(HttpStatusCode.Conflict, result.StatusCode); await NoEffects(test);
    }
    [Fact]
    public async Task NewFamilySelectionPreventsActivationOfAnEarlierDraft()
    {
        await using var test = await Create(); var draft = await Preview(test);
        await using var read = test.Database.Context(); var target = await read.Set<AuthenticationSessionRow>().SingleAsync(x => x.SessionId == test.Targets[0].SessionId);
        using var single = await test.Host.Post("/account/sessions/revocation/prepare", new { requestId = Guid.NewGuid(), targetReference = target.SessionId,
            expectedVersion = target.Version.ToString(System.Globalization.CultureInfo.InvariantCulture) }, draft.Cookies, draft.Csrf);
        Assert.Equal(HttpStatusCode.OK, single.StatusCode);
        using var activate = await test.Host.Post(Root + "/activate", new { requestId = draft.RequestId }, Merge(draft.Cookies, single), draft.Csrf);
        Assert.Equal(HttpStatusCode.Conflict, activate.StatusCode); Assert.False(activate.Headers.Contains("Set-Cookie")); await NoEffects(test);
    }
    [Fact]
    public async Task PreviewPreservesSelectedSingleUntilConfirmationAndSingleReplayCannotReselect()
    {
        await using var test = await Create(); await using var read = test.Database.Context();
        var target = await read.Set<AuthenticationSessionRow>().SingleAsync(x => x.SessionId == test.Targets[0].SessionId); var id = Guid.NewGuid();
        var body = new { requestId = id, targetReference = target.SessionId, expectedVersion = target.Version.ToString(System.Globalization.CultureInfo.InvariantCulture) };
        using var single = await test.Host.Post("/account/sessions/revocation/prepare", body, test.Actor.Cookies, test.Actor.Csrf);
        Assert.Equal(HttpStatusCode.OK, single.StatusCode); var singleView = await single.Content.ReadFromJsonAsync<JsonElement>();
        var draft = await Preview(test, Merge(test.Actor.Cookies, single));
        using (var options = await test.Host.Get("/authentication/step-up/options", draft.Cookies))
            Assert.Equal(singleView.GetProperty("operationReference").GetString(), (await options.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("operationReference").GetString());
        var active = await Activate(test, draft);
        using (var replay = await test.Host.Post("/account/sessions/revocation/prepare", body, active.Cookies, active.Csrf))
        { Assert.Equal(HttpStatusCode.OK, replay.StatusCode); Assert.False(replay.Headers.Contains("Set-Cookie")); }
        using var selected = await test.Host.Get("/authentication/step-up/options", active.Cookies);
        Assert.Equal(active.OperationReference, (await selected.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("operationReference").GetString());
        await NoEffects(test);
    }
    [Fact]
    public async Task CompletedBulkHandsBackIndependentOwnerWhileExactOriginalResultRemainsReadable()
    {
        await using var test = await Create(); var active = await Complete(test, await Activate(test, await Preview(test)));
        using var options = await test.Host.Get("/authentication/step-up/options", active.Cookies); var json = await options.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("fixture-correlation", json.GetProperty("operationReference").GetString());
        using var fallback = await test.Host.Post("/authentication/step-up/password", new { requestId = json.GetProperty("requestId").GetGuid(), password = Password },
            Merge(active.Cookies, options), json.GetProperty("csrfToken").GetString()); Assert.Equal(HttpStatusCode.OK, fallback.StatusCode);
        using var result = await test.Host.Post(Root + "/result", new { requestId = active.RequestId }, Merge(active.Cookies, options), active.Csrf);
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
    }
    [Fact]
    public async Task CompletedOriginalRemainsReadableAfterDiscoveryAndAssuranceVersionsChange()
    {
        await using var test = await Create(); var active = await Complete(test, await Activate(test, await Preview(test)));
        test.Profile.DiscoveryVersion = "fixture-discovery-v2"; test.Profile.AssuranceVersion = "fixture-step-v2";
        using var result = await test.Host.Post(Root + "/result", new { requestId = active.RequestId }, active.Cookies, active.Csrf);
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        using var context = await test.Host.Get(Root + "/context", active.Cookies);
        Assert.Equal("COMPLETED", (await context.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetString());
    }
    [Theory]
    [InlineData("original")]
    [InlineData("selection")]
    [InlineData("actor")]
    [InlineData("expired")]
    [InlineData("authentication")]
    public async Task InvalidCapabilitiesCannotSelectOrExecuteBulkAndDoNotMaskFallback(string invalid)
    {
        await using var test = await Create(); var active = await Activate(test, await Preview(test)); var cookies = active.Cookies;
        if (invalid == "original") cookies = Replace(cookies, SessionBulkRevocationBrowserEndpoints.CookieName, SessionBulkRevocationBrowserEndpoints.CookieName + "=tampered");
        if (invalid == "selection") cookies = Replace(cookies, SessionRevocationOriginalSelection.CookieName, SessionRevocationOriginalSelection.CookieName + "=tampered");
        if (invalid == "actor") cookies = Replace(Replace(test.Targets[0].Cookies, SessionBulkRevocationBrowserEndpoints.CookieName, Only(cookies, SessionBulkRevocationBrowserEndpoints.CookieName)),
            SessionRevocationOriginalSelection.CookieName, Only(cookies, SessionRevocationOriginalSelection.CookieName));
        if (invalid == "expired") test.Host.Clock.Advance(test.Host.Policies.Policy.BrowserContextLifetime);
        if (invalid == "authentication") test.Host.Policies.Policy = test.Host.Policies.Policy with { Version = "fixture-auth-v2" };
        using var options = await test.Host.Get("/authentication/step-up/options", cookies);
        Assert.Equal(HttpStatusCode.OK, options.StatusCode); Assert.Equal("fixture-correlation", (await options.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("operationReference").GetString());
        await NoEffects(test);
    }
    [Theory]
    [InlineData("extra")]
    [InlineData("duplicate")]
    [InlineData("number")]
    [InlineData("null")]
    [InlineData("empty")]
    [InlineData("targets")]
    public async Task StrictCommandsRejectClientMembershipAndMalformedSelectors(string kind)
    {
        await using var test = await Create(); var id = Guid.NewGuid().ToString();
        var raw = kind switch
        {
            "extra" => $$"""{"requestId":"{{id}}","accountId":"client"}""",
            "duplicate" => $$"""{"requestId":"{{id}}","requestId":"{{id}}"}""",
            "number" => "{\"requestId\":1}", "null" => "{\"requestId\":null}",
            "empty" => "{\"requestId\":\"00000000-0000-0000-0000-000000000000\"}",
            _ => $$"""{"requestId":"{{id}}","targets":[]}"""
        };
        using var response = await test.Host.RawPost(Root + "/preview", raw, test.Actor.Cookies, test.Actor.Csrf);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode); await NoEffects(test);
    }
    [Fact]
    public async Task AuthenticationCsrfOriginQueryMediaSizeAndCurrentBulkPermissionRemainIndependent()
    {
        await using var test = await Create(); var body = new { };
        using (var response = await test.Host.Get(Root + "/context")) Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        using (var response = await test.Host.Post(Root + "/preview", body, test.Actor.Cookies, null)) Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using (var response = await test.Host.Post(Root + "/preview", body, test.Actor.Cookies, test.Actor.Csrf, "https://foreign.example.test")) Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        using (var response = await test.Host.Get(Root + "/context?selector=x", test.Actor.Cookies)) Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using (var response = await test.Host.RawPost(Root + "/preview", "{}", test.Actor.Cookies, test.Actor.Csrf, "text/plain")) Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
        using (var response = await test.Host.RawPost(Root + "/preview", new string(' ', 4097), test.Actor.Cookies, test.Actor.Csrf)) Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        test.Host.Access.DeniedOperation = SessionBulkRevocationApplication.Scope.Operation;
        using (var response = await test.Host.Post(Root + "/preview", body, test.Actor.Cookies, test.Actor.Csrf)) Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        await NoEffects(test);
    }
    [Fact]
    public async Task OptionalRegistrationAndPublicationPreserveLegacyContentAndFailClosed()
    {
        await using (var absent = await Create(false))
        { using var response = await absent.Host.Get(Root + "/options", absent.Actor.Cookies); Assert.Equal(HttpStatusCode.NotFound, response.StatusCode); }
        await using var test = await Create();
        using (var listing = await test.Host.Get("/account/sessions/options", test.Actor.Cookies)) Assert.Equal(74, (await listing.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("content").EnumerateObject().Count());
        using (var single = await test.Host.Get("/account/sessions/revocation/options", test.Actor.Cookies)) Assert.Equal(18, (await single.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("content").EnumerateObject().Count());
        using (var bulk = await test.Host.Get(Root + "/options", test.Actor.Cookies)) Assert.Equal(SessionBulkRevocationPresentation.RequiredKeys.Count,
            (await bulk.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("content").EnumerateObject().Count());
        test.Profile.Published = false;
        using var invalid = await test.Host.Post(Root + "/preview", new { }, test.Actor.Cookies, test.Actor.Csrf);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, invalid.StatusCode); await NoEffects(test);
    }
}
