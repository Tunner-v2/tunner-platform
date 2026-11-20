using Microsoft.EntityFrameworkCore;
using Tunner.Accounts;
using Tunner.Platform.Policies;

namespace Tunner.Identity.Persistence;

internal sealed partial class AuthenticationApplication
{
    private sealed record FederationUnlinkWitness(Guid TransactionId, FederationLinkOperationRow Operation,
        ExternalIdentityLinkRow Link, StepUpProofRow Proof, FederationUnlinkSessionRequest? Request,
        AuthenticationSessionTerminationReceipt? Receipt)
    { public override string ToString() => nameof(FederationUnlinkWitness); }
    private FederationUnlinkWitness? federationUnlinkApplication;
    private readonly Dictionary<Guid, FederationUnlinkWitness> federationUnlinkReads = [];

    private FederationLinkManagementPolicyVersion? CurrentFederationLinkPolicy(string origin, DateTimeOffset at)
    {
        var sources = federationLinkPolicies?.Take(2).ToArray();
        if (sources is not { Length: 1 }) return null;
        try { var policy = sources[0].RequireCurrent(origin, at); policy.Require(origin, at); return policy; }
        catch (InvalidOperationException) { return null; }
    }

    private static bool SameFederationOperation(StepUpOperation first, StepUpOperation second) =>
        first.OperationId == second.OperationId && first.CorrelationId == second.CorrelationId && first.Scope == second.Scope &&
        first.ResourceId == second.ResourceId && first.ExpectedVersion == second.ExpectedVersion && first.ContextDigest == second.ContextDigest;

    private static bool FederationUnlinkProofMatches(FederationLinkOperationRow operation, StepUpProofRow proof)
    {
        if (operation.Kind != "UNLINK" || operation.TargetLinkId is null) return false;
        var binding = FederationLinkApplication.Operation(operation);
        return operation.ProofReference == proof.ProofId && proof.AccountId == operation.AccountId && proof.SessionId == operation.SessionId &&
            proof.Origin == operation.Origin && proof.Scheme == AuthenticationIdentity.Scheme && proof.OperationId == binding.OperationId &&
            proof.CorrelationId == binding.CorrelationId && proof.Operation == binding.Scope.Operation && proof.Context == binding.Scope.Context &&
            proof.ResourceId == binding.ResourceId && proof.ExpectedVersion == binding.ExpectedVersion && proof.ContextDigest == binding.ContextDigest &&
            proof.PolicyVersion == operation.AssurancePolicyVersion && proof.AuthenticationPolicyVersion == operation.AuthenticationPolicyVersion &&
            proof.SecurityStampDigest == operation.SecurityStampDigest;
    }

    private static bool FederationUnlinkHistoryBound(FederationLinkOperationRow operation, ExternalIdentityLinkRow link) =>
        operation.Kind == "UNLINK" && operation.State == "COMPLETED" && operation.ErrorCode is null && operation.AttemptId is null &&
        operation.AccountId == link.AccountId && operation.TargetLinkId == link.LinkId && operation.ResultLinkId == link.LinkId &&
        operation.ExpectedLinkVersion >= 1 && operation.ResultLinkVersion == operation.ExpectedLinkVersion + 1 && operation.ResultLinkVersion <= link.Version &&
        operation.ConfirmedAt is { } confirmed && operation.EndedAt is { } ended && confirmed >= operation.CreatedAt &&
        ended >= confirmed && ended < operation.ExpiresAt && operation.ProofReference is not null &&
        !string.IsNullOrWhiteSpace(operation.PostureFactsDigest) && !string.IsNullOrWhiteSpace(operation.PosturePolicyVersion) &&
        !string.IsNullOrWhiteSpace(operation.PostureEvidenceReference);

    private static bool FederationUnlinkTerminal(FederationLinkOperationRow operation, ExternalIdentityLinkRow link) =>
        FederationUnlinkHistoryBound(operation, link) && operation.ResultLinkVersion == link.Version &&
        link.LinkState == "UNLINKED" && link.EvidenceReference == operation.OperationId.ToString("N");

    // A committed Revoke retires the original source generation. A later ACTIVE version is a new link operation,
    // not permission to resurrect sessions that have not yet visited their ordinary validation boundary.
    private bool FederationHistoricalRevocationCurrentAt(AuthenticationSessionRow session, FederationUnlinkWitness witness, DateTimeOffset at)
    {
        var operation = witness.Operation;
        return database.Database.CurrentTransaction?.TransactionId == witness.TransactionId && witness.Request is null &&
            FederationUnlinkHistoryBound(operation, witness.Link) && operation.SessionEffect == nameof(FederationUnlinkSessionEffect.RevokeLinkedSessions) &&
            IsFederationMethod(session.AuthenticationMethods) && session.Scheme == AuthenticationIdentity.Scheme &&
            session.AccountId == operation.AccountId && session.FederationLinkId == operation.ResultLinkId &&
            session.FederationLinkVersion is { } generation && generation >= 1 && generation <= operation.ExpectedLinkVersion &&
            session.FederationProvider == operation.Provider && session.Origin == operation.Origin &&
            operation.EndedAt is { } ended && ended <= at && FederationUnlinkProofMatches(operation, witness.Proof) &&
            witness.Proof.State == "CONSUMED" && witness.Proof.ProtectedKey.Length == 0 &&
            witness.Proof.EndedAt is { } consumed && consumed >= ended && consumed <= at &&
            operation.PostureFrom is { } from && operation.PostureIncludesFrom is { } includesFrom &&
            operation.PostureIncludesUntil is { } includesUntil &&
            new EffectivePolicyInterval(from, includesFrom, operation.PostureUntil, includesUntil).Contains(ended);
    }

    private static bool FederationUnlinkPolicyMatches(FederationLinkOperationRow operation, FederationLinkManagementPolicyVersion policy) =>
        operation.PolicyVersion == policy.Values.Version && operation.PolicySnapshot == policy.Snapshot && operation.Origin == policy.Values.Origin &&
        operation.AssurancePolicyVersion == policy.Values.UnlinkAssurancePolicyVersion && operation.SessionEffect == policy.Values.UnlinkSessionEffect.ToString() &&
        operation.SessionUsePolicyVersion == policy.Values.SessionUsePolicyVersion &&
        operation.SessionEffectEvidenceReference == policy.Values.SessionEffectEvidenceReference;

    // Historical session use follows its explicit current effect authority, independently of future operation admission settings.
    private static bool FederationSessionEffectPolicyMatches(FederationLinkOperationRow operation, FederationLinkManagementPolicyVersion policy) =>
        operation.Origin == policy.Values.Origin && operation.SessionEffect == policy.Values.UnlinkSessionEffect.ToString() &&
        operation.SessionUsePolicyVersion == policy.Values.SessionUsePolicyVersion &&
        operation.SessionEffectEvidenceReference == policy.Values.SessionEffectEvidenceReference;

    private bool FederationUnlinkHistoryCurrentAt(AuthenticationSessionRow session, FederationAuthenticationSource source,
        FederationUnlinkWitness witness, DateTimeOffset at)
    {
        var operation = witness.Operation; var link = witness.Link;
        if (database.Database.CurrentTransaction?.TransactionId != witness.TransactionId || !FederationUnlinkTerminal(operation, link) ||
            !IsFederationMethod(session.AuthenticationMethods) || session.AccountId != link.AccountId || session.Origin != operation.Origin || session.Scheme != AuthenticationIdentity.Scheme ||
            session.FederationLinkId != link.LinkId || session.FederationLinkVersion is not { } originalVersion ||
            originalVersion > operation.ExpectedLinkVersion || operation.Provider != source.Provider.ToString() ||
            operation.EndedAt is not { } ended || session.AuthenticatedAt > ended || at < ended ||
            source.SessionUsePolicyVersion != operation.SessionUsePolicyVersion || !FederationUnlinkProofMatches(operation, witness.Proof) ||
            !FederationCurrentAt(source, link, session.AccountId, session.Origin, at, false, allowUnlinked: true)) return false;
        var policy = CurrentFederationLinkPolicy(session.Origin, at);
        if (policy is null || (witness.Request is not null
            ? !FederationUnlinkPolicyMatches(operation, policy)
            : !FederationSessionEffectPolicyMatches(operation, policy))) return false;
        // Original posture was accepted at the immutable unlink decision; it is not a continuing new proof.
        if (operation.PostureFrom is not { } from || operation.PostureIncludesFrom is not { } includesFrom ||
            operation.PostureIncludesUntil is not { } includesUntil ||
            !new EffectivePolicyInterval(from, includesFrom, operation.PostureUntil, includesUntil).Contains(ended)) return false;
        if (witness.Request is { } request)
            return ReferenceEquals(witness, federationUnlinkApplication) && request.ManagementOperationId == operation.OperationId &&
                request.PostureInterval.Contains(at) && witness.Proof.State == "ACTIVE" && witness.Proof.ExpiresAt > at &&
                witness.Proof.ProtectedKey.Length != 0;
        return witness.Proof.State == "CONSUMED" && witness.Proof.ProtectedKey.Length == 0 && witness.Proof.EndedAt is { } consumed && consumed >= ended && consumed <= at;
    }

    private async Task ReadFederationUnlinkAsync(AuthenticationSessionRow session, ExternalIdentityLinkRow link, CancellationToken ct)
    {
        federationUnlinkReads.Remove(session.SessionId);
        var transaction = database.Database.CurrentTransaction;
        if (transaction is null) return;
        if (federationUnlinkApplication is { } applied && applied.TransactionId == transaction.TransactionId &&
            applied.Operation.SessionId == session.SessionId && applied.Link.LinkId == link.LinkId)
        { federationUnlinkReads[session.SessionId] = applied; return; }
        if (session.FederationLinkVersion is not { } generation || generation < 1) return;
        var retired = await database.Set<FederationLinkOperationRow>().Where(x => x.AccountId == session.AccountId && x.Kind == "UNLINK" &&
            x.State == "COMPLETED" && x.TargetLinkId == link.LinkId && x.ResultLinkId == link.LinkId &&
            x.SessionEffect == nameof(FederationUnlinkSessionEffect.RevokeLinkedSessions) && x.Origin == session.Origin && x.Provider == session.FederationProvider &&
            x.ExpectedLinkVersion >= generation && x.ResultLinkVersion <= link.Version)
            .OrderBy(x => x.ResultLinkVersion).ThenBy(x => x.EndedAt).Take(2).ToArrayAsync(ct).ConfigureAwait(false);
        if (retired.Length != 0)
        {
            if (retired.Length > 1 && retired[0].ResultLinkVersion == retired[1].ResultLinkVersion || retired[0].ProofReference is not { } retirementProofId)
                throw new InvalidOperationException("Committed federation retirement history is unavailable.");
            var retirementProof = await database.Set<StepUpProofRow>().SingleOrDefaultAsync(x => x.ProofId == retirementProofId, ct).ConfigureAwait(false)
                ?? throw new InvalidOperationException("Committed federation retirement proof is unavailable.");
            federationUnlinkReads[session.SessionId] = new(transaction.TransactionId, retired[0], link, retirementProof, null, null);
            return;
        }
        if (link.LinkState != "UNLINKED") return;
        // The monotonically versioned source selects its exact latest completed unlink. Ambiguous same-version history denies use.
        var latest = await database.Set<FederationLinkOperationRow>().Where(x => x.AccountId == session.AccountId && x.Kind == "UNLINK" &&
            x.State == "COMPLETED" && x.TargetLinkId == link.LinkId && x.ResultLinkId == link.LinkId)
            .OrderByDescending(x => x.ResultLinkVersion).ThenByDescending(x => x.EndedAt).Take(2).ToArrayAsync(ct).ConfigureAwait(false);
        if (latest.Length == 0 || latest[0].ResultLinkVersion != link.Version ||
            latest.Length > 1 && latest[1].ResultLinkVersion == latest[0].ResultLinkVersion || latest[0].ProofReference is not { } proofId) return;
        var proof = await database.Set<StepUpProofRow>().SingleOrDefaultAsync(x => x.ProofId == proofId, ct).ConfigureAwait(false);
        if (proof is null) return;
        federationUnlinkReads[session.SessionId] = new(transaction.TransactionId, latest[0], link, proof, null, null);
    }

    private bool FederationUnlinkedSessionCurrentAt(AuthenticationSessionRow row, FederationAuthenticationSource source,
        ExternalIdentityLinkRow link, DateTimeOffset at)
    {
        if (!federationUnlinkReads.TryGetValue(row.SessionId, out var witness) || witness.Link.LinkId != link.LinkId ||
            !FederationUnlinkHistoryCurrentAt(row, source, witness, at)) return false;
        if (witness.Operation.SessionEffect == nameof(FederationUnlinkSessionEffect.PreserveExistingSessions)) return true;
        // This exception serves only the current shared StepUp finality check. ValidateAsync rejects ended rows before reaching it.
        return FederationTerminationFactsCurrentAt(row, witness, at);
    }

    internal async Task<AuthenticationSessionTerminationReceipt?> ApplyFederationUnlinkSessionEffectAsync(FederationUnlinkSessionRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        var transaction = database.Database.CurrentTransaction ?? throw new InvalidOperationException("Federation unlink requires its current Identity owner transaction.");
        var operation = await database.Set<FederationLinkOperationRow>().SingleOrDefaultAsync(x => x.OperationId == request.ManagementOperationId, ct).ConfigureAwait(false);
        var link = await database.ExternalIdentityLinks.SingleOrDefaultAsync(x => x.LinkId == request.LinkId && x.AccountId == request.Caller.AccountId, ct).ConfigureAwait(false);
        var actor = await database.Set<AuthenticationSessionRow>().SingleOrDefaultAsync(x => x.SessionId == request.Caller.SessionId, ct).ConfigureAwait(false);
        var proof = operation?.ProofReference is { } proofId ? await database.Set<StepUpProofRow>().SingleOrDefaultAsync(x => x.ProofId == proofId, ct).ConfigureAwait(false) : null;
        var now = clock.GetUtcNow(); var policy = CurrentFederationLinkPolicy(request.Caller.Origin, now);
        if (operation is null || link is null || actor is null || proof is null || policy is null || !Enum.IsDefined(request.Provider) ||
            !Enum.IsDefined(request.Effect) || !FederationUnlinkTerminal(operation, link) || !FederationUnlinkPolicyMatches(operation, policy) ||
            request.Operation.Scope != FederationLinkApplication.UnlinkScope || !SameFederationOperation(request.Operation, FederationLinkApplication.Operation(operation)) ||
            operation.AccountId != request.Caller.AccountId || operation.SessionId != request.Caller.SessionId || operation.Origin != request.Caller.Origin ||
            operation.Provider != request.Provider.ToString() || operation.ExpectedLinkVersion != request.PriorLinkVersion || link.Version != request.UnlinkedVersion ||
            operation.SessionEffect != request.Effect.ToString() || operation.PolicyVersion != request.ManagementPolicyVersion ||
            operation.SessionUsePolicyVersion != request.SessionUsePolicyVersion || operation.SessionEffectEvidenceReference != request.SessionEffectEvidenceReference ||
            operation.PosturePolicyVersion != request.PosturePolicyVersion || operation.PostureEvidenceReference != request.PostureEvidenceReference ||
            operation.PostureFrom != IdentityPersistenceTime.Normalize(request.PostureInterval.From) || operation.PostureIncludesFrom != request.PostureInterval.IncludesFrom ||
            operation.PostureUntil != (request.PostureInterval.Until is { } until ? IdentityPersistenceTime.Normalize(until) : (DateTimeOffset?)null) ||
            operation.PostureIncludesUntil != request.PostureInterval.IncludesUntil || !request.PostureInterval.Contains(now) ||
            !FederationUnlinkProofMatches(operation, proof) || proof.State != "ACTIVE" || proof.ProtectedKey.Length == 0 || proof.ExpiresAt <= now ||
            actor.State != "ACTIVE" || actor.AccountId != request.Caller.AccountId || actor.Origin != request.Caller.Origin || actor.Scheme != AuthenticationIdentity.Scheme ||
            actor.SecurityStampDigest != proof.SecurityStampDigest)
            throw new InvalidOperationException("An exact current evidenced federation unlink transition is required.");
        federationUnlinkApplication = new(transaction.TransactionId, operation, link, proof, request, null);
        // Persist only within the caller's transaction. Later source ReloadAsync must see this unlink rather than erase staged state.
        await database.SaveChangesAsync(ct).ConfigureAwait(false);
        now = clock.GetUtcNow(); policy = CurrentFederationLinkPolicy(request.Caller.Origin, now);
        if (policy is null || !FederationUnlinkPolicyMatches(operation, policy) || !request.PostureInterval.Contains(now) || proof.ExpiresAt <= now)
            throw new InvalidOperationException("Federation unlink authority changed before its session effect.");
        if (!IsFederationMethod(actor.AuthenticationMethods) || actor.FederationLinkId != link.LinkId) return null;
        federationUnlinkReads[actor.SessionId] = federationUnlinkApplication;
        var source = FederationAuthenticationSource.Read(actor, link);
        if (source is null || !FederationUnlinkHistoryCurrentAt(actor, source, federationUnlinkApplication, now))
            throw new InvalidOperationException("The actor federation source does not match the exact unlink.");
        if (request.Effect == FederationUnlinkSessionEffect.PreserveExistingSessions) return null;
        var prior = actor.Version;
        SetFederationTermination(actor, operation, clock.GetUtcNow());
        await database.SaveChangesAsync(ct).ConfigureAwait(false);
        var receipt = new AuthenticationSessionTerminationReceipt(actor.SessionId, actor.AccountId, actor.Origin,
            request.Operation.OperationId, request.Operation.CorrelationId, request.Operation.ContextDigest, link.LinkId, link.Version,
            policy.Values.Version, prior, actor.Version, "FEDERATION");
        federationUnlinkApplication = federationUnlinkApplication with { Receipt = receipt };
        federationUnlinkReads[actor.SessionId] = federationUnlinkApplication;
        return receipt;
    }

    private void SetFederationTermination(AuthenticationSessionRow row, FederationLinkOperationRow operation, DateTimeOffset at)
    {
        row.TerminationCause = "FEDERATION_UNLINKED"; row.TerminationPolicyVersion = operation.PolicyVersion;
        row.TerminationCredentialVersion = operation.ResultLinkVersion;
        var binding = FederationLinkApplication.Operation(operation);
        End(row, "REVOKED", "TunnerSessionRevoked", at, binding.OperationId, binding.CorrelationId);
    }

    private async Task<bool> EndRetiredFederationSessionAsync(AuthenticationSessionRow row, CancellationToken ct)
    {
        if (database.Database.CurrentTransaction is null || row.State != "ACTIVE" || row.FederationLinkId is not { } linkId) return false;
        var link = await LockFederationLinkAsync(linkId, row.AccountId, ct).ConfigureAwait(false);
        if (link is null) return false;
        await ReadFederationUnlinkAsync(row, link, ct).ConfigureAwait(false);
        var source = FederationAuthenticationSource.Read(row, link);
        var now = clock.GetUtcNow();
        if (source is null || !federationUnlinkReads.TryGetValue(row.SessionId, out var witness) ||
            witness.Operation.SessionEffect != nameof(FederationUnlinkSessionEffect.RevokeLinkedSessions) ||
            !(FederationHistoricalRevocationCurrentAt(row, witness, now) || FederationUnlinkHistoryCurrentAt(row, source, witness, now))) return false;
        SetFederationTermination(row, witness.Operation, now); return true;
    }

    private bool FederationTerminationFactsCurrentAt(AuthenticationSessionRow row, FederationUnlinkWitness witness, DateTimeOffset at)
    {
        var receipt = witness.Receipt; var request = witness.Request;
        if (!ReferenceEquals(witness, federationUnlinkApplication) || receipt is null || request is null ||
            database.Database.CurrentTransaction?.TransactionId != witness.TransactionId || request.Effect != FederationUnlinkSessionEffect.RevokeLinkedSessions ||
            receipt.AuthenticatorType != "FEDERATION" || receipt.Generation is not null || receipt.SessionId != row.SessionId ||
            receipt.AccountId != row.AccountId || receipt.Origin != row.Origin || receipt.CredentialReference != witness.Link.LinkId ||
            receipt.CredentialVersion != witness.Link.Version || receipt.PasskeyPolicyVersion != witness.Operation.PolicyVersion ||
            receipt.OperationId != request.Operation.OperationId || receipt.CorrelationId != request.Operation.CorrelationId || receipt.ContextDigest != request.Operation.ContextDigest ||
            receipt.PriorSessionVersion + 1 != receipt.EndedSessionVersion || row.Version != receipt.EndedSessionVersion ||
            row.State != "REVOKED" || row.ProtectedKey.Length != 0 || row.ProtectedTicket.Length != 0 || row.TerminationCause != "FEDERATION_UNLINKED" ||
            row.TerminationPolicyVersion != receipt.PasskeyPolicyVersion || row.TerminationCredentialVersion != receipt.CredentialVersion ||
            row.OperationId != receipt.OperationId || row.CorrelationId != receipt.CorrelationId || row.RevokedAt is not { } ended ||
            ended < row.AuthenticatedAt || ended > at || row.FederationLinkId != receipt.CredentialReference) return false;
        return true;
    }

    private async Task<AuthenticationTerminalVerification?> VerifyFederationTerminationAsync(StepUpCaller caller, StepUpOperation operation,
        AuthenticationSessionTerminationReceipt receipt, long validatedVersion, AuthenticationPolicy authenticationPolicy, DateTimeOffset at, CancellationToken ct)
    {
        if (federationUnlinkApplication is not { } witness || witness.Request is not { } request || witness.Receipt != receipt ||
            caller.SessionId != request.Caller.SessionId || caller.AccountId != request.Caller.AccountId || caller.Origin != request.Caller.Origin ||
            !SameFederationOperation(operation, request.Operation) || receipt.PriorSessionVersion != validatedVersion) return null;
        var row = await database.Set<AuthenticationSessionRow>().SingleOrDefaultAsync(x => x.SessionId == caller.SessionId, ct).ConfigureAwait(false);
        var link = await LockFederationLinkAsync(receipt.CredentialReference, caller.AccountId, ct).ConfigureAwait(false);
        if (row is null || link is null || Expired(row, authenticationPolicy, at)) return null;
        var source = FederationAuthenticationSource.Read(row, link);
        if (source is null || !FederationTerminationFactsCurrentAt(row, witness, at) || !FederationUnlinkHistoryCurrentAt(row, source, witness, at)) return null;
        var user = await database.Users.SingleAsync(x => x.AccountId == caller.AccountId, ct).ConfigureAwait(false); await database.Entry(user).ReloadAsync(ct).ConfigureAwait(false);
        if (!user.EmailConfirmed || string.IsNullOrWhiteSpace(user.SecurityStamp) || AuthenticationIdentity.Digest(user.SecurityStamp) != row.SecurityStampDigest ||
            !await database.VerifiedContacts.AnyAsync(x => x.AccountId == caller.AccountId && x.State == ContactVerificationState.Verified &&
                x.AnonymizedAtUtc == null && x.NormalizedEmail == user.NormalizedEmail, ct).ConfigureAwait(false)) return null;
        var factor = row.AuthenticationMethods == "federation+totp" ? await ReadTotpSessionFactorAsync(row, ct).ConfigureAwait(false) : null;
        var account = await database.Accounts.SingleAsync(x => x.AccountId == caller.AccountId, ct).ConfigureAwait(false); await database.Entry(account).ReloadAsync(ct).ConfigureAwait(false);
        var decision = await AccessAsync(account, operation.Scope.Operation, clock.GetUtcNow(), ct).ConfigureAwait(false);
        var decisionAt = clock.GetUtcNow(); var auth = policies.RequireEffective(decisionAt); auth.Validate();
        return decision?.Allowed == true && decision.EffectiveInterval.Contains(decisionAt) && !Expired(row, auth, decisionAt) &&
            FederationTerminationFactsCurrentAt(row, witness, decisionAt) && FederationUnlinkHistoryCurrentAt(row, source, witness, decisionAt) &&
            MethodFactsCurrentAt(row, auth, decisionAt) && (row.AuthenticationMethods != "federation+totp" || TotpSessionFactorCurrentAt(row, factor))
            ? new(decision.EffectiveInterval) : null;
    }
}
