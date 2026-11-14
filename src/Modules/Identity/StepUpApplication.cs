using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Tunner.Database.Outbox;

namespace Tunner.Identity.Persistence;

/// <summary>Private Identity transaction boundary. No HTTP route or implicit runtime policy activation.</summary>
internal sealed class StepUpApplication(IdentityPersistenceDbContext database, AuthenticationApplication authentication,
    IAuthenticationPolicySource authenticationPolicies, IAssurancePolicySource assurancePolicies,
    AuthenticationPasswordWork passwordWork, IDataProtectionProvider protection, TimeProvider clock, IRecoveryCodePolicySource? recoveryPolicies = null)
{
    private readonly IDataProtector retryProtection = protection.CreateProtector("Tunner.User.StepUp.Proof.RetryKey.v1");
    internal async Task<StepUpIssueResult> IssuePasswordAsync(StepUpCaller caller, StepUpOperation operation,
        string expectedPolicyVersion, string password, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller); ArgumentNullException.ThrowIfNull(operation); ArgumentException.ThrowIfNullOrWhiteSpace(expectedPolicyVersion); RequireCleanBoundary();
        var now = clock.GetUtcNow(); var authPolicy = authenticationPolicies.RequireEffective(now); authPolicy.Validate();
        var policy = assurancePolicies.RequireEffective(operation.Scope, now, expectedPolicyVersion);
        if (string.IsNullOrEmpty(password) || password.EnumerateRunes().Count() > authPolicy.PasswordMaximumLength)
            return new("VALIDATION_ERROR", null);
        if (policy.RequireMethodAssurance("pwd") != policy.RequiredAssurance) return new("STEP_UP_REQUIRED", null);
        await using var transaction = await database.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        try
        {
            if (!await authentication.AllowAsync("step-up:N:" + caller.NetworkDigest, authPolicy.NetworkAttemptLimit, authPolicy, now, ct).ConfigureAwait(false) ||
                !await authentication.AllowAsync("step-up:S:" + caller.SessionId.ToString("N"), authPolicy.IdentifierAttemptLimit, authPolicy, now, ct).ConfigureAwait(false))
                return await FinishAsync(transaction, new StepUpIssueResult("RATE_OR_ABUSE_LIMITED", null), ct).ConfigureAwait(false);
            var session = await authentication.LockForOperationAsync(caller, operation.Scope.Operation, ct).ConfigureAwait(false);
            if (session is null) return await FinishAsync(transaction, new StepUpIssueResult("AUTHENTICATION_FAILED", null), ct).ConfigureAwait(false);
            var user = await database.Users.SingleOrDefaultAsync(x => x.AccountId == caller.AccountId, ct).ConfigureAwait(false);
            if (passwordWork.Verify(authPolicy, user, password) == PasswordVerificationResult.Failed)
            {
                database.Add(new OutboxMessage(Guid.NewGuid(), "1.0", "StepUpAuthenticationRejected", "1", now, "tunner/identity",
                    caller.SessionId.ToString("N"), "RESTRICTED_SECURITY", JsonSerializer.Serialize(new { reference = operation.OperationId }),
                    "1", operation.OperationId, operation.CorrelationId));
                return await FinishAsync(transaction, new StepUpIssueResult("AUTHENTICATION_FAILED", null), ct).ConfigureAwait(false);
            }
            var verifiedAt = clock.GetUtcNow();
            // Re-read time, policy and live authority after credential work, without extending a prior proof.
            session = await authentication.LockForOperationAsync(caller, operation.Scope.Operation, ct).ConfigureAwait(false);
            now = clock.GetUtcNow(); policy = assurancePolicies.RequireEffective(operation.Scope, now, expectedPolicyVersion);
            authPolicy = authenticationPolicies.RequireEffective(now); authPolicy.Validate();
            if (session is null) return await FinishAsync(transaction, new StepUpIssueResult("AUTHENTICATION_FAILED", null), ct).ConfigureAwait(false);
            if (policy.RequireMethodAssurance("pwd") != policy.RequiredAssurance) return await FinishAsync(transaction, new StepUpIssueResult("STEP_UP_REQUIRED", null), ct).ConfigureAwait(false);
            var issued = await IssueVerifiedProofAsync(caller, session, operation, expectedPolicyVersion, "pwd", null, null, null, verifiedAt, ct).ConfigureAwait(false);
            return await FinishAsync(transaction, issued, ct).ConfigureAwait(false);
        }
        catch { database.ChangeTracker.Clear(); throw; }
    }

    // Only Identity's verified authenticators call this; no browser can supply a method, credential, or proof bearer.
    internal async Task<StepUpIssueResult> IssueVerifiedProofAsync(StepUpCaller caller, AuthenticationSessionRow session, StepUpOperation operation,
        string expectedPolicyVersion, string method, PasskeyCredentialRow? credential, string? passkeyPolicyVersion, string? passkeyAssuranceKey, DateTimeOffset verifiedAt, CancellationToken ct,
        TotpAuthenticatorRow? totp = null, string? totpPolicyVersion = null, FederationOperationProofSource? federation = null)
    {
        if (database.Database.CurrentTransaction is null || session.State != "ACTIVE" || session.AccountId != caller.AccountId || session.SessionId != caller.SessionId || session.Origin != caller.Origin)
            throw new InvalidOperationException("A current locked Identity transaction is required.");
        var prior = await database.Set<StepUpProofRow>().SingleOrDefaultAsync(x => x.SessionId == caller.SessionId && x.OperationId == operation.OperationId, ct).ConfigureAwait(false);
        var now = clock.GetUtcNow(); var policy = assurancePolicies.RequireEffective(operation.Scope, now, expectedPolicyVersion);
        var authPolicy = authenticationPolicies.RequireEffective(now); authPolicy.Validate();
        if (!policy.MethodAssurances.TryGetValue(method, out var mapped) || mapped != policy.RequiredAssurance ||
            method.StartsWith("passkey:", StringComparison.Ordinal) && mapped.Equals("AAL3", StringComparison.OrdinalIgnoreCase)) return new("STEP_UP_REQUIRED", null);
        if (prior is not null)
        {
            await database.Entry(prior).ReloadAsync(ct).ConfigureAwait(false);
            var sourceCredential = await authentication.ReadProofCredentialAsync(prior, ct, refresh: true).ConfigureAwait(false);
            now = clock.GetUtcNow(); policy = assurancePolicies.RequireEffective(operation.Scope, now, expectedPolicyVersion);
            if (!Matches(prior, caller, operation) || !Valid(prior, session, policy, now) || prior.AuthenticationMethod != method ||
                prior.PasskeyCredentialReference != credential?.CredentialReference || prior.TotpAuthenticatorReference != totp?.AuthenticatorReference ||
                prior.FederationAttemptId != federation?.Attempt.AttemptId ||
                !authentication.ProofMethodCurrentAt(prior, sourceCredential, operation, now, false)) return new("VERSION_CONFLICT", null);
            return new(null, new(retryProtection.Unprotect(prior.ProtectedKey), prior.ExpiresAt));
        }
        var expires = verifiedAt + policy.ProofLifetime;
        if (federation is not null)
        {
            var native = federation.Attempt;
            if (native.ProviderAuthenticatedAt != verifiedAt || native.VerificationExpiresAt is not { } verificationExpiry)
                return new("STEP_UP_REQUIRED", null);
            if (verificationExpiry < expires) expires = verificationExpiry;
        }
        if (session.ExpiresAt < expires) expires = session.ExpiresAt;
        if (policy.Metadata.EffectiveInterval.Until is { } until && until < expires) expires = until;
        if (verifiedAt > now || expires <= now || AuthenticationApplication.Expired(session, authPolicy, now)) return new("STEP_UP_REQUIRED", null);
        var key = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var row = new StepUpProofRow
        {
            ProofId = Guid.NewGuid(), SessionId = caller.SessionId, AccountId = caller.AccountId, Origin = caller.Origin,
            Scheme = session.Scheme, SecurityStampDigest = session.SecurityStampDigest, KeyDigest = AuthenticationIdentity.Digest(key),
            ProtectedKey = retryProtection.Protect(key), OperationId = operation.OperationId, CorrelationId = operation.CorrelationId,
            Operation = operation.Scope.Operation, Context = operation.Scope.Context, ResourceId = operation.ResourceId,
            ExpectedVersion = operation.ExpectedVersion, ContextDigest = operation.ContextDigest, PolicyVersion = policy.Metadata.VersionId,
            AuthenticationPolicyVersion = authPolicy.Version, AuthenticationMethod = method, Assurance = policy.RequiredAssurance,
            FederationAttemptId = federation?.Attempt.AttemptId,
            PasskeyCredentialReference = credential?.CredentialReference, PasskeyPolicyVersion = passkeyPolicyVersion, PasskeyAssuranceKey = passkeyAssuranceKey,
            TotpAuthenticatorReference = totp?.AuthenticatorReference, TotpGeneration = totp?.Generation, TotpAuthenticatorVersion = totp?.Version, TotpPolicyVersion = totpPolicyVersion,
            ProcessingReference = authPolicy.ProcessingReference, RetentionReference = authPolicy.RetentionReference, AuthenticatedAt = verifiedAt, ExpiresAt = expires
        };
        if (!authentication.ProofMethodCurrentAt(row, new AuthenticatorProofSource(credential, totp, federation), operation, now, false)) return new("STEP_UP_REQUIRED", null);
        database.Add(row); Event(row, "StepUpProofIssued", now); return new(null, new(key, expires));
    }
    internal async Task<StepUpIssueResult> RecoverProofAsync(StepUpCaller caller, StepUpOperation operation, Guid proofId, string expectedVersion, CancellationToken ct)
    {
        var row = await database.Set<StepUpProofRow>().SingleOrDefaultAsync(x => x.ProofId == proofId, ct).ConfigureAwait(false);
        if (row is null) return new("STEP_UP_REQUIRED", null);
        await database.Entry(row).ReloadAsync(ct).ConfigureAwait(false);
        var credential = await authentication.ReadProofCredentialAsync(row, ct, refresh: true).ConfigureAwait(false);
        var now = clock.GetUtcNow(); var policy = assurancePolicies.RequireEffective(operation.Scope, now, expectedVersion);
        var session = await database.Set<AuthenticationSessionRow>().SingleAsync(x => x.SessionId == caller.SessionId, ct).ConfigureAwait(false);
        return Matches(row, caller, operation) && Valid(row, session, policy, now) && authentication.ProofMethodCurrentAt(row, credential, operation, now, false)
            ? new(null, new(retryProtection.Unprotect(row.ProtectedKey), row.ExpiresAt)) : new("STEP_UP_REQUIRED", null);
    }

    // The callback is an Identity-owner local mutation using this same context/transaction, never provider I/O.
    internal Task<StepUpExecutionResult> ExecuteAsync(StepUpCaller caller, StepUpOperation operation, string key,
        Func<CancellationToken, Task<bool>> applyCurrentOperation, CancellationToken ct = default) =>
        RecoveryCodeApplication.Owns(operation.Scope) ? throw new InvalidOperationException("Recovery issuance requires its typed winning outcome path.") :
        ExecuteOutcomeAsync(caller, operation, key, async token => await applyCurrentOperation(token).ConfigureAwait(false)
            ? UserStepUpOperationApplyOutcome.SessionContinues() : UserStepUpOperationApplyOutcome.Conflict(), ct);
    internal Task<StepUpExecutionResult> ExecuteOutcomeAsync(StepUpCaller caller, StepUpOperation operation, string key,
        Func<CancellationToken, Task<UserStepUpOperationApplyOutcome>> applyCurrentOperation, CancellationToken ct = default)
        => SessionRevocationApplication.Owns(operation.Scope) || SessionBulkRevocationApplication.Owns(operation.Scope)
            ? throw new InvalidOperationException("Session revocation requires its typed owner execution path.")
            : ExecuteOutcomeCoreAsync(caller, operation, key, applyCurrentOperation, null, ct);

    internal Task<StepUpExecutionResult> ExecuteSessionRevocationAsync(StepUpCaller caller, StepUpOperation operation,
        SessionRevocationRequest request, string key, CancellationToken ct = default)
    {
        var bound = SessionRevocationBinding.Operation(caller, request);
        if (operation.OperationId != bound.OperationId || operation.CorrelationId != bound.CorrelationId || operation.Scope != bound.Scope ||
            operation.ResourceId != bound.ResourceId || operation.ExpectedVersion != bound.ExpectedVersion || operation.ContextDigest != bound.ContextDigest)
            throw new ArgumentException("The exact session owner binding is required.", nameof(operation));
        var owner = AuthenticationApplication.SessionRevocationExecutionSet.Single(database, authentication, caller, request, operation);
        return ExecuteOutcomeCoreAsync(caller, operation, key, null, owner, ct);
    }

    internal Task<StepUpExecutionResult> ExecuteSessionBulkRevocationAsync(StepUpCaller caller, SessionBulkRevocationManifest manifest,
        ISessionBulkRevocationDiscoverySource discovery, string key, CancellationToken ct = default)
    {
        var operation = manifest.Operation(caller);
        var owner = AuthenticationApplication.SessionRevocationExecutionSet.Bulk(database, authentication, caller, manifest, discovery, assurancePolicies);
        return ExecuteOutcomeCoreAsync(caller, operation, key, null, owner, ct);
    }

    private async Task<StepUpExecutionResult> ExecuteOutcomeCoreAsync(StepUpCaller caller, StepUpOperation operation, string key,
        Func<CancellationToken, Task<UserStepUpOperationApplyOutcome>>? applyCurrentOperation,
        AuthenticationApplication.SessionRevocationExecutionSet? sessionRevocation, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(caller); ArgumentNullException.ThrowIfNull(operation);
        if (sessionRevocation is null) ArgumentNullException.ThrowIfNull(applyCurrentOperation);
        RequireCleanBoundary();
        if (key is not { Length: 64 } || !key.All(Uri.IsHexDigit)) return new(false, "STEP_UP_REQUIRED");
        var now = clock.GetUtcNow();
        RecoveryCodeFirstDelivery? pending = null;
        try
        {
          bool committedTerminal;
          await using (var transaction = await database.Database.BeginTransactionAsync(ct).ConfigureAwait(false))
          {
            if (sessionRevocation is not null)
                await authentication.LockSessionsCanonicalAsync(sessionRevocation.TargetSessionIds.Append(caller.SessionId), ct).ConfigureAwait(false);
            var session = await authentication.LockForOperationAsync(caller, operation.Scope.Operation, ct).ConfigureAwait(false);
            if (session is null) return await FinishAsync(transaction, new StepUpExecutionResult(false, "AUTHENTICATION_FAILED"), ct).ConfigureAwait(false);
            now = clock.GetUtcNow();
            var digest = AuthenticationIdentity.Digest(key);
            if (sessionRevocation is not null && await sessionRevocation.ReadReceiptAsync(digest, ct).ConfigureAwait(false) is not null)
            {
                var current = await authentication.VerifyContinuedOperationAsync(caller, operation, session, ct).ConfigureAwait(false);
                var at = clock.GetUtcNow(); var activePolicy = authenticationPolicies.RequireEffective(at); activePolicy.Validate();
                if (current is null || !current.SessionUseInterval.Contains(at) || !current.OperationInterval.Contains(at) ||
                    session.State != "ACTIVE" || AuthenticationApplication.Expired(session, activePolicy, at) ||
                    !authentication.MethodFactsCurrentAt(session, activePolicy, at))
                    return await FinishAsync(transaction, new StepUpExecutionResult(false, "AUTHENTICATION_FAILED"), ct).ConfigureAwait(false);
                return await FinishAsync(transaction, new StepUpExecutionResult(true, null), ct).ConfigureAwait(false);
            }
            var row = await database.Set<StepUpProofRow>().SingleOrDefaultAsync(x => x.KeyDigest == digest, ct).ConfigureAwait(false);
            if (row is null) return await FinishAsync(transaction, new StepUpExecutionResult(false, "STEP_UP_REQUIRED"), ct).ConfigureAwait(false);
            await database.Entry(row).ReloadAsync(ct).ConfigureAwait(false);
            if (!Matches(row, caller, operation)) return await FinishAsync(transaction, new StepUpExecutionResult(false, "STEP_UP_REQUIRED"), ct).ConfigureAwait(false);
            var policy = assurancePolicies.RequireEffective(operation.Scope, now, row.PolicyVersion);
            var proofCredential = await authentication.ReadProofCredentialAsync(row, ct, refresh: true).ConfigureAwait(false);
            now = clock.GetUtcNow(); policy = assurancePolicies.RequireEffective(operation.Scope, now, row.PolicyVersion);
            if (!Valid(row, session, policy, now) || !authentication.ProofMethodCurrentAt(row, proofCredential, operation, now, false)) return await FinishAsync(transaction, new StepUpExecutionResult(false, "STEP_UP_REQUIRED"), ct).ConfigureAwait(false);
            var validatedSessionVersion = session.Version;
            using var outcome = sessionRevocation is not null ? await sessionRevocation.ApplyAsync(session, ct).ConfigureAwait(false)
                : await applyCurrentOperation!(ct).ConfigureAwait(false);
            if (!outcome.Applied)
            {
                await transaction.RollbackAsync(ct).ConfigureAwait(false); database.ChangeTracker.Clear();
                return new(false, "VERSION_CONFLICT");
            }
            if (!outcome.ClosedFor(operation))
            {
                await transaction.RollbackAsync(ct).ConfigureAwait(false); database.ChangeTracker.Clear();
                return new(false, "STEP_UP_REQUIRED");
            }
            // Await every current-authority verification first, then decide once against their actual validity bounds.
            var verificationAt = clock.GetUtcNow(); var verificationPolicy = authenticationPolicies.RequireEffective(verificationAt); verificationPolicy.Validate();
            var termination = outcome.Receipt is { } receipt
                ? await authentication.VerifyTerminationAsync(caller, operation, receipt, validatedSessionVersion, verificationPolicy, verificationAt, ct).ConfigureAwait(false) : null;
            var methodCurrent = outcome.Receipt is not null || await authentication.MethodIsCurrentAsync(session, verificationPolicy, verificationAt, ct).ConfigureAwait(false);
            var continuation = outcome.Receipt is null ? await authentication.VerifyContinuedOperationAsync(caller, operation, session, ct).ConfigureAwait(false) : null;
            proofCredential = await authentication.ReadProofCredentialAsync(row, ct).ConfigureAwait(false);
            var decisionAt = clock.GetUtcNow(); policy = assurancePolicies.RequireEffective(operation.Scope, decisionAt, row.PolicyVersion);
            var currentAuthentication = authenticationPolicies.RequireEffective(decisionAt); currentAuthentication.Validate();
            var terminal = termination is not null && termination.AccessInterval.Contains(decisionAt);
            if (!Valid(row, session, policy, decisionAt) || AuthenticationApplication.Expired(session, currentAuthentication, decisionAt) ||
                sessionRevocation is not null && !sessionRevocation.TargetFactsCurrentAt(currentAuthentication, decisionAt) ||
                !authentication.MethodFactsCurrentAt(session, currentAuthentication, decisionAt) ||
                !authentication.ProofMethodCurrentAt(row, proofCredential, operation, decisionAt, outcome.PasskeyValidity is not null || outcome.TotpValidity is not null || outcome.FederationValidity is not null) || !authentication.PasskeyOutcomeCurrentAt(outcome, caller, operation, decisionAt) ||
                outcome.RecoveryValidity is { } recovery && !recovery.CurrentAt(database, caller, operation, recoveryPolicies, currentAuthentication, decisionAt) ||
                outcome.FederationValidity is { } federation && !federation.CurrentAt(database, caller, operation, decisionAt) ||
                (outcome.Receipt is not null ? !terminal : session.State != "ACTIVE" || !methodCurrent || continuation is null ||
                    !continuation.SessionUseInterval.Contains(decisionAt) || !continuation.OperationInterval.Contains(decisionAt)))
            {
                await transaction.RollbackAsync(ct).ConfigureAwait(false); database.ChangeTracker.Clear();
                return new(false, "STEP_UP_REQUIRED");
            }
            now = decisionAt;
            End(row, "CONSUMED", now); Event(row, "StepUpProofConsumed", now);
            await FinishAsync(transaction, true, ct).ConfigureAwait(false);
            pending = outcome.TakeRecoveryDelivery(); committedTerminal = terminal;
          }
          // The payload remains in the outer guard until successful COMMIT and transaction disposal.
          var result = StepUpExecutionResult.Committed(committedTerminal, pending); pending = null; return result;
        }
        catch { database.ChangeTracker.Clear(); throw; }
        finally { pending?.Dispose(); }
    }

    internal async Task<int> ExpireAsync(CancellationToken ct = default)
    {
        RequireCleanBoundary(); var now = clock.GetUtcNow();
        // Technical batch bound, not a policy lifetime or metadata-retention default.
        var ids = await database.Set<StepUpProofRow>().AsNoTracking().Where(x => x.State == "ACTIVE" && x.ExpiresAt <= now)
            .OrderBy(x => x.ExpiresAt).Take(100).Select(x => x.ProofId).ToArrayAsync(ct).ConfigureAwait(false);
        var ended = 0;
        foreach (var id in ids)
        {
            await using var transaction = await database.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
            var session = await database.Set<StepUpProofRow>().AsNoTracking().Where(x => x.ProofId == id).Select(x => x.SessionId).SingleAsync(ct).ConfigureAwait(false);
            await authentication.LockSessionAsync(session, ct).ConfigureAwait(false);
            var row = await database.Set<StepUpProofRow>().SingleAsync(x => x.ProofId == id, ct).ConfigureAwait(false);
            await database.Entry(row).ReloadAsync(ct).ConfigureAwait(false);
            if (row.State == "ACTIVE" && row.ExpiresAt <= now) { End(row, "EXPIRED", now); Event(row, "StepUpProofExpired", now); ended++; }
            await FinishAsync(transaction, true, ct).ConfigureAwait(false);
        }
        return ended;
    }
    private void RequireCleanBoundary()
    {
        if (database.Database.CurrentTransaction is not null || database.ChangeTracker.HasChanges())
            throw new InvalidOperationException("Step-up requires its own clean Identity transaction boundary.");
    }
    private static bool Matches(StepUpProofRow row, StepUpCaller caller, StepUpOperation operation) =>
        row.AccountId == caller.AccountId && row.SessionId == caller.SessionId && row.Origin == caller.Origin && row.Scheme == AuthenticationIdentity.Scheme &&
        row.OperationId == operation.OperationId && row.CorrelationId == operation.CorrelationId && row.Operation == operation.Scope.Operation && row.Context == operation.Scope.Context &&
        row.ResourceId == operation.ResourceId && row.ExpectedVersion == operation.ExpectedVersion && row.ContextDigest == operation.ContextDigest;
    private static bool Valid(StepUpProofRow row, AuthenticationSessionRow session, AssurancePolicyVersion policy, DateTimeOffset now) =>
        row.State == "ACTIVE" && row.ProtectedKey.Length != 0 && row.SecurityStampDigest == session.SecurityStampDigest &&
        now >= row.AuthenticatedAt && now < row.ExpiresAt && now - row.AuthenticatedAt < policy.ProofLifetime &&
        row.PolicyVersion == policy.Metadata.VersionId && row.Assurance == policy.RequiredAssurance && policy.RequireMethodAssurance(row.AuthenticationMethod) == row.Assurance;
    private static void End(StepUpProofRow row, string state, DateTimeOffset now)
    { row.State = state; row.EndedAt = now; row.ProtectedKey = string.Empty; row.Version++; }
    private async Task<T> FinishAsync<T>(IDbContextTransaction transaction, T result, CancellationToken ct)
    {
        await database.SaveChangesAsync(ct).ConfigureAwait(false); await transaction.CommitAsync(ct).ConfigureAwait(false); return result;
    }
    private void Event(StepUpProofRow row, string type, DateTimeOffset now) => database.Add(new OutboxMessage(Guid.NewGuid(), "1.0", type, "1", now,
        "tunner/identity", row.ProofId.ToString("N"), "RESTRICTED_SECURITY", JsonSerializer.Serialize(new { reference = row.ProofId.ToString("N") }),
        row.Version.ToString(CultureInfo.InvariantCulture), row.OperationId, row.CorrelationId));
}
