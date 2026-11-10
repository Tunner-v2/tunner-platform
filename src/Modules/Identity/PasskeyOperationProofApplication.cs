using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Tunner.Identity.Persistence;

internal sealed record PasskeyOperationProofResult(string? ErrorCode, PasskeyCeremonyRow? Ceremony = null, StepUpProofGrant? Proof = null);

internal sealed partial class PasskeyApplication
{
    internal async Task<PasskeyCapability> ProofCapabilityAsync(StepUpCaller caller, AssurancePolicyVersion assurance, CancellationToken ct)
    {
        PasskeyPolicy policy;
        try { policy = Current(caller.Origin); } catch (InvalidOperationException) { return new(false, null); }
        if (assurance.RequiredAssurance.Equals("AAL3", StringComparison.OrdinalIgnoreCase)) return new(false, null);
        var user = await database.Users.SingleOrDefaultAsync(x => x.AccountId == caller.AccountId, ct).ConfigureAwait(false);
        if (user is null) return new(false, null);
        var active = await database.Set<PasskeyCredentialRow>().Where(x => x.AccountId == caller.AccountId && x.State == "ACTIVE").Select(x => x.CredentialDigest).ToArrayAsync(ct).ConfigureAwait(false);
        var credentials = await users.GetPasskeysAsync(user).ConfigureAwait(false);
        bool Accepted(bool backup, bool backed) => assurance.MethodAssurances.TryGetValue(PasskeyPolicy.MethodKey(backup, backed), out var value) && value == assurance.RequiredAssurance;
        var available = credentials.Any(x => active.Contains(CredentialDigest(x.CredentialId), StringComparer.Ordinal) &&
            (Accepted(x.IsBackupEligible, false) || x.IsBackupEligible && Accepted(true, true)));
        return new(available, available ? policy.MaximumCredentialJsonBytes : null);
    }
    internal async Task<PasskeyOperationProofResult> StartOperationProofAsync(StepUpCaller caller, UserStepUpOperationContext owner,
        Guid requestId, Guid attemptId, HttpContext http, CancellationToken ct)
    {
        Clean(); if (requestId == Guid.Empty || attemptId == Guid.Empty || owner.Completed) return new("VERSION_CONFLICT");
        var policy = Current(caller.Origin); var now = clock.GetUtcNow(); var auth = authenticationPolicies.RequireEffective(now); auth.Validate();
        await using var transaction = await database.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        if (!await authentication.AllowAsync("step-up:N:" + caller.NetworkDigest, auth.NetworkAttemptLimit, auth, now, ct).ConfigureAwait(false) ||
            !await authentication.AllowAsync("step-up:S:" + caller.SessionId.ToString("N"), auth.IdentifierAttemptLimit, auth, now, ct).ConfigureAwait(false))
            return await Finish(transaction, new PasskeyOperationProofResult("RATE_OR_ABUSE_LIMITED"), ct).ConfigureAwait(false);
        var session = await authentication.LockForOperationAsync(caller, owner.Binding.Scope.Operation, ct).ConfigureAwait(false);
        if (session is null) return await Finish(transaction, new PasskeyOperationProofResult("AUTHENTICATION_FAILED"), ct).ConfigureAwait(false);
        await LockCredentialOwner(caller.AccountId, ct).ConfigureAwait(false);
        var browser = MutationBrowser(caller);
        var prior = await database.Set<PasskeyCeremonyRow>().SingleOrDefaultAsync(x => x.BrowserDigest == browser && x.RequestId == attemptId && x.Kind == "STEP_UP", ct).ConfigureAwait(false);
        if (prior is not null)
        {
            await database.Entry(prior).ReloadAsync(ct).ConfigureAwait(false);
            return await Finish(transaction, new PasskeyOperationProofResult(ProofBinding(prior, caller, owner, requestId) ? null : "VERSION_CONFLICT", prior), ct).ConfigureAwait(false);
        }
        now = clock.GetUtcNow(); policy = Current(caller.Origin);
        var assurance = assurancePolicies.RequireEffective(owner.Binding.Scope, now, owner.ExpectedPolicyVersion);
        if (!(await ProofCapabilityAsync(caller, assurance, ct).ConfigureAwait(false)).Available)
            return await Finish(transaction, new PasskeyOperationProofResult("STEP_UP_REQUIRED"), ct).ConfigureAwait(false);
        if (await database.Set<PasskeyCeremonyRow>().CountAsync(x => x.SessionId == caller.SessionId && x.Kind == "STEP_UP" && x.State == "PENDING" && x.ExpiresAt > now, ct).ConfigureAwait(false) >= policy.MaximumPendingCeremonies)
            return await Finish(transaction, new PasskeyOperationProofResult("RATE_OR_ABUSE_LIMITED"), ct).ConfigureAwait(false);
        var user = await database.Users.SingleAsync(x => x.AccountId == caller.AccountId, ct).ConfigureAwait(false);
        var options = await Handler(policy).MakeRequestOptionsAsync(user, http).ConfigureAwait(false);
        now = clock.GetUtcNow(); _ = assurancePolicies.RequireEffective(owner.Binding.Scope, now, owner.ExpectedPolicyVersion);
        if (Current(caller.Origin).Version != policy.Version) return await Finish(transaction, new PasskeyOperationProofResult("VERSION_CONFLICT"), ct).ConfigureAwait(false);
        var row = new PasskeyCeremonyRow
        {
            CeremonyId = Guid.NewGuid(), RequestId = attemptId, StepUpRequestId = requestId, Kind = "STEP_UP", BrowserDigest = browser,
            AccountId = caller.AccountId, SessionId = caller.SessionId, Origin = caller.Origin, PolicyVersion = policy.Version,
            AuthenticationPolicyVersion = auth.Version, SecurityStampDigest = session.SecurityStampDigest, AssurancePolicyVersion = owner.ExpectedPolicyVersion,
            CreatedAt = now, ExpiresAt = Min(now + policy.CeremonyLifetime, session.ExpiresAt), RecoveryExpiresAt = now + policy.CeremonyLifetime + policy.ResultRecoveryLifetime,
            BoundOperationId = owner.Binding.OperationId, BoundCorrelationId = owner.Binding.CorrelationId, BoundOperation = owner.Binding.Scope.Operation,
            BoundContext = owner.Binding.Scope.Context, BoundResourceId = owner.Binding.ResourceId, BoundExpectedVersion = owner.Binding.ExpectedVersion,
            BoundContextDigest = owner.Binding.ContextDigest, OptionsJson = options.RequestOptionsJson,
            ProtectedState = stateProtection.Protect(options.AssertionState ?? throw new InvalidOperationException("Native ceremony state is unavailable.")),
            ProcessingReference = policy.ProcessingReference, RetentionReference = policy.RetentionReference
        };
        database.Add(row); Event(row, "PasskeyOperationProofStarted", now);
        return await Finish(transaction, new PasskeyOperationProofResult(null, row), ct).ConfigureAwait(false);
    }
    internal async Task<PasskeyOperationProofResult> CompleteOperationProofAsync(StepUpCaller caller, UserStepUpOperationContext owner,
        Guid requestId, Guid attemptId, Guid ceremonyId, JsonElement? response, CancellationToken ct, HttpContext? http = null)
    {
        Clean(); var policy = Current(caller.Origin); var json = response is { } value ? CredentialJson(value, policy) : null;
        _ = assurancePolicies.RequireEffective(owner.Binding.Scope, clock.GetUtcNow(), owner.ExpectedPolicyVersion);
        var responseDigest = json is null ? null : AuthenticationIdentity.Digest(json);
        var credentialId = response is { } credentialJson ? ReadCredentialId(credentialJson) : null;
        await using var transaction = await database.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        var now = clock.GetUtcNow(); var auth = authenticationPolicies.RequireEffective(now); auth.Validate();
        if (!await authentication.AllowAsync("step-up:N:" + caller.NetworkDigest, auth.NetworkAttemptLimit, auth, now, ct).ConfigureAwait(false) ||
            !await authentication.AllowAsync("step-up:S:" + caller.SessionId.ToString("N"), auth.IdentifierAttemptLimit, auth, now, ct).ConfigureAwait(false))
            return await Finish(transaction, new PasskeyOperationProofResult("RATE_OR_ABUSE_LIMITED"), ct).ConfigureAwait(false);
        var session = await authentication.LockForOperationAsync(caller, owner.Binding.Scope.Operation, ct).ConfigureAwait(false);
        if (session is null) return await Finish(transaction, new PasskeyOperationProofResult("AUTHENTICATION_FAILED"), ct).ConfigureAwait(false);
        await LockCredentialOwner(caller.AccountId, ct).ConfigureAwait(false); await LockCeremony(ceremonyId, ct).ConfigureAwait(false);
        var row = await database.Set<PasskeyCeremonyRow>().SingleOrDefaultAsync(x => x.CeremonyId == ceremonyId && x.RequestId == attemptId && x.Kind == "STEP_UP", ct).ConfigureAwait(false);
        if (row is null || !ProofBinding(row, caller, owner, requestId)) return await Finish(transaction, new PasskeyOperationProofResult("VERSION_CONFLICT"), ct).ConfigureAwait(false);
        await database.Entry(row).ReloadAsync(ct).ConfigureAwait(false);
        if (row.State == "COMPLETED")
        {
            if (responseDigest is not null && row.RequestDigest != responseDigest) return await Finish(transaction, new PasskeyOperationProofResult("VERSION_CONFLICT", row), ct).ConfigureAwait(false);
            var original = row.ProofReference is { } proof ? await stepUp.RecoverProofAsync(caller, owner.Binding, proof, owner.ExpectedPolicyVersion, ct).ConfigureAwait(false) : new StepUpIssueResult("STEP_UP_REQUIRED", null);
            return await Finish(transaction, new PasskeyOperationProofResult(original.ErrorCode, row, original.Grant), ct).ConfigureAwait(false);
        }
        now = clock.GetUtcNow();
        if (row.State != "PENDING" || !CurrentBinding(row, policy, now) || row.SecurityStampDigest != session.SecurityStampDigest)
            return await FailOperationProof(transaction, row, "STEP_UP_REQUIRED", ct).ConfigureAwait(false);
        if (json is null || credentialId is null || http is null) return await Finish(transaction, new PasskeyOperationProofResult("VERSION_CONFLICT", row), ct).ConfigureAwait(false);
        await LockCredential(credentialId, ct).ConfigureAwait(false); var digest = CredentialDigest(credentialId);
        var credential = await database.Set<PasskeyCredentialRow>().SingleOrDefaultAsync(x => x.AccountId == caller.AccountId && x.CredentialDigest == digest && x.State == "ACTIVE", ct).ConfigureAwait(false);
        if (credential is null) return await FailOperationProof(transaction, row, "AUTHENTICATION_FAILED", ct).ConfigureAwait(false);
        await database.Entry(credential).ReloadAsync(ct).ConfigureAwait(false);
        var verified = await Handler(policy).PerformAssertionAsync(new PasskeyAssertionContext { HttpContext = http, CredentialJson = json,
            AssertionState = stateProtection.Unprotect(row.ProtectedState) }).ConfigureAwait(false);
        if (!verified.Succeeded || verified.User.AccountId != caller.AccountId || !verified.Passkey.CredentialId.AsSpan().SequenceEqual(credentialId))
            return await FailOperationProof(transaction, row, "AUTHENTICATION_FAILED", ct).ConfigureAwait(false);
        var verifiedAt = clock.GetUtcNow();
        var facts = PasskeyPolicy.AssuranceKey(verified.Passkey.IsBackupEligible, verified.Passkey.IsBackedUp);
        var method = PasskeyPolicy.MethodKey(verified.Passkey.IsBackupEligible, verified.Passkey.IsBackedUp);
        var assurance = assurancePolicies.RequireEffective(owner.Binding.Scope, clock.GetUtcNow(), owner.ExpectedPolicyVersion);
        if (!assurance.MethodAssurances.TryGetValue(method, out var mapped) || mapped != assurance.RequiredAssurance || mapped.Equals("AAL3", StringComparison.OrdinalIgnoreCase))
            return await FailOperationProof(transaction, row, "STEP_UP_REQUIRED", ct).ConfigureAwait(false);
        // Preserve rejected-attempt accounting while native counter/proof state can roll back as a unit.
        await database.SaveChangesAsync(ct).ConfigureAwait(false); await transaction.CreateSavepointAsync("operation_passkey", ct).ConfigureAwait(false);
        var stored = await users.AddOrUpdatePasskeyAsync(verified.User, verified.Passkey).ConfigureAwait(false);
        if (!stored.Succeeded) return await RollbackOperationProof(transaction, row.CeremonyId, "VERSION_CONFLICT", ct).ConfigureAwait(false);
        var issued = await stepUp.IssueVerifiedProofAsync(caller, session, owner.Binding, owner.ExpectedPolicyVersion, method, credential, policy.Version, facts, verifiedAt, ct).ConfigureAwait(false);
        if (issued.ErrorCode is not null || !CurrentBinding(row, Current(caller.Origin), clock.GetUtcNow()))
            return await RollbackOperationProof(transaction, row.CeremonyId, issued.ErrorCode ?? "STEP_UP_REQUIRED", ct).ConfigureAwait(false);
        var proofRow = database.ChangeTracker.Entries<StepUpProofRow>().Select(x => x.Entity).Single(x => x.OperationId == owner.Binding.OperationId && x.SessionId == caller.SessionId);
        var authority = await authentication.VerifyContinuedOperationAsync(caller, owner.Binding, session, ct).ConfigureAwait(false);
        var decisionAt = clock.GetUtcNow(); var effectivePasskey = policies.RequireEffective(decisionAt);
        var effectiveAuthentication = authenticationPolicies.RequireEffective(decisionAt); effectiveAuthentication.Validate();
        assurance = assurancePolicies.RequireEffective(owner.Binding.Scope, decisionAt, owner.ExpectedPolicyVersion);
        if (authority is null || !authority.SessionUseInterval.Contains(decisionAt) || !authority.OperationInterval.Contains(decisionAt) ||
            !CurrentBinding(row, effectivePasskey, decisionAt) || AuthenticationApplication.Expired(session, effectiveAuthentication, decisionAt) ||
            decisionAt < proofRow.AuthenticatedAt || decisionAt >= proofRow.ExpiresAt || decisionAt - proofRow.AuthenticatedAt >= assurance.ProofLifetime ||
            !assurance.MethodAssurances.TryGetValue(method, out var finalAssurance) || finalAssurance != proofRow.Assurance ||
            !authentication.ProofMethodCurrentAt(proofRow, credential, owner.Binding, decisionAt, false))
            return await RollbackOperationProof(transaction, row.CeremonyId, "STEP_UP_REQUIRED", ct).ConfigureAwait(false);
        row.ProofReference = proofRow.ProofId; row.CredentialReference = credential.CredentialReference; row.RequestDigest = responseDigest!;
        credential.LastUsedAt = clock.GetUtcNow(); credential.UsageVersion++;
        End(row, "COMPLETED", clock.GetUtcNow()); Event(row, "PasskeyOperationProofVerified", clock.GetUtcNow());
        return await Finish(transaction, new PasskeyOperationProofResult(null, row, issued.Grant), ct).ConfigureAwait(false);
    }
    internal async Task<PasskeyCeremonyRow?> ReadOperationProofAsync(StepUpCaller caller, UserStepUpOperationContext owner,
        Guid requestId, Guid attemptId, Guid ceremonyId, CancellationToken ct)
    {
        var row = await database.Set<PasskeyCeremonyRow>().AsNoTracking().SingleOrDefaultAsync(x => x.CeremonyId == ceremonyId && x.RequestId == attemptId && x.Kind == "STEP_UP", ct).ConfigureAwait(false);
        return row is not null && ProofBinding(row, caller, owner, requestId) ? row : null;
    }
    internal async Task<PasskeyCeremonyRow?> ReadConsumedOperationProofAsync(StepUpCaller caller, UserStepUpOperationContext owner,
        Guid requestId, Guid attemptId, Guid? ceremonyId, JsonElement? response, CancellationToken ct)
    {
        var row = await database.Set<PasskeyCeremonyRow>().AsNoTracking().SingleOrDefaultAsync(x => x.RequestId == attemptId && x.StepUpRequestId == requestId &&
            x.SessionId == caller.SessionId && x.Kind == "STEP_UP", ct).ConfigureAwait(false);
        if (row is null || !ProofBinding(row, caller, owner, requestId) || row.State != "COMPLETED" || row.RecoveryExpiresAt <= clock.GetUtcNow() ||
            ceremonyId is { } expected && expected != row.CeremonyId || row.ProofReference is null ||
            response is { } payload && row.RequestDigest != AuthenticationIdentity.Digest(CredentialJson(payload, Current(caller.Origin)))) return null;
        return await database.Set<StepUpProofRow>().AsNoTracking().AnyAsync(x => x.ProofId == row.ProofReference && x.State == "CONSUMED" &&
            x.SessionId == caller.SessionId && x.AccountId == caller.AccountId && x.OperationId == owner.Binding.OperationId && x.ContextDigest == owner.Binding.ContextDigest &&
            x.PasskeyCredentialReference == row.CredentialReference, ct).ConfigureAwait(false) ? row : null;
    }
    internal async Task<string> OperationProofStatusAsync(PasskeyCeremonyRow row, CancellationToken ct)
    {
        if (row.State is "REJECTED" or "EXPIRED") return row.State;
        if (row.State == "COMPLETED" && row.ProofReference is { } id)
        {
            var proof = await database.Set<StepUpProofRow>().AsNoTracking().SingleOrDefaultAsync(x => x.ProofId == id, ct).ConfigureAwait(false);
            if (proof is not { State: "ACTIVE" }) return "EXPIRED";
            var source = await authentication.ReadProofCredentialAsync(proof, ct, refresh: true).ConfigureAwait(false);
            var now = clock.GetUtcNow(); var scope = new AssurancePolicyScope(row.BoundOperation!, row.BoundContext!);
            AssurancePolicyVersion assurance;
            try { assurance = assurancePolicies.RequireEffective(scope, now, row.AssurancePolicyVersion); }
            catch (AssurancePolicyVersionConflictException) { return "EXPIRED"; }
            var operation = new StepUpOperation(row.BoundOperationId!, row.BoundCorrelationId!, scope, row.BoundResourceId!, row.BoundExpectedVersion!.Value, row.BoundContextDigest!);
            return proof.ExpiresAt > now && now - proof.AuthenticatedAt < assurance.ProofLifetime &&
                assurance.MethodAssurances.TryGetValue(proof.AuthenticationMethod, out var mapped) && mapped == proof.Assurance &&
                authentication.ProofMethodCurrentAt(proof, source, operation, now, false) ? "PROOF_ISSUED" : "EXPIRED";
        }
        var current = policies.RequireEffective(clock.GetUtcNow());
        return CurrentBinding(row, current, clock.GetUtcNow()) ? "PENDING" : "EXPIRED";
    }
    private static bool ProofBinding(PasskeyCeremonyRow row, StepUpCaller caller, UserStepUpOperationContext owner, Guid requestId) =>
        Actor(row, caller) && row.Kind == "STEP_UP" && row.StepUpRequestId == requestId && row.BoundOperationId == owner.Binding.OperationId &&
        row.BoundCorrelationId == owner.Binding.CorrelationId && row.BoundOperation == owner.Binding.Scope.Operation && row.BoundContext == owner.Binding.Scope.Context &&
        row.BoundResourceId == owner.Binding.ResourceId && row.BoundExpectedVersion == owner.Binding.ExpectedVersion && row.BoundContextDigest == owner.Binding.ContextDigest && row.AssurancePolicyVersion == owner.ExpectedPolicyVersion;
    private async Task<PasskeyOperationProofResult> FailOperationProof(Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction, PasskeyCeremonyRow row, string error, CancellationToken ct)
    {
        var result = await FailCeremony(transaction, row, error, clock.GetUtcNow(), ct).ConfigureAwait(false); return new(result.ErrorCode, result.Ceremony);
    }
    private async Task<PasskeyOperationProofResult> RollbackOperationProof(Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction, Guid id, string error, CancellationToken ct)
    {
        await transaction.RollbackToSavepointAsync("operation_passkey", ct).ConfigureAwait(false); database.ChangeTracker.Clear();
        var row = await database.Set<PasskeyCeremonyRow>().SingleAsync(x => x.CeremonyId == id, ct).ConfigureAwait(false);
        return await FailOperationProof(transaction, row, error, ct).ConfigureAwait(false);
    }
}
