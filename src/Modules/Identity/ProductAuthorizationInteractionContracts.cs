using System.Collections.Immutable;
using System.Text.Json;

namespace Tunner.Identity.Persistence;

/// <summary>Supported OIDC authentication controls. ACR values retain the client's preference order.</summary>
public sealed record ProductAuthorizationAuthenticationContext(string? Prompt, long? MaxAgeSeconds,
    ImmutableArray<string> RequestedAcrValues)
{
    internal void Require(ProductAuthorizationPolicy policy)
    {
        if (Prompt is not (null or "none" or "login") || MaxAgeSeconds < 0 ||
            !Values(RequestedAcrValues) || RequestedAcrValues.Any(value => value.Length > policy.MaximumCorrelationLength))
            throw new ArgumentException("Supported exact OIDC authentication controls are required.");
    }

    internal static bool Values(ImmutableArray<string> values) => !values.IsDefault &&
        values.All(value => !string.IsNullOrWhiteSpace(value) && !value.Any(char.IsWhiteSpace)) &&
        values.Distinct(StringComparer.Ordinal).Count() == values.Length;
    public override string ToString() => nameof(ProductAuthorizationAuthenticationContext);
}

/// <summary>Qualified exact mapping. No ordering of assurance names or methods is inferred.</summary>
public sealed record ProductAuthorizationAcrMapping(string RequestedAcr, string SessionAssurance,
    string SessionAuthenticationMethods, string IssuedAcr, ImmutableArray<string> IssuedAmr)
{
    internal void Require()
    {
        if (new[] { RequestedAcr, SessionAssurance, SessionAuthenticationMethods, IssuedAcr }.Any(string.IsNullOrWhiteSpace) ||
            RequestedAcr.Any(char.IsWhiteSpace) || IssuedAcr != RequestedAcr ||
            !ProductAuthorizationRequest.Canonical(IssuedAmr))
            throw new InvalidOperationException("Explicit requested-ACR, actual-session and issued-claim qualification is required.");
    }
    public override string ToString() => nameof(ProductAuthorizationAcrMapping);
}

/// <summary>Claim disclosure and exact mappings are versioned within the enclosing authorization policy.</summary>
public sealed record ProductAuthorizationInteractionQualification(bool EmitAuthenticationTime,
    ImmutableArray<ProductAuthorizationAcrMapping> AcrMappings)
{
    internal void Require()
    {
        if (AcrMappings.IsDefault || AcrMappings.Any(mapping => mapping is null))
            throw new InvalidOperationException("Explicit Product authentication-context qualification is required.");
        foreach (var mapping in AcrMappings) mapping.Require();
        if (AcrMappings.Select(mapping => (mapping.RequestedAcr, mapping.SessionAssurance, mapping.SessionAuthenticationMethods))
                .Distinct().Count() != AcrMappings.Length)
            throw new InvalidOperationException("A session authentication fact must have one exact claim mapping per requested ACR.");
    }
    public override string ToString() => nameof(ProductAuthorizationInteractionQualification);
}

/// <summary>Original server-captured request. Cross redirects only inside protected continuation material.</summary>
internal sealed record ProductAuthorizationInteractionContext(string RequestDigest, string Origin,
    DateTimeOffset ReceivedAt, Guid? OriginalSessionId, string PolicySnapshot)
{
    public override string ToString() => nameof(ProductAuthorizationInteractionContext);
}

internal enum ProductAuthorizationInteractionStatus { Satisfied, RequiresSignIn, RequiresFreshProof, Denied }

/// <summary>Actual authentication instant; later token issuance or refresh must never reset it.</summary>
internal sealed record ProductAuthorizationAuthenticationClaims(DateTimeOffset? AuthenticationTime, string? Acr,
    ImmutableArray<string> Amr)
{
    internal string Snapshot => JsonSerializer.Serialize(this);
    public override string ToString() => nameof(ProductAuthorizationAuthenticationClaims);
}

internal sealed record ProductAuthorizationInteractionEvaluation(ProductAuthorizationInteractionStatus Status,
    ProductAuthorizationAuthenticationClaims? AuthenticationClaims = null)
{
    public override string ToString() => nameof(ProductAuthorizationInteractionEvaluation);
}
