namespace Tunner.Identity.Persistence;

/// <summary>An explicitly qualified upstream method set and its owner-approved assurance mapping.</summary>
public sealed class FederationQualifiedAuthenticationMethods
{
    public FederationQualifiedAuthenticationMethods(IEnumerable<string> methods, string assurance)
    {
        ArgumentNullException.ThrowIfNull(methods);
        var values = methods.ToArray();
        if (values.Length == 0 || values.Any(value => string.IsNullOrWhiteSpace(value) || value.Any(char.IsControl)) ||
            values.Distinct(StringComparer.Ordinal).Count() != values.Length ||
            string.IsNullOrWhiteSpace(assurance) || assurance.Any(char.IsControl))
            throw new ArgumentException("An exact qualified authentication-method set and assurance are required.");
        Methods = Array.AsReadOnly(values.Order(StringComparer.Ordinal).ToArray()); Assurance = assurance;
    }
    public IReadOnlyCollection<string> Methods { get; }
    public string Assurance { get; }
    public override string ToString() => nameof(FederationQualifiedAuthenticationMethods);
}

/// <summary>Optional owner inputs. Absence never enables federated operation proof.</summary>
public sealed class FederationFreshAuthenticationProfile
{
    public FederationFreshAuthenticationProfile(string version, string capabilityEvidenceReference,
        string assuranceMappingEvidenceReference, TimeSpan maximumAuthenticationAge, string continuationPath,
        IEnumerable<FederationQualifiedAuthenticationMethods> qualifiedMethods)
    {
        ArgumentNullException.ThrowIfNull(qualifiedMethods);
        var values = qualifiedMethods.ToArray();
        if (new[] { version, capabilityEvidenceReference, assuranceMappingEvidenceReference }
                .Any(value => string.IsNullOrWhiteSpace(value) || value.Any(char.IsControl)) ||
            maximumAuthenticationAge <= TimeSpan.Zero || !FederationProfileVersion.CleanPath(continuationPath) || values.Length == 0 ||
            values.Any(value => value is null) || values.Any(first => values.Count(second =>
                first.Methods.SequenceEqual(second.Methods, StringComparer.Ordinal)) != 1))
            throw new ArgumentException("Explicit qualified freshness and assurance inputs are required.");
        Version = version; CapabilityEvidenceReference = capabilityEvidenceReference;
        AssuranceMappingEvidenceReference = assuranceMappingEvidenceReference;
        MaximumAuthenticationAge = maximumAuthenticationAge;
        ContinuationPath = continuationPath;
        QualifiedMethods = Array.AsReadOnly(values);
    }
    public string Version { get; }
    public string CapabilityEvidenceReference { get; }
    public string AssuranceMappingEvidenceReference { get; }
    public TimeSpan MaximumAuthenticationAge { get; }
    public string ContinuationPath { get; }
    public IReadOnlyCollection<FederationQualifiedAuthenticationMethods> QualifiedMethods { get; }

    internal FederationVerifiedAuthentication? Bind(DateTimeOffset authenticatedAt, IReadOnlyCollection<string> methods, DateTimeOffset at)
    {
        if (authenticatedAt > at ||
            authenticatedAt.UtcTicks > DateTimeOffset.MaxValue.UtcTicks - MaximumAuthenticationAge.Ticks ||
            authenticatedAt + MaximumAuthenticationAge <= at || methods.Count == 0 ||
            methods.Any(value => string.IsNullOrWhiteSpace(value) || value.Any(char.IsControl)) ||
            methods.Distinct(StringComparer.Ordinal).Count() != methods.Count) return null;
        var ordered = methods.Order(StringComparer.Ordinal).ToArray();
        var mapping = QualifiedMethods.SingleOrDefault(value => value.Methods.SequenceEqual(ordered, StringComparer.Ordinal));
        return mapping is null ? null : new(authenticatedAt, authenticatedAt + MaximumAuthenticationAge,
            Version, CapabilityEvidenceReference, AssuranceMappingEvidenceReference, mapping.Assurance, Array.AsReadOnly(ordered));
    }
    public override string ToString() => nameof(FederationFreshAuthenticationProfile);
}

/// <summary>Closed native-validation input, independently rebound to the original owner operation before proof issuance.</summary>
internal sealed class FederationVerifiedAuthentication(DateTimeOffset authenticatedAt, DateTimeOffset expiresAt,
    string freshnessPolicyVersion, string capabilityEvidenceReference, string assuranceMappingEvidenceReference,
    string assurance, IReadOnlyCollection<string> methods)
{
    internal DateTimeOffset AuthenticatedAt { get; } = authenticatedAt;
    internal DateTimeOffset ExpiresAt { get; } = expiresAt;
    internal string FreshnessPolicyVersion { get; } = freshnessPolicyVersion;
    internal string CapabilityEvidenceReference { get; } = capabilityEvidenceReference;
    internal string AssuranceMappingEvidenceReference { get; } = assuranceMappingEvidenceReference;
    internal string Assurance { get; } = assurance;
    internal IReadOnlyCollection<string> Methods { get; } = methods;
    public override string ToString() => nameof(FederationVerifiedAuthentication);
}
