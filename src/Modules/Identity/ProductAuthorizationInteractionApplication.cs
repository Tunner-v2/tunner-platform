using System.Collections.Immutable;

namespace Tunner.Identity.Persistence;

internal sealed partial class ProductAuthorizationApplication
{
    /// <summary>Capture once before interaction. Never replace this context when the user resumes.</summary>
    internal ValueTask<ProductAuthorizationInteractionContext> CaptureInteractionAsync(ProductAuthorizationRequest request,
        string origin, Guid? originalSessionId, CancellationToken ct)
    {
        RequireTransaction(); ArgumentNullException.ThrowIfNull(request); ct.ThrowIfCancellationRequested();
        var policy = Policy(origin); request.Require(policy);
        if (originalSessionId == Guid.Empty) throw new ArgumentException("An actual original browser session is required.");
        return ValueTask.FromResult(new ProductAuthorizationInteractionContext(request.Digest, origin,
            IdentityPersistenceTime.Normalize(clock.GetUtcNow()), originalSessionId, policy.Snapshot));
    }

    /// <summary>No Relationship activation or grant effect. Reuses authorization's Account/session/current-factor checks.</summary>
    internal async Task<ProductAuthorizationInteractionEvaluation> EvaluateInteractionAsync(StepUpCaller? caller,
        ProductAuthorizationRequest request, string origin, ProductAuthorizationInteractionContext? interaction, CancellationToken ct)
    {
        RequireTransaction(); ArgumentNullException.ThrowIfNull(request);
        var policy = Policy(origin); request.Require(policy);
        var registration = await registry.RequireCurrentAsync(request, clock.GetUtcNow(), ct).ConfigureAwait(false);
        if (!Registered(request, registration, clock.GetUtcNow()) || !SupportedInteraction(request, policy) ||
            !InteractionBound(request, origin, policy, interaction, clock.GetUtcNow())) return DeniedInteraction;
        if (caller is null) return new(ProductAuthorizationInteractionStatus.RequiresSignIn);
        if (caller.Origin != origin) return DeniedInteraction;
        var actor = await authentication.LockProductAuthorizationAsync(caller, AuthorizeOperation, ct).ConfigureAwait(false);
        if (actor is null || !await authentication.ProductAuthorizationCurrentAsync(actor, AuthorizeOperation, ct).ConfigureAwait(false))
            return DeniedInteraction;
        return EvaluateActorInteraction(actor, request, origin, policy, interaction, clock.GetUtcNow());
    }

    private static readonly ProductAuthorizationInteractionEvaluation DeniedInteraction = new(ProductAuthorizationInteractionStatus.Denied);

    private static bool InteractionBound(ProductAuthorizationRequest request, string origin, ProductAuthorizationPolicy policy,
        ProductAuthorizationInteractionContext? interaction, DateTimeOffset at) =>
        (request.AuthenticationContext is null && interaction is null) ||
        interaction is not null && interaction.RequestDigest == request.Digest && interaction.Origin == origin &&
        interaction.PolicySnapshot == policy.Snapshot && interaction.ReceivedAt <= at && interaction.OriginalSessionId != Guid.Empty;

    private static bool SupportedInteraction(ProductAuthorizationRequest request, ProductAuthorizationPolicy policy)
    {
        if (request.AuthenticationContext is not { } controls) return true;
        var qualification = policy.InteractionQualification;
        if (controls.MaxAgeSeconds is not null && qualification?.EmitAuthenticationTime != true) return false;
        return controls.RequestedAcrValues.Length == 0 || controls.RequestedAcrValues.Any(requested =>
            qualification?.AcrMappings.Any(mapping => mapping.RequestedAcr == requested) == true ||
            policy.StepUpQualification?.Mappings.Any(mapping => mapping.RequestedAcr == requested) == true);
    }

    internal static ProductAuthorizationInteractionEvaluation EvaluateActorInteraction(ProductAuthorizationActor actor,
        ProductAuthorizationRequest request, string origin, ProductAuthorizationPolicy policy,
        ProductAuthorizationInteractionContext? interaction, DateTimeOffset at)
    {
        if (!SupportedInteraction(request, policy) || !InteractionBound(request, origin, policy, interaction, at)) return DeniedInteraction;
        var controls = request.AuthenticationContext;
        if (controls is null)
            return Assurance(actor, policy, at) ? new(ProductAuthorizationInteractionStatus.Satisfied) : DeniedInteraction;
        var session = actor.Session;
        if (at < session.AuthenticatedAt) return DeniedInteraction;
        // Cookie touch/renewal or operation-bound proof cannot satisfy a forced fresh native sign-in.
        if ((controls.Prompt == "login" || controls.MaxAgeSeconds == 0) &&
            (interaction is null || session.SessionId == interaction.OriginalSessionId ||
             actor.AuthenticationPerformedAt is not { } performedAt || performedAt < interaction.ReceivedAt || performedAt > at))
            return new(ProductAuthorizationInteractionStatus.RequiresSignIn);
        if ((controls.MaxAgeSeconds is > 0 && (at - session.AuthenticatedAt).TotalSeconds > controls.MaxAgeSeconds.Value) ||
            at - session.AuthenticatedAt >= policy.MaximumAuthenticationAge)
            return new(ProductAuthorizationInteractionStatus.RequiresSignIn);
        if (!policy.AllowedAuthenticationMethods.Contains(session.AuthenticationMethods, StringComparer.Ordinal) ||
            !policy.AllowedAssurances.Contains(session.Assurance, StringComparer.Ordinal))
            return new(ProductAuthorizationInteractionStatus.RequiresFreshProof);

        var qualification = policy.InteractionQualification;
        ProductAuthorizationAcrMapping? selected = null;
        foreach (var requested in controls.RequestedAcrValues)
        {
            selected = qualification?.AcrMappings.FirstOrDefault(mapping => mapping.RequestedAcr == requested &&
                mapping.SessionAssurance == session.Assurance && mapping.SessionAuthenticationMethods == session.AuthenticationMethods);
            if (selected is not null) break;
        }
        if (controls.RequestedAcrValues.Length > 0 && selected is null)
            return new(ProductAuthorizationInteractionStatus.RequiresFreshProof);
        // Prompt none/login alone remains compatible without introducing an implicit claim disclosure policy.
        if (qualification is null) return new(ProductAuthorizationInteractionStatus.Satisfied);
        return new(ProductAuthorizationInteractionStatus.Satisfied, new(qualification.EmitAuthenticationTime ? session.AuthenticatedAt : null,
            selected?.IssuedAcr, selected?.IssuedAmr ?? ImmutableArray<string>.Empty));
    }

    private async Task<ProductAuthorizationInteractionEvaluation> EvaluateNativeInteractionAsync(ProductAuthorizationActor actor,
        ProductAuthorizationRequest request, string origin, ProductAuthorizationPolicy policy,
        ProductAuthorizationInteractionContext? interaction, ProductAuthorizationNativeStepUp? native,
        StepUpCaller? caller, ProductAuthorizationRegistration registration, string operationId, string correlationId, CancellationToken ct)
    {
        var ordinary = EvaluateActorInteraction(actor, request, origin, policy, interaction, clock.GetUtcNow());
        if (native is null) return ordinary;
        // Operation proof supplements only an unmet operation assurance. It cannot repair denial or fresh-login requirements.
        try
        {
            if (ordinary.Status != ProductAuthorizationInteractionStatus.RequiresFreshProof || caller is null ||
                proofApplication is null || proofPresentations is null ||
                !NativeProofBound(native, request, actor.Session.AccountId, actor.AccountVersion, actor.Session.SessionId, origin,
                    actor.Session.AuthenticatedAt, registration, operationId, correlationId, interaction) ||
                !await ProductAuthorizationStepUp.ValidateNativeAsync(proofApplication, proofPresentations, policies, clock,
                    caller, policy, native, ct).ConfigureAwait(false)) return DeniedInteraction;
        }
        catch (AssurancePolicyVersionConflictException) { return DeniedInteraction; }
        return native.Evaluation;
    }

    private async Task<bool> InteractionCurrentAsync(ProductAuthorizationLineage original, ProductAuthorizationActor actor,
        ProductAuthorizationPolicy policy, StepUpCaller? caller, CancellationToken ct)
    {
        var evaluation = await EvaluateNativeInteractionAsync(actor, original.Request, original.Origin, policy, original.Interaction,
            original.NativeStepUp, caller, original.Registration, original.OperationId, original.CorrelationId, ct).ConfigureAwait(false);
        return evaluation.Status == ProductAuthorizationInteractionStatus.Satisfied &&
            evaluation.AuthenticationClaims?.Snapshot == original.AuthenticationClaims?.Snapshot;
    }

    // Pure final-clock bounds after all asynchronous current-authority checks have completed.
    private static bool InteractionCurrentAt(ProductAuthorizationLineage original, ProductAuthorizationActor actor,
        ProductAuthorizationPolicy policy, DateTimeOffset at)
    {
        var evaluation = EvaluateActorInteraction(actor, original.Request, original.Origin, policy, original.Interaction, at);
        return original.NativeStepUp is { } native
            ? evaluation.Status == ProductAuthorizationInteractionStatus.RequiresFreshProof && at < native.Receipt.ExpiresAt &&
                original.AuthenticationClaims?.Snapshot == native.Claims.Snapshot
            : evaluation.Status == ProductAuthorizationInteractionStatus.Satisfied &&
                evaluation.AuthenticationClaims?.Snapshot == original.AuthenticationClaims?.Snapshot;
    }

    private static bool NativeProofBound(ProductAuthorizationNativeStepUp native, ProductAuthorizationRequest request,
        string accountId, long accountVersion, Guid sessionId, string origin, DateTimeOffset authenticatedAt,
        ProductAuthorizationRegistration registration, string operationId, string correlationId,
        ProductAuthorizationInteractionContext? interaction)
    {
        if (native.Binding is not { } binding || native.Receipt is not { } receipt || native.Claims is null ||
            native.PresentationFacts is not { } facts || receipt.Mapping is not { } mapping || binding.Mappings.IsDefaultOrEmpty ||
            binding.Mappings.Any(value => value is null) || request.AuthenticationContext is not { } controls || controls.Prompt == "none" ||
            interaction is null || !ProductAuthorizationRequest.Canonical(facts.Scopes) ||
            !ProductAuthorizationRequest.Canonical(native.Claims.Amr) || !ProductAuthorizationRequest.Canonical(mapping.IssuedAmr) ||
            !ProductAuthorizationAuthenticationContext.Values(facts.RequestedAcrValues) ||
            !ProductAuthorizationAuthenticationContext.Values(controls.RequestedAcrValues) ||
            !Uri.TryCreate(request.RedirectUri, UriKind.Absolute, out var redirect)) return false;
        return binding.RequestDigest == request.Digest && binding.OperationId == operationId && binding.CorrelationId == correlationId &&
            binding.AccountId == accountId && binding.AccountVersion == accountVersion && binding.SessionId == sessionId && binding.Origin == origin &&
            binding.SessionAuthenticatedAt == authenticatedAt && receipt.BindingDigest == binding.Digest && receipt.ProofId != Guid.Empty &&
            receipt.ConsumedVersion > 1 && receipt.ConsumedAt >= interaction.ReceivedAt && receipt.ConsumedAt >= receipt.ProofAuthenticatedAt &&
            receipt.ExpiresAt > receipt.ConsumedAt && controls.RequestedAcrValues.Contains(mapping.RequestedAcr, StringComparer.Ordinal) &&
            facts.RequestDigest == request.Digest && facts.AccountId == accountId && facts.AccountVersion == accountVersion &&
            facts.SessionId == sessionId && facts.Origin == origin && !facts.LoginOnly && facts.ProductId == request.ProductId &&
            facts.ProductVersion == registration.ProductVersion && facts.Environment == request.Environment &&
            facts.EnvironmentVersion == registration.EnvironmentVersion && facts.ClientId == request.ClientId &&
            facts.ClientVersion == registration.ClientVersion && facts.RegisteredReturnOrigin == redirect.GetLeftPart(UriPartial.Authority) &&
            facts.Scopes.SequenceEqual(request.Scopes, StringComparer.Ordinal) &&
            facts.RequestedAcrValues.SequenceEqual(controls.RequestedAcrValues, StringComparer.Ordinal);
    }
}
