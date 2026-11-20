using System.Collections.Immutable;
using System.Text.Json;
using Microsoft.AspNetCore.Http;

namespace Tunner.Identity.Persistence;

/// <summary>Explicit conjunction of actual session and operation proof; no label ordering or production defaults.</summary>
public sealed record ProductAuthorizationOperationProofMapping(string RequestedAcr, string SessionAuthenticationMethods,
    string SessionAssurance, string ProofAuthenticationMethod, string ProofAssurance, AssurancePolicyScope Scope,
    string AssurancePolicyVersion, string IssuedAcr, ImmutableArray<string> IssuedAmr);

public sealed record ProductAuthorizationStepUpQualification(ImmutableArray<ProductAuthorizationOperationProofMapping> Mappings)
{
    internal void Require()
    {
        if (Mappings.IsDefault || Mappings.Length == 0 || Mappings.Any(mapping => mapping is null || mapping.Scope is null ||
            mapping.Scope.Operation != ProductAuthorizationApplication.AuthorizeOperation ||
            new[] { mapping.RequestedAcr, mapping.SessionAuthenticationMethods, mapping.SessionAssurance,
                mapping.ProofAuthenticationMethod, mapping.ProofAssurance, mapping.AssurancePolicyVersion }.Any(string.IsNullOrWhiteSpace) ||
            mapping.RequestedAcr.Any(char.IsWhiteSpace) || mapping.IssuedAcr != mapping.RequestedAcr ||
            !ProductAuthorizationRequest.Canonical(mapping.IssuedAmr)))
            throw new InvalidOperationException("Explicit Product operation-proof qualification is required.");
        if (Mappings.Select(mapping => (mapping.RequestedAcr, mapping.SessionAuthenticationMethods, mapping.SessionAssurance,
                mapping.ProofAuthenticationMethod)).Distinct().Count() != Mappings.Length)
            throw new InvalidOperationException("Each exact Product/session/proof combination needs one qualified mapping.");
    }
    public override string ToString() => nameof(ProductAuthorizationStepUpQualification);
}

/// <summary>Created only by the current native request owner, never from a browser command.</summary>
internal sealed record ProductAuthorizationProofBinding(string NativeRequestTokenId, string OperationId, string CorrelationId,
    string RequestDigest, string AccountId, long AccountVersion, Guid SessionId, string Origin, DateTimeOffset SessionAuthenticatedAt,
    string ProductPolicyDigest, string OwnerFactsDigest, string AssurancePolicyDigest,
    ImmutableArray<ProductAuthorizationOperationProofMapping> Mappings)
{
    internal string Digest => AuthenticationIdentity.Digest(JsonSerializer.Serialize(this));
    internal StepUpOperation Operation => new(OperationId, CorrelationId, Mappings[0].Scope, NativeRequestTokenId, AccountVersion, Digest);
    internal void Require(StepUpCaller caller, ProductAuthorizationPolicy current)
    {
        if (AccountId != caller.AccountId || SessionId != caller.SessionId || SessionId == Guid.Empty || Origin != caller.Origin || AccountVersion <= 0 ||
            string.IsNullOrWhiteSpace(NativeRequestTokenId) || string.IsNullOrWhiteSpace(OperationId) || string.IsNullOrWhiteSpace(CorrelationId) ||
            new[] { RequestDigest, OwnerFactsDigest, AssurancePolicyDigest, ProductPolicyDigest }.Any(value => value is not { Length: 64 } || !value.All(Uri.IsHexDigit)) ||
            ProductPolicyDigest != AuthenticationIdentity.Digest(current.Snapshot) || current.StepUpQualification is not { } qualification ||
            Mappings.IsDefaultOrEmpty || Mappings.Any(mapping => mapping is null))
            throw new InvalidOperationException("The exact current native Product operation binding is required.");
        var first = Mappings[0];
        if (Mappings.Any(mapping => mapping.Scope != first.Scope || mapping.AssurancePolicyVersion != first.AssurancePolicyVersion ||
                mapping.RequestedAcr != first.RequestedAcr || mapping.SessionAuthenticationMethods != first.SessionAuthenticationMethods ||
                mapping.SessionAssurance != first.SessionAssurance) ||
            Mappings.Select(mapping => mapping.ProofAuthenticationMethod).Distinct(StringComparer.Ordinal).Count() != Mappings.Length)
            throw new InvalidOperationException("A native proof binds one exact session/ACR and a common proof scope/version.");
        var expected = qualification.Mappings.Where(mapping => mapping.RequestedAcr == first.RequestedAcr &&
                mapping.SessionAuthenticationMethods == first.SessionAuthenticationMethods && mapping.SessionAssurance == first.SessionAssurance)
            .OrderBy(mapping => mapping.ProofAuthenticationMethod, StringComparer.Ordinal)
            .ThenBy(mapping => JsonSerializer.Serialize(mapping), StringComparer.Ordinal);
        if (!Mappings.Select(mapping => JsonSerializer.Serialize(mapping)).SequenceEqual(expected.Select(mapping => JsonSerializer.Serialize(mapping)), StringComparer.Ordinal))
            throw new InvalidOperationException("The full canonical qualified proof-method set must be bound before verification.");
    }
    public override string ToString() => nameof(ProductAuthorizationProofBinding);
}

internal sealed record ProductAuthorizationPreparedProof(Guid ProofId, string BindingDigest,
    ProductAuthorizationOperationProofMapping Mapping, DateTimeOffset ProofAuthenticatedAt, DateTimeOffset ExpiresAt)
{
    public override string ToString() => nameof(ProductAuthorizationPreparedProof);
}

internal sealed record ProductAuthorizationConsumedProof(Guid ProofId, string BindingDigest, long ConsumedVersion,
    DateTimeOffset ConsumedAt, DateTimeOffset ProofAuthenticatedAt, DateTimeOffset ExpiresAt,
    ProductAuthorizationOperationProofMapping Mapping)
{
    public override string ToString() => nameof(ProductAuthorizationConsumedProof);
}

/// <summary>Feature-specific existing-dialog handoff. Store only a protected proof ID, never a browser proof bearer.</summary>
internal interface IProductAuthorizationNativeProofHandoff
{
    Task<IResult> StageForNativeAsync(HttpContext http, UserStepUpOperationContext operation, string verifiedProofKey,
        CancellationToken cancellationToken);
}
