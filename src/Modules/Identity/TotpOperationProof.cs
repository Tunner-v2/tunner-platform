using Microsoft.EntityFrameworkCore;

namespace Tunner.Identity.Persistence;

internal sealed partial class TotpApplication
{
    internal async Task<TotpFactorView[]> ActiveFactorViewsAsync(string accountId, CancellationToken ct)
    {
        var values = await database.Set<TotpAuthenticatorRow>().AsNoTracking().Where(x => x.AccountId == accountId && x.State == "ACTIVE")
            .OrderBy(x => x.CreatedAt).ThenBy(x => x.AuthenticatorReference).Select(x => new { x.AuthenticatorReference, x.Version, x.State, x.CreatedAt, x.ActivatedAt }).ToArrayAsync(ct).ConfigureAwait(false);
        return values.Select((x, i) => new TotpFactorView(x.AuthenticatorReference, x.Version, x.State, x.CreatedAt, x.ActivatedAt, i + 1)).ToArray();
    }
    private static bool ProofAttemptMatches(TotpProofAttemptRow row, StepUpCaller caller, UserStepUpOperationContext owner, Guid reference) =>
        row.SessionId == caller.SessionId && row.AccountId == caller.AccountId && row.OperationId == owner.Binding.OperationId &&
        row.ContextDigest == owner.Binding.ContextDigest && row.AuthenticatorReference == reference;
    internal async Task<StepUpIssueResult> RecoverOperationProofAsync(StepUpCaller caller, UserStepUpOperationContext owner, Guid attemptId, Guid reference, CancellationToken ct)
    {
        Clean(); if (attemptId == Guid.Empty || reference == Guid.Empty) return new("VALIDATION_ERROR", null);
        await using var transaction = await database.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        var session = await authentication.LockForOperationAsync(caller, owner.Binding.Scope.Operation, ct).ConfigureAwait(false);
        if (session is null) return await Finish(transaction, new StepUpIssueResult("AUTHENTICATION_FAILED", null), ct).ConfigureAwait(false);
        await LockOwner(caller.AccountId, ct).ConfigureAwait(false);
        var attempt = await database.Set<TotpProofAttemptRow>().AsNoTracking().SingleOrDefaultAsync(x => x.SessionId == caller.SessionId && x.AttemptId == attemptId, ct).ConfigureAwait(false);
        if (attempt is null || !ProofAttemptMatches(attempt, caller, owner, reference)) return await Finish(transaction, new StepUpIssueResult("VERSION_CONFLICT", null), ct).ConfigureAwait(false);
        var result = await stepUp.RecoverProofAsync(caller, owner.Binding, attempt.ProofId, owner.ExpectedPolicyVersion, ct).ConfigureAwait(false);
        return await Finish(transaction, result, ct).ConfigureAwait(false);
    }
    internal async Task<TotpProofRecovery?> ReadProofRecoveryAsync(StepUpCaller caller, UserStepUpOperationContext owner, Guid requestId, Guid attemptId, Guid reference, CancellationToken ct)
    {
        var attempt = await database.Set<TotpProofAttemptRow>().AsNoTracking().SingleOrDefaultAsync(x => x.SessionId == caller.SessionId && x.AttemptId == attemptId, ct).ConfigureAwait(false);
        if (attempt is null || !ProofAttemptMatches(attempt, caller, owner, reference)) return null;
        var proof = await database.Set<StepUpProofRow>().AsNoTracking().SingleOrDefaultAsync(x => x.ProofId == attempt.ProofId, ct).ConfigureAwait(false);
        if (proof is null || proof.AccountId != caller.AccountId || proof.SessionId != caller.SessionId || proof.Origin != caller.Origin || proof.AuthenticationMethod != "totp" ||
            proof.TotpAuthenticatorReference != reference || proof.TotpGeneration != attempt.Generation || proof.OperationId != owner.Binding.OperationId || proof.ContextDigest != owner.Binding.ContextDigest) return null;
        var now = clock.GetUtcNow();
        if (owner.Completed && proof.State == "CONSUMED") return new(requestId, attemptId, reference, "CONSUMED", proof.ExpiresAt);
        var source = await authentication.ReadProofCredentialAsync(proof, ct, refresh: true).ConfigureAwait(false); now = clock.GetUtcNow();
        var status = "EXPIRED";
        try
        {
            var assurance = assurancePolicies.RequireEffective(owner.Binding.Scope, now, owner.ExpectedPolicyVersion);
            if (proof.State == "ACTIVE" && proof.ExpiresAt > now && now >= proof.AuthenticatedAt && now - proof.AuthenticatedAt < assurance.ProofLifetime &&
                assurance.MethodAssurances.TryGetValue("totp", out var mapped) && mapped == proof.Assurance && mapped == assurance.RequiredAssurance &&
                authentication.ProofMethodCurrentAt(proof, source, owner.Binding, now, false)) status = "PROOF_ACTIVE";
        }
        catch (AssurancePolicyVersionConflictException) { }
        return new(requestId, attemptId, reference, status, proof.ExpiresAt);
    }
    internal async Task<bool> ProofCapabilityAsync(StepUpCaller caller, AssurancePolicyVersion assurance, CancellationToken ct)
    {
        TotpPolicy policy; try { policy = Current(caller.Origin); } catch (InvalidOperationException) { return false; }
        return assurance.MethodAssurances.TryGetValue("totp", out var mapped) && mapped == assurance.RequiredAssurance && mapped == policy.OperationProofAssurance &&
            await database.Set<TotpAuthenticatorRow>().AnyAsync(x => x.AccountId == caller.AccountId && x.State == "ACTIVE", ct).ConfigureAwait(false);
    }
    internal async Task<StepUpIssueResult> IssueOperationProofAsync(StepUpCaller caller, UserStepUpOperationContext owner, TotpCodeCommand command, CancellationToken ct)
    {
        Clean(); if (command.AttemptId == Guid.Empty || command.AuthenticatorReference == Guid.Empty || TotpNativeVerifier.CanonicalCode(command.Code) is null) return new("VALIDATION_ERROR", null);
        var policy = Current(caller.Origin); var scope = owner.Binding.Scope;
        // Reconcile accepted original attempts before vault I/O or a fresh code check.
        await using (var recovery = await database.Database.BeginTransactionAsync(ct).ConfigureAwait(false))
        {
            var session = await authentication.LockForOperationAsync(caller, scope.Operation, ct).ConfigureAwait(false);
            if (session is null) return await Finish(recovery, new StepUpIssueResult("AUTHENTICATION_FAILED", null), ct).ConfigureAwait(false);
            await LockOwner(caller.AccountId, ct).ConfigureAwait(false);
            var original = await database.Set<TotpProofAttemptRow>().AsNoTracking().SingleOrDefaultAsync(x => x.SessionId == caller.SessionId && x.AttemptId == command.AttemptId, ct).ConfigureAwait(false);
            if (original is not null)
            {
                if (original.AccountId != caller.AccountId || original.OperationId != owner.Binding.OperationId || original.ContextDigest != owner.Binding.ContextDigest || original.AuthenticatorReference != command.AuthenticatorReference)
                    return await Finish(recovery, new StepUpIssueResult("VERSION_CONFLICT", null), ct).ConfigureAwait(false);
                var grant = await stepUp.RecoverProofAsync(caller, owner.Binding, original.ProofId, owner.ExpectedPolicyVersion, ct).ConfigureAwait(false);
                return await Finish(recovery, grant, ct).ConfigureAwait(false);
            }
            // An active proof from a prior owner failure belongs to the same original operation; never accept another code for it.
            var prior = await database.Set<StepUpProofRow>().AsNoTracking().SingleOrDefaultAsync(x => x.SessionId == caller.SessionId && x.OperationId == owner.Binding.OperationId, ct).ConfigureAwait(false);
            if (prior is not null) return await Finish(recovery, new StepUpIssueResult("VERSION_CONFLICT", null), ct).ConfigureAwait(false);
            await Finish(recovery, true, ct).ConfigureAwait(false);
        }
        var source = await database.Set<TotpAuthenticatorRow>().AsNoTracking().SingleOrDefaultAsync(x => x.AuthenticatorReference == command.AuthenticatorReference && x.AccountId == caller.AccountId && x.State == "ACTIVE", ct).ConfigureAwait(false);
        if (source is null) return new("AUTHENTICATION_FAILED", null);
        using var seed = await seeds.ReadAsync(source.Binding, source.Reference, ct).ConfigureAwait(false); if (seed is null) return new("DEPENDENCY_UNAVAILABLE", null);
        await using var transaction = await database.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        var currentSession = await authentication.LockForOperationAsync(caller, scope.Operation, ct).ConfigureAwait(false);
        if (currentSession is null) return await Finish(transaction, new StepUpIssueResult("AUTHENTICATION_FAILED", null), ct).ConfigureAwait(false);
        await LockOwner(caller.AccountId, ct).ConfigureAwait(false);
        var raced = await database.Set<TotpProofAttemptRow>().AsNoTracking().SingleOrDefaultAsync(x => x.SessionId == caller.SessionId && x.AttemptId == command.AttemptId, ct).ConfigureAwait(false);
        if (raced is not null)
        {
            var recovered = raced.AccountId == caller.AccountId && raced.OperationId == owner.Binding.OperationId && raced.ContextDigest == owner.Binding.ContextDigest && raced.AuthenticatorReference == command.AuthenticatorReference
                ? await stepUp.RecoverProofAsync(caller, owner.Binding, raced.ProofId, owner.ExpectedPolicyVersion, ct).ConfigureAwait(false) : new StepUpIssueResult("VERSION_CONFLICT", null);
            return await Finish(transaction, recovered, ct).ConfigureAwait(false);
        }
        var factor = await Factor(source.AuthenticatorReference, caller.AccountId, ct).ConfigureAwait(false); var now = clock.GetUtcNow(); policy = Current(caller.Origin);
        var assurance = assurancePolicies.RequireEffective(scope, now, owner.ExpectedPolicyVersion);
        if (factor?.State != "ACTIVE" || factor.Generation != source.Generation || factor.Version != source.Version || factor.Reference != source.Reference ||
            !assurance.MethodAssurances.TryGetValue("totp", out var mapped) || mapped != assurance.RequiredAssurance || mapped != policy.OperationProofAssurance)
            return await Finish(transaction, new StepUpIssueResult("STEP_UP_REQUIRED", null), ct).ConfigureAwait(false);
        var auth = authenticationPolicies.RequireEffective(now); auth.Validate();
        if (!await Rate(caller, auth, ct).ConfigureAwait(false)) return await Finish(transaction, new StepUpIssueResult("RATE_OR_ABUSE_LIMITED", null), ct).ConfigureAwait(false);
        await database.SaveChangesAsync(ct).ConfigureAwait(false); await transaction.CreateSavepointAsync("totp_proof", ct).ConfigureAwait(false);
        var error = await verifier.AcceptAsync(factor, policy, seed.Require(), command.Code, ct).ConfigureAwait(false);
        if (error is not null) return await Finish(transaction, new StepUpIssueResult(error, null), ct).ConfigureAwait(false);
        var verifiedAt = clock.GetUtcNow();
        var issued = await stepUp.IssueVerifiedProofAsync(caller, currentSession, owner.Binding, owner.ExpectedPolicyVersion, "totp", null, null, null, verifiedAt, ct, factor, policy.Version).ConfigureAwait(false);
        var authority = await authentication.VerifyContinuedOperationAsync(caller, owner.Binding, currentSession, ct).ConfigureAwait(false); var decisionAt = clock.GetUtcNow();
        assurance = assurancePolicies.RequireEffective(scope, decisionAt, owner.ExpectedPolicyVersion);
        if (issued.ErrorCode is not null || authority is null || !authority.SessionUseInterval.Contains(decisionAt) || !authority.OperationInterval.Contains(decisionAt) ||
            Current(caller.Origin).Version != policy.Version || !assurance.MethodAssurances.TryGetValue("totp", out mapped) || mapped != policy.OperationProofAssurance || mapped != assurance.RequiredAssurance)
        {
            await transaction.RollbackToSavepointAsync("totp_proof", ct).ConfigureAwait(false); database.ChangeTracker.Clear();
            return await Finish(transaction, new StepUpIssueResult(issued.ErrorCode ?? "STEP_UP_REQUIRED", null), ct).ConfigureAwait(false);
        }
        var proof = database.ChangeTracker.Entries<StepUpProofRow>().Select(x => x.Entity).Single(x => x.SessionId == caller.SessionId && x.OperationId == owner.Binding.OperationId);
        if (decisionAt >= proof.ExpiresAt || !authentication.ProofMethodCurrentAt(proof, new AuthenticatorProofSource(null, factor), owner.Binding, decisionAt, false))
        {
            await transaction.RollbackToSavepointAsync("totp_proof", ct).ConfigureAwait(false); database.ChangeTracker.Clear();
            return await Finish(transaction, new StepUpIssueResult("STEP_UP_REQUIRED", null), ct).ConfigureAwait(false);
        }
        database.Add(new TotpProofAttemptRow { AttemptId = command.AttemptId, SessionId = caller.SessionId, AccountId = caller.AccountId, OperationId = owner.Binding.OperationId,
            ContextDigest = owner.Binding.ContextDigest, AuthenticatorReference = factor.AuthenticatorReference, Generation = factor.Generation, ProofId = proof.ProofId, ExpiresAt = proof.ExpiresAt });
        return await Finish(transaction, issued, ct).ConfigureAwait(false);
    }
}
