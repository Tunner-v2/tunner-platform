using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Tunner.Database.Outbox;
using Tunner.Platform.Policies;

namespace Tunner.Identity.Persistence;

/// <summary>Private Identity owner. Native server processing owns protocol validation, redemption and the outer transaction.</summary>
internal sealed partial class ProductAuthorizationApplication(IdentityPersistenceDbContext database, AuthenticationApplication authentication,
    IProductAuthorizationPolicySource policies, IProductAuthorizationRegistrySource registry,
    IProductAuthorizationRelationshipSource relationships, TimeProvider clock,
    StepUpApplication? proofApplication = null, IProductAuthorizationPresentationSource? proofPresentations = null)
{
    internal const string LineageProperty = "tunner.product.authorization.v1";
    internal const string AuthorizeOperation = "AUTHORIZE_PRODUCT";
    internal const string RedeemOperation = "REDEEM_PRODUCT_AUTHORIZATION";
    private static readonly JsonSerializerOptions LineageJson = new() { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };

    internal async Task<ProductAuthorizationRegistration?> RequireRegistrationAsync(ProductAuthorizationRequest request, string origin, CancellationToken ct)
    {
        RequireTransaction(); ArgumentNullException.ThrowIfNull(request);
        var policy = Policy(origin); request.Require(policy);
        var registration = await registry.RequireCurrentAsync(request, clock.GetUtcNow(), ct).ConfigureAwait(false);
        return Registered(request, registration, clock.GetUtcNow()) ? registration : null;
    }

    internal async Task<ProductAuthorizationGrant?> AuthorizeAsync(StepUpCaller caller, ProductAuthorizationRequest request,
        string operationId, string correlationId, CancellationToken ct, ProductAuthorizationInteractionContext? interaction = null,
        ProductAuthorizationNativeStepUp? nativeStepUp = null)
    {
        RequireTransaction(); ArgumentNullException.ThrowIfNull(caller); ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(operationId); ArgumentException.ThrowIfNullOrWhiteSpace(correlationId);
        var policy = Policy(caller.Origin); request.Require(policy);
        var registration = await registry.RequireCurrentAsync(request, clock.GetUtcNow(), ct).ConfigureAwait(false);
        if (!Registered(request, registration, clock.GetUtcNow())) return null;
        var actor = await authentication.LockProductAuthorizationAsync(caller, AuthorizeOperation, ct).ConfigureAwait(false);
        if (actor is null || !await authentication.ProductAuthorizationCurrentAsync(actor, AuthorizeOperation, ct).ConfigureAwait(false)) return null;
        var evaluation = await EvaluateNativeInteractionAsync(actor, request, caller.Origin, policy, interaction, nativeStepUp,
            caller, registration, operationId, correlationId, ct).ConfigureAwait(false);
        if (evaluation.Status != ProductAuthorizationInteractionStatus.Satisfied) return null;
        var relationshipRequest = new ProductRelationshipRequest(caller.AccountId, actor.AccountVersion, caller.SessionId,
            request.ProductId, registration.ProductVersion, request.Environment, registration.EnvironmentVersion,
            request.ClientId, registration.ClientVersion, request.Digest, operationId, correlationId);
        // The external owner commits its own Relationship fact. A failed Identity issuance does not claim to undo that fact.
        var relationship = await relationships.EnsureActiveAsync(relationshipRequest, clock.GetUtcNow(), ct).ConfigureAwait(false);
        if (!Related(relationshipRequest, relationship, clock.GetUtcNow())) return null;
        var at = clock.GetUtcNow();
        var expires = Earliest(at + policy.AuthorizationCodeLifetime, actor.Session.ExpiresAt,
            actor.Session.AuthenticatedAt + policy.MaximumAuthenticationAge, policy.Metadata.EffectiveInterval.Until,
            registration.EffectiveInterval.Until, relationship.EffectiveInterval.Until, nativeStepUp?.Receipt.ExpiresAt);
        if (expires <= at) return null;
        var lineage = new ProductAuthorizationLineage(1, request, caller.AccountId, actor.AccountVersion, caller.SessionId,
            caller.Origin, actor.Session.AuthenticatedAt, actor.Session.AuthenticationMethods, actor.Session.Assurance,
            actor.AuthenticationPolicy.Version, actor.Session.SecurityStampDigest, JsonSerializer.Serialize(actor.SessionUse),
            JsonSerializer.Serialize(actor.ProductAuthorization), policy.Snapshot, registration,
            relationshipRequest, relationship, operationId, correlationId, at, expires)
        { Interaction = interaction, AuthenticationClaims = evaluation.AuthenticationClaims, NativeStepUp = nativeStepUp };
        var grant = new ProductAuthorizationGrant(lineage, policy, actor) { ProofCaller = nativeStepUp is null ? null : caller };
        return await VerifyCurrentAsync(grant, ct).ConfigureAwait(false) ? grant : null;
    }

    internal async Task<ProductAuthorizationGrant?> RedeemAsync(ProductAuthorizationLineage original, CancellationToken ct,
        string? currentNetworkDigest = null)
    {
        RequireTransaction(); ArgumentNullException.ThrowIfNull(original);
        if (!Shape(original)) return null;
        var policy = Policy(original.Origin); original.Request.Require(policy);
        if (policy.Snapshot != original.PolicySnapshot || clock.GetUtcNow() >= original.CodeExpiresAt) return null;
        var actor = await authentication.LockProductAuthorizationAsync(original, RedeemOperation, ct).ConfigureAwait(false);
        if (actor is null || !OriginalActor(original, actor)) return null;
        StepUpCaller? caller = null;
        if (original.NativeStepUp is not null)
        {
            if (string.IsNullOrWhiteSpace(currentNetworkDigest)) return null;
            caller = StepUpCaller.FromProductAuthorizationActor(actor, original.Origin, currentNetworkDigest);
        }
        if (!await InteractionCurrentAsync(original, actor, policy, caller, ct).ConfigureAwait(false)) return null;
        var grant = new ProductAuthorizationGrant(original, policy, actor) { IsRedemption = true, ProofCaller = caller };
        return await VerifyCurrentAsync(grant, ct).ConfigureAwait(false) ? grant : null;
    }

    /// <summary>Called again by the native pre-output boundary, while the original Identity transaction and actor locks remain held.</summary>
    internal async Task<bool> VerifyCurrentAsync(ProductAuthorizationGrant grant, CancellationToken ct)
    {
        RequireTransaction(); ArgumentNullException.ThrowIfNull(grant);
        var original = grant.Lineage;
        if (!Shape(original) || !OriginalActor(original, grant.Actor)) return false;
        var policy = Policy(original.Origin);
        if (policy.Snapshot != original.PolicySnapshot || grant.Policy.Snapshot != policy.Snapshot) return false;
        original.Request.Require(policy);
        var registration = await registry.RequireCurrentAsync(original.Request, clock.GetUtcNow(), ct).ConfigureAwait(false);
        if (!Registered(original.Request, registration, clock.GetUtcNow()) || !Same(original.Registration, registration)) return false;
        var relationship = await relationships.RequireCurrentAsync(original.RelationshipRequest, clock.GetUtcNow(), ct).ConfigureAwait(false);
        if (!Related(original.RelationshipRequest, relationship, clock.GetUtcNow()) || !Same(original.Relationship, relationship)) return false;
        // This rechecks current Account restrictions, verified contact/security stamp and current authenticator facts.
        if (!await authentication.ProductAuthorizationCurrentAsync(grant.Actor,
                grant.IsRedemption ? RedeemOperation : AuthorizeOperation, ct).ConfigureAwait(false)) return false;
        if (!await InteractionCurrentAsync(original, grant.Actor, policy, grant.ProofCaller, ct).ConfigureAwait(false)) return false;
        var at = clock.GetUtcNow(); var finalPolicy = Policy(original.Origin);
        return finalPolicy.Snapshot == original.PolicySnapshot && at >= original.AuthorizedAt && at < original.CodeExpiresAt &&
            InteractionCurrentAt(original, grant.Actor, finalPolicy, at) && registration.EffectiveInterval.Contains(at) &&
            relationship.EffectiveInterval.Contains(at) && OriginalActor(original, grant.Actor);
    }

    /// <summary>Stage only for a successful native issuance/redemption. Save/commit remain with the native pre-output coordinator.</summary>
    internal void StageNativeEffect(ProductAuthorizationGrant grant, string authorizationId, bool redeemed)
    {
        RequireTransaction(); ArgumentNullException.ThrowIfNull(grant); ArgumentException.ThrowIfNullOrWhiteSpace(authorizationId);
        if (redeemed != grant.IsRedemption || !Shape(grant.Lineage))
            throw new InvalidOperationException("An exact native Product authorization effect is required.");
        var original = grant.Lineage;
        database.Add(new OutboxMessage(Guid.NewGuid(), "1.0", redeemed ? "ProductAuthorizationRedeemed" : "ProductAuthorizationGranted",
            "1", clock.GetUtcNow(), "tunner/identity", authorizationId, grant.Policy.Classification,
            JsonSerializer.Serialize(new { reference = authorizationId, product = original.Request.ProductId,
                environment = original.Request.Environment, relationship = original.Relationship.RelationshipId,
                relationshipVersion = original.Relationship.Version, policyVersion = grant.Policy.Metadata.VersionId,
                originalRequestDigest = original.Request.Digest }),
            "1", original.OperationId, original.CorrelationId));
    }

    internal static JsonElement WriteLineage(ProductAuthorizationGrant grant) => JsonSerializer.SerializeToElement(grant.Lineage, LineageJson);
    internal static ProductAuthorizationLineage? ReadLineage(JsonElement value)
    {
        try
        {
            var lineage = value.Deserialize<ProductAuthorizationLineage>(LineageJson);
            return lineage is not null && Shape(lineage) ? lineage : null;
        }
        catch (JsonException) { return null; }
        catch (ArgumentException) { return null; }
        catch (InvalidOperationException) { return null; }
    }

    private ProductAuthorizationPolicy Policy(string origin)
    {
        var at = clock.GetUtcNow(); var policy = policies.RequireCurrent(origin, at);
        policy.Require(origin, at); return policy;
    }
    private void RequireTransaction()
    {
        if (database.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Native Product processing must own the existing Identity transaction.");
    }
    private static bool Assurance(ProductAuthorizationActor actor, ProductAuthorizationPolicy policy, DateTimeOffset at) =>
        at >= actor.Session.AuthenticatedAt && at - actor.Session.AuthenticatedAt < policy.MaximumAuthenticationAge &&
        policy.AllowedAuthenticationMethods.Contains(actor.Session.AuthenticationMethods, StringComparer.Ordinal) &&
        policy.AllowedAssurances.Contains(actor.Session.Assurance, StringComparer.Ordinal);

    private static bool Registered(ProductAuthorizationRequest request, ProductAuthorizationRegistration registration, DateTimeOffset at) =>
        registration is not null && registration.RequestDigest == request.Digest && registration.ProductId == request.ProductId &&
        registration.ProductVersion > 0 && registration.ProductState == "ACTIVE" && registration.Environment == request.Environment &&
        registration.EnvironmentVersion > 0 && registration.EnvironmentState == "ACTIVE" && registration.ClientId == request.ClientId &&
        registration.ClientVersion > 0 && registration.ClientState == "ACTIVE" && registration.RedirectUri == request.RedirectUri &&
        !string.IsNullOrWhiteSpace(registration.NativeApplicationId) && registration.ClientType is "public" or "confidential" &&
        registration.ExplicitProductRequestAllowed && !string.IsNullOrWhiteSpace(registration.PolicyVersion) &&
        !string.IsNullOrWhiteSpace(registration.EvidenceReference) && registration.EffectiveInterval is not null &&
        registration.EffectiveInterval.Contains(at) && ProductAuthorizationRequest.Canonical(registration.AllowedScopes) &&
        ProductAuthorizationRequest.Canonical(registration.AllowedResources) &&
        request.Scopes.All(x => registration.AllowedScopes.Contains(x, StringComparer.Ordinal)) &&
        request.Resources.All(x => registration.AllowedResources.Contains(x, StringComparer.Ordinal));

    private static bool Related(ProductRelationshipRequest request, ProductRelationshipDecision decision, DateTimeOffset at) =>
        decision is not null && decision.RequestDigest == request.Digest && decision.Active && decision.IndependentlyCommitted &&
        !string.IsNullOrWhiteSpace(decision.RelationshipId) && decision.Version > 0 &&
        new[] { decision.PolicyVersion, decision.EvidenceReference, decision.Subject, decision.SubjectMappingVersion,
            decision.SubjectMappingEvidenceReference }.All(x => !string.IsNullOrWhiteSpace(x)) &&
        decision.EffectiveInterval is not null && decision.EffectiveInterval.Contains(at);

    private static bool Shape(ProductAuthorizationLineage original) => original.SchemaVersion == 1 && original.Request is not null &&
        original.Registration is not null && original.RelationshipRequest is not null && original.Relationship is not null &&
        ProductAuthorizationRequest.Canonical(original.Request.Scopes) && ProductAuthorizationRequest.Canonical(original.Request.Resources) &&
        !string.IsNullOrWhiteSpace(original.AccountId) && original.AccountVersion > 0 && original.SessionId != Guid.Empty &&
        !string.IsNullOrWhiteSpace(original.Origin) && !string.IsNullOrWhiteSpace(original.PolicySnapshot) &&
        !string.IsNullOrWhiteSpace(original.SessionUseSnapshot) && !string.IsNullOrWhiteSpace(original.AccountAuthorizationSnapshot) &&
        !string.IsNullOrWhiteSpace(original.OperationId) && !string.IsNullOrWhiteSpace(original.CorrelationId) &&
        original.SecurityStampDigest is { Length: 64 } && original.SecurityStampDigest.All(Uri.IsHexDigit) &&
        original.AuthenticatedAt <= original.AuthorizedAt && original.CodeExpiresAt > original.AuthorizedAt &&
        original.RelationshipRequest.AccountId == original.AccountId && original.RelationshipRequest.AccountVersion == original.AccountVersion &&
        original.RelationshipRequest.SessionId == original.SessionId && original.RelationshipRequest.ProductId == original.Request.ProductId &&
        original.RelationshipRequest.Environment == original.Request.Environment && original.RelationshipRequest.ClientId == original.Request.ClientId &&
        original.RelationshipRequest.ProductVersion == original.Registration.ProductVersion &&
        original.RelationshipRequest.EnvironmentVersion == original.Registration.EnvironmentVersion &&
        original.RelationshipRequest.ClientVersion == original.Registration.ClientVersion &&
        original.RelationshipRequest.RequestDigest == original.Request.Digest &&
        original.RelationshipRequest.OperationId == original.OperationId && original.RelationshipRequest.CorrelationId == original.CorrelationId &&
        original.Relationship.RequestDigest == original.RelationshipRequest.Digest &&
        (original.NativeStepUp is null || NativeProofBound(original.NativeStepUp, original.Request, original.AccountId,
            original.AccountVersion, original.SessionId, original.Origin, original.AuthenticatedAt, original.Registration,
            original.OperationId, original.CorrelationId, original.Interaction) &&
            original.NativeStepUp.Receipt.ConsumedAt <= original.AuthorizedAt &&
            original.CodeExpiresAt <= original.NativeStepUp.Receipt.ExpiresAt &&
            original.AuthenticationClaims?.Snapshot == original.NativeStepUp.Claims.Snapshot);

    private static bool OriginalActor(ProductAuthorizationLineage original, ProductAuthorizationActor actor) =>
        actor.Session.AccountId == original.AccountId && actor.AccountVersion == original.AccountVersion &&
        actor.Session.SessionId == original.SessionId && actor.Session.Origin == original.Origin &&
        actor.Session.AuthenticatedAt == original.AuthenticatedAt && actor.Session.AuthenticationMethods == original.AuthenticationMethods &&
        actor.Session.Assurance == original.Assurance && actor.Session.SecurityStampDigest == original.SecurityStampDigest &&
        actor.AuthenticationPolicy.Version == original.AuthenticationPolicyVersion &&
        JsonSerializer.Serialize(actor.SessionUse) == original.SessionUseSnapshot &&
        JsonSerializer.Serialize(actor.ProductAuthorization) == original.AccountAuthorizationSnapshot;

    private static bool Same<T>(T left, T right) => JsonSerializer.Serialize(left) == JsonSerializer.Serialize(right);
    private static DateTimeOffset Earliest(DateTimeOffset first, params DateTimeOffset?[] others) =>
        others.Where(x => x.HasValue).Aggregate(first, (minimum, candidate) => candidate!.Value < minimum ? candidate.Value : minimum);
}
