using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static Tunner.Identity.Persistence.Tests.AuthenticationTestHost;

namespace Tunner.Identity.Persistence.Tests;

public sealed class AuthenticationOutcomeTests(IdentityPostgresFixture fixture) : IClassFixture<IdentityPostgresFixture>
{
    private static async Task<(string Cookies, string Csrf)> Current(AuthenticationTestHost host, string cookies)
    {
        using var response = await host.Get("/authentication/session", cookies); response.EnsureSuccessStatusCode();
        return (Merge(cookies, response), (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("csrfToken").GetString()!);
    }
    [Fact]
    public async Task BodyLostAfterCookieRotationRecoversOnlyOriginalPasswordAttemptWithoutCreatingSession()
    {
        await using var db = await fixture.CreateDatabaseAsync(); await using var host = await Start(db); await host.SeedAsync(db);
        var original = await host.Options(); using var signed = await host.Post("/authentication/sign-in", Command(original.Key), original.Cookies, original.Csrf);
        Assert.Equal(HttpStatusCode.OK, signed.StatusCode); // Deliberately ignore body: only committed Set-Cookie was received.
        var current = await Current(host, Merge(original.Cookies, signed));
        using var recovered = await host.Post("/authentication/result", new { idempotencyKey = original.Key, email = "person@example.test", rememberMe = false }, current.Cookies, current.Csrf);
        Assert.Equal(HttpStatusCode.OK, recovered.StatusCode);
        var body = await recovered.Content.ReadFromJsonAsync<JsonElement>(); Assert.Equal("COMPLETED", body.GetProperty("status").GetString()); Assert.Equal(original.Key, body.GetProperty("idempotencyKey").GetGuid());
        Assert.False(recovered.Headers.Contains("Set-Cookie")); Assert.DoesNotContain("account", body.GetRawText(), StringComparison.OrdinalIgnoreCase);
        await using var context = db.Context(); Assert.Equal(1, await context.Set<AuthenticationSessionRow>().CountAsync());
    }
    [Theory]
    [InlineData("key")][InlineData("email")][InlineData("remember")][InlineData("browser")]
    public async Task ExistingSessionCannotProveChangedAttemptOrContext(string changed)
    {
        await using var db = await fixture.CreateDatabaseAsync(); await using var host = await Start(db); await host.SeedAsync(db);
        var original = await host.Options(); using var signed = await host.Post("/authentication/sign-in", Command(original.Key), original.Cookies, original.Csrf);
        var current = await Current(host, Merge(original.Cookies, signed)); var cookies = current.Cookies;
        if (changed == "browser") { var fresh = await host.Options(cookies); cookies = fresh.Cookies; current = await Current(host, cookies); }
        using var result = await host.Post("/authentication/result", new { idempotencyKey = changed == "key" ? Guid.NewGuid() : original.Key,
            email = changed == "email" ? "another@example.test" : "person@example.test", rememberMe = changed == "remember" }, current.Cookies, current.Csrf);
        Assert.Equal(HttpStatusCode.NotFound, result.StatusCode);
    }
    [Fact]
    public async Task LostFailureForChangedEmailCannotBeMistakenForPriorSuccessfulAttemptUsingSameBootstrap()
    {
        await using var db = await fixture.CreateDatabaseAsync(); await using var host = await Start(db); await host.SeedAsync(db);
        var original = await host.Options(); using var signed = await host.Post("/authentication/sign-in", Command(original.Key), original.Cookies, original.Csrf);
        var cookies = Merge(original.Cookies, signed);
        // Reusing the original credential transaction for a different attempt fails; current A remains authenticated.
        using var failed = await host.Post("/authentication/sign-in", Command(original.Key, email: "another@example.test"), cookies, original.Csrf);
        Assert.False(failed.IsSuccessStatusCode);
        var current = await Current(host, cookies);
        using var recovered = await host.Post("/authentication/result", new { idempotencyKey = original.Key, email = "another@example.test", rememberMe = false }, current.Cookies, current.Csrf);
        Assert.Equal(HttpStatusCode.NotFound, recovered.StatusCode);
        using var old = await host.Post("/authentication/result", new { idempotencyKey = original.Key, email = "person@example.test", rememberMe = false }, current.Cookies, current.Csrf);
        Assert.Equal(HttpStatusCode.OK, old.StatusCode);
    }
    [Fact]
    public async Task DifferentCurrentSessionAndEndedSessionCannotRecoverOldSuccess()
    {
        await using var db = await fixture.CreateDatabaseAsync(); await using var host = await Start(db); await host.SeedAsync(db);
        var original = await host.Options(); using var signed = await host.Post("/authentication/sign-in", Command(original.Key), original.Cookies, original.Csrf); var oldCookies = Merge(original.Cookies, signed);
        var next = await host.Options(oldCookies); using var rotated = await host.Post("/authentication/sign-in", Command(next.Key), next.Cookies, next.Csrf);
        var active = await Current(host, Merge(next.Cookies, rotated));
        using var wrong = await host.Post("/authentication/result", new { idempotencyKey = original.Key, email = "person@example.test", rememberMe = false }, active.Cookies, active.Csrf);
        Assert.Equal(HttpStatusCode.NotFound, wrong.StatusCode);
        using var revoked = await host.Post("/authentication/result", new { idempotencyKey = original.Key, email = "person@example.test", rememberMe = false }, oldCookies, original.Csrf);
        Assert.Equal(HttpStatusCode.Unauthorized, revoked.StatusCode);
    }
    [Fact]
    public async Task ExpiredSessionAndForgedCsrfAndWrongOriginCannotResolveAnOutcome()
    {
        await using var db = await fixture.CreateDatabaseAsync(); await using var host = await Start(db); await host.SeedAsync(db);
        var original = await host.Options(); using var signed = await host.Post("/authentication/sign-in", Command(original.Key), original.Cookies, original.Csrf); var current = await Current(host, Merge(original.Cookies, signed));
        var command = new { idempotencyKey = original.Key, email = "person@example.test", rememberMe = false };
        using var csrf = await host.Post("/authentication/result", command, current.Cookies, "forged"); Assert.Equal(HttpStatusCode.BadRequest, csrf.StatusCode);
        using var origin = await host.Post("/authentication/result", command, current.Cookies, current.Csrf, "https://other.example.test"); Assert.Equal(HttpStatusCode.Forbidden, origin.StatusCode);
        host.Clock.Advance(host.Policies.Policy.AbsoluteLifetime);
        using var expired = await host.Post("/authentication/result", command, current.Cookies, current.Csrf); Assert.Equal(HttpStatusCode.Unauthorized, expired.StatusCode);
    }
}
