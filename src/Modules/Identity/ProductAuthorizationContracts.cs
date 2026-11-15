using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using Tunner.Platform.Policies;

namespace Tunner.Identity.Persistence;

/// <summary>Canonical protocol input, constructed by the native server adapter, never an authorization decision.</summary>
public sealed record ProductAuthorizationRequest(string ProductId, string Environment, string ClientId, string RedirectUri,
    ImmutableArray<string> Scopes, ImmutableArray<string> Resources, string CodeChallenge, string CodeChallengeMethod,
    string ResponseType, string ResponseMode, string? State, string? Nonce)
{
    // Omit absent controls so uncontrolled request digests retain their exact original serialization.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ProductAuthorizationAuthenticationContext? AuthenticationContext { get; init; }
    [JsonIgnore] public string Digest => AuthenticationIdentity.Digest(JsonSerializer.Serialize(this));
    internal void Require(ProductAuthorizationPolicy policy)
    {
        if (new[] { ProductId, ClientId, RedirectUri }.Any(string.IsNullOrWhiteSpace) ||
            Environment is not ("SANDBOX" or "STAGING" or "PRODUCTION") ||
            !Uri.TryCreate(RedirectUri, UriKind.Absolute, out var redirect) || redirect.Scheme != "https" ||
            redirect.UserInfo.Length != 0 || redirect.Fragment.Length != 0 || redirect.AbsoluteUri != RedirectUri ||
            ResponseType != "code" || ResponseMode != "form_post" || CodeChallengeMethod != "S256" ||
            CodeChallenge is not { Length: 43 } || CodeChallenge.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '-' and not '_') ||
            State?.Length > policy.MaximumCorrelationLength || Nonce?.Length > policy.MaximumCorrelationLength ||
            !Canonical(Scopes) || !Canonical(Resources) || Resources.Length == 0 ||
            (!policy.AllowRefreshTokens && Scopes.Contains("offline_access", StringComparer.Ordinal)))
            throw new ArgumentException("A supported canonical Product authorization request is required.");
        AuthenticationContext?.Require(policy);
        if (AuthenticationContext is { } controls && (controls.MaxAgeSeconds is not null || controls.RequestedAcrValues.Length > 0) &&
            !Scopes.Contains("openid", StringComparer.Ordinal))
            throw new ArgumentException("OIDC authentication-time and ACR controls require the openid scope.");
    }
    internal static bool Canonical(ImmutableArray<string> values) => !values.IsDefault &&
        values.All(x => !string.IsNullOrWhiteSpace(x) && !x.Any(char.IsWhiteSpace)) &&
        values.SequenceEqual(values.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal), StringComparer.Ordinal);
    public override string ToString() => nameof(ProductAuthorizationRequest);
}

/// <summary>Trusted host qualification. All policy, privacy, assurance and lifetime values are explicit.</summary>
public sealed record ProductAuthorizationPolicy(PolicyVersionCandidate<string> Metadata, string Origin,
    string EvidenceReference, string ProcessingReference, string RetentionReference, string Classification,
    ImmutableArray<string> AllowedAuthenticationMethods, ImmutableArray<string> AllowedAssurances,
    TimeSpan MaximumAuthenticationAge, TimeSpan AuthorizationCodeLifetime, TimeSpan AccessTokenLifetime, TimeSpan IdentityTokenLifetime,
    bool AllowRefreshTokens, TimeSpan? RefreshTokenLifetime, int MaximumCorrelationLength)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ProductAuthorizationInteractionQualification? InteractionQualification { get; init; }
    internal string Snapshot => JsonSerializer.Serialize(this);
    internal void Require(string origin, DateTimeOffset at)
    {
        if (Metadata is null || Metadata.Scope != origin || Metadata.Status != "ACTIVE" ||
            string.IsNullOrWhiteSpace(Metadata.VersionId) || !Metadata.EffectiveInterval.Contains(at) || Origin != origin ||
            !Uri.TryCreate(origin, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.UserInfo.Length != 0 ||
            uri.AbsolutePath != "/" || uri.Query.Length != 0 || uri.Fragment.Length != 0 ||
            new[] { EvidenceReference, ProcessingReference, RetentionReference }.Any(string.IsNullOrWhiteSpace) ||
            Classification is not ("INTERNAL" or "CONFIDENTIAL" or "RESTRICTED_PERSONAL" or "RESTRICTED_SECURITY") ||
            !ProductAuthorizationRequest.Canonical(AllowedAuthenticationMethods) || AllowedAuthenticationMethods.Length == 0 ||
            !ProductAuthorizationRequest.Canonical(AllowedAssurances) || AllowedAssurances.Length == 0 ||
            MaximumAuthenticationAge <= TimeSpan.Zero || AuthorizationCodeLifetime <= TimeSpan.Zero || AccessTokenLifetime <= TimeSpan.Zero ||
            IdentityTokenLifetime <= TimeSpan.Zero ||
            MaximumCorrelationLength <= 0 || (AllowRefreshTokens ? RefreshTokenLifetime is null || RefreshTokenLifetime <= TimeSpan.Zero : RefreshTokenLifetime is not null))
            throw new InvalidOperationException("Current explicit Product authorization qualification is unavailable.");
        InteractionQualification?.Require();
    }
    public override string ToString() => nameof(ProductAuthorizationPolicy);
}
public interface IProductAuthorizationPolicySource
{
    ProductAuthorizationPolicy RequireCurrent(string origin, DateTimeOffset at);
}

/// <summary>Registry-owned exact client, Product and environment qualification; no browser assertion activates a registration.</summary>
public sealed record ProductAuthorizationRegistration(string RequestDigest, string ProductId, long ProductVersion,
    string ProductState, string Environment, long EnvironmentVersion, string EnvironmentState, string ClientId,
    long ClientVersion, string ClientState, string NativeApplicationId, string ClientType, string RedirectUri,
    ImmutableArray<string> AllowedScopes, ImmutableArray<string> AllowedResources, string PolicyVersion,
    string EvidenceReference, bool ExplicitProductRequestAllowed, EffectivePolicyInterval EffectiveInterval)
{
    public override string ToString() => nameof(ProductAuthorizationRegistration);
}
public interface IProductAuthorizationRegistrySource
{
    ValueTask<ProductAuthorizationRegistration> RequireCurrentAsync(ProductAuthorizationRequest request,
        DateTimeOffset at, CancellationToken cancellationToken);
}

public sealed record ProductRelationshipRequest(string AccountId, long AccountVersion, Guid SessionId,
    string ProductId, long ProductVersion, string Environment, long EnvironmentVersion, string ClientId,
    long ClientVersion, string RequestDigest, string OperationId, string CorrelationId)
{
    [JsonIgnore] public string Digest => AuthenticationIdentity.Digest(JsonSerializer.Serialize(this));
    public override string ToString() => nameof(ProductRelationshipRequest);
}

/// <summary>This port reports an independently committed owner fact, never implicit enlistment in Identity's transaction.</summary>
public sealed record ProductRelationshipDecision(string RequestDigest, string RelationshipId, long Version,
    bool Active, bool IndependentlyCommitted, string PolicyVersion, string EvidenceReference, string Subject,
    string SubjectMappingVersion, string SubjectMappingEvidenceReference, EffectivePolicyInterval EffectiveInterval)
{
    public override string ToString() => nameof(ProductRelationshipDecision);
}
public interface IProductAuthorizationRelationshipSource
{
    ValueTask<ProductRelationshipDecision> EnsureActiveAsync(ProductRelationshipRequest request, DateTimeOffset at,
        CancellationToken cancellationToken);
    ValueTask<ProductRelationshipDecision> RequireCurrentAsync(ProductRelationshipRequest request, DateTimeOffset at,
        CancellationToken cancellationToken);
}

/// <summary>Private native authorization Properties payload. It is not a Product or SDK contract.</summary>
internal sealed record ProductAuthorizationLineage(int SchemaVersion, ProductAuthorizationRequest Request,
    string AccountId, long AccountVersion, Guid SessionId, string Origin, DateTimeOffset AuthenticatedAt,
    string AuthenticationMethods, string Assurance, string AuthenticationPolicyVersion, string SecurityStampDigest,
    string SessionUseSnapshot, string AccountAuthorizationSnapshot, string PolicySnapshot,
    ProductAuthorizationRegistration Registration, ProductRelationshipRequest RelationshipRequest,
    ProductRelationshipDecision Relationship, string OperationId, string CorrelationId, DateTimeOffset AuthorizedAt,
    DateTimeOffset CodeExpiresAt)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ProductAuthorizationInteractionContext? Interaction { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ProductAuthorizationAuthenticationClaims? AuthenticationClaims { get; init; }
    public override string ToString() => nameof(ProductAuthorizationLineage);
}

internal sealed record ProductAuthorizationGrant(ProductAuthorizationLineage Lineage, ProductAuthorizationPolicy Policy,
    ProductAuthorizationActor Actor)
{
    internal string Subject => Lineage.Relationship.Subject;
    internal bool IsRedemption { get; init; }
    public override string ToString() => nameof(ProductAuthorizationGrant);
}

internal sealed record ProductAuthorizationActor(AuthenticationSessionRow Session, long AccountVersion,
    AuthenticationPolicy AuthenticationPolicy, AccountAuthenticationAccessDecision SessionUse,
    AccountAuthenticationAccessDecision ProductAuthorization)
{
    public override string ToString() => nameof(ProductAuthorizationActor);
}
