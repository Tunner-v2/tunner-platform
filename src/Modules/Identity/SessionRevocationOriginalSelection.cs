using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;

namespace Tunner.Identity.Persistence;

/// <summary>Selects one protected original independently of the shorter proof dialog view.</summary>
internal static class SessionRevocationOriginalSelection
{
    internal const string CookieName = "__Host-Tunner.SessionOriginalSelection";
    private const string Purpose = "Tunner.User.SessionRevocation.Selection.v1";
    internal sealed record Selection(string Family, Guid RequestId, string BindingDigest, string AccountId, Guid ActorSessionId,
        string Origin, string AuthenticationVersion, DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt)
    { public override string ToString() => nameof(Selection); }
    internal static Selection Create(string family, Guid requestId, UserStepUpOperationContext operation, string authVersion,
        DateTimeOffset createdAt, DateTimeOffset expiresAt) => new(family, requestId, StepUpBrowserService.Digest(operation),
            operation.Actor.AccountId, operation.Actor.SessionId, operation.Actor.Origin, authVersion, createdAt, expiresAt);
    internal static string Protect(IDataProtectionProvider provider, Selection selection)
        => provider.CreateProtector(Purpose).Protect(JsonSerializer.Serialize(selection));
    internal static bool Matches(HttpContext http, IDataProtectionProvider provider, UserStepUpActor actor, string authenticationVersion,
        DateTimeOffset now, string family, Guid requestId, UserStepUpOperationContext operation)
    {
        var selected = ReadEvidence(http, provider, actor);
        return selected is not null && selected.Family == family && selected.RequestId == requestId &&
            selected.BindingDigest == StepUpBrowserService.Digest(operation) && selected.AuthenticationVersion == authenticationVersion &&
            selected.CreatedAt <= now && now < selected.ExpiresAt;
    }
    // Expired evidence can deny replacement of an uncertain original; it never grants selection or execution.
    internal static Selection? ReadEvidence(HttpContext http, IDataProtectionProvider provider, UserStepUpActor actor)
    {
        if (!http.Request.Cookies.TryGetValue(CookieName, out var value) || value.Length > 4096) return null;
        Selection? selected;
        try { selected = JsonSerializer.Deserialize<Selection>(provider.CreateProtector(Purpose).Unprotect(value)); }
        catch (CryptographicException) { return null; } catch (JsonException) { return null; }
        return selected is not null && selected.Family is "SINGLE" or "BULK" && selected.RequestId != Guid.Empty &&
            selected.BindingDigest is { Length: 64 } && selected.BindingDigest.All(Uri.IsHexDigit) && selected.AccountId == actor.AccountId &&
            selected.ActorSessionId == actor.SessionId && selected.Origin == actor.Origin && !string.IsNullOrWhiteSpace(selected.AuthenticationVersion) &&
            selected.CreatedAt < selected.ExpiresAt ? selected : null;
    }
    internal static string Fingerprint(HttpContext http) => http.Request.Cookies.TryGetValue(CookieName, out var value)
        ? AuthenticationIdentity.Digest(value) : AuthenticationIdentity.Digest(string.Empty);
    internal static CookieOptions Cookie(TimeSpan remaining) => new()
        { Path = "/", HttpOnly = true, Secure = true, SameSite = SameSiteMode.Strict, IsEssential = true, MaxAge = remaining };
    internal static bool Fits(string name, string protectedValue, CookieOptions options, int maximumBytes)
        => maximumBytes is > 0 and <= 4096 && Encoding.UTF8.GetByteCount(options.CreateCookieHeader(name, protectedValue).ToString()) <= maximumBytes;
}
