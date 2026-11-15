using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using Tunner.Platform.Policies;

namespace Tunner.Identity.Persistence;

public enum ProductAuthorizationLifecycleOperation { Refresh, Introspection, Revocation }
public enum ProductAuthorizationArtifactEffect { None, PresentedToken, AuthorizationChain }

/// <summary>Trusted native store metadata, never a raw token, token payload or client credential.</summary>
public sealed record ProductAuthorizationNativeState(string AuthorizationId, string AuthorizationStatus,
    string NativeApplicationId, string Subject, string TokenId, string TokenType, string TokenStatus,
    DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt, ImmutableArray<string> Scopes, ImmutableArray<string> Resources)
{
    public override string ToString() => nameof(ProductAuthorizationNativeState);
}

public sealed record ProductAuthorizationAccountFacts(string AccountId, bool Exists, string? State, long? Version,
    bool OperationAccessEvaluated, AccountAuthenticationAccessDecision? OperationAccess)
{
    public override string ToString() => nameof(ProductAuthorizationAccountFacts);
}

/// <summary>Recorded state and current validation facts. Reading these never records browser activity.</summary>
public sealed record ProductAuthorizationSessionFacts(Guid SessionId, string AccountId, string Origin, string Scheme,
    string State, long Version, DateTimeOffset AuthenticatedAt, DateTimeOffset LastSeenAt, DateTimeOffset ExpiresAt,
    DateTimeOffset? EffectiveExpiresAt,
    string AuthenticationMethods, string Assurance, string? AuthenticationPolicyVersion, string? AuthenticationPolicyDigest,
    bool BindingMatches, bool? Expired, bool SecurityStampCurrent, bool VerifiedContactCurrent, bool? MethodCurrent)
{
    public override string ToString() => nameof(ProductAuthorizationSessionFacts);
}

/// <summary>Current owner snapshots; changes are inputs to qualified grant/token continuity policy.</summary>
public sealed record ProductAuthorizationExternalFacts(ProductAuthorizationAccountFacts Account,
    ProductAuthorizationSessionFacts? SourceSession, ProductAuthorizationRegistration Registration,
    ProductRelationshipDecision Relationship)
{
    [JsonIgnore] public string Digest => AuthenticationIdentity.Digest(JsonSerializer.Serialize(this));
    public override string ToString() => nameof(ProductAuthorizationExternalFacts);
}

/// <summary>Exact initial operation facts. Native state is the observation before this operation's expected mutation.</summary>
public sealed record ProductAuthorizationLifecycleFacts(ProductAuthorizationLifecycleOperation Operation, DateTimeOffset ObservedAt,
    string RequestingClientId, string RequestingNativeApplicationId, ProductAuthorizationNativeState InitialNativeState,
    string OriginalLineageDigest, string OriginalRequestDigest, string AccountId, long OriginalAccountVersion,
    Guid SourceSessionId, string Origin, string ProductId, string Environment, string OriginalClientId,
    string OriginalProductPolicyVersion, string OriginalProductPolicyDigest, ProductAuthorizationRegistration OriginalRegistration,
    ProductRelationshipDecision OriginalRelationship, ImmutableArray<string> OriginalScopes, ImmutableArray<string> OriginalResources,
    ImmutableArray<string> RequestedScopes,
    ImmutableArray<string> RequestedResources, ProductAuthorizationExternalFacts External)
{
    [JsonIgnore] public string Digest => AuthenticationIdentity.Digest(JsonSerializer.Serialize(this));
    public override string ToString() => nameof(ProductAuthorizationLifecycleFacts);
}

/// <summary>Explicit trusted qualification, with no default lifetime, disclosure or revocation cascade.</summary>
public sealed record ProductAuthorizationLifecycleDecision(string FactsDigest, string PolicyVersion, string EvidenceReference,
    string ProcessingReference, string RetentionReference, string Classification, EffectivePolicyInterval EffectiveInterval,
    bool Allowed, bool ArtifactActive, bool RefreshRotates,
    DateTimeOffset? AccessTokenExpiresAt, DateTimeOffset? IdentityTokenExpiresAt, DateTimeOffset? RefreshTokenExpiresAt,
    ImmutableArray<string> IntrospectionClaims, ProductAuthorizationArtifactEffect Effect)
{
    public override string ToString() => nameof(ProductAuthorizationLifecycleDecision);
}

public interface IProductAuthorizationLifecycleSource
{
    ValueTask<ProductAuthorizationLifecycleDecision> RequireCurrentAsync(ProductAuthorizationLifecycleFacts facts,
        DateTimeOffset at, CancellationToken cancellationToken);
}

internal sealed record ProductAuthorizationLifecycleRequest(ProductAuthorizationLifecycleOperation Operation,
    string RequestingClientId, string RequestingNativeApplicationId, ProductAuthorizationNativeState NativeState,
    ImmutableArray<string> RequestedScopes, ImmutableArray<string> RequestedResources)
{
    public override string ToString() => nameof(ProductAuthorizationLifecycleRequest);
}

internal sealed record ProductAuthorizationLifecycleEvaluation(ProductAuthorizationLineage Original,
    ProductAuthorizationLifecycleRequest Request, ProductAuthorizationLifecycleFacts Facts,
    ProductAuthorizationLifecycleDecision Decision)
{
    internal string Subject => Original.Relationship.Subject;
    public override string ToString() => nameof(ProductAuthorizationLifecycleEvaluation);
}

internal sealed record ProductAuthorizationLifecycleActorFacts(ProductAuthorizationAccountFacts Account,
    ProductAuthorizationSessionFacts? Session);
