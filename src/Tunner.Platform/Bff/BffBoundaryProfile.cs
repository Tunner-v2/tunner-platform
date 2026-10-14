namespace Tunner.Platform.Bff;

/// <summary>Technical browser surfaces, not Account or workforce roles.</summary>
public enum BffSurface { User, Admin }

internal sealed record BffBoundaryProfile(string ApplicationName, string Scheme, string CookieName, string AntiforgeryCookieName, Uri? BrowserOrigin)
{
    internal static BffBoundaryProfile Create(BffSurface surface, string? browserOrigin)
    {
        var name = surface switch
        {
            BffSurface.User => "Tunner.User.Bff",
            BffSurface.Admin => "Tunner.Admin.Bff",
            _ => throw new ArgumentOutOfRangeException(nameof(surface), "Unknown BFF surface.")
        };
        Uri? origin = null;
        if (browserOrigin is not null && !TryOrigin(browserOrigin, out origin))
            throw new ArgumentException("BFF browser origin must be an absolute HTTPS origin without credentials, path, query or fragment.", nameof(browserOrigin));
        return new(name, name + ".Session", $"__Host-Tunner.{surface}.Session", $"__Host-Tunner.{surface}.Csrf", origin);
    }

    internal static bool TryOrigin(string value, out Uri? origin)
    {
        origin = null;
        if (string.IsNullOrWhiteSpace(value) || !string.Equals(value, value.Trim(), StringComparison.Ordinal) || value.Any(char.IsControl)) return false;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var candidate) || candidate.Scheme != Uri.UriSchemeHttps ||
            candidate.UserInfo.Length != 0 || candidate.AbsolutePath != "/" || candidate.Query.Length != 0 || candidate.Fragment.Length != 0) return false;
        origin = candidate;
        return true;
    }

    internal bool Matches(Uri? origin) => origin is not null && BrowserOrigin is not null &&
        Uri.Compare(BrowserOrigin, origin, UriComponents.SchemeAndServer, UriFormat.SafeUnescaped, StringComparison.OrdinalIgnoreCase) == 0;
}
