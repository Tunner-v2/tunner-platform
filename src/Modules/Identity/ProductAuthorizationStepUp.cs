using System.Collections.Immutable;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using OpenIddict.Abstractions;
using Tunner.Platform.Operations;

namespace Tunner.Identity.Persistence;

internal sealed record ProductAuthorizationPreparedStepUp(ProductAuthorizationProofBinding Binding,
    ProductAuthorizationPreparedProof Proof, ProductAuthorizationAuthenticationClaims Claims,
    ProductAuthorizationPresentationFacts PresentationFacts, string PresentationDigest)
{
    internal ProductAuthorizationInteractionEvaluation Evaluation => new(ProductAuthorizationInteractionStatus.Satisfied, Claims);
    public override string ToString() => nameof(ProductAuthorizationPreparedStepUp);
}

internal sealed record ProductAuthorizationNativeStepUp(ProductAuthorizationProofBinding Binding,
    ProductAuthorizationConsumedProof Receipt, ProductAuthorizationAuthenticationClaims Claims,
    ProductAuthorizationPresentationFacts PresentationFacts, string PresentationDigest)
{
    internal ProductAuthorizationInteractionEvaluation Evaluation => new(ProductAuthorizationInteractionStatus.Satisfied, Claims);
    public override string ToString() => nameof(ProductAuthorizationNativeStepUp);
}

/// <summary>Native Product owner of existing shared proofs. Readiness is not authorization or a session upgrade.</summary>
internal sealed class ProductAuthorizationStepUp(IdentityPersistenceDbContext database, AuthenticationApplication authentication,
    ProductAuthorizationApplication application, StepUpApplication stepUp, ProductAuthorizationContinuation continuation,
    ProductAuthorizationServerProfile profile, IProductAuthorizationPolicySource policies, IAssurancePolicySource assurancePolicies,
    IProductAuthorizationPresentationSource presentations, IOpenIddictApplicationManager clients, AuthenticationPartitionKey partitions,
    IHttpContextAccessor accessor, TimeProvider clock) : IUserStepUpOperationSource, IProductAuthorizationNativeProofHandoff
{
    private sealed record Candidate(ProductAuthorizationRestoredContinuation Saved, StepUpCaller Caller,
        ProductAuthorizationPolicy Policy, ProductAuthorizationProofBinding Binding,
        ProductAuthorizationPresentationFacts PresentationFacts, ProductAuthorizationPresentation Presentation)
    {
        internal UserStepUpOperationContext Context => new(new(Caller), Binding.OperationId, Binding.CorrelationId,
            Binding.Operation.Scope, Binding.NativeRequestTokenId, Binding.AccountVersion, Binding.Digest,
            Binding.Mappings[0].AssurancePolicyVersion);
        public override string ToString() => nameof(Candidate);
    }

    public Task<UserStepUpOperationContext?> ResolveAsync(UserStepUpActor actor, CancellationToken ct) =>
        !Http.Request.Cookies.ContainsKey(ProductAuthorizationContinuation.CookieName)
        ? Task.FromResult<UserStepUpOperationContext?>(null)
        : InOwnerTransactionAsync<UserStepUpOperationContext?>(async () =>
        {
            var saved = await continuation.ReadForReviewAsync(Http, ct).ConfigureAwait(false);
            if (saved is null || saved.ReviewDenied || saved.ReviewApprovalDigest is not null) return null;
            var candidate = await CandidateAsync(saved, ct).ConfigureAwait(false);
            return candidate is not null && Matches(actor, candidate.Caller) ? candidate.Context : null;
        }, ct);

    // The shared engine must use the native handoff. Generic execution cannot consume a Product proof early.
    public Task<bool> ApplyAsync(UserStepUpActor actor, UserStepUpOperationContext operation,
        IdentityPersistenceDbContext context, CancellationToken ct) => Task.FromResult(false);

    internal async Task<UserStepUpOperationContext?> ResolveForSavedAsync(ProductAuthorizationRestoredContinuation saved, CancellationToken ct)
    {
        RequireTransaction();
        var candidate = await CandidateAsync(saved, ct).ConfigureAwait(false);
        return candidate?.Context;
    }

    public Task<IResult> StageForNativeAsync(HttpContext http, UserStepUpOperationContext operation, string verifiedProofKey,
        CancellationToken ct) => StageForNativeAsync(http, operation, verifiedProofKey, null, ct);

    internal Task<IResult> StageForNativeAsync(HttpContext http, UserStepUpOperationContext operation, string verifiedProofKey,
        NativeProofCompletion? nativeCompletion, CancellationToken ct) => InOwnerTransactionAsync<IResult>(async () =>
    {
        var saved = await continuation.ReadForReviewAsync(http, ct).ConfigureAwait(false);
        if (saved is null || saved.ReviewDenied || saved.ReviewApprovalDigest is not null) return Conflict();
        var candidate = await CandidateAsync(saved, ct).ConfigureAwait(false);
        if (candidate is null || !SameOperation(operation, candidate.Context)) return Conflict();
        var proof = await stepUp.PrepareProductAuthorizationProofAsync(candidate.Caller, candidate.Binding, candidate.Policy,
            verifiedProofKey, ct).ConfigureAwait(false);
        if (proof is null || !await continuation.StageStepUpProofAsync(saved, proof, candidate.Binding, ct).ConfigureAwait(false)) return Conflict();
        return Ready(candidate.Context, nativeCompletion);
    }, ct);

    /// <summary>Shared password/native-factor recovery returns the same prepared proof, never a completed authorization.</summary>
    internal Task<IResult?> RecoverReadyAsync(HttpContext http, UserStepUpOperationContext operation, CancellationToken ct) =>
        InOwnerTransactionAsync<IResult?>(async () =>
        {
            var saved = await continuation.ReadForReviewAsync(http, ct).ConfigureAwait(false);
            if (saved is null || saved.ReviewDenied || saved.ReviewApprovalDigest is not null) return null;
            var candidate = await CandidateAsync(saved, ct).ConfigureAwait(false);
            if (candidate is null || !SameOperation(operation, candidate.Context)) return null;
            var prepared = await PreparedAsync(candidate, ct).ConfigureAwait(false);
            return prepared is null ? null : Ready(candidate.Context);
        }, ct);

    /// <summary>Review/native preview only. The protected ID is rechecked by the existing active-proof validator.</summary>
    internal async Task<ProductAuthorizationPreparedStepUp?> PreparedAsync(ProductAuthorizationRestoredContinuation saved, CancellationToken ct,
        bool forDenial = false)
    {
        RequireTransaction();
        var candidate = await CandidateAsync(saved, ct, allowDenied: forDenial).ConfigureAwait(false);
        return candidate is null ? null : await PreparedAsync(candidate, ct).ConfigureAwait(false);
    }

    private async Task<ProductAuthorizationPreparedStepUp?> PreparedAsync(Candidate candidate, CancellationToken ct)
    {
        var saved = candidate.Saved;
        if (saved.PreparedStepUpProof is not { } original || saved.PreparedStepUpBinding is not { } binding ||
            binding.Digest != candidate.Binding.Digest || original.BindingDigest != binding.Digest) return null;
        // RecoverProofAsync reloads its row. Use the corrected shared active read so a native
        // transaction's pending CONSUMED state can never be reloaded back to database ACTIVE.
        var current = await stepUp.ReadPreparedProductAuthorizationProofAsync(candidate.Caller, candidate.Binding,
            candidate.Policy, original.ProofId, ct).ConfigureAwait(false);
        if (current is null || JsonSerializer.Serialize(current) != JsonSerializer.Serialize(original)) return null;
        return new(candidate.Binding, current, Claims(candidate.Policy, candidate.Binding, current.Mapping),
            candidate.PresentationFacts, AuthenticationIdentity.Digest(candidate.Presentation.Snapshot));
    }

    /// <summary>Only native authorization calls this, inside its transaction before Relationship/grant side effects.</summary>
    internal async Task<ProductAuthorizationNativeStepUp?> ConsumeAsync(ProductAuthorizationRestoredContinuation saved, CancellationToken ct)
    {
        RequireTransaction();
        if (saved.ReviewDenied || string.IsNullOrWhiteSpace(saved.ReviewApprovalDigest)) return null;
        var candidate = await CandidateAsync(saved, ct).ConfigureAwait(false);
        if (candidate is null) return null;
        var prepared = await PreparedAsync(candidate, ct).ConfigureAwait(false);
        if (prepared is null) return null;
        var receipt = await stepUp.ConsumeProductAuthorizationProofAsync(candidate.Caller, prepared.Binding,
            candidate.Policy, prepared.Proof.ProofId, ct).ConfigureAwait(false);
        if (receipt is null || JsonSerializer.Serialize(receipt.Mapping) != JsonSerializer.Serialize(prepared.Proof.Mapping)) return null;
        return new(prepared.Binding, receipt, prepared.Claims, prepared.PresentationFacts, prepared.PresentationDigest);
    }

    /// <summary>Final native output/code recheck. The native caller supplies its independently validated original actor.</summary>
    internal async Task<bool> CurrentAsync(StepUpCaller caller, ProductAuthorizationPolicy currentPolicy,
        ProductAuthorizationNativeStepUp original, CancellationToken ct)
    {
        RequireTransaction();
        return await ValidateNativeAsync(stepUp, presentations, policies, clock, caller, currentPolicy, original, ct).ConfigureAwait(false);
    }

    internal static async Task<bool> ValidateNativeAsync(StepUpApplication stepUp, IProductAuthorizationPresentationSource presentations,
        IProductAuthorizationPolicySource policies, TimeProvider clock, StepUpCaller caller, ProductAuthorizationPolicy currentPolicy,
        ProductAuthorizationNativeStepUp original, CancellationToken ct)
    {
        if (original.Binding is null || original.Receipt?.Mapping is null || original.Claims is null || original.PresentationFacts is null ||
            original.PresentationDigest is not { Length: 64 } || !original.PresentationDigest.All(Uri.IsHexDigit)) return false;
        if (caller.AccountId != original.Binding.AccountId || caller.SessionId != original.Binding.SessionId ||
            caller.Origin != original.Binding.Origin || original.PresentationFacts.AccountId != caller.AccountId ||
            original.PresentationFacts.AccountVersion != original.Binding.AccountVersion || original.PresentationFacts.SessionId != caller.SessionId ||
            original.PresentationFacts.Origin != original.Binding.Origin || original.PresentationFacts.RequestDigest != original.Binding.RequestDigest ||
            original.PresentationFacts.AuthorizationPolicyVersion != currentPolicy.Metadata.VersionId ||
            JsonSerializer.Serialize(Claims(currentPolicy, original.Binding, original.Receipt.Mapping)) != JsonSerializer.Serialize(original.Claims)) return false;
        var publication = await presentations.RequirePublishedAsync(original.PresentationFacts, clock.GetUtcNow(), ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The original qualified Product proof publication is unavailable.");
        publication.Require(original.PresentationFacts, clock.GetUtcNow());
        var at = clock.GetUtcNow(); var policy = policies.RequireCurrent(caller.Origin, at); policy.Require(caller.Origin, at);
        if (AuthenticationIdentity.Digest(publication.Snapshot) != original.PresentationDigest || policy.Snapshot != currentPolicy.Snapshot ||
            !await stepUp.ProductAuthorizationProofCurrentAsync(caller, original.Binding, currentPolicy, original.Receipt, ct).ConfigureAwait(false)) return false;
        at = clock.GetUtcNow(); policy = policies.RequireCurrent(caller.Origin, at); policy.Require(caller.Origin, at);
        return policy.Snapshot == currentPolicy.Snapshot && publication.Metadata.EffectiveInterval.Contains(at) && at < original.Receipt.ExpiresAt;
    }

    private async Task<Candidate?> CandidateAsync(ProductAuthorizationRestoredContinuation saved, CancellationToken ct, bool allowDenied = false)
    {
        RequireTransaction();
        if (!profile.RequireAuthorizationReview || saved.ReviewDenied && !allowDenied || !ReferenceEquals(saved.Http, Http)) return null;
        var caller = Caller(saved.Http);
        if (saved.AccountId != caller.AccountId || saved.SessionId != caller.SessionId || saved.Origin != caller.Origin) return null;
        var policy = Policy(); saved.Request.Require(policy);
        if (policy.StepUpQualification is not { } qualification || saved.Request.AuthenticationContext is not { } controls || controls.Prompt == "none") return null;
        var registration = await application.RequireRegistrationAsync(saved.Request, caller.Origin, ct).ConfigureAwait(false);
        if (registration is null || registration.NativeApplicationId != saved.NativeApplicationId ||
            !await ProductAuthorizationReview.NativeClientCurrentAsync(clients, saved.Request, registration, ct).ConfigureAwait(false)) return null;
        var actor = await authentication.LockProductAuthorizationAsync(caller, ProductAuthorizationApplication.AuthorizeOperation, ct).ConfigureAwait(false);
        if (actor is null || !await authentication.ProductAuthorizationCurrentAsync(actor, ProductAuthorizationApplication.AuthorizeOperation, ct).ConfigureAwait(false)) return null;
        var ordinary = ProductAuthorizationApplication.EvaluateActorInteraction(actor, saved.Request, caller.Origin, policy,
            saved.Interaction, clock.GetUtcNow());
        if (ordinary.Status != ProductAuthorizationInteractionStatus.RequiresFreshProof) return null;
        var mappings = ImmutableArray<ProductAuthorizationOperationProofMapping>.Empty;
        foreach (var requested in controls.RequestedAcrValues)
        {
            mappings = qualification.Mappings.Where(mapping => mapping.RequestedAcr == requested &&
                mapping.SessionAuthenticationMethods == actor.Session.AuthenticationMethods && mapping.SessionAssurance == actor.Session.Assurance)
                .OrderBy(mapping => mapping.ProofAuthenticationMethod, StringComparer.Ordinal)
                .ThenBy(mapping => JsonSerializer.Serialize(mapping), StringComparer.Ordinal).ToImmutableArray();
            if (mappings.Length != 0) break;
        }
        if (mappings.Length == 0) return null;
        var first = mappings[0];
        if (mappings.Any(mapping => mapping.Scope != first.Scope || mapping.AssurancePolicyVersion != first.AssurancePolicyVersion))
            throw new InvalidOperationException("One requested Product ACR requires a common explicit proof scope and assurance-policy version.");
        var assurance = assurancePolicies.RequireEffective(first.Scope, clock.GetUtcNow(), first.AssurancePolicyVersion);
        if (mappings.Any(mapping => mapping.ProofAssurance != assurance.RequiredAssurance ||
                assurance.RequireMethodAssurance(mapping.ProofAuthenticationMethod) != mapping.ProofAssurance) ||
            !assurance.MethodAssurances.Where(pair => pair.Value == assurance.RequiredAssurance).Select(pair => pair.Key)
                .Order(StringComparer.Ordinal).SequenceEqual(mappings.Select(mapping => mapping.ProofAuthenticationMethod), StringComparer.Ordinal))
            throw new InvalidOperationException("The exact allowed proof methods require unambiguous current Product and assurance qualification.");
        var facts = new ProductAuthorizationPresentationFacts(saved.Request.Digest, saved.Request.ProductId, registration.ProductVersion,
            saved.Request.Environment, registration.EnvironmentVersion, saved.Request.ClientId, registration.ClientVersion,
            caller.AccountId, actor.AccountVersion, caller.SessionId, caller.Origin, new Uri(saved.Request.RedirectUri).GetLeftPart(UriPartial.Authority),
            saved.Request.Scopes, controls.RequestedAcrValues, ImmutableArray<string>.Empty, policy.Metadata.VersionId);
        var publication = await presentations.RequirePublishedAsync(facts, clock.GetUtcNow(), ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The exact Product proof preparation publication is unavailable.");
        publication.Require(facts, clock.GetUtcNow());
        var currentRegistration = await application.RequireRegistrationAsync(saved.Request, caller.Origin, ct).ConfigureAwait(false);
        if (!await authentication.ProductAuthorizationCurrentAsync(actor, ProductAuthorizationApplication.AuthorizeOperation, ct).ConfigureAwait(false) ||
            policy.Snapshot != Policy().Snapshot || !registration.EffectiveInterval.Contains(clock.GetUtcNow()) ||
            JsonSerializer.Serialize(currentRegistration) != JsonSerializer.Serialize(registration) ||
            !await ProductAuthorizationReview.NativeClientCurrentAsync(clients, saved.Request, registration, ct).ConfigureAwait(false) ||
            StepUpApplication.ProductAuthorizationAssuranceDigest(assurancePolicies.RequireEffective(first.Scope, clock.GetUtcNow(), first.AssurancePolicyVersion)) !=
                StepUpApplication.ProductAuthorizationAssuranceDigest(assurance)) return null;
        var ownerFacts = Digest(new { saved.TokenId, requestDigest = saved.Request.Digest, saved.NativeApplicationId, saved.OperationId,
            saved.CorrelationId, saved.Origin, saved.CreatedAt, saved.ExpiresAt, saved.PolicySnapshot, saved.BindingDigest,
            saved.Interaction, saved.ReviewGeneration, registration, actor.AccountVersion, actor.Session.SessionId,
            actor.Session.AuthenticatedAt, actor.Session.AuthenticationMethods, actor.Session.Assurance, actor.Session.SecurityStampDigest,
            actor.AuthenticationPolicy, actor.SessionUse, actor.ProductAuthorization, actor.AuthenticationPerformedAt,
            publication = publication.Snapshot });
        var binding = new ProductAuthorizationProofBinding(saved.TokenId, saved.OperationId, saved.CorrelationId, saved.Request.Digest,
            caller.AccountId, actor.AccountVersion, caller.SessionId, caller.Origin, actor.Session.AuthenticatedAt,
            AuthenticationIdentity.Digest(policy.Snapshot), ownerFacts, StepUpApplication.ProductAuthorizationAssuranceDigest(assurance), mappings);
        binding.Require(caller, policy);
        return new(saved, caller, policy, binding, facts, publication);
    }

    private async Task<T> InOwnerTransactionAsync<T>(Func<Task<T>> action, CancellationToken ct)
    {
        if (database.Database.CurrentTransaction is not null) return await action().ConfigureAwait(false);
        if (database.ChangeTracker.HasChanges()) throw new InvalidOperationException("Product proof preparation requires a clean Identity transaction.");
        await using var transaction = await database.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        var result = await action().ConfigureAwait(false);
        await database.SaveChangesAsync(ct).ConfigureAwait(false); await transaction.CommitAsync(ct).ConfigureAwait(false);
        return result;
    }
    private HttpContext Http => accessor.HttpContext ?? throw new InvalidOperationException("A current native Product browser request is required.");
    private StepUpCaller Caller(HttpContext http) => StepUpCaller.FromPrincipal(http.User, profile.Origin,
        partitions.Network(http.Connection.RemoteIpAddress ?? throw new ArgumentException("A current peer is required.")));
    private ProductAuthorizationPolicy Policy()
    {
        var at = clock.GetUtcNow(); var policy = policies.RequireCurrent(profile.Origin, at); policy.Require(profile.Origin, at); return policy;
    }
    private void RequireTransaction()
    {
        if (database.Database.CurrentTransaction is null) throw new InvalidOperationException("Native Product proof evaluation requires the caller-owned Identity transaction.");
    }
    private static bool Matches(UserStepUpActor actor, StepUpCaller caller) =>
        actor.AccountId == caller.AccountId && actor.SessionId == caller.SessionId && actor.Origin == caller.Origin;
    private static bool SameOperation(UserStepUpOperationContext left, UserStepUpOperationContext right) =>
        !left.Completed && !right.Completed && StepUpBrowserService.Digest(left) == StepUpBrowserService.Digest(right);
    private static ProductAuthorizationAuthenticationClaims Claims(ProductAuthorizationPolicy policy,
        ProductAuthorizationProofBinding binding, ProductAuthorizationOperationProofMapping mapping) =>
        new(policy.InteractionQualification?.EmitAuthenticationTime == true ? binding.SessionAuthenticatedAt : null,
            mapping.IssuedAcr, mapping.IssuedAmr);
    private static IResult Ready(UserStepUpOperationContext operation, NativeProofCompletion? native = null) => native is null
        ? Results.Json(new { status = "PROOF_READY", operationReference = operation.Binding.CorrelationId })
        : Results.Json(new { status = "PROOF_READY", operationReference = operation.Binding.CorrelationId,
            native.RequestId, native.AttemptId, native.CeremonyId });
    private static IResult Conflict() => TechnicalProblemDetails.HttpResult(409, "about:blank", "The request could not be completed.",
        new TechnicalProblemMetadata("VERSION_CONFLICT", Guid.NewGuid().ToString("N"), "UNSPECIFIED"));
    private static string Digest<T>(T value) => AuthenticationIdentity.Digest(JsonSerializer.Serialize(value));
}
