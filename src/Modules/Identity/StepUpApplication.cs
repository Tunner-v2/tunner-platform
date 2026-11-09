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
    AuthenticationPasswordWork passwordWork, IDataProtectionProvider protection, TimeProvider clock)
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
            // Re-read time, policy and live authority after credential work, without extending a prior proof.
            session = await authentication.LockForOperationAsync(caller, operation.Scope.Operation, ct).ConfigureAwait(false);
            now = clock.GetUtcNow(); policy = assurancePolicies.RequireEffective(operation.Scope, now, expectedPolicyVersion);
            authPolicy = authenticationPolicies.RequireEffective(now); authPolicy.Validate();
            if (session is null) return await FinishAsync(transaction, new StepUpIssueResult("AUTHENTICATION_FAILED", null), ct).ConfigureAwait(false);
            if (policy.RequireMethodAssurance("pwd") != policy.RequiredAssurance) return await FinishAsync(transaction, new StepUpIssueResult("STEP_UP_REQUIRED", null), ct).ConfigureAwait(false);
            var prior = await database.Set<StepUpProofRow>().SingleOrDefaultAsync(x => x.SessionId == caller.SessionId && x.OperationId == operation.OperationId, ct).ConfigureAwait(false);
            if (prior is not null)
            {
                await database.Entry(prior).ReloadAsync(ct).ConfigureAwait(false);
                if (!Matches(prior, caller, operation) || !Valid(prior, session, policy, now))
                    return await FinishAsync(transaction, new StepUpIssueResult("VERSION_CONFLICT", null), ct).ConfigureAwait(false);
                return await FinishAsync(transaction, new StepUpIssueResult(null, new(retryProtection.Unprotect(prior.ProtectedKey), prior.ExpiresAt)), ct).ConfigureAwait(false);
            }
            var expires = now + policy.ProofLifetime;
            if (session.ExpiresAt < expires) expires = session.ExpiresAt;
            if (policy.Metadata.EffectiveInterval.Until is { } until && until < expires) expires = until;
            if (expires <= now) throw new InvalidOperationException("No current step-up validity interval is available.");
            var key = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            var row = new StepUpProofRow
            {
                ProofId = Guid.NewGuid(), SessionId = caller.SessionId, AccountId = caller.AccountId, Origin = caller.Origin,
                Scheme = session.Scheme, SecurityStampDigest = session.SecurityStampDigest, KeyDigest = AuthenticationIdentity.Digest(key),
                ProtectedKey = retryProtection.Protect(key), OperationId = operation.OperationId, CorrelationId = operation.CorrelationId,
                Operation = operation.Scope.Operation, Context = operation.Scope.Context, ResourceId = operation.ResourceId,
                ExpectedVersion = operation.ExpectedVersion, ContextDigest = operation.ContextDigest, PolicyVersion = policy.Metadata.VersionId,
                AuthenticationPolicyVersion = authPolicy.Version, AuthenticationMethod = "pwd", Assurance = policy.RequiredAssurance,
                ProcessingReference = authPolicy.ProcessingReference, RetentionReference = authPolicy.RetentionReference,
                AuthenticatedAt = now, ExpiresAt = expires
            };
            database.Add(row); Event(row, "StepUpProofIssued", now);
            return await FinishAsync(transaction, new StepUpIssueResult(null, new(key, expires)), ct).ConfigureAwait(false);
        }
        catch { database.ChangeTracker.Clear(); throw; }
    }

    // The callback is an Identity-owner local mutation using this same context/transaction, never provider I/O.
    internal async Task<StepUpExecutionResult> ExecuteAsync(StepUpCaller caller, StepUpOperation operation, string key,
        Func<CancellationToken, Task<bool>> applyCurrentOperation, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller); ArgumentNullException.ThrowIfNull(operation); ArgumentNullException.ThrowIfNull(applyCurrentOperation);
        RequireCleanBoundary();
        if (key is not { Length: 64 } || !key.All(Uri.IsHexDigit)) return new(false, "STEP_UP_REQUIRED");
        var now = clock.GetUtcNow();
        await using var transaction = await database.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        try
        {
            var session = await authentication.LockForOperationAsync(caller, operation.Scope.Operation, ct).ConfigureAwait(false);
            if (session is null) return await FinishAsync(transaction, new StepUpExecutionResult(false, "AUTHENTICATION_FAILED"), ct).ConfigureAwait(false);
            now = clock.GetUtcNow();
            var digest = AuthenticationIdentity.Digest(key);
            var row = await database.Set<StepUpProofRow>().SingleOrDefaultAsync(x => x.KeyDigest == digest, ct).ConfigureAwait(false);
            if (row is null) return await FinishAsync(transaction, new StepUpExecutionResult(false, "STEP_UP_REQUIRED"), ct).ConfigureAwait(false);
            await database.Entry(row).ReloadAsync(ct).ConfigureAwait(false);
            if (!Matches(row, caller, operation)) return await FinishAsync(transaction, new StepUpExecutionResult(false, "STEP_UP_REQUIRED"), ct).ConfigureAwait(false);
            var policy = assurancePolicies.RequireEffective(operation.Scope, now, row.PolicyVersion);
            if (!Valid(row, session, policy, now)) return await FinishAsync(transaction, new StepUpExecutionResult(false, "STEP_UP_REQUIRED"), ct).ConfigureAwait(false);
            if (!await applyCurrentOperation(ct).ConfigureAwait(false))
            {
                await transaction.RollbackAsync(ct).ConfigureAwait(false); database.ChangeTracker.Clear();
                return new(false, "VERSION_CONFLICT");
            }
            // A slow mutation/lock wait may cross an expiry or policy boundary: roll the entire operation back.
            now = clock.GetUtcNow(); policy = assurancePolicies.RequireEffective(operation.Scope, now, row.PolicyVersion);
            var currentAuthentication = authenticationPolicies.RequireEffective(now); currentAuthentication.Validate();
            if (!Valid(row, session, policy, now) || AuthenticationApplication.Expired(session, currentAuthentication, now) || session.Assurance != currentAuthentication.PasswordAssurance)
            {
                await transaction.RollbackAsync(ct).ConfigureAwait(false); database.ChangeTracker.Clear();
                return new(false, "STEP_UP_REQUIRED");
            }
            End(row, "CONSUMED", now); Event(row, "StepUpProofConsumed", now);
            return await FinishAsync(transaction, new StepUpExecutionResult(true, null), ct).ConfigureAwait(false);
        }
        catch { database.ChangeTracker.Clear(); throw; }
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
