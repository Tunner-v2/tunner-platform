using System.Text;

namespace Tunner.Identity.Persistence;

internal enum FederationProviderKind { Google, Microsoft }

/// <summary>
/// Identity-binding groundwork only; no protocol verification, current-policy resolution or authorization.
/// A future qualified adapter must obtain this exact stable namespace and explicit issuer mapping from the
/// trusted admitted profile, finish framework protocol checks, then revalidate profile/currentness at execution.
/// Identity-bearing mapping changes require separately qualified migration; this type performs none.
/// </summary>
internal sealed class FederationProviderRegistration
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private readonly HashSet<string> admittedIssuers;
    private readonly string clientId;
    private readonly string providerNamespace;
    private readonly string profileVersion;
    private readonly string mappingEvidenceReference;
    private readonly int maximumSubjectUtf8Bytes;

    internal FederationProviderRegistration(FederationProviderKind provider, string stableNamespaceId, string issuer, string clientId,
        IEnumerable<string> explicitlyAdmittedIssuerAliases, string profileVersion, string mappingEvidenceReference,
        int maximumNamespaceUtf8Bytes, int maximumSubjectUtf8Bytes)
    {
        if (!Enum.IsDefined(provider)) throw new ArgumentOutOfRangeException(nameof(provider));
        if (maximumNamespaceUtf8Bytes <= 0 || maximumSubjectUtf8Bytes <= 0)
            throw new ArgumentException("Explicit positive identity bounds are required.");
        ArgumentException.ThrowIfNullOrWhiteSpace(stableNamespaceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(clientId);
        ArgumentException.ThrowIfNullOrWhiteSpace(profileVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(mappingEvidenceReference);
        ArgumentNullException.ThrowIfNull(explicitlyAdmittedIssuerAliases);
        if (stableNamespaceId.Contains('\0') || clientId.Contains('\0') || ByteCount(stableNamespaceId) > maximumNamespaceUtf8Bytes)
            throw new ArgumentException("The registered namespace is outside its admitted identity shape.");
        _ = ByteCount(clientId);
        admittedIssuers = new(StringComparer.Ordinal) { RequireIssuer(provider, issuer) };
        foreach (var alias in explicitlyAdmittedIssuerAliases) admittedIssuers.Add(RequireIssuer(provider, alias));
        // Namespace is trusted registered identity, never synthesized from issuer spelling, client or policy version.
        providerNamespace = stableNamespaceId; this.clientId = clientId; this.profileVersion = profileVersion;
        this.mappingEvidenceReference = mappingEvidenceReference; this.maximumSubjectUtf8Bytes = maximumSubjectUtf8Bytes;
        Provider = provider;
    }

    internal FederationProviderKind Provider { get; }
    internal string ProviderNamespace => providerNamespace;
    internal string ClientId => clientId;
    internal string RegistrationProfileVersion => profileVersion;
    internal string IssuerMappingEvidenceReference => mappingEvidenceReference;
    internal IEnumerable<string> AdmittedIssuers => admittedIssuers.Order(StringComparer.Ordinal);

    internal FederationIdentityKey BindExternalIdentity(string protocolIssuer, string protocolClientId, string subject)
    {
        if (!admittedIssuers.Contains(protocolIssuer) || !StringComparer.Ordinal.Equals(protocolClientId, clientId))
            throw new ArgumentException("The external identity does not match its exact admitted registration profile.");
        if (subject is null || subject.Length == 0 || subject.Contains('\0') || ByteCount(subject) > maximumSubjectUtf8Bytes)
            throw new ArgumentException("The external subject is outside its admitted identity shape.");
        return new(providerNamespace, subject, profileVersion, mappingEvidenceReference);
    }

    private static string RequireIssuer(FederationProviderKind provider, string value)
    {
        // Claim issuer is not a discovery/network endpoint. Google documents this one exact legacy literal;
        // it still needs explicit profile admission, never implicit equivalence or a Microsoft fallback.
        if (value is null || value.Length == 0 || value.Contains('\0'))
            throw new ArgumentException("An exact admitted issuer literal is required.");
        _ = ByteCount(value);
        if (provider == FederationProviderKind.Google && value == "accounts.google.com") return value;
        if (value.Contains('*') || !Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps ||
            uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0)
            throw new ArgumentException("The registered issuer shape is unsupported.");
        return value;
    }

    private static int ByteCount(string value)
    {
        try { return StrictUtf8.GetByteCount(value); }
        catch (EncoderFallbackException) { throw new ArgumentException("Identity text must be valid UTF-8."); }
    }

    public override string ToString() => nameof(FederationProviderRegistration);
}

/// <summary>Key/provenance only, never provider proof or permission to link an Account.</summary>
internal sealed class FederationIdentityKey(string providerNamespace, string subject, string profileVersion, string mappingEvidenceReference)
{
    internal string ProviderNamespace { get; } = providerNamespace;
    internal string Subject { get; } = subject;
    internal string RegistrationProfileVersion { get; } = profileVersion;
    internal string IssuerMappingEvidenceReference { get; } = mappingEvidenceReference;

    // The future trusted owner must establish current Account/proof/confirmation/profile/mapping authority,
    // transaction/currentness and operation/outbox lineage. This prepares the existing persistence row only.
    internal ExternalIdentityLinkRow PrepareOwnedLink(Guid linkId, string accountId, string evidenceReference)
    {
        if (linkId == Guid.Empty) throw new ArgumentException("A stable owned link identity is required.", nameof(linkId));
        ArgumentException.ThrowIfNullOrWhiteSpace(accountId);
        ArgumentException.ThrowIfNullOrWhiteSpace(evidenceReference);
        return new() { LinkId = linkId, AccountId = accountId, Provider = ProviderNamespace, Subject = Subject, Version = 1, EvidenceReference = evidenceReference };
    }

    public override string ToString() => nameof(FederationIdentityKey);
}
