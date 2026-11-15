using System.Collections.Immutable;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OpenIddict.Abstractions;
using Tunner.Database.Outbox;

namespace Tunner.Identity.Persistence;

/// <summary>Private lifecycle owner. Current grant policy controls continuity/effects; native stores own artifact transitions.</summary>
internal sealed class ProductAuthorizationLifecycleApplication(IdentityPersistenceDbContext database, AuthenticationApplication authentication,
    IProductAuthorizationRegistrySource registry, IProductAuthorizationRelationshipSource relationships,
    IProductAuthorizationLifecycleSource lifecycle, TimeProvider clock)
{
    private static readonly ImmutableHashSet<string> SupportedDisclosure = ImmutableHashSet.Create(StringComparer.Ordinal,
        "aud", "client_id", "cnf", "exp", "iat", "iss", "jti", "nbf", "scope", "sub", "token_type", "token_usage", "username");

    internal static string AccountOperation(ProductAuthorizationLifecycleOperation operation) => operation switch
    {
        ProductAuthorizationLifecycleOperation.Refresh => "REFRESH_PRODUCT_AUTHORIZATION",
        ProductAuthorizationLifecycleOperation.Introspection => "INTROSPECT_PRODUCT_AUTHORIZATION",
        ProductAuthorizationLifecycleOperation.Revocation => "REVOKE_PRODUCT_AUTHORIZATION",
        _ => throw new ArgumentOutOfRangeException(nameof(operation))
    };

    internal async Task<ProductAuthorizationLifecycleEvaluation> EvaluateAsync(ProductAuthorizationLineage original,
        ProductAuthorizationLifecycleRequest request, CancellationToken ct)
    {
        RequireTransaction(); ArgumentNullException.ThrowIfNull(original); ArgumentNullException.ThrowIfNull(request);
        RequireRequest(original, request);
        var external = await ObserveAsync(original, request.Operation, ct).ConfigureAwait(false);
        var observedAt = clock.GetUtcNow();
        var facts = new ProductAuthorizationLifecycleFacts(request.Operation, observedAt, request.RequestingClientId,
            request.RequestingNativeApplicationId, request.NativeState, Digest(original), original.Request.Digest,
            original.AccountId, original.AccountVersion, original.SessionId, original.Origin, original.Request.ProductId,
            original.Request.Environment, original.Request.ClientId, OriginalPolicyVersion(original),
            AuthenticationIdentity.Digest(original.PolicySnapshot), original.Registration, original.Relationship,
            original.Request.Scopes, original.Request.Resources, request.RequestedScopes, request.RequestedResources, external);
        var decision = await lifecycle.RequireCurrentAsync(facts, observedAt, ct).ConfigureAwait(false);
        RequireDecision(facts, decision, clock.GetUtcNow());
        return new(original, request, facts, decision);
    }

    /// <summary>External facts must still match; expected native writes are checked separately from the original decision digest.</summary>
    internal async Task<bool> VerifyCurrentAsync(ProductAuthorizationLifecycleEvaluation evaluation,
        ProductAuthorizationNativeState afterNative, CancellationToken ct)
    {
        RequireTransaction(); ArgumentNullException.ThrowIfNull(evaluation); ArgumentNullException.ThrowIfNull(afterNative);
        if (!ExpectedNativeState(evaluation, afterNative)) return false;
        var external = await ObserveAsync(evaluation.Original, evaluation.Request.Operation, ct).ConfigureAwait(false);
        if (external.Digest != evaluation.Facts.External.Digest) return false;
        // InitialNativeState remains explicitly the pre-operation observation. Reusing it does not
        // misclassify this transaction's own redeemed/revoked native status as an external authority change.
        var current = await lifecycle.RequireCurrentAsync(evaluation.Facts, clock.GetUtcNow(), ct).ConfigureAwait(false);
        RequireDecision(evaluation.Facts, current, clock.GetUtcNow());
        var at = clock.GetUtcNow();
        return Same(current, evaluation.Decision) && current.EffectiveInterval.Contains(at) && ExternalCurrentAt(external, at) &&
            (!current.ArtifactActive || at < evaluation.Request.NativeState.ExpiresAt) && ExpiriesCurrentAt(current, at);
    }

    internal void StageNativeEffect(ProductAuthorizationLifecycleEvaluation evaluation, ProductAuthorizationNativeState afterNative,
        string operationId, string correlationId)
    {
        RequireTransaction(); ArgumentNullException.ThrowIfNull(evaluation);
        ArgumentException.ThrowIfNullOrWhiteSpace(operationId); ArgumentException.ThrowIfNullOrWhiteSpace(correlationId);
        if (!ExpectedNativeState(evaluation, afterNative))
            throw new InvalidOperationException("The exact native lifecycle effect has not been observed.");
        var decision = evaluation.Decision;
        if (!decision.Allowed) return;
        var type = decision.Effect != ProductAuthorizationArtifactEffect.None ? "ProductAuthorizationRevoked" : evaluation.Request.Operation switch
        {
            ProductAuthorizationLifecycleOperation.Refresh when decision.ArtifactActive => "ProductAuthorizationRefreshed",
            ProductAuthorizationLifecycleOperation.Refresh => "ProductAuthorizationRefreshDenied",
            ProductAuthorizationLifecycleOperation.Introspection => "ProductAuthorizationIntrospected",
            _ => throw new InvalidOperationException("Known-artifact revocation requires a qualified native effect.")
        };
        database.Add(new OutboxMessage(Guid.NewGuid(), "1.0", type, "1", clock.GetUtcNow(), "tunner/identity",
            afterNative.AuthorizationId, decision.Classification,
            JsonSerializer.Serialize(new { reference = afterNative.AuthorizationId, tokenReference = afterNative.TokenId,
                product = evaluation.Facts.ProductId, environment = evaluation.Facts.Environment,
                originalRequestDigest = evaluation.Facts.OriginalRequestDigest, factsDigest = evaluation.Facts.Digest,
                policyVersion = decision.PolicyVersion, evidenceReference = decision.EvidenceReference,
                processingReference = decision.ProcessingReference, retentionReference = decision.RetentionReference,
                operation = evaluation.Request.Operation.ToString(), active = decision.ArtifactActive,
                effect = decision.Effect.ToString(), originalOperation = evaluation.Original.OperationId }),
            "1", operationId, correlationId));
    }

    private async Task<ProductAuthorizationExternalFacts> ObserveAsync(ProductAuthorizationLineage original,
        ProductAuthorizationLifecycleOperation operation, CancellationToken ct)
    {
        var actor = await authentication.ReadProductLifecycleFactsAsync(original, operation, ct).ConfigureAwait(false);
        var registration = await registry.RequireCurrentAsync(original.Request, clock.GetUtcNow(), ct).ConfigureAwait(false);
        if (registration is null || registration.RequestDigest != original.Request.Digest ||
            registration.ProductId != original.Request.ProductId || registration.Environment != original.Request.Environment ||
            registration.ClientId != original.Request.ClientId || registration.RedirectUri != original.Request.RedirectUri ||
            registration.ProductVersion <= 0 || registration.EnvironmentVersion <= 0 || registration.ClientVersion <= 0 ||
            new[] { registration.NativeApplicationId, registration.ProductState, registration.EnvironmentState, registration.ClientState,
                registration.PolicyVersion, registration.EvidenceReference }.Any(string.IsNullOrWhiteSpace) ||
            registration.ClientType is not ("public" or "confidential") ||
            !ProductAuthorizationRequest.Canonical(registration.AllowedScopes) || !ProductAuthorizationRequest.Canonical(registration.AllowedResources) ||
            registration.EffectiveInterval is null || !registration.EffectiveInterval.Contains(clock.GetUtcNow()))
            throw new InvalidOperationException("Exact current Product/environment/client lifecycle evidence is unavailable.");
        var relationship = await relationships.RequireCurrentAsync(original.RelationshipRequest, clock.GetUtcNow(), ct).ConfigureAwait(false);
        if (relationship is null || relationship.RequestDigest != original.RelationshipRequest.Digest || relationship.Version <= 0 ||
            !relationship.IndependentlyCommitted || new[] { relationship.RelationshipId, relationship.PolicyVersion, relationship.EvidenceReference,
                relationship.Subject, relationship.SubjectMappingVersion, relationship.SubjectMappingEvidenceReference }.Any(string.IsNullOrWhiteSpace) ||
            relationship.EffectiveInterval is null || !relationship.EffectiveInterval.Contains(clock.GetUtcNow()))
            throw new InvalidOperationException("Exact current Product Relationship lifecycle evidence is unavailable.");
        // Current inactive/suspended/changed facts are deliberately retained for the lifecycle owner.
        // They are neither replaced with the original snapshots nor silently turned into a cascade.
        return new(actor.Account, actor.Session, registration, relationship);
    }

    private static void RequireRequest(ProductAuthorizationLineage original, ProductAuthorizationLifecycleRequest request)
    {
        if (!Enum.IsDefined(request.Operation) || original.Request is null || original.Registration is null || original.Relationship is null ||
            original.RelationshipRequest is null || string.IsNullOrWhiteSpace(original.PolicySnapshot) ||
            new[] { original.AccountId, original.Origin, request.RequestingClientId, request.RequestingNativeApplicationId }.Any(string.IsNullOrWhiteSpace) ||
            original.AccountVersion <= 0 || original.SessionId == Guid.Empty || request.NativeState is not { } state ||
            new[] { state.AuthorizationId, state.TokenId }.Any(string.IsNullOrWhiteSpace) || state.AuthorizationStatus != "valid" ||
            state.NativeApplicationId != original.Registration.NativeApplicationId || state.Subject != original.Relationship.Subject ||
            state.TokenType is not (OpenIddictConstants.TokenTypeIdentifiers.AccessToken or OpenIddictConstants.TokenTypeIdentifiers.RefreshToken) || state.ExpiresAt <= state.CreatedAt ||
            (state.TokenStatus != "valid" && !(request.Operation == ProductAuthorizationLifecycleOperation.Refresh && state.TokenStatus == "redeemed")) ||
            request.Operation == ProductAuthorizationLifecycleOperation.Refresh && state.TokenType != OpenIddictConstants.TokenTypeIdentifiers.RefreshToken ||
            !ProductAuthorizationRequest.Canonical(state.Scopes) || !ProductAuthorizationRequest.Canonical(state.Resources) ||
            !ProductAuthorizationRequest.Canonical(request.RequestedScopes) || !ProductAuthorizationRequest.Canonical(request.RequestedResources) ||
            !Subset(state.Scopes, original.Request.Scopes) || !Subset(state.Resources, original.Request.Resources) ||
            !Subset(request.RequestedScopes, state.Scopes) || !Subset(request.RequestedResources, state.Resources))
            throw new ArgumentException("An exact native lifecycle operation and immutable original artifact binding are required.");
    }

    private static void RequireDecision(ProductAuthorizationLifecycleFacts facts, ProductAuthorizationLifecycleDecision decision, DateTimeOffset at)
    {
        if (decision is null || decision.FactsDigest != facts.Digest ||
            new[] { decision.PolicyVersion, decision.EvidenceReference, decision.ProcessingReference, decision.RetentionReference }.Any(string.IsNullOrWhiteSpace) ||
            decision.Classification is not ("INTERNAL" or "CONFIDENTIAL" or "RESTRICTED_PERSONAL" or "RESTRICTED_SECURITY") ||
            decision.EffectiveInterval is null || !decision.EffectiveInterval.Contains(at) || !Enum.IsDefined(decision.Effect) ||
            !ProductAuthorizationRequest.Canonical(decision.IntrospectionClaims) || decision.IntrospectionClaims.Any(x => !SupportedDisclosure.Contains(x)) ||
            (!decision.Allowed && (decision.ArtifactActive || decision.Effect != ProductAuthorizationArtifactEffect.None)) ||
            decision.ArtifactActive && decision.Effect != ProductAuthorizationArtifactEffect.None ||
            facts.Operation != ProductAuthorizationLifecycleOperation.Introspection && decision.IntrospectionClaims.Length != 0 ||
            !decision.ArtifactActive && decision.IntrospectionClaims.Length != 0 || !ExpiriesCurrentAt(decision, at))
            throw new InvalidOperationException("Current exact qualified Product artifact lifecycle policy is unavailable.");
        if (facts.Operation == ProductAuthorizationLifecycleOperation.Refresh && decision.Allowed && decision.ArtifactActive)
        {
            if (facts.InitialNativeState.TokenType != OpenIddictConstants.TokenTypeIdentifiers.RefreshToken || facts.InitialNativeState.ExpiresAt <= at ||
                !facts.OriginalScopes.Contains("offline_access", StringComparer.Ordinal) || facts.RequestedResources.Length == 0 ||
                decision.AccessTokenExpiresAt is null || decision.RefreshTokenExpiresAt is null ||
                facts.RequestedScopes.Contains("openid", StringComparer.Ordinal) != (decision.IdentityTokenExpiresAt is not null) ||
                facts.External.Account.OperationAccess?.Allowed != true)
                throw new InvalidOperationException("Qualified refresh permission and explicit token expiries are required.");
        }
        else if (decision.AccessTokenExpiresAt is not null || decision.IdentityTokenExpiresAt is not null || decision.RefreshTokenExpiresAt is not null)
            throw new InvalidOperationException("Only an active qualified refresh decision can authorize new token expiries.");
        if (facts.Operation == ProductAuthorizationLifecycleOperation.Revocation && decision.Allowed &&
            (decision.ArtifactActive || decision.Effect == ProductAuthorizationArtifactEffect.None))
            throw new InvalidOperationException("Known-artifact revocation requires an explicit presented-token or authorization-chain effect.");
        if (facts.Operation == ProductAuthorizationLifecycleOperation.Introspection && decision.ArtifactActive &&
            (facts.InitialNativeState.ExpiresAt <= at || !facts.External.Account.Exists || facts.External.Account.OperationAccess?.Allowed != true))
            throw new InvalidOperationException("Current qualified Account and artifact evidence is required for active introspection.");
    }

    private static bool ExpectedNativeState(ProductAuthorizationLifecycleEvaluation evaluation, ProductAuthorizationNativeState current)
    {
        var initial = evaluation.Request.NativeState; var decision = evaluation.Decision;
        if (current.AuthorizationId != initial.AuthorizationId || current.NativeApplicationId != initial.NativeApplicationId ||
            current.Subject != initial.Subject || current.TokenId != initial.TokenId || current.TokenType != initial.TokenType ||
            current.CreatedAt != initial.CreatedAt || current.ExpiresAt != initial.ExpiresAt ||
            !current.Scopes.SequenceEqual(initial.Scopes, StringComparer.Ordinal) || !current.Resources.SequenceEqual(initial.Resources, StringComparer.Ordinal)) return false;
        var authorizationStatus = initial.AuthorizationStatus; var tokenStatus = initial.TokenStatus;
        if (decision.Allowed && decision.Effect != ProductAuthorizationArtifactEffect.None)
        {
            tokenStatus = "revoked";
            if (decision.Effect == ProductAuthorizationArtifactEffect.AuthorizationChain) authorizationStatus = "revoked";
        }
        else if (evaluation.Request.Operation == ProductAuthorizationLifecycleOperation.Refresh && decision.Allowed && decision.ArtifactActive)
            tokenStatus = decision.RefreshRotates ? "redeemed" : "valid";
        return current.AuthorizationStatus == authorizationStatus && current.TokenStatus == tokenStatus;
    }

    private static bool ExternalCurrentAt(ProductAuthorizationExternalFacts facts, DateTimeOffset at)
    {
        if (!facts.Registration.EffectiveInterval.Contains(at) || !facts.Relationship.EffectiveInterval.Contains(at) ||
            facts.Account.OperationAccess is { } access && !access.EffectiveInterval.Contains(at)) return false;
        if (facts.SourceSession is not { EffectiveExpiresAt: { } deadline, Expired: { } expired } session) return true;
        return expired == (at < session.AuthenticatedAt || at < session.LastSeenAt || at >= deadline);
    }
    private static bool ExpiriesCurrentAt(ProductAuthorizationLifecycleDecision decision, DateTimeOffset at) =>
        new[] { decision.AccessTokenExpiresAt, decision.IdentityTokenExpiresAt, decision.RefreshTokenExpiresAt }.All(expiry => expiry is null || expiry > at);
    private static bool Subset(ImmutableArray<string> values, ImmutableArray<string> allowed) => !allowed.IsDefault && values.All(x => allowed.Contains(x, StringComparer.Ordinal));
    private static string Digest<T>(T value) => AuthenticationIdentity.Digest(JsonSerializer.Serialize(value));
    private static bool Same<T>(T left, T right) => JsonSerializer.Serialize(left) == JsonSerializer.Serialize(right);
    private static string OriginalPolicyVersion(ProductAuthorizationLineage original)
    {
        using var json = JsonDocument.Parse(original.PolicySnapshot);
        return json.RootElement.GetProperty("Metadata").GetProperty("VersionId").GetString() is { Length: > 0 } version
            ? version : throw new InvalidOperationException("Original Product policy lineage is unavailable.");
    }
    private void RequireTransaction()
    {
        if (database.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Native Product lifecycle processing requires the caller-owned Identity transaction.");
    }
}
