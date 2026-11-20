using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Tunner.Database.Outbox;

namespace Tunner.Identity.Persistence;

internal sealed class DeviceOperationApplication(DeviceApplication devices, StepUpApplication stepUp)
{
    internal Task<UserStepUpOperationContext?> PrepareAsync(StepUpCaller caller, DeviceTrustRemovalRequest request,
        IAssurancePolicySource assurance, CancellationToken ct = default) => devices.PrepareRemovalAsync(caller, request, assurance, ct);
    internal Task<DeviceTrustRemovalResult> ReadResultAsync(StepUpCaller caller, DeviceTrustRemovalRequest request, CancellationToken ct = default)
        => devices.ReadRemovalResultAsync(caller, request, null, ct);
    internal async Task<DeviceTrustRemovalResult> RevokeAsync(StepUpCaller caller, DeviceTrustRemovalRequest request, string key, CancellationToken ct = default)
    {
        if (key is not { Length: 64 } || !key.All(Uri.IsHexDigit)) return new("STEP_UP_REQUIRED");
        var original = await devices.ReadRemovalResultAsync(caller, request, AuthenticationIdentity.Digest(key), ct).ConfigureAwait(false);
        if (original.Applied) return original;
        var context = await devices.ResolveRemovalAsync(caller, request, ct).ConfigureAwait(false);
        if (context is null || context.Completed) return new("NOT_APPLIED_OR_NOT_DISCLOSABLE");
        using var execution = await stepUp.ExecuteOutcomeAsync(caller, context.Binding, key,
            token => devices.ApplyRemovalAsync(caller, context, token), ct).ConfigureAwait(false);
        if (!execution.Applied)
        {
            // A concurrent exact winner may already have consumed this proof. Only its causal owner result reconciles.
            var raced = await devices.ReadRemovalResultAsync(caller, request, AuthenticationIdentity.Digest(key), ct).ConfigureAwait(false);
            return raced.Applied ? raced : new(execution.ErrorCode);
        }
        return await devices.ReadRemovalResultAsync(caller, request, AuthenticationIdentity.Digest(key), ct).ConfigureAwait(false);
    }
}

internal sealed partial class DeviceApplication
{
    private static bool Actor(DeviceOperationRow row, StepUpCaller caller) => row.AccountId == caller.AccountId &&
        row.SessionId == caller.SessionId && row.Origin == caller.Origin;
    private static bool Request(DeviceOperationRow row, DeviceTrustRemovalRequest request) => row.RequestId == request.RequestId &&
        row.DeviceId == request.DeviceId && row.ExpectedDeviceVersion == request.ExpectedDeviceVersion &&
        row.ExpectedTrustVersion == request.ExpectedTrustVersion && row.ExpectedAccountVersion == request.ExpectedAccountVersion &&
        row.CorrelationId == request.CorrelationId;
    private static string Context(DeviceOperationRow row) => AuthenticationIdentity.Digest(JsonSerializer.Serialize(new {
        row.Id, row.RequestId, row.AccountId, row.SessionId, row.Origin, row.DeviceId, row.ExpectedDeviceVersion,
        row.ExpectedTrustVersion, row.ExpectedAccountVersion, row.SecurityStampDigest, row.PolicyVersion, row.PolicySnapshot,
        row.AssurancePolicyVersion, row.CorrelationId, row.CreatedAt, row.ExpiresAt, row.RecoveryExpiresAt }));
    private static StepUpOperation Operation(DeviceOperationRow row) => new("OP-DEVICE-TRUST-REVOKE-" + row.Id.ToString("N"),
        row.CorrelationId, RevokeScope, row.DeviceId.ToString("N"), row.ExpectedTrustVersion, row.ContextDigest);
    private static bool SameOperation(StepUpOperation a, StepUpOperation b) => a.OperationId == b.OperationId &&
        a.CorrelationId == b.CorrelationId && a.Scope == b.Scope && a.ResourceId == b.ResourceId &&
        a.ExpectedVersion == b.ExpectedVersion && a.ContextDigest == b.ContextDigest;
    private static UserStepUpOperationContext OperationContext(DeviceOperationRow row, StepUpCaller caller)
    {
        var op = Operation(row);
        return new(new(caller), op.OperationId, op.CorrelationId, op.Scope, op.ResourceId, op.ExpectedVersion,
            op.ContextDigest, row.AssurancePolicyVersion, row.State == "COMPLETED");
    }
    private async Task<bool> CurrentActor(StepUpCaller caller, AuthenticationSessionRow session, StepUpOperation operation,
        DeviceEvidencePolicy originalPolicy, CancellationToken ct)
    {
        var auth = authenticationPolicies.RequireEffective(clock.GetUtcNow()); auth.Validate();
        var method = await authentication.MethodIsCurrentAsync(session, auth, clock.GetUtcNow(), ct).ConfigureAwait(false);
        var authorization = await authentication.VerifyContinuedOperationAsync(caller, operation, session, ct).ConfigureAwait(false);
        var at = clock.GetUtcNow(); var finalAuth = authenticationPolicies.RequireEffective(at); finalAuth.Validate();
        return auth == finalAuth && originalPolicy.Snapshot == Policy(caller.Origin, at).Snapshot && method && session.State == "ACTIVE" &&
            !AuthenticationApplication.Expired(session, finalAuth, at) && authentication.MethodFactsCurrentAt(session, finalAuth, at) &&
            authorization is not null && authorization.SessionUseInterval.Contains(at) && authorization.OperationInterval.Contains(at);
    }

    internal async Task<UserStepUpOperationContext?> PrepareRemovalAsync(StepUpCaller caller, DeviceTrustRemovalRequest request,
        IAssurancePolicySource assurance, CancellationToken ct)
    {
        request.Validate(); Clean(); var policy = Policy(caller.Origin, clock.GetUtcNow());
        try
        {
            await using var tx = await database.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
            var session = await authentication.LockForOperationAsync(caller, RevokeScope.Operation, ct).ConfigureAwait(false);
            if (session is null) { await database.SaveChangesAsync(ct).ConfigureAwait(false); await tx.CommitAsync(ct).ConfigureAwait(false); return null; }
            var prior = await database.Set<DeviceOperationRow>().SingleOrDefaultAsync(x => x.SessionId == caller.SessionId && x.RequestId == request.RequestId, ct).ConfigureAwait(false);
            if (prior is not null)
            {
                await database.Entry(prior).ReloadAsync(ct).ConfigureAwait(false);
                var current = Actor(prior, caller) && Request(prior, request) && prior.ContextDigest == Context(prior) &&
                    clock.GetUtcNow() < prior.RecoveryExpiresAt && (prior.State == "COMPLETED" ||
                    clock.GetUtcNow() < prior.ExpiresAt && prior.PolicySnapshot == policy.Snapshot);
                current = current && await CurrentActor(caller, session, Operation(prior), policy, ct).ConfigureAwait(false);
                await database.SaveChangesAsync(ct).ConfigureAwait(false); await tx.CommitAsync(ct).ConfigureAwait(false);
                return current ? OperationContext(prior, caller) : null;
            }
            var account = await database.Accounts.SingleAsync(x => x.AccountId == caller.AccountId, ct).ConfigureAwait(false);
            await LockDevice(request.DeviceId, ct).ConfigureAwait(false);
            var target = await database.Set<DeviceRecordRow>().AsNoTracking().SingleOrDefaultAsync(x => x.DeviceId == request.DeviceId, ct).ConfigureAwait(false);
            var trust = await LatestTrust(request.DeviceId, ct).ConfigureAwait(false);
            if (target is null || target.AccountId != caller.AccountId || target.Origin != caller.Origin ||
                target.Version != request.ExpectedDeviceVersion || trust is null || trust.Version != request.ExpectedTrustVersion ||
                trust.State == "REVOKED" || account.Version != request.ExpectedAccountVersion ||
                await database.Set<DeviceOperationRow>().CountAsync(x => x.AccountId == caller.AccountId && x.State == "PENDING" && x.ExpiresAt > clock.GetUtcNow(), ct).ConfigureAwait(false) >= policy.MaximumPendingOperations)
            { await tx.RollbackAsync(ct).ConfigureAwait(false); database.ChangeTracker.Clear(); return null; }
            var at = clock.GetUtcNow(); policy = Policy(caller.Origin, at);
            var created = IdentityPersistenceTime.Normalize(at);
            var expires = at + policy.OperationLifetime;
            if (session.ExpiresAt < expires) expires = session.ExpiresAt;
            if (policy.Metadata.EffectiveInterval.Until is { } until && until < expires) expires = until;
            if (expires <= at) return null;
            var row = new DeviceOperationRow { Id = Guid.NewGuid(), RequestId = request.RequestId, AccountId = caller.AccountId,
                SessionId = caller.SessionId, Origin = caller.Origin, DeviceId = request.DeviceId, ExpectedDeviceVersion = target.Version,
                ExpectedTrustVersion = trust.Version, ExpectedAccountVersion = account.Version, SecurityStampDigest = session.SecurityStampDigest,
                PolicyVersion = policy.Metadata.VersionId, PolicySnapshot = policy.Snapshot, AssurancePolicyVersion = policy.RevokeAssurancePolicyVersion,
                CorrelationId = request.CorrelationId, CreatedAt = created, ExpiresAt = IdentityPersistenceTime.Normalize(expires),
                RecoveryExpiresAt = IdentityPersistenceTime.Normalize(expires + policy.ResultLifetime) };
            row.ContextDigest = Context(row);
            _ = assurance.RequireEffective(RevokeScope, at, row.AssurancePolicyVersion);
            if (!await CurrentActor(caller, session, Operation(row), policy, ct).ConfigureAwait(false) || clock.GetUtcNow() >= expires)
            { await tx.RollbackAsync(ct).ConfigureAwait(false); database.ChangeTracker.Clear(); return null; }
            _ = assurance.RequireEffective(RevokeScope, clock.GetUtcNow(), row.AssurancePolicyVersion);
            database.Add(row); await database.SaveChangesAsync(ct).ConfigureAwait(false); await tx.CommitAsync(ct).ConfigureAwait(false);
            return OperationContext(row, caller);
        }
        catch { database.ChangeTracker.Clear(); throw; }
    }

    internal async Task<UserStepUpOperationContext?> ResolveRemovalAsync(StepUpCaller caller, DeviceTrustRemovalRequest request, CancellationToken ct)
    {
        request.Validate(); Clean();
        var row = await database.Set<DeviceOperationRow>().AsNoTracking().SingleOrDefaultAsync(x => x.SessionId == caller.SessionId && x.RequestId == request.RequestId, ct).ConfigureAwait(false);
        if (row is null || !Actor(row, caller) || !Request(row, request) || row.ContextDigest != Context(row) ||
            row.State != "PENDING" || clock.GetUtcNow() >= row.ExpiresAt || row.PolicySnapshot != Policy(caller.Origin, clock.GetUtcNow()).Snapshot) return null;
        return OperationContext(row, caller);
    }

    internal async Task<UserStepUpOperationApplyOutcome> ApplyRemovalAsync(StepUpCaller caller, UserStepUpOperationContext context, CancellationToken ct)
    {
        if (database.Database.CurrentTransaction is null || context.Binding.Scope != RevokeScope) return UserStepUpOperationApplyOutcome.Conflict();
        var row = await database.Set<DeviceOperationRow>().SingleOrDefaultAsync(x => x.SessionId == caller.SessionId &&
            x.ContextDigest == context.Binding.ContextDigest, ct).ConfigureAwait(false);
        if (row is null) return UserStepUpOperationApplyOutcome.Conflict(); await database.Entry(row).ReloadAsync(ct).ConfigureAwait(false);
        await LockDevice(row.DeviceId, ct).ConfigureAwait(false);
        var target = await database.Set<DeviceRecordRow>().SingleOrDefaultAsync(x => x.DeviceId == row.DeviceId, ct).ConfigureAwait(false);
        if (target is not null) await database.Entry(target).ReloadAsync(ct).ConfigureAwait(false);
        var prior = await LatestTrust(row.DeviceId, ct).ConfigureAwait(false);
        var account = await database.Accounts.SingleAsync(x => x.AccountId == caller.AccountId, ct).ConfigureAwait(false);
        var session = await database.Set<AuthenticationSessionRow>().SingleAsync(x => x.SessionId == caller.SessionId, ct).ConfigureAwait(false);
        var proof = await database.Set<StepUpProofRow>().SingleOrDefaultAsync(x => x.SessionId == caller.SessionId && x.OperationId == context.Binding.OperationId, ct).ConfigureAwait(false);
        var at = clock.GetUtcNow(); var policy = Policy(caller.Origin, at);
        if (!Actor(row, caller) || row.ContextDigest != Context(row) || !SameOperation(Operation(row), context.Binding) || row.State != "PENDING" ||
            row.AssurancePolicyVersion != context.ExpectedPolicyVersion || row.PolicySnapshot != policy.Snapshot || at < row.CreatedAt || at >= row.ExpiresAt ||
            account.Version != row.ExpectedAccountVersion || session.SecurityStampDigest != row.SecurityStampDigest ||
            target is null || target.AccountId != caller.AccountId || target.Origin != caller.Origin || target.Version != row.ExpectedDeviceVersion ||
            prior is null || prior.Version != row.ExpectedTrustVersion || prior.State == "REVOKED" || proof is null ||
            proof.State != "ACTIVE" || proof.ContextDigest != row.ContextDigest || proof.PolicyVersion != row.AssurancePolicyVersion)
            return UserStepUpOperationApplyOutcome.Conflict();
        var eventId = Guid.NewGuid(); var recordedAt = IdentityPersistenceTime.Normalize(at);
        database.Add(new DeviceTrustRow { DeviceId = target.DeviceId, Version = prior.Version + 1, AccountId = target.AccountId,
            Origin = target.Origin, DeviceVersion = target.Version, State = "REVOKED", SourceSessionId = prior.SourceSessionId,
            RemovalOperationId = row.Id, PolicyVersion = policy.Metadata.VersionId, PolicySnapshot = policy.Snapshot,
            RecordedAt = recordedAt, OperationId = context.Binding.OperationId, CorrelationId = row.CorrelationId, OutboxEventId = eventId });
        row.State = "COMPLETED"; row.Version++; row.ProofId = proof.ProofId; row.ResultTrustVersion = prior.Version + 1;
        row.CompletedAt = recordedAt; row.OutboxEventId = eventId;
        Event(eventId, "DeviceTrustRevoked", row.DeviceId, row.ResultTrustVersion.Value, context.Binding.OperationId,
            row.CorrelationId, policy.Classification, recordedAt);
        return UserStepUpOperationApplyOutcome.SessionContinues().ForDevice(new(this, row.Id, database.Database.CurrentTransaction.TransactionId));
    }

    internal bool RemovalCurrentAt(IdentityPersistenceDbContext db, StepUpCaller caller, StepUpOperation operation,
        Guid id, Guid transactionId, DateTimeOffset at)
    {
        if (!ReferenceEquals(db, database) || database.Database.CurrentTransaction?.TransactionId != transactionId || operation.Scope != RevokeScope) return false;
        var row = database.ChangeTracker.Entries<DeviceOperationRow>().Select(x => x.Entity).SingleOrDefault(x => x.Id == id);
        if (row is null || !Actor(row, caller) || row.State != "COMPLETED" || row.Version != 2 || !SameOperation(Operation(row), operation) ||
            row.ContextDigest != Context(row) || at < row.CreatedAt || at >= row.ExpiresAt || row.PolicySnapshot != Policy(caller.Origin, at).Snapshot) return false;
        var target = database.ChangeTracker.Entries<DeviceRecordRow>().Select(x => x.Entity).SingleOrDefault(x => x.DeviceId == row.DeviceId);
        var trust = database.ChangeTracker.Entries<DeviceTrustRow>().Select(x => x.Entity).SingleOrDefault(x => x.DeviceId == row.DeviceId && x.Version == row.ResultTrustVersion);
        var account = database.ChangeTracker.Entries<AccountRow>().Select(x => x.Entity).SingleOrDefault(x => x.AccountId == caller.AccountId);
        return account?.Version == row.ExpectedAccountVersion && target?.Version == row.ExpectedDeviceVersion &&
            target.AccountId == caller.AccountId && target.Origin == caller.Origin && trust is not null && trust.State == "REVOKED" &&
            trust.RemovalOperationId == row.Id && trust.OperationId == operation.OperationId && trust.CorrelationId == operation.CorrelationId &&
            trust.OutboxEventId == row.OutboxEventId && trust.RecordedAt == row.CompletedAt;
    }

    internal Task<DeviceTrustRemovalResult> ReadRemovalResultAsync(StepUpCaller caller, DeviceTrustRemovalRequest request, string? keyDigest, CancellationToken ct)
        => ReadRemovalResultCoreAsync(caller, request, keyDigest, false, ct);
    private async Task<DeviceTrustRemovalResult> ReadRemovalResultCoreAsync(StepUpCaller caller, DeviceTrustRemovalRequest request, string? keyDigest,
        bool joinTransaction, CancellationToken ct)
    {
        request.Validate();
        if (joinTransaction)
        { if (database.Database.CurrentTransaction is null) throw new InvalidOperationException("An existing Identity owner transaction is required."); }
        else Clean();
        var policy = Policy(caller.Origin, clock.GetUtcNow());
        try
        {
            await using var tx = joinTransaction ? null : await database.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
            var session = await authentication.LockForOperationAsync(caller, RevokeScope.Operation, ct).ConfigureAwait(false);
            if (session is null)
            {
                if (tx is not null) { await database.SaveChangesAsync(ct).ConfigureAwait(false); await tx.CommitAsync(ct).ConfigureAwait(false); }
                return new("AUTHENTICATION_FAILED");
            }
            var row = await database.Set<DeviceOperationRow>().AsNoTracking().SingleOrDefaultAsync(x => x.SessionId == caller.SessionId && x.RequestId == request.RequestId, ct).ConfigureAwait(false);
            DeviceTrustRemovalReceipt? receipt = null;
            if (row is not null && Actor(row, caller) && Request(row, request) && row.State == "COMPLETED" && row.Version == 2 &&
                row.ContextDigest == Context(row) && clock.GetUtcNow() < row.RecoveryExpiresAt && row.CompletedAt is { } completed &&
                row.ResultTrustVersion == row.ExpectedTrustVersion + 1 && row.ProofId is { } proofId)
            {
                var trust = await database.Set<DeviceTrustRow>().AsNoTracking().SingleOrDefaultAsync(x => x.DeviceId == row.DeviceId && x.Version == row.ResultTrustVersion, ct).ConfigureAwait(false);
                var proof = await database.Set<StepUpProofRow>().AsNoTracking().SingleOrDefaultAsync(x => x.ProofId == proofId, ct).ConfigureAwait(false);
                var operation = Operation(row);
                if (trust is not null && trust.AccountId == caller.AccountId && trust.Origin == caller.Origin && trust.DeviceVersion == row.ExpectedDeviceVersion &&
                    trust.State == "REVOKED" && trust.RemovalOperationId == row.Id && trust.RecordedAt == completed && trust.OutboxEventId == row.OutboxEventId &&
                    trust.OperationId == operation.OperationId && trust.CorrelationId == operation.CorrelationId && proof is not null &&
                    proof.AccountId == caller.AccountId && proof.SessionId == caller.SessionId && proof.Origin == caller.Origin && proof.Scheme == AuthenticationIdentity.Scheme &&
                    proof.OperationId == operation.OperationId && proof.CorrelationId == operation.CorrelationId && proof.Operation == RevokeScope.Operation &&
                    proof.Context == RevokeScope.Context && proof.ResourceId == operation.ResourceId && proof.ExpectedVersion == operation.ExpectedVersion &&
                    proof.ContextDigest == row.ContextDigest && proof.PolicyVersion == row.AssurancePolicyVersion && proof.State == "CONSUMED" && proof.Version == 2 &&
                    proof.ProtectedKey.Length == 0 && proof.EndedAt is { } consumed && completed >= proof.AuthenticatedAt && consumed >= completed && consumed < proof.ExpiresAt &&
                    (keyDigest is null || proof.KeyDigest == keyDigest))
                {
                    var events = await database.Set<OutboxMessage>().AsNoTracking().Where(x => x.Source == "tunner/identity" &&
                        x.OperationId == operation.OperationId && x.CorrelationId == operation.CorrelationId &&
                        (x.EventType == "DeviceTrustRevoked" || x.EventType == "StepUpProofConsumed")).ToArrayAsync(ct).ConfigureAwait(false);
                    if (events.Count(x => x.EventId == row.OutboxEventId && x.EventType == "DeviceTrustRevoked" && x.Subject == row.DeviceId.ToString("N") &&
                        x.ResourceVersion == trust.Version.ToString(CultureInfo.InvariantCulture) && x.OccurredAtUtc == completed) == 1 &&
                        events.Count(x => x.EventType == "DeviceTrustRevoked") == 1 &&
                        events.Count(x => x.EventType == "StepUpProofConsumed" && x.Subject == proof.ProofId.ToString("N") &&
                            x.ResourceVersion == proof.Version.ToString(CultureInfo.InvariantCulture) && x.OccurredAtUtc == consumed) == 1)
                        receipt = new(row.DeviceId, row.ExpectedDeviceVersion, row.ExpectedTrustVersion, trust.Version,
                            operation.OperationId, operation.CorrelationId, completed);
                }
                if (!await CurrentActor(caller, session, operation, policy, ct).ConfigureAwait(false) || clock.GetUtcNow() >= row.RecoveryExpiresAt) receipt = null;
            }
            if (tx is not null) { await database.SaveChangesAsync(ct).ConfigureAwait(false); await tx.CommitAsync(ct).ConfigureAwait(false); }
            return receipt is null ? new("NOT_APPLIED_OR_NOT_DISCLOSABLE") : new(null, receipt);
        }
        catch { if (!joinTransaction) database.ChangeTracker.Clear(); throw; }
    }

    /// <summary>Resolves the same immutable browser original both outside and inside the shared/native proof transaction.</summary>
    internal async Task<(UserStepUpOperationContext? Context, DeviceOperationRow? Original)> ResolveBrowserRemovalAsync(
        StepUpCaller caller, DeviceTrustRemovalRequest request, CancellationToken ct)
    {
        request.Validate(); var owns = database.Database.CurrentTransaction is null;
        if (owns) Clean();
        try
        {
            await using var tx = owns ? await database.Database.BeginTransactionAsync(ct).ConfigureAwait(false) : null;
            var actor = await authentication.LockForOperationAsync(caller, RevokeScope.Operation, ct).ConfigureAwait(false);
            if (actor is null)
            {
                if (tx is not null) { await database.SaveChangesAsync(ct).ConfigureAwait(false); await tx.CommitAsync(ct).ConfigureAwait(false); }
                return (null, null);
            }
            var row = await database.Set<DeviceOperationRow>().AsNoTracking().SingleOrDefaultAsync(x => x.SessionId == caller.SessionId && x.RequestId == request.RequestId, ct).ConfigureAwait(false);
            if (row is null || !Actor(row, caller) || !Request(row, request) || row.ContextDigest != Context(row))
            {
                if (tx is not null) { await database.SaveChangesAsync(ct).ConfigureAwait(false); await tx.CommitAsync(ct).ConfigureAwait(false); }
                return (null, null);
            }
            var at = clock.GetUtcNow(); var policy = Policy(caller.Origin, at);
            var current = row.State == "COMPLETED"
                ? (await ReadRemovalResultCoreAsync(caller, request, null, true, ct).ConfigureAwait(false)).Applied
                : row.State == "PENDING" && row.CreatedAt <= at && at < row.ExpiresAt && row.PolicySnapshot == policy.Snapshot &&
                    row.SecurityStampDigest == actor.SecurityStampDigest && row.AssurancePolicyVersion == policy.RevokeAssurancePolicyVersion;
            if (current && row.State == "PENDING")
            {
                await LockDevice(row.DeviceId, ct).ConfigureAwait(false);
                var target = await database.Set<DeviceRecordRow>().AsNoTracking().SingleOrDefaultAsync(x => x.DeviceId == row.DeviceId, ct).ConfigureAwait(false);
                var trust = await LatestTrust(row.DeviceId, ct).ConfigureAwait(false);
                var account = await database.Accounts.SingleAsync(x => x.AccountId == caller.AccountId, ct).ConfigureAwait(false);
                current = target is not null && target.AccountId == caller.AccountId && target.Origin == caller.Origin &&
                    target.Version == row.ExpectedDeviceVersion && trust?.Version == row.ExpectedTrustVersion && trust.State != "REVOKED" &&
                    account.Version == row.ExpectedAccountVersion;
            }
            current = current && await CurrentActor(caller, actor, Operation(row), policy, ct).ConfigureAwait(false) &&
                clock.GetUtcNow() < (row.State == "COMPLETED" ? row.RecoveryExpiresAt : row.ExpiresAt);
            if (tx is not null) { await database.SaveChangesAsync(ct).ConfigureAwait(false); await tx.CommitAsync(ct).ConfigureAwait(false); }
            return (current ? OperationContext(row, caller) : null, row);
        }
        catch { if (owns) database.ChangeTracker.Clear(); throw; }
    }
}
