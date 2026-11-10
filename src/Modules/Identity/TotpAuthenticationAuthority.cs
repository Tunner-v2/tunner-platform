using Microsoft.EntityFrameworkCore;
using Tunner.Platform.Policies;

namespace Tunner.Identity.Persistence;

internal sealed partial class AuthenticationApplication
{
    internal TotpPolicy? CurrentTotpPolicy(DateTimeOffset now)
    {
        var value = totpPolicies?.SingleOrDefault()?.RequireEffective(now); value?.Validate(); return value;
    }
    internal bool TotpProofCurrentAt(StepUpProofRow proof, TotpAuthenticatorRow? factor, StepUpOperation operation, DateTimeOffset now, bool ownDisable)
    {
        var current = CurrentTotpPolicy(now);
        if (current is null || current.Version != proof.TotpPolicyVersion || current.Origin != proof.Origin || factor is null ||
            proof.PasskeyCredentialReference is not null || proof.PasskeyPolicyVersion is not null || proof.PasskeyAssuranceKey is not null ||
            current.OperationProofAssurance != proof.Assurance || factor.AccountId != proof.AccountId || factor.AuthenticatorReference != proof.TotpAuthenticatorReference ||
            factor.Generation != proof.TotpGeneration) return false;
        if (factor.State == "ACTIVE" && factor.Version == proof.TotpAuthenticatorVersion) return true;
        return ownDisable && operation.Scope is var scope && (scope == TotpApplication.DisableScope || scope == TotpApplication.ResetScope) &&
            operation.ResourceId == factor.AuthenticatorReference.ToString("N") && operation.ExpectedVersion == proof.TotpAuthenticatorVersion &&
            operation.ExpectedVersion + 1 == factor.Version && factor.State == "DISABLED" && factor.DisableOperationId == operation.OperationId &&
            factor.DisableCorrelationId == operation.CorrelationId && factor.DisablePolicyVersion == current.Version;
    }
    private bool TotpSessionFactsCurrentAt(AuthenticationSessionRow row, AuthenticationPolicy auth, DateTimeOffset now)
    {
        var current = CurrentTotpPolicy(now); var primary = row.AuthenticationMethods switch { "pwd+totp" => "pwd", "passkey+totp" => "passkey", _ => null };
        if (current is null || primary is null || current.Origin != row.Origin || row.TotpPolicyVersion != current.Version ||
            row.MfaRequirementPolicyVersion is null || !current.MfaAssurances.TryGetValue(primary, out var mapped) || mapped != row.Assurance) return false;
        if (primary == "pwd") return row.PasskeyCredentialReference is null && !string.IsNullOrWhiteSpace(auth.PasswordAssurance);
        var passkey = passkeyPolicies.SingleOrDefault()?.RequireEffective(now);
        return passkey is not null && passkey.Origin == row.Origin && row.PasskeyAssuranceKey is not null && row.PasskeyPolicyVersion == passkey.Version;
    }
    private async Task<bool> TotpSessionCurrentAsync(AuthenticationSessionRow row, AuthenticationPolicy auth, DateTimeOffset now, CancellationToken ct)
    {
        if (!TotpSessionFactsCurrentAt(row, auth, now)) return false;
        var factor = await database.Set<TotpAuthenticatorRow>().AsNoTracking().SingleOrDefaultAsync(x => x.AuthenticatorReference == row.TotpAuthenticatorReference && x.AccountId == row.AccountId, ct).ConfigureAwait(false);
        if (factor is null || factor.Generation != row.TotpGeneration || !(factor.State == "ACTIVE" && factor.Version == row.TotpAuthenticatorVersion ||
            factor.State == "DISABLED" && factor.Version == row.TotpAuthenticatorVersion + 1 && factor.DisableSessionEffect == nameof(TotpSessionEffect.PreserveExistingSessions))) return false;
        if (row.AuthenticationMethods == "pwd+totp") return true;
        var credential = await database.Set<PasskeyCredentialRow>().AsNoTracking().SingleOrDefaultAsync(x => x.CredentialReference == row.PasskeyCredentialReference && x.AccountId == row.AccountId, ct).ConfigureAwait(false);
        return credential is not null && (credential.State == "ACTIVE" || credential.State == "REVOKED" && credential.RevocationSessionEffect == nameof(PasskeyRevocationSessionEffect.PreserveExistingSessions));
    }
    internal async Task<AuthenticationSessionTerminationReceipt?> EndTotpActorSessionAsync(StepUpCaller caller, StepUpOperation operation,
        TotpAuthenticatorRow factor, TotpPolicy policy, CancellationToken ct)
    {
        var row = await database.Set<AuthenticationSessionRow>().SingleAsync(x => x.SessionId == caller.SessionId, ct).ConfigureAwait(false);
        if (row.TotpAuthenticatorReference != factor.AuthenticatorReference) return null;
        var effect = operation.Scope == TotpApplication.DisableScope ? policy.DisableSessionEffect : policy.ResetSessionEffect;
        if ((operation.Scope != TotpApplication.DisableScope && operation.Scope != TotpApplication.ResetScope) || effect != TotpSessionEffect.RevokeFactorSessions ||
            row.TotpGeneration != factor.Generation || row.AccountId != caller.AccountId || row.Origin != caller.Origin || row.State != "ACTIVE" || factor.State != "DISABLED" ||
            operation.ResourceId != factor.AuthenticatorReference.ToString("N") || operation.ExpectedVersion + 1 != factor.Version)
            throw new InvalidOperationException("An exact current TOTP-session transition is required.");
        await database.SaveChangesAsync(ct).ConfigureAwait(false); var prior = row.Version;
        row.TerminationCause = "TOTP_DISABLED"; row.TerminationPolicyVersion = policy.Version; row.TerminationCredentialVersion = factor.Version;
        End(row, "REVOKED", "TunnerSessionRevoked", clock.GetUtcNow(), operation.OperationId, operation.CorrelationId);
        await database.SaveChangesAsync(ct).ConfigureAwait(false);
        return new(row.SessionId, row.AccountId, row.Origin, operation.OperationId, operation.CorrelationId, operation.ContextDigest,
            factor.AuthenticatorReference, factor.Version, policy.Version, prior, row.Version, "TOTP", factor.Generation);
    }
    private async Task<AuthenticationTerminalVerification?> VerifyTotpTerminationAsync(StepUpCaller caller, StepUpOperation operation,
        AuthenticationSessionTerminationReceipt receipt, long validatedVersion, AuthenticationPolicy auth, DateTimeOffset now, CancellationToken ct)
    {
        var row = await database.Set<AuthenticationSessionRow>().AsNoTracking().SingleOrDefaultAsync(x => x.SessionId == caller.SessionId, ct).ConfigureAwait(false);
        var factor = await database.Set<TotpAuthenticatorRow>().AsNoTracking().SingleOrDefaultAsync(x => x.AuthenticatorReference == receipt.CredentialReference && x.AccountId == caller.AccountId, ct).ConfigureAwait(false);
        var policy = CurrentTotpPolicy(now);
        if (row is null || factor is null || policy is null || (operation.Scope != TotpApplication.DisableScope && operation.Scope != TotpApplication.ResetScope) ||
            operation.ResourceId != factor.AuthenticatorReference.ToString("N") || operation.ExpectedVersion + 1 != factor.Version ||
            receipt.SessionId != caller.SessionId || receipt.AccountId != caller.AccountId || receipt.Origin != caller.Origin ||
            receipt.OperationId != operation.OperationId || receipt.CorrelationId != operation.CorrelationId || receipt.ContextDigest != operation.ContextDigest ||
            receipt.PriorSessionVersion != validatedVersion || receipt.EndedSessionVersion != validatedVersion + 1 || row.Version != receipt.EndedSessionVersion ||
            row.State != "REVOKED" || row.ProtectedKey.Length != 0 || row.ProtectedTicket.Length != 0 || row.AccountId != caller.AccountId || row.Origin != caller.Origin ||
            row.TotpAuthenticatorReference != factor.AuthenticatorReference || row.TotpGeneration != factor.Generation || receipt.Generation != factor.Generation ||
            factor.State != "DISABLED" || factor.Version != receipt.CredentialVersion || factor.DisableSessionEffect != nameof(TotpSessionEffect.RevokeFactorSessions) ||
            factor.DisableOperationId != operation.OperationId || factor.DisableCorrelationId != operation.CorrelationId || factor.DisablePolicyVersion != policy.Version ||
            row.TerminationCause != "TOTP_DISABLED" || row.TerminationPolicyVersion != policy.Version || row.TerminationCredentialVersion != factor.Version ||
            row.OperationId != operation.OperationId || row.CorrelationId != operation.CorrelationId || receipt.PasskeyPolicyVersion != policy.Version ||
            row.RevokedAt is not { } ended || ended < row.AuthenticatedAt || ended > now || Expired(row, auth, now)) return null;
        var user = await database.Users.SingleAsync(x => x.AccountId == caller.AccountId, ct).ConfigureAwait(false); await database.Entry(user).ReloadAsync(ct).ConfigureAwait(false);
        if (!user.EmailConfirmed || string.IsNullOrWhiteSpace(user.SecurityStamp) || AuthenticationIdentity.Digest(user.SecurityStamp) != row.SecurityStampDigest ||
            !await database.VerifiedContacts.AnyAsync(x => x.AccountId == caller.AccountId && x.State == Tunner.Accounts.ContactVerificationState.Verified && x.AnonymizedAtUtc == null && x.NormalizedEmail == user.NormalizedEmail, ct).ConfigureAwait(false)) return null;
        var account = await database.Accounts.SingleAsync(x => x.AccountId == caller.AccountId, ct).ConfigureAwait(false); await database.Entry(account).ReloadAsync(ct).ConfigureAwait(false);
        var decision = await AccessAsync(account, operation.Scope.Operation, now, ct).ConfigureAwait(false);
        return decision?.Allowed == true && decision.EffectiveInterval.Contains(clock.GetUtcNow()) && CurrentTotpPolicy(clock.GetUtcNow())?.Version == policy.Version
            ? new(decision.EffectiveInterval) : null;
    }
    private bool TotpOutcomeCurrentAt(UserStepUpOperationApplyOutcome outcome, StepUpCaller caller, StepUpOperation operation, DateTimeOffset now)
    {
        var validity = outcome.TotpValidity!; var row = database.ChangeTracker.Entries<TotpOperationRow>().Select(x => x.Entity).SingleOrDefault(x => x.EnrollmentId == validity.EnrollmentId);
        var policy = CurrentTotpPolicy(now);
        if (row is null || policy is null || policy.Version != validity.PolicyVersion || row.PolicyVersion != policy.Version || policy.Origin != caller.Origin ||
            row.AccountId != caller.AccountId || row.SessionId != caller.SessionId || row.Origin != caller.Origin || validity.AccountId != caller.AccountId ||
            validity.SessionId != caller.SessionId || validity.Origin != caller.Origin || validity.OperationId != operation.OperationId || validity.ContextDigest != operation.ContextDigest ||
            TotpApplication.Operation(row).ContextDigest != operation.ContextDigest || row.ExpiresAt != validity.ExpiresAt || now >= validity.ExpiresAt) return false;
        if (operation.Scope == TotpApplication.EnrollmentScope) return row.State == "AUTHORIZED" && validity.EligibilityInterval is null;
        return (operation.Scope == TotpApplication.DisableScope || operation.Scope == TotpApplication.ResetScope) && row.State == "COMPLETED" &&
            validity.EligibilityInterval is { } interval && interval.Contains(now) && row.EligibilityPolicyVersion == validity.EligibilityPolicyVersion &&
            row.EligibilityEvidenceReference == validity.EligibilityEvidenceReference && row.EligibilityFrom == interval.From && row.EligibilityUntil == interval.Until &&
            row.EligibilityIncludesFrom == interval.IncludesFrom && row.EligibilityIncludesUntil == interval.IncludesUntil;
    }
}
