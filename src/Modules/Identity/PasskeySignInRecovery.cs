using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace Tunner.Identity.Persistence;

internal sealed record PasskeySignInSelection(Guid RequestId, Guid CeremonyId, string BrowserDigest, string Origin, bool RememberMe, DateTimeOffset ExpiresAt);
internal sealed partial class PasskeyBrowserService
{
    internal const string SignInRecoveryCookie = "__Host-Tunner.Passkey.SignIn";
    private IDataProtector SignInRecoveryProtection => protection.CreateProtector("Tunner.User.Passkey.SignInRecovery.v1");
    private void SetSignInRecovery(HttpContext http, PasskeyCeremonyRow row)
    {
        var selected = new PasskeySignInSelection(row.RequestId, row.CeremonyId, row.BrowserDigest, row.Origin, row.RememberMe, row.RecoveryExpiresAt);
        http.Response.Cookies.Append(SignInRecoveryCookie, SignInRecoveryProtection.Protect(JsonSerializer.Serialize(selected)),
            new CookieOptions { Path = "/", HttpOnly = true, Secure = true, SameSite = SameSiteMode.Strict, IsEssential = true, MaxAge = row.RecoveryExpiresAt - clock.GetUtcNow() });
    }
    private PasskeySignInSelection? ReadSignInRecovery(HttpContext http)
    {
        if (!http.Request.Cookies.TryGetValue(SignInRecoveryCookie, out var cookie)) return null;
        try
        {
            var selected = JsonSerializer.Deserialize<PasskeySignInSelection>(SignInRecoveryProtection.Unprotect(cookie));
            return selected is not null && selected.Origin == surface.Origin && selected.ExpiresAt > clock.GetUtcNow() ? selected : null;
        }
        catch (CryptographicException) { return null; } catch (JsonException) { return null; }
    }
    internal async Task<PasskeyRecoveryView?> SignInRecoveryBootstrapAsync(HttpContext http)
    {
        var selected = ReadSignInRecovery(http); if (selected is null) return null;
        var now = clock.GetUtcNow();
        var hasOriginalBrowser = AuthenticationEndpoints.TryBrowser(http, AuthenticationEndpoints.Protector(protection), now, out var browser);
        if (hasOriginalBrowser && AuthenticationIdentity.Digest(browser!.Nonce) != selected.BrowserDigest) return null;
        var row = await database.Set<PasskeyCeremonyRow>().AsNoTracking().SingleOrDefaultAsync(x => x.CeremonyId == selected.CeremonyId && x.RequestId == selected.RequestId &&
            x.Kind == "SIGN_IN" && x.BrowserDigest == selected.BrowserDigest && x.Origin == selected.Origin && x.RememberMe == selected.RememberMe, http.RequestAborted).ConfigureAwait(false);
        if (row is null || row.RecoveryExpiresAt != selected.ExpiresAt) return null;
        var current = AuthenticationIdentity.SessionId(http.User);
        if (http.User.Identity?.IsAuthenticated == true && current != (row.State == "COMPLETED" ? row.ResultSessionId : row.PreviousSessionId)) return null;
        var signInRequired = row.State == "COMPLETED" && current != row.ResultSessionId;
        var status = row.State;
        if (status == "PENDING" && (!hasOriginalBrowser || browser!.PolicyVersion != authenticationPolicies.RequireEffective(now).Version || !application.CurrentBinding(row, application.Current(surface.Origin), now))) status = "EXPIRED";
        var protectedRecovery = SignInRecoveryProtection.Protect(JsonSerializer.Serialize(selected));
        return new(row.RequestId, row.CeremonyId, "SIGN_IN", status, null, 0, row.ExpiresAt, protectedRecovery, signInRequired, RememberMe: row.RememberMe);
    }
}
