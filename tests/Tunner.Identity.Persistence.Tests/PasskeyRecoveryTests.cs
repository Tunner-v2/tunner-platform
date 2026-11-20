using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static Tunner.Identity.Persistence.Tests.AuthenticationTestHost;

namespace Tunner.Identity.Persistence.Tests;

public sealed partial class PasskeyTests
{
    [Fact]
    public async Task SignInReloadRecoversOnlyOriginalPendingChallengeAndRememberChoice()
    {
        await using var db = await fixture.CreateDatabaseAsync(); await using var host = await StartPasskeys(db); await host.SeedAsync(db);
        using var key = new PasskeyAuthenticator(); await Enroll(host, await Login(host), key);
        var boot = await host.Options(); using var started = await host.Post("/authentication/passkeys/options", new { requestId = boot.Key, rememberMe = true }, boot.Cookies, boot.Csrf);
        started.EnsureSuccessStatusCode(); var first = await started.Content.ReadFromJsonAsync<JsonElement>(); var id = first.GetProperty("ceremonyId").GetGuid(); var cookies = Merge(boot.Cookies, started);
        using var reload = await host.Get("/authentication/options", cookies); var view = await reload.Content.ReadFromJsonAsync<JsonElement>(); var recovery = view.GetProperty("passkeyRecovery");
        Assert.Equal(boot.Key, recovery.GetProperty("requestId").GetGuid()); Assert.Equal(id, recovery.GetProperty("ceremonyId").GetGuid());
        Assert.Equal("SIGN_IN", recovery.GetProperty("kind").GetString()); Assert.Equal("PENDING", recovery.GetProperty("status").GetString()); Assert.True(recovery.GetProperty("rememberMe").GetBoolean());
        Assert.Equal(JsonValueKind.Null, recovery.GetProperty("publicKey").ValueKind); Assert.Equal(JsonValueKind.Null, recovery.GetProperty("credentialReference").ValueKind);
        foreach (var field in new[] { "clientDataJSON", "authenticatorData", "attestationObject", "signature", "rawId" }) Assert.DoesNotContain(field, recovery.GetRawText(), StringComparison.Ordinal);
        using var wrong = await host.Post("/authentication/passkeys/options", new { requestId = boot.Key, rememberMe = false }, Merge(cookies, reload), view.GetProperty("csrfToken").GetString()); Assert.Equal(HttpStatusCode.Conflict, wrong.StatusCode);
        using var resumed = await host.Post("/authentication/passkeys/options", new { requestId = boot.Key, rememberMe = true }, Merge(cookies, reload), view.GetProperty("csrfToken").GetString()); resumed.EnsureSuccessStatusCode();
        var same = await resumed.Content.ReadFromJsonAsync<JsonElement>(); Assert.Equal(id, same.GetProperty("ceremonyId").GetGuid()); Assert.Equal(first.GetProperty("publicKey").GetProperty("challenge").GetString(), same.GetProperty("publicKey").GetProperty("challenge").GetString());
        await using var read = db.Context(); Assert.Equal(1, await read.Set<PasskeyCeremonyRow>().CountAsync(x => x.Kind == "SIGN_IN"));
    }
    [Theory]
    [InlineData(true)][InlineData(false)]
    public async Task CompletedSignInReloadResolvesExactResultWithOrWithoutIssuedCookie(bool receivedCookie)
    {
        await using var db = await fixture.CreateDatabaseAsync(); await using var host = await StartPasskeys(db); await host.SeedAsync(db);
        using var key = new PasskeyAuthenticator(); await Enroll(host, await Login(host), key); var attempt = await AssertionOptions(host); var id = attempt.Ceremony.GetProperty("ceremonyId").GetGuid();
        using var signed = await host.Post("/authentication/passkeys/sign-in", new { requestId = attempt.Bootstrap.Key, ceremonyId = id, credential = key.Assert(attempt.Ceremony.GetProperty("publicKey"), Origin) }, attempt.Bootstrap.Cookies, attempt.Bootstrap.Csrf); signed.EnsureSuccessStatusCode();
        var cookies = receivedCookie ? Merge(attempt.Bootstrap.Cookies, signed) : attempt.Bootstrap.Cookies;
        using var reload = await host.Get("/authentication/options", cookies); reload.EnsureSuccessStatusCode(); var view = await reload.Content.ReadFromJsonAsync<JsonElement>(); var recovery = view.GetProperty("passkeyRecovery");
        Assert.Equal(attempt.Bootstrap.Key, recovery.GetProperty("requestId").GetGuid()); Assert.Equal(id, recovery.GetProperty("ceremonyId").GetGuid()); Assert.Equal("COMPLETED", recovery.GetProperty("status").GetString()); Assert.Equal(!receivedCookie, recovery.GetProperty("signInRequired").GetBoolean());
        cookies = Merge(cookies, reload); var csrf = view.GetProperty("csrfToken").GetString();
        if (receivedCookie) { using var live = await host.Get("/authentication/session", cookies); csrf = (await live.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("csrfToken").GetString(); cookies = Merge(cookies, live); }
        using var result = await host.Post("/authentication/passkeys/result", new { requestId = attempt.Bootstrap.Key, ceremonyId = id }, cookies, csrf); result.EnsureSuccessStatusCode();
        var exact = await result.Content.ReadFromJsonAsync<JsonElement>(); Assert.Equal("COMPLETED", exact.GetProperty("status").GetString()); Assert.Equal(receivedCookie, exact.GetProperty("authenticated").GetBoolean()); Assert.Equal(!receivedCookie, exact.GetProperty("signInRequired").GetBoolean()); Assert.False(result.Headers.Contains("Set-Cookie"));
        using var wrong = await host.Post("/authentication/passkeys/result", new { requestId = Guid.NewGuid(), ceremonyId = id }, cookies, csrf); Assert.False(wrong.IsSuccessStatusCode);
        await using var read = db.Context(); Assert.Equal(1, await read.Set<AuthenticationSessionRow>().CountAsync(x => x.AuthenticationMethods == "passkey")); Assert.Equal(1, await read.Set<PasskeyCredentialRow>().Select(x => x.UsageVersion).SingleAsync());
        if (!receivedCookie) { using var live = await host.Get("/authentication/session", cookies); Assert.Equal(HttpStatusCode.Unauthorized, live.StatusCode); }
    }
    [Fact]
    public async Task MissingOriginalBootstrapResolvesPendingAsExpiredAndCannotRecreateOriginalRequest()
    {
        await using var db = await fixture.CreateDatabaseAsync(); await using var host = await StartPasskeys(db); await host.SeedAsync(db);
        using var key = new PasskeyAuthenticator(); await Enroll(host, await Login(host), key); var attempt = await AssertionOptions(host);
        var cookies = string.Join("; ", attempt.Bootstrap.Cookies.Split("; ").Where(x => !x.StartsWith(AuthenticationEndpoints.BrowserCookieName + "=", StringComparison.Ordinal)));
        using var reload = await host.Get("/authentication/options", cookies); var view = await reload.Content.ReadFromJsonAsync<JsonElement>(); var recovery = view.GetProperty("passkeyRecovery");
        Assert.Equal("EXPIRED", recovery.GetProperty("status").GetString()); Assert.Equal(attempt.Ceremony.GetProperty("ceremonyId").GetGuid(), recovery.GetProperty("ceremonyId").GetGuid());
        using var rejected = await host.Post("/authentication/passkeys/options", new { requestId = attempt.Bootstrap.Key, rememberMe = false }, Merge(cookies, reload), view.GetProperty("csrfToken").GetString()); Assert.Equal(HttpStatusCode.Conflict, rejected.StatusCode);
        using var result = await host.Post("/authentication/passkeys/result", new { requestId = attempt.Bootstrap.Key, ceremonyId = attempt.Ceremony.GetProperty("ceremonyId").GetGuid() }, Merge(cookies, reload), view.GetProperty("csrfToken").GetString()); result.EnsureSuccessStatusCode();
        Assert.Equal("EXPIRED", (await result.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetString());
        await using var read = db.Context(); Assert.Equal(1, await read.Set<PasskeyCeremonyRow>().CountAsync(x => x.Kind == "SIGN_IN")); Assert.Empty(await read.Set<AuthenticationSessionRow>().Where(x => x.AuthenticationMethods == "passkey").ToArrayAsync());
    }
    [Fact]
    public async Task DifferentCurrentSessionCannotBeAttributedToOriginalCommittedSignIn()
    {
        await using var db = await fixture.CreateDatabaseAsync(); await using var host = await StartPasskeys(db); await host.SeedAsync(db);
        using var key = new PasskeyAuthenticator(); await Enroll(host, await Login(host), key); var original = await AssertionOptions(host); var id = original.Ceremony.GetProperty("ceremonyId").GetGuid();
        using var signed = await host.Post("/authentication/passkeys/sign-in", new { requestId = original.Bootstrap.Key, ceremonyId = id, credential = key.Assert(original.Ceremony.GetProperty("publicKey"), Origin) }, original.Bootstrap.Cookies, original.Bootstrap.Csrf); signed.EnsureSuccessStatusCode();
        var other = await Login(host); var cookies = other + "; " + string.Join("; ", original.Bootstrap.Cookies.Split("; ").Where(x => x.StartsWith(PasskeyBrowserService.SignInRecoveryCookie + "=", StringComparison.Ordinal)));
        using var live = await host.Get("/authentication/session", cookies); var csrf = (await live.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("csrfToken").GetString();
        using var result = await host.Post("/authentication/passkeys/result", new { requestId = original.Bootstrap.Key, ceremonyId = id }, cookies, csrf); Assert.Equal(HttpStatusCode.Conflict, result.StatusCode);
        using var reload = await host.Get("/authentication/options", cookies); Assert.Equal(JsonValueKind.Null, (await reload.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("passkeyRecovery").ValueKind);
    }
}
