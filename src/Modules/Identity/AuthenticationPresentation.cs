using System.Collections.ObjectModel;

namespace Tunner.Identity.Persistence;

/// <summary>Uses the existing Identity published-text provenance contract. No implicit publication or legal fallback.</summary>
public sealed class AuthenticationPresentation
{
    private static readonly string[] RequiredKeys = ["brand.tunner","login.heading","login.description","login.google","login.microsoft","login.unavailable","login.separator","login.email.label","login.email.placeholder","login.password.label","login.password.placeholder","login.remember","login.forgot","login.submit","login.create.description","login.create","login.show","login.hide","authentication.footer.privacy","authentication.footer.terms","authentication.footer.support","authentication.working","authentication.error.email","authentication.error.password","authentication.error.highlighted","authentication.error.credentials","authentication.error.limited","authentication.error.unavailable","signedIn.heading","signedIn.description","signedIn.note.heading","signedIn.note.description","signedIn.signOut","signedIn.caption"];
    public AuthenticationPresentation(string locale, IEnumerable<RegistrationPublishedText> content, Uri termsUri, Uri privacyUri, Uri? supportUri)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(locale); ArgumentNullException.ThrowIfNull(content);
        var values = content.ToDictionary(x => x.Key, StringComparer.Ordinal);
        if (RequiredKeys.Any(key => !values.ContainsKey(key)) || values.Values.Any(x => x.State != "PUBLISHED" || x.Locale != locale ||
            new[] { x.Key, x.VariantRef, x.VersionRef, x.PublicationRef, x.Text }.Any(string.IsNullOrWhiteSpace)))
            throw new ArgumentException("Authentication requires published content with provenance.", nameof(content));
        foreach (var uri in new[] { termsUri, privacyUri, supportUri }.OfType<Uri>())
            if (!uri.IsAbsoluteUri || uri.Scheme != Uri.UriSchemeHttps || uri.UserInfo.Length != 0) throw new ArgumentException("Published links must use credential-free HTTPS.");
        Locale = locale; Content = new ReadOnlyDictionary<string, RegistrationPublishedText>(values); TermsUri = termsUri; PrivacyUri = privacyUri; SupportUri = supportUri;
    }
    public string Locale { get; }
    public IReadOnlyDictionary<string, RegistrationPublishedText> Content { get; }
    public Uri TermsUri { get; }
    public Uri PrivacyUri { get; }
    public Uri? SupportUri { get; }
}
public interface IAuthenticationPresentationSource { AuthenticationPresentation RequirePublished(AuthenticationPolicy policy); }
