using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Tunner.Accounts;
using Tunner.Database.Outbox;
using Xunit;
using static Tunner.Identity.Persistence.Tests.AuthenticationTestHost;

namespace Tunner.Identity.Persistence.Tests;

public sealed class AuthenticationTests(IdentityPostgresFixture fixture) : IClassFixture<IdentityPostgresFixture>
{
    [Fact]
    public async Task CanonicalScreensGuardDirectEntryAndDoNotExposeAnAlternateHtmlRoute()
    {
        await using var db = await fixture.CreateDatabaseAsync(); await using var host = await Start(db); await host.SeedAsync(db);
        using (var login = await host.Get("/login")) { Assert.Equal(HttpStatusCode.OK, login.StatusCode); Assert.Equal("text/html", login.Content.Headers.ContentType?.MediaType); }
        using (var guarded = await host.Get("/signed-in")) { Assert.Equal(HttpStatusCode.Redirect, guarded.StatusCode); Assert.Equal("/login", guarded.Headers.Location?.OriginalString); }
        using (var alias = await host.Get("/registration/authentication.html")) Assert.Equal(HttpStatusCode.Unauthorized, alias.StatusCode);
        var boot = await host.Options(); using var signed = await host.Post("/authentication/sign-in", Command(boot.Key), boot.Cookies, boot.Csrf); Assert.Equal(HttpStatusCode.OK, signed.StatusCode);
        using var page = await host.Get("/signed-in", Merge(boot.Cookies, signed)); Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.Contains("frame-ancestors 'none'", page.Headers.GetValues("Content-Security-Policy").Single(), StringComparison.Ordinal);
        Assert.True(page.Headers.CacheControl?.NoStore); Assert.Equal("no-referrer", page.Headers.GetValues("Referrer-Policy").Single());
    }
    [Fact]
    public async Task PasswordEstablishesOpaqueHostOnlySessionAndSignOutRevokesIt()
    {
        await using var db = await fixture.CreateDatabaseAsync(); await using var host = await Start(db); await host.SeedAsync(db);
        var boot = await host.Options(); using var signed = await host.Post("/authentication/sign-in", Command(boot.Key), boot.Cookies, boot.Csrf);
        Assert.Equal(HttpStatusCode.OK, signed.StatusCode);
        var header = signed.Headers.GetValues("Set-Cookie").Single(x => x.StartsWith(AuthenticationIdentity.CookieName + "=", StringComparison.Ordinal));
        foreach (var expected in new[] { "path=/", "secure", "httponly", "samesite=lax" }) Assert.Contains(expected, header, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("domain=", header, StringComparison.OrdinalIgnoreCase); Assert.DoesNotContain("expires=", header, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("{\"authenticated\":true}", await signed.Content.ReadAsStringAsync());
        var cookies = Merge(boot.Cookies, signed); using var session = await host.Get("/authentication/session", cookies);
        Assert.Equal(HttpStatusCode.OK, session.StatusCode); Assert.True(session.Headers.CacheControl?.NoStore);
        var status = await session.Content.ReadFromJsonAsync<JsonElement>(); var csrf = status.GetProperty("csrfToken").GetString()!;
        using (var noToken = await host.Post("/authentication/sign-out", new { }, cookies, null)) Assert.Equal(HttpStatusCode.BadRequest, noToken.StatusCode);
        using (var crossSite = await host.Post("/authentication/sign-out", new { }, cookies, csrf, "https://outside.example.test")) Assert.Equal(HttpStatusCode.Forbidden, crossSite.StatusCode);
        using var logout = await host.Post("/authentication/sign-out", new { }, cookies, csrf); Assert.Equal(HttpStatusCode.OK, logout.StatusCode);
        using (var replay = await host.Get("/authentication/session", cookies)) Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);
        using (var repeated = await host.Post("/authentication/sign-out", new { }, cookies, csrf)) Assert.Equal(HttpStatusCode.Unauthorized, repeated.StatusCode);
        await using var context = db.Context(); var row = await context.Set<AuthenticationSessionRow>().SingleAsync(); Assert.Equal("REVOKED", row.State);
        Assert.Empty(row.ProtectedKey); Assert.Empty(row.ProtectedTicket);
        Assert.Equal(2, await context.Set<OutboxMessage>().CountAsync());
        Assert.DoesNotContain(Password, JsonSerializer.Serialize(row)); Assert.DoesNotContain("person@example.test", JsonSerializer.Serialize(row));
    }
    [Theory]
    [InlineData("/authentication/sign-in")]
    [InlineData("/authentication/sign-in/")]
    [InlineData("/AUTHENTICATION/SIGN-IN")]
    public async Task ReauthenticationRotatesLookupKeyAndImmediatelyRejectsOldCookie(string signInPath)
    {
        await using var db = await fixture.CreateDatabaseAsync(); await using var host = await Start(db); await host.SeedAsync(db);
        var first = await host.Options(); using var signed = await host.Post("/authentication/sign-in", Command(first.Key), first.Cookies, first.Csrf);
        Assert.Equal(HttpStatusCode.OK, signed.StatusCode); var oldCookies = Merge(first.Cookies, signed);
        var next = await host.Options(oldCookies); using var again = await host.Post(signInPath, Command(next.Key), next.Cookies, next.Csrf);
        Assert.Equal(HttpStatusCode.OK, again.StatusCode); var current = Merge(next.Cookies, again);
        using (var old = await host.Get("/authentication/session", oldCookies)) Assert.Equal(HttpStatusCode.Unauthorized, old.StatusCode);
        using (var valid = await host.Get("/authentication/session", current)) Assert.Equal(HttpStatusCode.OK, valid.StatusCode);
        using (var stale = await host.Post("/authentication/sign-in", Command(next.Key), current, next.Csrf)) Assert.Equal(HttpStatusCode.Forbidden, stale.StatusCode);
        await using var context = db.Context(); var rows = await context.Set<AuthenticationSessionRow>().ToArrayAsync();
        Assert.Equal(2, rows.Length); Assert.Single(rows, x => x.State == "ACTIVE"); Assert.Single(rows, x => x.State == "REVOKED");
        Assert.NotEqual(rows[0].KeyDigest, rows[1].KeyDigest);
    }
    [Fact]
    public async Task ConcurrentSuccessfulRetryKeepsOneSessionAndCannotReviveAfterLogout()
    {
        await using var db = await fixture.CreateDatabaseAsync(); await using var host = await Start(db); await host.SeedAsync(db);
        var boot = await host.Options(); var responses = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => host.Post("/authentication/sign-in", Command(boot.Key), boot.Cookies, boot.Csrf)));
        try
        {
            foreach (var response in responses) Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var cookies = Merge(boot.Cookies, responses[0]); using var session = await host.Get("/authentication/session", cookies);
            var info = await session.Content.ReadFromJsonAsync<JsonElement>();
            using var logout = await host.Post("/authentication/sign-out", new { }, cookies, info.GetProperty("csrfToken").GetString()); Assert.Equal(HttpStatusCode.OK, logout.StatusCode);
            using var retry = await host.Post("/authentication/sign-in", Command(boot.Key), boot.Cookies, boot.Csrf); Assert.Equal(HttpStatusCode.Unauthorized, retry.StatusCode);
            await using var context = db.Context(); Assert.Equal("REVOKED", (await context.Set<AuthenticationSessionRow>().SingleAsync()).State);
            Assert.Equal(2, await context.Set<OutboxMessage>().CountAsync());
        }
        finally { foreach (var response in responses) response.Dispose(); }
    }
    [Theory]
    [InlineData(AccountState.Closed, true)]
    [InlineData(AccountState.VerificationPending, true)]
    [InlineData(AccountState.Active, false)]
    public async Task ClosedPendingAndUnverifiedCredentialsAreRejectedWithoutDisclosure(AccountState state, bool confirmed)
    {
        await using var db = await fixture.CreateDatabaseAsync(); await using var host = await Start(db); await host.SeedAsync(db, state, confirmed);
        var boot = await host.Options(); using var response = await host.Post("/authentication/sign-in", Command(boot.Key), boot.Cookies, boot.Csrf);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode); var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("AUTHENTICATION_FAILED", body, StringComparison.Ordinal); Assert.DoesNotContain(state.ToString(), body); Assert.DoesNotContain("person@example.test", body);
        await using var context = db.Context(); Assert.False(await context.Set<AuthenticationSessionRow>().AnyAsync());
    }
    [Theory]
    [InlineData(AccountState.Restricted, true)]
    [InlineData(AccountState.Restricted, false)]
    [InlineData(AccountState.Closing, true)]
    [InlineData(AccountState.Closing, false)]
    [InlineData(AccountState.Active, true)]
    public async Task TrustedScopedAccessControlsRestrictionsAndMinorAgeAloneDoesNotDeny(AccountState state, bool allowed)
    {
        await using var db = await fixture.CreateDatabaseAsync(); await using var host = await Start(db); await host.SeedAsync(db, state, minor: true); host.Access.Allowed = allowed;
        var boot = await host.Options(); using var response = await host.Post("/authentication/sign-in", Command(boot.Key), boot.Cookies, boot.Csrf);
        Assert.Equal(allowed ? HttpStatusCode.OK : HttpStatusCode.Unauthorized, response.StatusCode);
        if (allowed) { using var session = await host.Get("/authentication/session", Merge(boot.Cookies, response)); Assert.Equal(HttpStatusCode.OK, session.StatusCode); }
    }
    [Theory]
    [InlineData("stamp")]
    [InlineData("closed")]
    [InlineData("restriction")]
    [InlineData("contact")]
    public async Task CurrentAuthorityImmediatelyInvalidatesExistingSession(string change)
    {
        await using var db = await fixture.CreateDatabaseAsync(); await using var host = await Start(db); await host.SeedAsync(db);
        var boot = await host.Options(); using var signed = await host.Post("/authentication/sign-in", Command(boot.Key), boot.Cookies, boot.Csrf); Assert.Equal(HttpStatusCode.OK, signed.StatusCode);
        await using (var update = db.Context())
        {
            if (change == "stamp") (await update.Users.SingleAsync()).SecurityStamp = Guid.NewGuid().ToString("N");
            if (change == "closed") { var account = await update.Accounts.SingleAsync(); account.State = AccountState.Closed; account.Version++; }
            if (change == "contact") { var contact = await update.VerifiedContacts.SingleAsync(); contact.State = ContactVerificationState.Revoked; contact.Version++; }
            if (change == "restriction") host.Access.Allowed = false;
            await update.SaveChangesAsync();
        }
        using var session = await host.Get("/authentication/session", Merge(boot.Cookies, signed)); Assert.Equal(HttpStatusCode.Unauthorized, session.StatusCode);
        await using var check = db.Context(); Assert.Equal("REVOKED", (await check.Set<AuthenticationSessionRow>().SingleAsync()).State);
    }
    [Fact]
    public async Task TrustedMaintenanceClearsExpiredBearerMaterialWithoutAUserRequestOrDuplicateEvents()
    {
        await using var db = await fixture.CreateDatabaseAsync(); await using var host = await Start(db); await host.SeedAsync(db);
        var boot = await host.Options(); using var signed = await host.Post("/authentication/sign-in", Command(boot.Key), boot.Cookies, boot.Csrf); Assert.Equal(HttpStatusCode.OK, signed.StatusCode);
        host.Clock.Advance(TimeSpan.FromMinutes(5));
        await using var scope = host.Services.CreateAsyncScope(); var maintenance = scope.ServiceProvider.GetRequiredService<IAuthenticationSessionMaintenance>();
        Assert.Equal(1, await maintenance.ExpireSessionsAsync()); Assert.Equal(0, await maintenance.ExpireSessionsAsync());
        Assert.Equal(2, await maintenance.PurgeExpiredAbuseAsync()); Assert.Equal(0, await maintenance.PurgeExpiredAbuseAsync());
        await using var check = db.Context(); var row = await check.Set<AuthenticationSessionRow>().SingleAsync(); Assert.Equal("EXPIRED", row.State); Assert.Empty(row.ProtectedKey); Assert.Empty(row.ProtectedTicket);
        Assert.Equal(2, await check.Set<OutboxMessage>().CountAsync());
    }
    [Fact]
    public async Task IdleExpiryIsAuthoritativeAndNeverResurrected()
    {
        await using var db = await fixture.CreateDatabaseAsync(); await using var host = await Start(db); await host.SeedAsync(db);
        var boot = await host.Options(); using var signed = await host.Post("/authentication/sign-in", Command(boot.Key), boot.Cookies, boot.Csrf); Assert.Equal(HttpStatusCode.OK, signed.StatusCode);
        host.Clock.Advance(TimeSpan.FromMinutes(5)); var cookies = Merge(boot.Cookies, signed);
        using (var expired = await host.Get("/authentication/session", cookies)) Assert.Equal(HttpStatusCode.Unauthorized, expired.StatusCode);
        using (var stillEnded = await host.Get("/authentication/session", cookies)) Assert.Equal(HttpStatusCode.Unauthorized, stillEnded.StatusCode);
        await using var check = db.Context(); Assert.Equal("EXPIRED", (await check.Set<AuthenticationSessionRow>().SingleAsync()).State);
    }
    [Fact]
    public async Task ActivityCannotExtendAbsoluteExpiryAndRememberUsesExplicitPolicy()
    {
        await using var db = await fixture.CreateDatabaseAsync(); await using var host = await Start(db); await host.SeedAsync(db);
        var boot = await host.Options(); using var signed = await host.Post("/authentication/sign-in", Command(boot.Key, remember: true), boot.Cookies, boot.Csrf); Assert.Equal(HttpStatusCode.OK, signed.StatusCode);
        Assert.Contains(signed.Headers.GetValues("Set-Cookie"), x => x.StartsWith(AuthenticationIdentity.CookieName + "=", StringComparison.Ordinal) && x.Contains("expires=", StringComparison.OrdinalIgnoreCase));
        var cookies = Merge(boot.Cookies, signed);
        for (var i = 0; i < 32; i++) { host.Clock.Advance(TimeSpan.FromMinutes(15)); using var status = await host.Get("/authentication/session", cookies); Assert.Equal(i == 31 ? HttpStatusCode.Unauthorized : HttpStatusCode.OK, status.StatusCode); }
        await using var check = db.Context(); var row = await check.Set<AuthenticationSessionRow>().SingleAsync(); Assert.Equal("EXPIRED", row.State); Assert.Equal(TimeSpan.FromHours(8), row.ExpiresAt - row.AuthenticatedAt);
    }
    [Fact]
    public async Task InvalidUnknownDuplicateOversizedAndUnboundPayloadsCannotBypassBackendValidation()
    {
        await using var db = await fixture.CreateDatabaseAsync(); await using var host = await Start(db); await host.SeedAsync(db); var boot = await host.Options();
        using (var noCsrf = await host.Post("/authentication/sign-in", Command(boot.Key), boot.Cookies, null)) Assert.Equal(HttpStatusCode.BadRequest, noCsrf.StatusCode);
        using (var wrongOrigin = await host.Post("/authentication/sign-in", Command(boot.Key), boot.Cookies, boot.Csrf, "https://outside.example.test")) Assert.Equal(HttpStatusCode.Forbidden, wrongOrigin.StatusCode);
        using (var noContext = await host.Post("/authentication/sign-in", Command(boot.Key), boot.Cookies.Replace(AuthenticationEndpoints.BrowserCookieName, "Untrusted", StringComparison.Ordinal), boot.Csrf)) Assert.Equal(HttpStatusCode.Forbidden, noContext.StatusCode);
        using (var wrongKey = await host.Post("/authentication/sign-in", Command(Guid.NewGuid()), boot.Cookies, boot.Csrf)) Assert.Equal(HttpStatusCode.BadRequest, wrongKey.StatusCode);
        using (var missing = await host.Post("/authentication/sign-in", new { }, boot.Cookies, boot.Csrf)) Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        using (var longPassword = await host.Post("/authentication/sign-in", Command(boot.Key, new string('x', 129)), boot.Cookies, boot.Csrf)) Assert.Equal(HttpStatusCode.BadRequest, longPassword.StatusCode);
        using (var huge = await host.Post("/authentication/sign-in", new { padding = new string('x', 6000) }, boot.Cookies, boot.Csrf)) Assert.Equal(HttpStatusCode.RequestEntityTooLarge, huge.StatusCode);
        var raw = JsonSerializer.Serialize(Command(boot.Key)); raw = raw[..^1] + ",\"AccountId\":\"forged\"}";
        using (var actor = await host.RawPost("/authentication/sign-in", raw, boot.Cookies, boot.Csrf)) Assert.Equal(HttpStatusCode.BadRequest, actor.StatusCode);
        using (var duplicate = await host.RawPost("/authentication/sign-in", "{\"Email\":\"a\",\"email\":\"b\"}", boot.Cookies, boot.Csrf)) Assert.Equal(HttpStatusCode.BadRequest, duplicate.StatusCode);
        await using var check = db.Context(); Assert.False(await check.Set<AuthenticationSessionRow>().AnyAsync());
    }
    [Fact]
    public async Task LegacyPasswordAuthenticatesWithoutRegistrationMinimumAndAbuseIsBoundToIdentifier()
    {
        await using var db = await fixture.CreateDatabaseAsync(); await using var host = await Start(db); await host.SeedAsync(db, password: "old-pass");
        host.Policies.Policy = host.Policies.Policy with { IdentifierAttemptLimit = 2 };
        var boot = await host.Options(); using var accepted = await host.Post("/authentication/sign-in", Command(boot.Key, "old-pass"), boot.Cookies, boot.Csrf); Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        var next = await host.Options(Merge(boot.Cookies, accepted));
        using (var rejected = await host.Post("/authentication/sign-in", Command(next.Key, "incorrect"), next.Cookies, next.Csrf)) Assert.Equal(HttpStatusCode.Unauthorized, rejected.StatusCode);
        using var limited = await host.Post("/authentication/sign-in", Command(next.Key, "old-pass"), next.Cookies, next.Csrf); Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        await using var check = db.Context(); Assert.Single(await check.Set<AuthenticationSessionRow>().ToListAsync());
    }
    [Fact]
    public async Task MissingOrStaleTrustedAuthorityCannotCreateSession()
    {
        await using var db = await fixture.CreateDatabaseAsync(); await using var host = await Start(db); await host.SeedAsync(db); var boot = await host.Options();
        host.Access.Stale = true; using (var stale = await host.Post("/authentication/sign-in", Command(boot.Key), boot.Cookies, boot.Csrf)) Assert.Equal(HttpStatusCode.ServiceUnavailable, stale.StatusCode);
        host.Policies.Available = false; using (var unavailable = await host.Get("/authentication/options")) Assert.Equal(HttpStatusCode.ServiceUnavailable, unavailable.StatusCode);
        await using var check = db.Context(); Assert.False(await check.Set<AuthenticationSessionRow>().AnyAsync());
    }
    [Fact]
    public async Task ForeignOrAdminCookieDoesNotAuthenticateAndStorageCannotExtendSessionIdentity()
    {
        await using var db = await fixture.CreateDatabaseAsync(); await using var first = await Start(db); await first.SeedAsync(db); var boot = await first.Options();
        using var signed = await first.Post("/authentication/sign-in", Command(boot.Key), boot.Cookies, boot.Csrf); Assert.Equal(HttpStatusCode.OK, signed.StatusCode); var cookies = Merge(boot.Cookies, signed);
        await using var second = await Start(db); using (var foreign = await second.Get("/authentication/session", cookies)) Assert.Equal(HttpStatusCode.Unauthorized, foreign.StatusCode);
        using (var admin = await first.Get("/authentication/session", cookies.Replace(AuthenticationIdentity.CookieName, "__Host-Tunner.Admin.Session", StringComparison.Ordinal))) Assert.Equal(HttpStatusCode.Unauthorized, admin.StatusCode);
        await using var context = db.Context(); var row = await context.Set<AuthenticationSessionRow>().SingleAsync(); row.ExpiresAt = row.ExpiresAt.AddHours(1); row.Version++;
        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }
}
