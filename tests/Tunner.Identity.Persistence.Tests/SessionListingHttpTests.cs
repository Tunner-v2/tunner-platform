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

public sealed class SessionListingHttpTests(SessionListingPostgresFixture fixture) : IClassFixture<SessionListingPostgresFixture>
{
    private sealed class Budgets
    {
        internal string Version { get; set; } = "fixture-listing-http-v1";
        internal bool Available { get; set; } = true;
        internal int Instances { get; set; }
        internal static readonly EffectivePolicyInterval Interval = new(new(2026, 10, 20, 0, 0, 0, TimeSpan.Zero), true,
            new DateTimeOffset(2026, 10, 23, 0, 0, 0, TimeSpan.Zero), false);
    }
    private sealed class BudgetSource(Budgets state) : ISessionListingHttpBudgetSource
    {
        public SessionListingHttpBudget RequireEffective(DateTimeOffset at) => state.Available
            ? new SessionListingHttpBudgetCatalog([new(new(state.Version, "ACTIVE", SessionListingHttpBudget.Scope, Budgets.Interval, null,
                ["SYNTHETIC-LOCAL-TEST-NOT-PRODUCTION-APPROVAL"]), 1, 4096)]).RequireEffective(at)
            : throw new InvalidOperationException("Fixture listing capacity unavailable.");
    }
    private sealed class Scenario(IdentityTestDatabase database, AuthenticationTestHost host, Budgets budgets, string cookies, string csrf) : IAsyncDisposable
    {
        internal IdentityTestDatabase Database { get; } = database;
        internal AuthenticationTestHost Host { get; } = host;
        internal Budgets Budgets { get; } = budgets;
        internal string Cookies { get; } = cookies;
        internal string Csrf { get; } = csrf;
        public async ValueTask DisposeAsync() { await Host.DisposeAsync(); await Database.DisposeAsync(); }
    }
    private async Task<Scenario> Create(bool configured = true)
    {
        var database = await fixture.CreateDatabaseAsync(); var budgets = new Budgets();
        var host = await Start(database, services =>
        {
            if (configured) services.AddScoped<ISessionListingHttpBudgetSource>(_ => { budgets.Instances++; return new BudgetSource(budgets); });
        });
        try
        {
            await host.SeedAsync(database); var browser = await Login(host);
            return new(database, host, budgets, browser.Cookies, browser.Csrf);
        }
        catch { await host.DisposeAsync(); await database.DisposeAsync(); throw; }
    }
    private static async Task<(string Cookies, string Csrf)> Login(AuthenticationTestHost host, string email = "person@example.test")
    {
        var boot = await host.Options();
        using var response = await host.Post("/authentication/sign-in", Command(boot.Key, email: email), boot.Cookies, boot.Csrf);
        Assert.True(response.StatusCode == HttpStatusCode.OK);
        var cookies = Merge(boot.Cookies, response);
        using var current = await host.Get("/authentication/session", cookies); Assert.True(current.StatusCode == HttpStatusCode.OK);
        return (Merge(cookies, current), (await current.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("csrfToken").GetString()!);
    }
    private static async Task<JsonElement> Read(Scenario test, string? continuation = null)
    {
        using var response = await test.Host.Post(SessionListingEndpoints.Path, new { continuation }, test.Cookies, test.Csrf);
        Assert.True(response.StatusCode == HttpStatusCode.OK); Headers(response);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }
    private static void Headers(HttpResponseMessage response)
    {
        Assert.True(response.Headers.CacheControl?.NoStore == true && response.Headers.GetValues("X-Content-Type-Options").Single() == "nosniff" &&
            response.Headers.GetValues("Referrer-Policy").Single() == "no-referrer");
    }

    [Fact]
    public async Task SourceAbsentLeavesListingUnavailableAndExistingAuthenticationWorking()
    {
        await using var test = await Create(false);
        using var unavailable = await test.Host.Post(SessionListingEndpoints.Path, new { }, test.Cookies, test.Csrf);
        Assert.True(unavailable.StatusCode == HttpStatusCode.NotFound); Headers(unavailable);
        using var session = await test.Host.Get("/authentication/session", test.Cookies); Assert.True(session.StatusCode == HttpStatusCode.OK);
        using var ended = await test.Host.Post("/authentication/sign-out", new { }, test.Cookies, test.Csrf); Assert.True(ended.StatusCode == HttpStatusCode.OK);
        using var old = await test.Host.Get("/authentication/session", test.Cookies); Assert.True(old.StatusCode == HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task SafeBoundedHttpPagesUseOpaqueBodyContinuationAndLeaveTargetsUntouched()
    {
        await using var test = await Create();
        var target = await SessionListingTests.SignIn(test.Host); await SessionListingTests.SignIn(test.Host);
        await using var before = test.Database.Context();
        var row = await before.Set<AuthenticationSessionRow>().AsNoTracking().SingleAsync(x => x.SessionId == target.SessionId);
        var events = await before.Set<OutboxMessage>().CountAsync(); var seen = new HashSet<Guid>(); string? next = null;
        for (var i = 0; i < 3; i++)
        {
            var page = await Read(test, next);
            Assert.True(page.EnumerateObject().Select(x => x.Name).ToHashSet(StringComparer.Ordinal).SetEquals(["sessions", "hasMore", "continuation"]));
            var items = page.GetProperty("sessions").EnumerateArray().ToArray(); Assert.True(items.Length == 1);
            var item = items[0];
            Assert.True(item.EnumerateObject().Select(x => x.Name).ToHashSet(StringComparer.Ordinal).SetEquals(
                ["reference", "version", "isCurrent", "authenticatedAt", "lastSeenAt", "expiresAt", "recordedState", "effectiveState", "evaluatedAt"]));
            Assert.True(seen.Add(item.GetProperty("reference").GetGuid()) && item.GetProperty("version").ValueKind == JsonValueKind.String &&
                item.GetProperty("effectiveState").GetString() == "ACTIVE" && !page.GetRawText().Contains(row.AccountId, StringComparison.Ordinal));
            Assert.True(page.GetProperty("hasMore").GetBoolean() == (i < 2)); next = page.GetProperty("continuation").GetString();
        }
        Assert.True(next is null && seen.Count == 3 && test.Budgets.Instances == 3);
        await using var after = test.Database.Context(); var unchanged = await after.Set<AuthenticationSessionRow>().SingleAsync(x => x.SessionId == target.SessionId);
        Assert.True(unchanged.Version == row.Version && unchanged.LastSeenAt == row.LastSeenAt && unchanged.State == row.State && await after.Set<OutboxMessage>().CountAsync() == events);
    }

    [Fact]
    public async Task RealCsrfOriginAndStrictShapeFailuresRemainProtected()
    {
        await using var test = await Create();
        using (var missing = await test.Host.Post(SessionListingEndpoints.Path, new { }, test.Cookies, null))
        { Assert.True(missing.StatusCode == HttpStatusCode.BadRequest); Headers(missing); }
        using (var forged = await test.Host.Post(SessionListingEndpoints.Path, new { }, test.Cookies, "fixture-invalid-csrf"))
        { Assert.True(forged.StatusCode == HttpStatusCode.BadRequest); Headers(forged); }
        using (var foreign = await test.Host.Post(SessionListingEndpoints.Path, new { }, test.Cookies, test.Csrf, "https://foreign.example.test"))
        { Assert.True(foreign.StatusCode == HttpStatusCode.Forbidden); Headers(foreign); }
        foreach (var body in new[] { "[]", "null", "{", "{\"accountId\":\"untrusted\"}", "{\"maximumRows\":1}",
            "{\"continuation\":null,\"Continuation\":null}", "{\"continuation\":12}", "{\"continuation\":\"\"}" })
        {
            using var response = await test.Host.RawPost(SessionListingEndpoints.Path, body, test.Cookies, test.Csrf);
            Assert.True(response.StatusCode == HttpStatusCode.BadRequest); Headers(response);
        }
        using var query = await test.Host.Post(SessionListingEndpoints.Path + "?continuation=untrusted", new { }, test.Cookies, test.Csrf);
        Assert.True(query.StatusCode == HttpStatusCode.BadRequest); Headers(query);
    }

    [Fact]
    public async Task UnsupportedContentTypeIsRejectedByTheRealHttpsHandler()
    {
        await using var test = await Create();
        using var response = await test.Host.RawPost(SessionListingEndpoints.Path, "{}", test.Cookies, test.Csrf, "text/plain");
        Assert.True(response.StatusCode == HttpStatusCode.UnsupportedMediaType); Headers(response);
    }

    [Fact]
    public async Task OversizedBodyAndUnavailableBudgetFailSafely()
    {
        await using var test = await Create();
        using (var large = await test.Host.Post(SessionListingEndpoints.Path, new { continuation = new string('x', 4096) }, test.Cookies, test.Csrf))
        { Assert.True(large.StatusCode == HttpStatusCode.RequestEntityTooLarge); Headers(large); }
        test.Budgets.Available = false;
        using var unavailable = await test.Host.Post(SessionListingEndpoints.Path, new { }, test.Cookies, test.Csrf);
        Assert.True(unavailable.StatusCode == HttpStatusCode.ServiceUnavailable); Headers(unavailable);
    }

    [Fact]
    public async Task TamperedAndForeignSessionOrAccountCursorsCannotSelectAnotherListing()
    {
        await using var test = await Create(); await SessionListingTests.SignIn(test.Host);
        var cursor = (await Read(test)).GetProperty("continuation").GetString(); Assert.NotNull(cursor);
        using (var tampered = await test.Host.Post(SessionListingEndpoints.Path, new { continuation = "!" + cursor }, test.Cookies, test.Csrf))
        { Assert.True(tampered.StatusCode == HttpStatusCode.NotFound); Headers(tampered); }
        var second = await Login(test.Host);
        using (var foreignSession = await test.Host.Post(SessionListingEndpoints.Path, new { continuation = cursor }, second.Cookies, second.Csrf))
        { Assert.True(foreignSession.StatusCode == HttpStatusCode.NotFound); Headers(foreignSession); }
        await test.Host.SeedAsync(test.Database, foreignEmail: true); var foreign = await Login(test.Host, "foreign@example.test");
        using var foreignAccount = await test.Host.Post(SessionListingEndpoints.Path, new { continuation = cursor }, foreign.Cookies, foreign.Csrf);
        Assert.True(foreignAccount.StatusCode == HttpStatusCode.NotFound); Headers(foreignAccount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CursorRejectsChangedAuthenticationOrBudgetVersion(bool budget)
    {
        await using var test = await Create(); await SessionListingTests.SignIn(test.Host);
        var cursor = (await Read(test)).GetProperty("continuation").GetString(); Assert.NotNull(cursor);
        if (budget) test.Budgets.Version = "fixture-listing-http-v2";
        else test.Host.Policies.Policy = test.Host.Policies.Policy with { Version = "fixture-auth-new" };
        using var changed = await test.Host.Post(SessionListingEndpoints.Path, new { continuation = cursor }, test.Cookies, test.Csrf);
        Assert.True(changed.StatusCode == HttpStatusCode.Conflict); Headers(changed);
    }

    [Fact]
    public async Task SubsequentPagesDoNotExtendOriginalCursorExpiry()
    {
        await using var test = await Create(); await SessionListingTests.SignIn(test.Host); await SessionListingTests.SignIn(test.Host);
        var original = (await Read(test)).GetProperty("continuation").GetString();
        test.Host.Clock.Advance(test.Host.Policies.Policy.BrowserContextLifetime / 2);
        var second = (await Read(test, original)).GetProperty("continuation").GetString(); Assert.NotNull(second);
        test.Host.Clock.Advance(test.Host.Policies.Policy.BrowserContextLifetime / 2 + TimeSpan.FromSeconds(1));
        using var expired = await test.Host.Post(SessionListingEndpoints.Path, new { continuation = second }, test.Cookies, test.Csrf);
        Assert.True(expired.StatusCode == HttpStatusCode.NotFound); Headers(expired);
    }

    [Fact]
    public async Task AnonymousRevokedAndListDeniedCallersReceiveNoListing()
    {
        await using var test = await Create();
        using (var anonymous = await test.Host.Post(SessionListingEndpoints.Path, new { }, null, null))
        { Assert.True(anonymous.StatusCode == HttpStatusCode.Unauthorized); Headers(anonymous); }
        test.Host.Access.DeniedOperation = AuthenticationApplication.ListSessionsOperation;
        using (var denied = await test.Host.Post(SessionListingEndpoints.Path, new { }, test.Cookies, test.Csrf))
        { Assert.True(denied.StatusCode == HttpStatusCode.Unauthorized); Headers(denied); }
        test.Host.Access.DeniedOperation = null;
        using var ended = await test.Host.Post("/authentication/sign-out", new { }, test.Cookies, test.Csrf); Assert.True(ended.StatusCode == HttpStatusCode.OK);
        using var revoked = await test.Host.Post(SessionListingEndpoints.Path, new { }, test.Cookies, test.Csrf);
        Assert.True(revoked.StatusCode == HttpStatusCode.Unauthorized); Headers(revoked);
    }
}
