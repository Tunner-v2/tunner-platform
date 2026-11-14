using System.Collections.ObjectModel;

namespace Tunner.Identity.Persistence;

/// <summary>Approved Sessions presentation using the shared published-text provenance contract; no fallback publication.</summary>
public sealed class SessionListingPresentation
{
    internal static readonly ReadOnlyCollection<string> RequiredKeys = Array.AsReadOnly<string>([
        "brand.tunner", "account.sessions.header.accountCenter", "account.sessions.header.account", "account.sessions.header.menu",
        "account.sessions.header.search", "account.sessions.header.notifications", "account.sessions.navigation.account",
        "account.sessions.navigation.overview", "account.sessions.navigation.profile", "account.sessions.navigation.security",
        "account.sessions.navigation.sessions", "account.sessions.navigation.billing", "account.sessions.navigation.notifications",
        "account.sessions.navigation.products", "account.sessions.navigation.activity", "account.sessions.navigation.help",
        "account.sessions.navigation.helpDescription", "account.sessions.eyebrow", "account.sessions.title", "account.sessions.description",
        "account.sessions.current", "account.sessions.label", "account.sessions.currentHelp", "account.sessions.currentDetails",
        "account.sessions.other", "account.sessions.back", "account.sessions.information", "account.sessions.actions", "account.sessions.details",
        "account.sessions.reference", "account.sessions.started", "account.sessions.lastActivity", "account.sessions.expires", "account.sessions.expiresHelp",
        "account.sessions.savedStatus", "account.sessions.checkedStatus", "account.sessions.checkedAt", "account.sessions.unavailable",
        "account.sessions.unavailableHeading", "account.sessions.unavailableDescription", "account.sessions.collection", "account.sessions.collectionDescription",
        "account.sessions.moreAvailable", "account.sessions.allLoaded", "account.sessions.loadMore", "account.sessions.refresh", "account.sessions.about",
        "account.sessions.referenceHelp", "account.sessions.accessHeading", "account.sessions.accessDescription", "account.sessions.savedStatusHelp",
        "account.sessions.productHeading", "account.sessions.productBoundary", "account.sessions.securityHeading", "account.sessions.securityDescription",
        "account.sessions.reviewSecurity", "account.sessions.currentActionHelp", "account.sessions.otherActionHelp", "account.sessions.signOut",
        "account.sessions.signOutPending", "account.sessions.signOutUnknown", "account.sessions.checkSignIn", "account.sessions.loading",
        "account.sessions.error", "account.sessions.changed", "account.sessions.empty", "account.sessions.emptyDescription",
        "account.sessions.state.active", "account.sessions.state.revoked", "account.sessions.state.expired"
    ]);
    public SessionListingPresentation(string locale, IEnumerable<RegistrationPublishedText> content)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(locale); ArgumentNullException.ThrowIfNull(content);
        var values = content.ToDictionary(x => x.Key, StringComparer.Ordinal);
        if (RequiredKeys.Any(key => !values.ContainsKey(key)) || values.Values.Any(x => x.State != "PUBLISHED" || x.Locale != locale ||
            new[] { x.Key, x.VariantRef, x.VersionRef, x.PublicationRef, x.Text }.Any(string.IsNullOrWhiteSpace)))
            throw new ArgumentException("Sessions requires complete published content with provenance.", nameof(content));
        Locale = locale; Content = new ReadOnlyDictionary<string, RegistrationPublishedText>(values);
    }
    public string Locale { get; }
    public IReadOnlyDictionary<string, RegistrationPublishedText> Content { get; }
}
public interface ISessionListingPresentationSource
{
    SessionListingPresentation RequirePublished(AuthenticationPolicy policy);
}
