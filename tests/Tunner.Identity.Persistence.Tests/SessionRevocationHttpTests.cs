using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tunner.Database.Outbox;
using Tunner.Platform.Policies;
using Xunit;
using static Tunner.Identity.Persistence.Tests.AuthenticationTestHost;

namespace Tunner.Identity.Persistence.Tests;

public sealed class SessionRevocationHttpTests(SessionListingPostgresFixture fixture) : IClassFixture<SessionListingPostgresFixture>
{
    private const string Root = "/account/sessions/revocation";
    private sealed class Profile : IAssurancePolicySource, ISessionRevocationPresentationSource, IStepUpPresentationSource
    {
        internal string Version { get; set; } = "fixture-step-v1";
        internal bool Published { get; set; } = true;
        public AssurancePolicyVersion RequireEffective(AssurancePolicyScope scope, DateTimeOffset at, string? expectedVersion = null) =>
            new AssurancePolicyCatalog([new(new(Version, "ACTIVE", scope, new(at.AddHours(-1), true, at.AddHours(1), false), null,
                ["SYNTHETIC-TEST-ONLY"]), "fixture-password", TimeSpan.FromSeconds(30),
                new Dictionary<string, string> { ["pwd"] = "fixture-password" })]).RequireEffective(scope, at, expectedVersion);
        public SessionRevocationPresentation RequirePublished(AuthenticationPolicy policy) => new("en", SessionRevocationPresentation.RequiredKeys.Select(key =>
            new RegistrationPublishedText(key, "fixture-variant", "fixture-version", "SYNTHETIC-TEST-ONLY", "en", Published ? "PUBLISHED" : "DRAFT", "Fixture " + key)));
        public StepUpPresentation RequirePublished(AssurancePolicyVersion policy) => new StepUpBrowserTests.Presentation().RequirePublished(policy);
    }
    private sealed class Budget : ISessionListingHttpBudgetSource
    {
        public SessionListingHttpBudget RequireEffective(DateTimeOffset at) => new(new("fixture-list-v1", "ACTIVE", SessionListingHttpBudget.Scope,
            new(at.AddHours(-1), true, at.AddHours(1), false), null, ["SYNTHETIC-TEST-ONLY"]), 10, 4096);
    }
    private sealed class ListingPublication : ISessionListingPresentationSource
    {
        public SessionListingPresentation RequirePublished(AuthenticationPolicy policy) => new("en", SessionListingPresentation.RequiredKeys.Select(key =>
            new RegistrationPublishedText(key, "fixture", "fixture-v1", "SYNTHETIC-TEST-ONLY", "en", "PUBLISHED", "Fixture " + key)));
    }
    // The actual registered source is decorated and always resolves a different ordinary operation.
    // Typed session dispatch must not depend on the outer registered owner's runtime type.
    private sealed class DecoratedOwner(StepUpBrowserTests.Owner owner) : IUserStepUpOperationSource
    {
        public Task<UserStepUpOperationContext?> ResolveAsync(UserStepUpActor actor, CancellationToken ct) => owner.ResolveAsync(actor, ct);
        public Task<bool> ApplyAsync(UserStepUpActor actor, UserStepUpOperationContext operation, IdentityPersistenceDbContext db, CancellationToken ct)
        {
            if (SessionRevocationApplication.Owns(operation.Binding.Scope)) throw new InvalidOperationException("The generic owner must not execute session revocation.");
            return owner.ApplyAsync(actor, operation, db, ct);
        }
    }
    private sealed record Browser(string Cookies, string Csrf, Guid SessionId);
    private sealed record Selection(Guid RequestId, string OperationReference, string Cookies, string Csrf, JsonElement View);
    private sealed class Scenario(IdentityTestDatabase db, AuthenticationTestHost host, Profile profile, StepUpBrowserTests.Owner fallback, Browser actor, Browser target, long targetVersion) : IAsyncDisposable
    {
        internal IdentityTestDatabase Database { get; } = db;
        internal AuthenticationTestHost Host { get; } = host;
        internal Profile Profile { get; } = profile;
        internal StepUpBrowserTests.Owner Fallback { get; } = fallback;
        internal Browser Actor { get; } = actor;
        internal Browser Target { get; } = target;
        internal long TargetVersion { get; } = targetVersion;
        public async ValueTask DisposeAsync() { await Host.DisposeAsync(); await Database.DisposeAsync(); }
    }
    private async Task<Scenario> Create(bool enabled = true)
    {
        var db = await fixture.CreateDatabaseAsync(); var profile = new Profile(); var owner = new StepUpBrowserTests.Owner(db);
        await using (var setup = db.Context()) await setup.Database.ExecuteSqlRawAsync("CREATE TABLE identity.step_up_browser_target (id integer PRIMARY KEY, version bigint NOT NULL, value integer NOT NULL, session_id uuid NULL, operation_id text NULL); INSERT INTO identity.step_up_browser_target VALUES(1,1,0,NULL,NULL);");
        var host = await Start(db, services =>
        {
            services.AddSingleton<ISessionListingHttpBudgetSource, Budget>(); services.AddSingleton<ISessionListingPresentationSource, ListingPublication>();
            services.AddSingleton<IAssurancePolicySource>(profile); services.AddSingleton<IStepUpPresentationSource>(profile);
            services.AddSingleton<IUserStepUpOperationSource>(new DecoratedOwner(owner));
            if (enabled) { services.AddSingleton<ISessionRevocationPresentationSource>(profile); services.AddTunnerSessionRevocation(); }
        });
        try
        {
            await host.SeedAsync(db); var target = await Login(host, db); var actor = await Login(host, db);
            await using var read = db.Context(); var version = (await read.Set<AuthenticationSessionRow>().SingleAsync(x => x.SessionId == target.SessionId)).Version;
            return new(db, host, profile, owner, actor, target, version);
        }
        catch { await host.DisposeAsync(); await db.DisposeAsync(); throw; }
    }
    private static async Task<Browser> Login(AuthenticationTestHost host, IdentityTestDatabase db)
    {
        var boot = await host.Options(); using var signed = await host.Post("/authentication/sign-in", Command(boot.Key), boot.Cookies, boot.Csrf);
        Assert.Equal(HttpStatusCode.OK, signed.StatusCode); var cookies = Merge(boot.Cookies, signed);
        using var current = await host.Get("/authentication/session", cookies); Assert.Equal(HttpStatusCode.OK, current.StatusCode);
        var json = await current.Content.ReadFromJsonAsync<JsonElement>();
        await using var read = db.Context(); var id = await read.Set<AuthenticationSessionRow>().Where(x => x.IdempotencyKey == boot.Key).Select(x => x.SessionId).SingleAsync();
        return new(Merge(cookies, current), json.GetProperty("csrfToken").GetString()!, id);
    }
    private static async Task<Selection> Prepare(Scenario test)
    {
        var id = Guid.NewGuid(); using var response = await test.Host.Post(Root + "/prepare",
            new { requestId = id, targetReference = test.Target.SessionId, expectedVersion = test.TargetVersion.ToString(System.Globalization.CultureInfo.InvariantCulture) }, test.Actor.Cookies, test.Actor.Csrf);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode); Headers(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(); Assert.Equal("PENDING", json.GetProperty("status").GetString());
        var cookie = Assert.Single(response.Headers.GetValues("Set-Cookie"), x => x.StartsWith(SessionRevocationBrowserEndpoints.CookieName + "=", StringComparison.Ordinal));
        Assert.True(cookie.Contains("httponly", StringComparison.OrdinalIgnoreCase) && cookie.Contains("secure", StringComparison.OrdinalIgnoreCase) && cookie.Contains("samesite=strict", StringComparison.OrdinalIgnoreCase));
        return new(id, json.GetProperty("operationReference").GetString()!, Merge(test.Actor.Cookies, response), test.Actor.Csrf, json);
    }
    private static async Task<Selection> Complete(Scenario test, Selection selection)
    {
        using var options = await test.Host.Get("/authentication/step-up/options", selection.Cookies); Assert.Equal(HttpStatusCode.OK, options.StatusCode);
        var json = await options.Content.ReadFromJsonAsync<JsonElement>(); Assert.Equal(selection.OperationReference, json.GetProperty("operationReference").GetString());
        var csrf = json.GetProperty("csrfToken").GetString()!; var cookies = Merge(selection.Cookies, options);
        using var result = await test.Host.Post("/authentication/step-up/password", new { requestId = json.GetProperty("requestId").GetGuid(), password = Password }, cookies, csrf);
        Assert.Equal(HttpStatusCode.OK, result.StatusCode); Assert.Equal(selection.OperationReference, (await result.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("operationReference").GetString());
        return selection with { Cookies = Merge(cookies, result), Csrf = csrf };
    }
    private static void Headers(HttpResponseMessage response) => Assert.True(response.Headers.CacheControl?.NoStore == true &&
        response.Headers.GetValues("Referrer-Policy").Single() == "no-referrer" && response.Headers.GetValues("X-Content-Type-Options").Single() == "nosniff");
    private static string CookieOnly(string cookies, string name) => cookies.Split(';', StringSplitOptions.TrimEntries).Single(x => x.StartsWith(name + "=", StringComparison.Ordinal));
    private static string ReplaceCookie(string cookies, string name, string selected) => string.Join("; ", cookies.Split(';', StringSplitOptions.TrimEntries)
        .Where(x => !x.StartsWith(name + "=", StringComparison.Ordinal)).Append(selected));

    [Fact]
    public async Task ProtectedOriginalSurvivesReloadAndPasswordCompletionProvesExactCausalResultOnce()
    {
        await using var test = await Create(); var selected = await Prepare(test); test.Host.Clock.Advance(TimeSpan.FromSeconds(1));
        using (var reload = await test.Host.Get(Root + "/context", selected.Cookies))
        {
            Assert.Equal(HttpStatusCode.OK, reload.StatusCode); Headers(reload); var json = await reload.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(selected.RequestId, json.GetProperty("requestId").GetGuid()); Assert.Equal(selected.View.GetProperty("expiresAt").GetString(), json.GetProperty("expiresAt").GetString());
            Assert.Equal(selected.View.GetProperty("target").GetRawText(), json.GetProperty("target").GetRawText()); Assert.False(reload.Headers.Contains("Set-Cookie"));
        }
        selected = await Complete(test, selected);
        for (var i = 0; i < 2; i++)
        {
            using var result = await test.Host.Post(Root + "/result", new { requestId = selected.RequestId }, selected.Cookies, selected.Csrf);
            Assert.Equal(HttpStatusCode.OK, result.StatusCode); Headers(result); var json = await result.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal("REVOKED", json.GetProperty("status").GetString()); Assert.Equal(selected.OperationReference, json.GetProperty("operationReference").GetString());
            Assert.Equal(test.Target.SessionId, json.GetProperty("target").GetProperty("reference").GetGuid());
            Assert.Equal((test.TargetVersion + 1).ToString(System.Globalization.CultureInfo.InvariantCulture), json.GetProperty("target").GetProperty("version").GetString());
        }
        using var context = await test.Host.Get(Root + "/context", selected.Cookies); Assert.Equal("COMPLETED", (await context.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetString());
        await using var check = test.Database.Context(); Assert.Equal(1, await check.Set<StepUpProofRow>().CountAsync(x => x.State == "CONSUMED"));
        Assert.Equal(1, await check.Set<OutboxMessage>().CountAsync(x => x.EventType == "TunnerSessionRevoked" && x.Subject == test.Target.SessionId.ToString("N")));
        Assert.Equal(0, await check.Database.SqlQueryRaw<int>("SELECT value AS \"Value\" FROM identity.step_up_browser_target WHERE id=1").SingleAsync());
    }

    [Fact]
    public async Task UnconfirmedResultRetainsOriginalAndNeverCompletesFromGenericOwnerState()
    {
        await using var test = await Create(); var selected = await Prepare(test);
        using (var result = await test.Host.Post(Root + "/result", new { requestId = selected.RequestId }, selected.Cookies, selected.Csrf))
        { Assert.Equal(HttpStatusCode.Conflict, result.StatusCode); Assert.False(result.Headers.Contains("Set-Cookie")); }
        using (var context = await test.Host.Get(Root + "/context", selected.Cookies)) Assert.Equal("PENDING", (await context.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetString());
        selected = await Complete(test, selected);
        using var resultRead = await test.Host.Post(Root + "/result", new { requestId = selected.RequestId }, selected.Cookies, selected.Csrf);
        Assert.Equal(HttpStatusCode.OK, resultRead.StatusCode);
    }

    [Fact]
    public async Task RegistrationAbsentPreservesListingAndOrdinaryDecoratedOwner()
    {
        await using var test = await Create(false);
        using (var missing = await test.Host.Get(Root + "/options", test.Actor.Cookies)) Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        using (var list = await test.Host.Post("/account/sessions/list", new { continuation = (string?)null }, test.Actor.Cookies, test.Actor.Csrf)) Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        using var options = await test.Host.Get("/authentication/step-up/options", test.Actor.Cookies); Assert.Equal(HttpStatusCode.OK, options.StatusCode);
        var json = await options.Content.ReadFromJsonAsync<JsonElement>(); Assert.Equal("fixture-correlation", json.GetProperty("operationReference").GetString());
        using var result = await test.Host.Post("/authentication/step-up/password", new { requestId = json.GetProperty("requestId").GetGuid(), password = Password }, Merge(test.Actor.Cookies, options), json.GetProperty("csrfToken").GetString());
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        await using var read = test.Database.Context(); Assert.Equal(1, await read.Database.SqlQueryRaw<int>("SELECT value AS \"Value\" FROM identity.step_up_browser_target WHERE id=1").SingleAsync());
    }

    [Fact]
    public async Task CompletedSessionHandsBackUnchangedOrdinaryOwnerAndRetainsExactResult()
    {
        await using var test = await Create();
        using var earlierOptions = await test.Host.Get("/authentication/step-up/options", test.Actor.Cookies);
        Assert.Equal(HttpStatusCode.OK, earlierOptions.StatusCode);
        var earlier = await earlierOptions.Content.ReadFromJsonAsync<JsonElement>();
        var earlierCookies = Merge(test.Actor.Cookies, earlierOptions);
        test.Host.Clock.Advance(TimeSpan.FromSeconds(90));
        var selected = await Complete(test, await Prepare(test));
        using var ordinaryOptions = await test.Host.Get("/authentication/step-up/options", selected.Cookies);
        Assert.Equal(HttpStatusCode.OK, ordinaryOptions.StatusCode);
        var ordinary = await ordinaryOptions.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("PENDING", ordinary.GetProperty("status").GetString());
        Assert.Equal("fixture-correlation", ordinary.GetProperty("operationReference").GetString());
        var requestId = ordinary.GetProperty("requestId").GetGuid(); var csrf = ordinary.GetProperty("csrfToken").GetString()!;
        var cookies = Merge(selected.Cookies, ordinaryOptions);
        using (var wrongView = await test.Host.Post("/authentication/step-up/password", new { requestId, password = Password }, selected.Cookies, csrf))
            Assert.Equal(HttpStatusCode.Conflict, wrongView.StatusCode);
        var tampered = ReplaceCookie(cookies, StepUpBrowserEndpoints.CookieName, StepUpBrowserEndpoints.CookieName + "=tampered");
        using (var invalid = await test.Host.Post("/authentication/step-up/password", new { requestId, password = Password }, tampered, csrf))
            Assert.Equal(HttpStatusCode.Forbidden, invalid.StatusCode);
        // The earlier server-issued view is now beyond its two-minute authentication
        // context lifetime; the later original Sessions and ordinary views remain valid.
        test.Host.Clock.Advance(TimeSpan.FromSeconds(31));
        var expiredCookies = ReplaceCookie(cookies, StepUpBrowserEndpoints.CookieName, CookieOnly(earlierCookies, StepUpBrowserEndpoints.CookieName));
        using (var expired = await test.Host.Post("/authentication/step-up/password", new
        { requestId = earlier.GetProperty("requestId").GetGuid(), password = Password }, expiredCookies, earlier.GetProperty("csrfToken").GetString()))
            Assert.Equal(HttpStatusCode.Forbidden, expired.StatusCode);
        using var applied = await test.Host.Post("/authentication/step-up/password", new { requestId, password = Password }, cookies, csrf);
        Assert.Equal(HttpStatusCode.OK, applied.StatusCode);
        Assert.Equal("fixture-correlation", (await applied.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("operationReference").GetString());
        using var original = await test.Host.Post(Root + "/result", new { requestId = selected.RequestId }, Merge(cookies, applied), csrf);
        Assert.Equal(HttpStatusCode.OK, original.StatusCode);
        var receipt = await original.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(selected.OperationReference, receipt.GetProperty("operationReference").GetString());
        Assert.Equal("REVOKED", receipt.GetProperty("status").GetString());
        await using var check = test.Database.Context();
        Assert.Equal(1, await check.Database.SqlQueryRaw<int>("SELECT value AS \"Value\" FROM identity.step_up_browser_target WHERE id=1").SingleAsync());
        Assert.Equal(1, await check.Set<OutboxMessage>().CountAsync(x => x.EventType == "TunnerSessionRevoked" && x.Subject == test.Target.SessionId.ToString("N")));
        Assert.Equal(1, await check.Set<StepUpProofRow>().CountAsync(x => x.Operation == "SESSION_REVOKE" && x.State == "CONSUMED"));
    }

    [Fact]
    public async Task NewIndependentOwnerSupersedesSharedProofWithoutDeletingOriginalResultContext()
    {
        await using var test = await Create(); var selected = await Prepare(test); test.Fallback.Resource = "fixture-new-resource";
        using var proof = await test.Host.Get("/authentication/step-up/options", selected.Cookies); Assert.Equal(HttpStatusCode.OK, proof.StatusCode);
        Assert.Equal("fixture-correlation", (await proof.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("operationReference").GetString());
        Assert.DoesNotContain(proof.Headers.TryGetValues("Set-Cookie", out var values) ? values : [], x => x.StartsWith(SessionRevocationBrowserEndpoints.CookieName + "=", StringComparison.Ordinal));
        using var result = await test.Host.Post(Root + "/result", new { requestId = selected.RequestId }, selected.Cookies, selected.Csrf);
        Assert.Equal(HttpStatusCode.Conflict, result.StatusCode);
    }

    [Fact]
    public async Task OptionalPublicationDoesNotChangeLegacySeventyFourKeysAndFailsClosedWhenInvalid()
    {
        await using var test = await Create();
        using (var options = await test.Host.Get("/account/sessions/options", test.Actor.Cookies)) Assert.Equal(74, (await options.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("content").EnumerateObject().Count());
        using (var options = await test.Host.Get(Root + "/options", test.Actor.Cookies)) Assert.Equal(18, (await options.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("content").EnumerateObject().Count());
        test.Profile.Published = false;
        using var invalid = await test.Host.Get(Root + "/options", test.Actor.Cookies); Assert.Equal(HttpStatusCode.ServiceUnavailable, invalid.StatusCode);
        using var prepare = await test.Host.Post(Root + "/prepare", new { requestId = Guid.NewGuid(), targetReference = test.Target.SessionId, expectedVersion = test.TargetVersion.ToString(System.Globalization.CultureInfo.InvariantCulture) }, test.Actor.Cookies, test.Actor.Csrf);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, prepare.StatusCode);
    }

    [Theory]
    [InlineData("number")]
    [InlineData("leading-zero")]
    [InlineData("extra")]
    [InlineData("duplicate")]
    [InlineData("missing")]
    public async Task PrepareStrictBodyRejectsSelectorsTypesAndNoncanonicalVersions(string kind)
    {
        await using var test = await Create(); var id = Guid.NewGuid().ToString(); var target = test.Target.SessionId.ToString(); var version = test.TargetVersion.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var raw = kind switch
        {
            "number" => $$"""{"requestId":"{{id}}","targetReference":"{{target}}","expectedVersion":{{version}}}""",
            "leading-zero" => $$"""{"requestId":"{{id}}","targetReference":"{{target}}","expectedVersion":"0{{version}}"}""",
            "extra" => $$"""{"requestId":"{{id}}","targetReference":"{{target}}","expectedVersion":"{{version}}","accountId":"selector"}""",
            "duplicate" => $$"""{"requestId":"{{id}}","requestId":"{{id}}","targetReference":"{{target}}","expectedVersion":"{{version}}"}""",
            _ => $$"""{"requestId":"{{id}}","targetReference":"{{target}}"}"""
        };
        using var response = await test.Host.RawPost(Root + "/prepare", raw, test.Actor.Cookies, test.Actor.Csrf);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task AuthenticationCsrfOriginQueryMediaAndSizeGuardsAreIndependent()
    {
        await using var test = await Create(); var body = new { requestId = Guid.NewGuid(), targetReference = test.Target.SessionId, expectedVersion = test.TargetVersion.ToString(System.Globalization.CultureInfo.InvariantCulture) };
        using (var anonymous = await test.Host.Get(Root + "/context")) Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        using (var csrf = await test.Host.Post(Root + "/prepare", body, test.Actor.Cookies, null)) Assert.Equal(HttpStatusCode.BadRequest, csrf.StatusCode);
        using (var origin = await test.Host.Post(Root + "/prepare", body, test.Actor.Cookies, test.Actor.Csrf, "https://foreign.example.test")) Assert.Equal(HttpStatusCode.Forbidden, origin.StatusCode);
        using (var query = await test.Host.Get(Root + "/context?selector=none", test.Actor.Cookies)) Assert.Equal(HttpStatusCode.BadRequest, query.StatusCode);
        using (var media = await test.Host.RawPost(Root + "/prepare", "{}", test.Actor.Cookies, test.Actor.Csrf, "text/plain")) Assert.Equal(HttpStatusCode.UnsupportedMediaType, media.StatusCode);
        using (var size = await test.Host.RawPost(Root + "/prepare", new string(' ', 4097), test.Actor.Cookies, test.Actor.Csrf)) Assert.Equal(HttpStatusCode.RequestEntityTooLarge, size.StatusCode);
    }

    [Theory]
    [InlineData("tamper")]
    [InlineData("actor")]
    [InlineData("expiry")]
    [InlineData("authentication-policy")]
    public async Task OriginalCookieCannotMoveActorsOrSurviveTamperExpiryOrPolicyChange(string kind)
    {
        await using var test = await Create(); var selected = await Prepare(test); var cookies = selected.Cookies;
        if (kind == "tamper") cookies = ReplaceCookie(cookies, SessionRevocationBrowserEndpoints.CookieName, SessionRevocationBrowserEndpoints.CookieName + "=tampered");
        if (kind == "actor") cookies = ReplaceCookie(test.Target.Cookies, SessionRevocationBrowserEndpoints.CookieName, CookieOnly(cookies, SessionRevocationBrowserEndpoints.CookieName));
        if (kind == "expiry") test.Host.Clock.Advance(test.Host.Policies.Policy.BrowserContextLifetime);
        if (kind == "authentication-policy") test.Host.Policies.Policy = test.Host.Policies.Policy with { Version = "fixture-auth-v2" };
        using var context = await test.Host.Get(Root + "/context", cookies); Assert.Equal(HttpStatusCode.NotFound, context.StatusCode);
        await using var check = test.Database.Context(); Assert.Equal(0, await check.Set<StepUpProofRow>().CountAsync(x => x.State == "CONSUMED"));
    }

    [Theory]
    [InlineData("tamper")]
    [InlineData("expiry")]
    public async Task InvalidOriginalCookieDoesNotMaskIndependentFallbackProofOwner(string kind)
    {
        await using var test = await Create(); var selected = await Prepare(test);
        using var originalOptions = await test.Host.Get("/authentication/step-up/options", selected.Cookies);
        Assert.Equal(HttpStatusCode.OK, originalOptions.StatusCode);
        var originalProof = await originalOptions.Content.ReadFromJsonAsync<JsonElement>();
        var cookies = Merge(selected.Cookies, originalOptions); var csrf = originalProof.GetProperty("csrfToken").GetString()!;
        if (kind == "tamper") cookies = ReplaceCookie(cookies, SessionRevocationBrowserEndpoints.CookieName, SessionRevocationBrowserEndpoints.CookieName + "=tampered");
        else test.Host.Clock.Advance(test.Host.Policies.Policy.BrowserContextLifetime);

        using (var context = await test.Host.Get(Root + "/context", cookies)) Assert.Equal(HttpStatusCode.NotFound, context.StatusCode);
        using (var originalExecution = await test.Host.Post("/authentication/step-up/password", new
        { requestId = originalProof.GetProperty("requestId").GetGuid(), password = Password }, cookies, csrf))
            Assert.True(originalExecution.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Conflict);

        using var fallbackOptions = await test.Host.Get("/authentication/step-up/options", cookies);
        Assert.Equal(HttpStatusCode.OK, fallbackOptions.StatusCode);
        var fallback = await fallbackOptions.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("fixture-correlation", fallback.GetProperty("operationReference").GetString());
        Assert.NotEqual(selected.OperationReference, fallback.GetProperty("operationReference").GetString());
        using var ordinary = await test.Host.Post("/authentication/step-up/password", new
        { requestId = fallback.GetProperty("requestId").GetGuid(), password = Password }, Merge(cookies, fallbackOptions), fallback.GetProperty("csrfToken").GetString());
        Assert.Equal(HttpStatusCode.OK, ordinary.StatusCode);
        await using var check = test.Database.Context();
        Assert.Equal(1, await check.Database.SqlQueryRaw<int>("SELECT value AS \"Value\" FROM identity.step_up_browser_target WHERE id=1").SingleAsync());
        Assert.Equal("ACTIVE", (await check.Set<AuthenticationSessionRow>().SingleAsync(x => x.SessionId == test.Target.SessionId)).State);
        Assert.Equal(0, await check.Set<StepUpProofRow>().CountAsync(x => x.Operation == "SESSION_REVOKE" && x.State == "CONSUMED"));
        Assert.Equal(0, await check.Set<OutboxMessage>().CountAsync(x => x.EventType == "TunnerSessionRevoked"));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ChangedAssuranceOrTargetRequiresExplicitDecisionAndPreservesOriginal(bool assurance)
    {
        await using var test = await Create(); var selected = await Prepare(test);
        if (assurance) test.Profile.Version = "fixture-step-v2";
        else { using var touch = await test.Host.Get("/authentication/session", test.Target.Cookies); Assert.Equal(HttpStatusCode.OK, touch.StatusCode); }
        using var context = await test.Host.Get(Root + "/context", selected.Cookies); Assert.Equal(HttpStatusCode.OK, context.StatusCode);
        var view = await context.Content.ReadFromJsonAsync<JsonElement>(); Assert.Equal("CHANGED", view.GetProperty("status").GetString());
        Assert.Equal(selected.View.GetProperty("target").GetRawText(), view.GetProperty("target").GetRawText());
        using var result = await test.Host.Post(Root + "/result", new { requestId = selected.RequestId }, selected.Cookies, selected.Csrf); Assert.Equal(HttpStatusCode.Conflict, result.StatusCode);
        Assert.False(result.Headers.Contains("Set-Cookie"));
    }
}
