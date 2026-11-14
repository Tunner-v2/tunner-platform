using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Tunner.Database.Outbox;

namespace Tunner.Identity.Persistence;

internal sealed partial class AuthenticationApplication
{
    private DateTimeOffset RevocationNow => clock.GetUtcNow();
    private AuthenticationPolicy RevocationPolicy(DateTimeOffset at) => policies.RequireEffective(at);
    internal async Task<bool> AuthorizeSessionRevocationAsync(StepUpCaller caller, SessionRevocationRequest request, CancellationToken ct)
    {
        var result = await ReadRevocationAsync(caller, request, true, ct).ConfigureAwait(false);
        return result.ErrorCode is null;
    }

    internal Task<SessionRevocationResult> ReadSessionRevocationResultAsync(StepUpCaller caller, SessionRevocationRequest request, CancellationToken ct)
        => ReadRevocationAsync(caller, request, false, ct);

    private async Task<SessionRevocationResult> ReadRevocationAsync(StepUpCaller caller, SessionRevocationRequest request, bool prepare, CancellationToken ct)
    {
        var operation = SessionRevocationBinding.Operation(caller, request);
        if (database.Database.CurrentTransaction is not null || database.ChangeTracker.HasChanges())
            throw new InvalidOperationException("Session revocation reads require a clean Identity transaction boundary.");
        try
        {
            await using var tx = await database.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
            await LockSessionsCanonicalAsync([caller.SessionId, request.TargetSessionId], ct).ConfigureAwait(false);
            var actor = await LockForOperationAsync(caller, operation.Scope.Operation, ct).ConfigureAwait(false);
            if (actor is null)
            {
                await database.SaveChangesAsync(ct).ConfigureAwait(false); await tx.CommitAsync(ct).ConfigureAwait(false);
                return new("AUTHENTICATION_FAILED");
            }
            var execution = new SessionRevocationExecution(database, this, caller, request, operation);
            var receipt = await execution.ReadReceiptAsync(null, ct).ConfigureAwait(false);
            var available = receipt is not null || prepare && await execution.TargetCurrentAsync(actor, ct).ConfigureAwait(false);
            var actorPolicy = policies.RequireEffective(clock.GetUtcNow()); actorPolicy.Validate();
            var actorMethodCurrent = await MethodIsCurrentAsync(actor, actorPolicy, clock.GetUtcNow(), ct).ConfigureAwait(false);
            var current = await VerifyContinuedOperationAsync(caller, operation, actor, ct).ConfigureAwait(false);
            var at = clock.GetUtcNow(); var policy = policies.RequireEffective(at); policy.Validate();
            if (!available || !actorMethodCurrent || actorPolicy != policy || current is null || !current.SessionUseInterval.Contains(at) || !current.OperationInterval.Contains(at) ||
                actor.State != "ACTIVE" || Expired(actor, policy, at) || !MethodFactsCurrentAt(actor, policy, at) ||
                receipt is null && !execution.TargetFactsCurrentAt(policy, at))
            {
                await tx.RollbackAsync(ct).ConfigureAwait(false); database.ChangeTracker.Clear();
                return new("NOT_APPLIED_OR_NOT_DISCLOSABLE");
            }
            await database.SaveChangesAsync(ct).ConfigureAwait(false); await tx.CommitAsync(ct).ConfigureAwait(false);
            return new(null, receipt);
        }
        catch { database.ChangeTracker.Clear(); throw; }
    }

    internal sealed class SessionRevocationExecution(IdentityPersistenceDbContext db, AuthenticationApplication auth,
        StepUpCaller caller, SessionRevocationRequest request, StepUpOperation operation)
    {
        private AuthenticationSessionRow? target;
        private AuthenticationPolicy? targetPolicy;
        private bool methodCurrent;
        private bool applied;

        internal async Task<bool> TargetCurrentAsync(AuthenticationSessionRow actor, CancellationToken ct)
        {
            target = await db.Set<AuthenticationSessionRow>().FromSqlInterpolated(
                $"SELECT * FROM identity.tunner_sessions WHERE session_id={request.TargetSessionId} FOR UPDATE").SingleOrDefaultAsync(ct).ConfigureAwait(false);
            if (target is null) return false;
            await db.Entry(target).ReloadAsync(ct).ConfigureAwait(false);
            var at = auth.RevocationNow; targetPolicy = auth.RevocationPolicy(at); targetPolicy.Validate();
            if (target.SessionId == caller.SessionId || target.AccountId != caller.AccountId || target.Origin != caller.Origin ||
                target.Scheme != AuthenticationIdentity.Scheme || target.Version != request.ExpectedVersion || target.State != "ACTIVE" ||
                target.SecurityStampDigest != actor.SecurityStampDigest || Expired(target, targetPolicy, at)) return false;
            methodCurrent = await auth.MethodIsCurrentAsync(target, targetPolicy, at, ct).ConfigureAwait(false);
            at = auth.RevocationNow;
            return TargetFactsCurrentAt(auth.RevocationPolicy(at), at);
        }

        internal bool TargetFactsCurrentAt(AuthenticationPolicy policy, DateTimeOffset at) => target is not null &&
            targetPolicy == policy && methodCurrent && !Expired(target, policy, at) && auth.MethodFactsCurrentAt(target, policy, at) &&
            target.Version == request.ExpectedVersion + (applied ? 1 : 0) && target.State == (applied ? "REVOKED" : "ACTIVE") &&
            (!applied || target.OperationId == operation.OperationId && target.CorrelationId == operation.CorrelationId);

        internal async Task<UserStepUpOperationApplyOutcome> ApplyAsync(AuthenticationSessionRow actor, CancellationToken ct)
        {
            if (!await TargetCurrentAsync(actor, ct).ConfigureAwait(false)) return UserStepUpOperationApplyOutcome.Conflict();
            auth.End(target!, "REVOKED", "TunnerSessionRevoked", auth.RevocationNow, operation.OperationId, operation.CorrelationId);
            applied = true;
            return UserStepUpOperationApplyOutcome.SessionContinues();
        }

        internal async Task<SessionRevocationReceipt?> ReadReceiptAsync(string? keyDigest, CancellationToken ct)
        {
            var ended = await db.Set<AuthenticationSessionRow>().AsNoTracking().SingleOrDefaultAsync(x => x.SessionId == request.TargetSessionId, ct).ConfigureAwait(false);
            if (ended is null || ended.AccountId != caller.AccountId || ended.Origin != caller.Origin || ended.Scheme != AuthenticationIdentity.Scheme ||
                ended.State != "REVOKED" || ended.Version != request.ExpectedVersion + 1 || ended.OperationId != operation.OperationId ||
                ended.CorrelationId != operation.CorrelationId || ended.RevokedAt is not { } at || ended.ProtectedKey.Length != 0 || ended.ProtectedTicket.Length != 0) return null;
            var proof = await db.Set<StepUpProofRow>().AsNoTracking().SingleOrDefaultAsync(x => x.SessionId == caller.SessionId && x.OperationId == operation.OperationId, ct).ConfigureAwait(false);
            if (proof is null || proof.AccountId != caller.AccountId || proof.Origin != caller.Origin || proof.Scheme != AuthenticationIdentity.Scheme ||
                proof.Operation != operation.Scope.Operation || proof.Context != operation.Scope.Context || proof.ResourceId != operation.ResourceId ||
                proof.ExpectedVersion != operation.ExpectedVersion || proof.ContextDigest != operation.ContextDigest || proof.CorrelationId != operation.CorrelationId ||
                proof.State != "CONSUMED" || proof.Version != 2 || proof.ProtectedKey.Length != 0 || proof.EndedAt is not { } consumed ||
                consumed < at || consumed >= proof.ExpiresAt || at < proof.AuthenticatedAt || keyDigest is not null && proof.KeyDigest != keyDigest) return null;
            var targetVersion = ended.Version.ToString(CultureInfo.InvariantCulture); var proofVersion = proof.Version.ToString(CultureInfo.InvariantCulture);
            var targetSubject = ended.SessionId.ToString("N"); var proofSubject = proof.ProofId.ToString("N");
            var events = await db.Set<OutboxMessage>().AsNoTracking().Where(x => x.Source == "tunner/identity" &&
                x.OperationId == operation.OperationId && x.CorrelationId == operation.CorrelationId &&
                (x.EventType == "TunnerSessionRevoked" && x.Subject == targetSubject && x.ResourceVersion == targetVersion && x.OccurredAtUtc == at ||
                 x.EventType == "StepUpProofConsumed" && x.Subject == proofSubject && x.ResourceVersion == proofVersion && x.OccurredAtUtc == consumed))
                .ToArrayAsync(ct).ConfigureAwait(false);
            if (events.Count(x => x.EventType == "TunnerSessionRevoked") != 1 || events.Count(x => x.EventType == "StepUpProofConsumed") != 1) return null;
            return new(ended.SessionId, request.ExpectedVersion, ended.Version, operation.OperationId, operation.CorrelationId, at);
        }
    }
}
