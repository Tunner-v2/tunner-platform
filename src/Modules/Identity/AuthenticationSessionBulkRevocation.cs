using Microsoft.EntityFrameworkCore;

namespace Tunner.Identity.Persistence;

internal sealed partial class AuthenticationApplication
{
    internal async Task<SessionBulkRevocationPreparation> PrepareSessionBulkRevocationAsync(StepUpCaller caller, Guid requestId,
        string correlationId, ISessionBulkRevocationDiscoverySource discovery, IAssurancePolicySource assurance, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(caller); ArgumentException.ThrowIfNullOrWhiteSpace(correlationId);
        if (requestId == Guid.Empty) throw new ArgumentException("An original request is required.", nameof(requestId));
        RequireBulkBoundary();
        try
        {
            var at = clock.GetUtcNow(); var budget = SessionBulkRevocationDiscoveryBudget.Current(discovery, at);
            var initial = policies.RequireEffective(at); initial.Validate();
            var proofPolicy = assurance.RequireEffective(SessionBulkRevocationApplication.Scope, at);
            await using var tx = await database.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
            // One server query determines the complete original candidate set. Overflow is not a page.
            var original = await database.Set<AuthenticationSessionRow>().AsNoTracking().Where(x => x.AccountId == caller.AccountId &&
                x.Origin == caller.Origin && x.Scheme == AuthenticationIdentity.Scheme && x.SessionId != caller.SessionId && x.State == "ACTIVE")
                .OrderBy(x => x.SessionId).Take(budget.MaximumCandidates + 1)
                .Select(x => new SessionBulkRevocationTarget(x.SessionId, x.Version)).ToArrayAsync(ct).ConfigureAwait(false);
            await LockSessionsCanonicalAsync(original.Select(x => x.SessionReference).Append(caller.SessionId), ct).ConfigureAwait(false);
            var actor = await LockForOperationAsync(caller, SessionBulkRevocationApplication.Scope.Operation, ct).ConfigureAwait(false);
            if (actor is null)
            {
                // Preserve the ordinary validator's expiry/invalid-method terminalization.
                await database.SaveChangesAsync(ct).ConfigureAwait(false); await tx.CommitAsync(ct).ConfigureAwait(false);
                return new("AUTHENTICATION_FAILED");
            }
            if (original.Length > budget.MaximumCandidates) return await RejectBulkPreparation(tx, "DISCOVERY_CAPACITY_EXCEEDED", ct).ConfigureAwait(false);
            var eligible = new List<SessionBulkRevocationTarget>(); var checks = new List<SessionRevocationExecution>();
            foreach (var candidate in original)
            {
                var current = await database.Set<AuthenticationSessionRow>().AsNoTracking().SingleOrDefaultAsync(x => x.SessionId == candidate.SessionReference, ct).ConfigureAwait(false);
                if (current is null || current.Version != candidate.ExpectedVersion) return await RejectBulkPreparation(tx, "VERSION_CONFLICT", ct).ConfigureAwait(false);
                // Reuse the same effective expiry/stamp/method test as the qualified single owner.
                var request = new SessionRevocationRequest(requestId, candidate.SessionReference, candidate.ExpectedVersion, correlationId);
                var operation = SessionRevocationBinding.Operation(caller, request);
                var check = new SessionRevocationExecution(database, this, caller, request, operation);
                if (await check.TargetCurrentAsync(actor, ct).ConfigureAwait(false)) { eligible.Add(candidate); checks.Add(check); }
            }
            var actorMethod = await MethodIsCurrentAsync(actor, initial, clock.GetUtcNow(), ct).ConfigureAwait(false);
            var accessOperation = new StepUpOperation("OP-SESSION-REVOKE-OTHERS-" + requestId.ToString("N"), correlationId,
                SessionBulkRevocationApplication.Scope, requestId.ToString("N"), 0, new('0', 64));
            var access = await VerifyContinuedOperationAsync(caller, accessOperation, actor, ct).ConfigureAwait(false);
            var decisionAt = clock.GetUtcNow(); var finalPolicy = policies.RequireEffective(decisionAt); finalPolicy.Validate();
            var finalBudget = SessionBulkRevocationDiscoveryBudget.Current(discovery, decisionAt);
            if (initial != finalPolicy || !actorMethod || actor.State != "ACTIVE" || Expired(actor, finalPolicy, decisionAt) ||
                !MethodFactsCurrentAt(actor, finalPolicy, decisionAt) || access is null || !access.SessionUseInterval.Contains(decisionAt) ||
                !access.OperationInterval.Contains(decisionAt) || finalBudget.Metadata.VersionId != budget.Metadata.VersionId ||
                finalBudget.MaximumCandidates != budget.MaximumCandidates ||
                assurance.RequireEffective(SessionBulkRevocationApplication.Scope, decisionAt, proofPolicy.Metadata.VersionId).Metadata.VersionId != proofPolicy.Metadata.VersionId ||
                checks.Any(x => !x.TargetFactsCurrentAt(finalPolicy, decisionAt)))
                return await RejectBulkPreparation(tx, "VERSION_CONFLICT", ct).ConfigureAwait(false);
            if (eligible.Count == 0) return await RejectBulkPreparation(tx, "NO_OTHER_SESSIONS", ct).ConfigureAwait(false);
            var manifest = SessionBulkRevocationManifest.FromDiscovery(caller, requestId, correlationId, eligible, at,
                finalPolicy.Version, proofPolicy.Metadata.VersionId, budget);
            await database.SaveChangesAsync(ct).ConfigureAwait(false); await tx.CommitAsync(ct).ConfigureAwait(false);
            return new(null, manifest);
        }
        catch { database.ChangeTracker.Clear(); throw; }
    }
    private async Task<SessionBulkRevocationPreparation> RejectBulkPreparation(Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction tx, string error, CancellationToken ct)
    { await tx.RollbackAsync(ct).ConfigureAwait(false); database.ChangeTracker.Clear(); return new(error); }
    private void RequireBulkBoundary()
    {
        if (database.Database.CurrentTransaction is not null || database.ChangeTracker.HasChanges())
            throw new InvalidOperationException("Bulk sessions require a clean Identity transaction boundary.");
    }
    internal async Task<SessionBulkRevocationResult> ReadSessionBulkRevocationAsync(StepUpCaller caller, SessionBulkRevocationManifest manifest, CancellationToken ct)
        => await ReadSessionBulkRevocationCoreAsync(caller, manifest, null, null, ct).ConfigureAwait(false);
    internal Task<SessionBulkRevocationResult> ReadSessionBulkRevocationOriginalAsync(StepUpCaller caller, SessionBulkRevocationManifest manifest,
        ISessionBulkRevocationDiscoverySource discovery, IAssurancePolicySource assurance, CancellationToken ct)
        => ReadSessionBulkRevocationCoreAsync(caller, manifest, discovery, assurance, ct);
    private async Task<SessionBulkRevocationResult> ReadSessionBulkRevocationCoreAsync(StepUpCaller caller, SessionBulkRevocationManifest manifest,
        ISessionBulkRevocationDiscoverySource? discovery, IAssurancePolicySource? assurance, CancellationToken ct)
    {
        var operation = manifest.Operation(caller); RequireBulkBoundary();
        try
        {
            await using var tx = await database.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
            await LockSessionsCanonicalAsync(manifest.Targets.Select(x => x.SessionReference).Append(caller.SessionId), ct).ConfigureAwait(false);
            var actor = await LockForOperationAsync(caller, operation.Scope.Operation, ct).ConfigureAwait(false);
            if (actor is null)
            { await database.SaveChangesAsync(ct).ConfigureAwait(false); await tx.CommitAsync(ct).ConfigureAwait(false); return new("AUTHENTICATION_FAILED"); }
            var set = SessionRevocationExecutionSet.Bulk(database, this, caller, manifest, discovery, assurance);
            var receipts = await set.ReadReceiptAsync(null, ct).ConfigureAwait(false);
            var pending = receipts is null && discovery is not null && await set.TargetCurrentAsync(actor, ct).ConfigureAwait(false);
            var policy = policies.RequireEffective(clock.GetUtcNow()); policy.Validate();
            var method = await MethodIsCurrentAsync(actor, policy, clock.GetUtcNow(), ct).ConfigureAwait(false);
            var access = await VerifyContinuedOperationAsync(caller, operation, actor, ct).ConfigureAwait(false);
            var at = clock.GetUtcNow(); var current = policies.RequireEffective(at); current.Validate();
            if (receipts is null && !pending || policy != current || !method || actor.State != "ACTIVE" || Expired(actor, current, at) ||
                !MethodFactsCurrentAt(actor, current, at) || access is null || !access.SessionUseInterval.Contains(at) || !access.OperationInterval.Contains(at) ||
                receipts is null && !set.TargetFactsCurrentAt(current, at))
            { await tx.RollbackAsync(ct).ConfigureAwait(false); database.ChangeTracker.Clear(); return new("RESULT_UNCONFIRMED"); }
            await database.SaveChangesAsync(ct).ConfigureAwait(false); await tx.CommitAsync(ct).ConfigureAwait(false); return new(null, receipts);
        }
        catch { database.ChangeTracker.Clear(); throw; }
    }

    /// <summary>Small compositor over the existing target mechanics, used by one or many targets.</summary>
    internal sealed class SessionRevocationExecutionSet
    {
        private readonly SessionRevocationExecution[] targets;
        private readonly SessionBulkRevocationManifest? manifest;
        private readonly ISessionBulkRevocationDiscoverySource? discovery;
        private readonly IAssurancePolicySource? assurance;
        private SessionRevocationExecutionSet(SessionRevocationExecution[] targets, Guid[] identities,
            SessionBulkRevocationManifest? manifest = null, ISessionBulkRevocationDiscoverySource? discovery = null, IAssurancePolicySource? assurance = null)
        { this.targets = targets; TargetSessionIds = Array.AsReadOnly(identities); this.manifest = manifest; this.discovery = discovery; this.assurance = assurance; }
        internal IReadOnlyCollection<Guid> TargetSessionIds { get; }
        internal static SessionRevocationExecutionSet Single(IdentityPersistenceDbContext db, AuthenticationApplication auth, StepUpCaller caller,
            SessionRevocationRequest request, StepUpOperation operation) => new([new(db, auth, caller, request, operation)], [request.TargetSessionId]);
        internal static SessionRevocationExecutionSet Bulk(IdentityPersistenceDbContext db, AuthenticationApplication auth, StepUpCaller caller,
            SessionBulkRevocationManifest manifest, ISessionBulkRevocationDiscoverySource? discovery, IAssurancePolicySource? assurance)
        {
            var operation = manifest.Operation(caller);
            return new(manifest.Targets.Select(x => new SessionRevocationExecution(db, auth, caller,
                new(manifest.RequestId, x.SessionReference, x.ExpectedVersion, manifest.CorrelationId), operation)).ToArray(),
                manifest.Targets.Select(x => x.SessionReference).ToArray(), manifest, discovery, assurance);
        }
        internal async Task<IReadOnlyList<SessionRevocationReceipt>?> ReadReceiptAsync(string? keyDigest, CancellationToken ct)
        {
            var receipts = new List<SessionRevocationReceipt>(targets.Length);
            foreach (var target in targets)
            { var receipt = await target.ReadReceiptAsync(keyDigest, ct).ConfigureAwait(false); if (receipt is null) return null; receipts.Add(receipt); }
            return receipts.Count == 0 ? null : receipts.AsReadOnly();
        }
        internal async Task<UserStepUpOperationApplyOutcome> ApplyAsync(AuthenticationSessionRow actor, CancellationToken ct)
        {
            foreach (var target in targets)
            { using var outcome = await target.ApplyAsync(actor, ct).ConfigureAwait(false); if (!outcome.Applied) return UserStepUpOperationApplyOutcome.Conflict(); }
            return UserStepUpOperationApplyOutcome.SessionContinues();
        }
        internal async Task<bool> TargetCurrentAsync(AuthenticationSessionRow actor, CancellationToken ct)
        {
            foreach (var target in targets) if (!await target.TargetCurrentAsync(actor, ct).ConfigureAwait(false)) return false;
            return true;
        }
        internal bool TargetFactsCurrentAt(AuthenticationPolicy policy, DateTimeOffset at)
        {
            if (manifest is not null)
            {
                if (discovery is null || assurance is null || policy.Version != manifest.AuthenticationVersion) return false;
                var budget = SessionBulkRevocationDiscoveryBudget.Current(discovery, at);
                if (budget.Metadata.VersionId != manifest.DiscoveryVersion || budget.MaximumCandidates != manifest.MaximumCandidates ||
                    assurance.RequireEffective(SessionBulkRevocationApplication.Scope, at, manifest.AssuranceVersion).Metadata.VersionId != manifest.AssuranceVersion) return false;
            }
            return targets.All(x => x.TargetFactsCurrentAt(policy, at));
        }
    }
}
