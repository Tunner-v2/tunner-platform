using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Tunner.Accounts;
using Tunner.Database.Outbox;
using Tunner.Platform.Policies;

namespace Tunner.Identity.Persistence;

internal enum RecoveryCodeUsageAdmission { ExactUsageSnapshot = 1, IndependentUnusedEntry = 2 }
/// <summary>Server-owned operation context. No already-authenticated strong session is required.</summary>
internal sealed class RecoveryCodeConsumptionRequest(string accountId, string ownerOperationId, string correlationId, string contextDigest, string origin, string networkDigest)
{
    internal string AccountId { get; } = accountId;
    internal string OwnerOperationId { get; } = ownerOperationId;
    internal string CorrelationId { get; } = correlationId;
    internal string ContextDigest { get; } = contextDigest;
    internal string Origin { get; } = origin;
    internal string NetworkDigest { get; } = networkDigest;
    internal void Validate()
    {
        if (new[] { AccountId, OwnerOperationId, CorrelationId, Origin }.Any(string.IsNullOrWhiteSpace) ||
            ContextDigest is not { Length: 64 } || !ContextDigest.All(Uri.IsHexDigit) || NetworkDigest is not { Length: 64 } || !NetworkDigest.All(Uri.IsHexDigit))
            throw new ArgumentException("A complete trusted recovery consumption operation is required.");
    }
    public override string ToString() => nameof(RecoveryCodeConsumptionRequest);
}
internal sealed class RecoveryCodeConsumptionAdmission(IdentityPersistenceDbContext database, IDbContextTransaction transaction,
    RecoveryCodeConsumptionRequest request, AccountRow account, Guid setReference, long lifecycleVersion, RecoveryCodeUsageAdmission usageAdmission, long? expectedUsageVersion,
    string ownerPolicyVersion, string evidenceReference, EffectivePolicyInterval effectiveInterval, string? priorOutcomeReference = null)
{
    internal IdentityPersistenceDbContext Database { get; } = database;
    internal IDbContextTransaction Transaction { get; } = transaction;
    internal RecoveryCodeConsumptionRequest Request { get; } = request;
    internal AccountRow Account { get; } = account;
    internal long AccountVersion { get; } = account.Version;
    internal AccountState AccountState { get; } = account.State;
    internal Guid SetReference { get; } = setReference;
    internal long LifecycleVersion { get; } = lifecycleVersion;
    internal RecoveryCodeUsageAdmission UsageAdmission { get; } = usageAdmission;
    internal long? ExpectedUsageVersion { get; } = expectedUsageVersion;
    internal string OwnerPolicyVersion { get; } = ownerPolicyVersion;
    internal string EvidenceReference { get; } = evidenceReference;
    internal EffectivePolicyInterval EffectiveInterval { get; } = effectiveInterval;
    internal string? PriorOutcomeReference { get; } = priorOutcomeReference;
    public override string ToString() => nameof(RecoveryCodeConsumptionAdmission);
}
internal sealed class RecoveryCodeConsumptionOwnerOutcome(IdentityPersistenceDbContext database, IDbContextTransaction transaction,
    RecoveryCodeConsumptionAdmission admission, bool applied, string? outcomeReference, long accountVersion, AccountState accountState,
    string ownerPolicyVersion, EffectivePolicyInterval effectiveInterval, IEnumerable<Guid> outboxEventIds)
{
    internal IdentityPersistenceDbContext Database { get; } = database;
    internal IDbContextTransaction Transaction { get; } = transaction;
    internal RecoveryCodeConsumptionAdmission Admission { get; } = admission;
    internal bool Applied { get; } = applied;
    internal string? OutcomeReference { get; } = outcomeReference;
    internal long AccountVersion { get; } = accountVersion;
    internal AccountState AccountState { get; } = accountState;
    internal string OwnerPolicyVersion { get; } = ownerPolicyVersion;
    internal EffectivePolicyInterval EffectiveInterval { get; } = effectiveInterval;
    internal IReadOnlyList<Guid> OutboxEventIds { get; } = Array.AsReadOnly(outboxEventIds.ToArray());
    public override string ToString() => nameof(RecoveryCodeConsumptionOwnerOutcome);
}
/// <summary>P2-011 supplies its real owner. No speculative case table, public code-validity route or generic callback.</summary>
internal interface IRecoveryCodeConsumptionOwner
{
    // Admission must acquire the complete common owner/Account/(when applicable)session lock set in the existing order.
    // It reads actual owner state and does not mutate it. The executor acquires recovery set/entry locks afterward.
    Task<RecoveryCodeConsumptionAdmission?> AdmitAsync(IdentityPersistenceDbContext database, RecoveryCodeConsumptionRequest request, CancellationToken ct);
    // Same local transaction only: no network/provider I/O, nested transaction or earlier-order lock acquisition.
    Task<RecoveryCodeConsumptionOwnerOutcome> ApplyAsync(IdentityPersistenceDbContext database, RecoveryCodeConsumptionRequest request, RecoveryCodeConsumptionAdmission admission, CancellationToken ct);
    // Pure final owner-state/policy/effective-interval/tracked-mutation revalidation, after every awaited operation.
    // A null outcome is the safe duplicate-read branch and must prove the actual original completed owner outcome.
    bool CurrentAt(IdentityPersistenceDbContext database, RecoveryCodeConsumptionRequest request, RecoveryCodeConsumptionAdmission admission, RecoveryCodeConsumptionOwnerOutcome? outcome, DateTimeOffset at);
}
internal sealed record RecoveryCodeConsumptionResult(bool Consumed, bool Reconciled, string? ErrorCode, string? OutcomeReference = null)
{
    public override string ToString() => nameof(RecoveryCodeConsumptionResult);
}

internal sealed partial class RecoveryCodeApplication
{
    internal async Task<RecoveryCodeConsumptionResult> ExecuteConsumptionOutcomeAsync(RecoveryCodeConsumptionRequest request, string? input,
        IRecoveryCodeConsumptionOwner owner, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request); ArgumentNullException.ThrowIfNull(owner); request.Validate(); Clean();
        var current = Current(request.Origin); var policy = current.Values;
        await using var tx = await database.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        try
        {
            var admission = await owner.AdmitAsync(database, request, ct).ConfigureAwait(false);
            if (admission is null) return await RollbackConsumption(tx, "RECOVERY_CODE_REJECTED", ct).ConfigureAwait(false);
            if (!ReferenceEquals(admission.Database, database) || !ReferenceEquals(admission.Transaction, tx) || !ReferenceEquals(admission.Request, request) || !ReferenceEquals(database.Database.CurrentTransaction, tx) ||
                database.ChangeTracker.HasChanges() || admission.Account.AccountId != request.AccountId || admission.SetReference == Guid.Empty || admission.LifecycleVersion < 1 ||
                string.IsNullOrWhiteSpace(admission.OwnerPolicyVersion) || string.IsNullOrWhiteSpace(admission.EvidenceReference) || !admission.EffectiveInterval.Contains(clock.GetUtcNow()) ||
                admission.UsageAdmission is not (RecoveryCodeUsageAdmission.ExactUsageSnapshot or RecoveryCodeUsageAdmission.IndependentUnusedEntry) ||
                (admission.UsageAdmission == RecoveryCodeUsageAdmission.ExactUsageSnapshot ? admission.ExpectedUsageVersion is null or < 0 : admission.ExpectedUsageVersion is not null))
                throw new InvalidOperationException("A current same-owner recovery admission transaction is required.");
            // Positively retain the actual Account row lock before any recovery set/entry claim lock.
            var account = await database.Accounts.FromSqlInterpolated($"SELECT * FROM accounts.accounts WHERE account_id={request.AccountId} FOR NO KEY UPDATE").SingleAsync(ct).ConfigureAwait(false);
            await database.Entry(account).ReloadAsync(ct).ConfigureAwait(false);
            if (!ReferenceEquals(account, admission.Account) || account.Version != admission.AccountVersion || account.State != admission.AccountState)
                throw new InvalidOperationException("Recovery admission must bind the actual current tracked owner Account.");
            await LockOwner(request.AccountId, ct).ConfigureAwait(false);
            var prior = await database.Set<RecoveryCodeEntryRow>().SingleOrDefaultAsync(x => x.AccountId == request.AccountId && x.ConsumedByOperationId == request.OwnerOperationId, ct).ConfigureAwait(false);
            if (prior is not null || admission.PriorOutcomeReference is not null)
            {
                var at = clock.GetUtcNow();
                if (prior?.ConsumedAt is not null && prior.SetReference == admission.SetReference && prior.ConsumptionSetLifecycleVersion == admission.LifecycleVersion &&
                    prior.ConsumptionCorrelationId == request.CorrelationId && prior.ConsumptionContextDigest == request.ContextDigest &&
                    prior.ConsumptionOwnerPolicyVersion == admission.OwnerPolicyVersion && prior.ConsumptionOutcomeReference == admission.PriorOutcomeReference &&
                    !string.IsNullOrWhiteSpace(admission.PriorOutcomeReference) && admission.EffectiveInterval.Contains(at) && owner.CurrentAt(database, request, admission, null, at))
                    return await Finish(tx, new RecoveryCodeConsumptionResult(false, true, null, admission.PriorOutcomeReference), ct).ConfigureAwait(false);
                return await RollbackConsumption(tx, "VERSION_CONFLICT", ct).ConfigureAwait(false);
            }
            var auth = authenticationPolicies.RequireEffective(clock.GetUtcNow()); auth.Validate();
            if (!await authentication.AllowAsync("recovery-code:N:" + request.NetworkDigest, policy.ConsumptionNetworkAttemptLimit, auth, clock.GetUtcNow(), ct).ConfigureAwait(false) ||
                !await authentication.AllowAsync("recovery-code:A:" + request.AccountId, policy.ConsumptionAccountAttemptLimit, auth, clock.GetUtcNow(), ct).ConfigureAwait(false))
                return await Finish(tx, new RecoveryCodeConsumptionResult(false, false, "RATE_OR_ABUSE_LIMITED"), ct).ConfigureAwait(false);
            using var code = RecoveryCodeNative.Canonical(input);
            if (code is null) return await Finish(tx, new RecoveryCodeConsumptionResult(false, false, "RECOVERY_CODE_REJECTED"), ct).ConfigureAwait(false);
            var set = await database.Set<RecoveryCodeSetRow>().FromSqlInterpolated($"SELECT * FROM identity.recovery_code_sets WHERE account_id={request.AccountId} AND set_reference={admission.SetReference} FOR UPDATE").SingleOrDefaultAsync(ct).ConfigureAwait(false);
            if (set is null) return await Finish(tx, new RecoveryCodeConsumptionResult(false, false, "RECOVERY_CODE_REJECTED"), ct).ConfigureAwait(false);
            await database.Entry(set).ReloadAsync(ct).ConfigureAwait(false);
            if (set.State != "ACTIVE" || set.LifecycleVersion != admission.LifecycleVersion ||
                admission.ExpectedUsageVersion is { } expectedUsage && expectedUsage != set.UsageVersion)
                return await Finish(tx, new RecoveryCodeConsumptionResult(false, false, "RECOVERY_CODE_REJECTED"), ct).ConfigureAwait(false);
            if (set.CryptoProfileVersion != policy.Crypto.Version) throw new InvalidOperationException("The stored recovery-code profile is not admitted by this host.");
            var entries = await database.Set<RecoveryCodeEntryRow>().Where(x => x.AccountId == request.AccountId && x.SetReference == set.SetReference && x.ConsumedAt == null)
                .OrderBy(x => x.EntryReference).Take(policy.Crypto.MaximumCodesPerSet).ToArrayAsync(ct).ConfigureAwait(false);
            if (await database.Set<RecoveryCodeEntryRow>().CountAsync(x => x.AccountId == request.AccountId && x.SetReference == set.SetReference, ct).ConfigureAwait(false) > policy.Crypto.MaximumCodesPerSet)
                throw new InvalidOperationException("Stored recovery-code candidate capacity is not admitted.");
            foreach (var entry in entries)
            {
                await database.Entry(entry).ReloadAsync(ct).ConfigureAwait(false);
                if (entry.CryptoProfileVersion != policy.Crypto.Version || !native.AdmittedVerifier(entry.Verifier)) throw new InvalidOperationException("Stored recovery-code verifier profile is not admitted.");
            }
            var candidates = entries.Select(x => new RecoveryCodeVerifierCandidate(new(request.AccountId, set.SetReference, x.EntryReference, x.CryptoProfileVersion), x.Verifier)).ToArray();
            var matched = native.VerifyCandidates(candidates, code, ct);
            if (matched is null) return await Finish(tx, new RecoveryCodeConsumptionResult(false, false, "RECOVERY_CODE_REJECTED"), ct).ConfigureAwait(false);
            var claim = entries.Single(x => x.EntryReference == matched); var verifier = claim.Verifier; var priorUsage = set.UsageVersion;
            claim.ConsumedAt = clock.GetUtcNow(); claim.ConsumedByOperationId = request.OwnerOperationId; claim.ConsumptionCorrelationId = request.CorrelationId;
            claim.ConsumptionContextDigest = request.ContextDigest; claim.ConsumptionPolicyVersion = policy.Version; claim.ConsumptionOwnerPolicyVersion = admission.OwnerPolicyVersion; claim.Version++;
            claim.ConsumptionSetLifecycleVersion = set.LifecycleVersion;
            set.UsageVersion++;
            var applied = await owner.ApplyAsync(database, request, admission, ct).ConfigureAwait(false);
            if (!applied.Applied) return await RollbackConsumption(tx, "VERSION_CONFLICT", ct).ConfigureAwait(false);
            claim.ConsumptionOutcomeReference = applied.OutcomeReference;
            var decisionAt = clock.GetUtcNow(); var final = policies.RequireEffective(decisionAt); final.Require(request.Origin, decisionAt);
            if (!ReferenceEquals(database.Database.CurrentTransaction, tx) || !ReferenceEquals(applied.Database, database) || !ReferenceEquals(applied.Transaction, tx) || !ReferenceEquals(applied.Admission, admission) ||
                final.Digest() != current.Digest() || !native.Supports(final.Values.Crypto) || !admission.EffectiveInterval.Contains(decisionAt) || !applied.EffectiveInterval.Contains(decisionAt) ||
                applied.OwnerPolicyVersion != admission.OwnerPolicyVersion || string.IsNullOrWhiteSpace(applied.OutcomeReference) || account.Version != applied.AccountVersion || account.State != applied.AccountState ||
                set.AccountId != request.AccountId || set.State != "ACTIVE" || set.LifecycleVersion != admission.LifecycleVersion || set.UsageVersion != priorUsage + 1 ||
                claim.AccountId != request.AccountId || claim.SetReference != set.SetReference || claim.EntryReference != matched || claim.Verifier != verifier || claim.CryptoProfileVersion != policy.Crypto.Version ||
                claim.Version != 2 || claim.ConsumedAt is null || claim.ConsumedAt < set.CreatedAt || claim.ConsumedAt > decisionAt || claim.ConsumedByOperationId != request.OwnerOperationId || claim.ConsumptionCorrelationId != request.CorrelationId ||
                claim.ConsumptionContextDigest != request.ContextDigest || claim.ConsumptionPolicyVersion != policy.Version || claim.ConsumptionOwnerPolicyVersion != admission.OwnerPolicyVersion ||
                claim.ConsumptionSetLifecycleVersion != admission.LifecycleVersion ||
                !owner.CurrentAt(database, request, admission, applied, decisionAt))
                return await RollbackConsumption(tx, "VERSION_CONFLICT", ct).ConfigureAwait(false);
            database.ChangeTracker.DetectChanges();
            var ownerEvents = database.ChangeTracker.Entries<OutboxMessage>().Where(x => x.State == EntityState.Added && x.Entity.OperationId == request.OwnerOperationId &&
                x.Entity.CorrelationId == request.CorrelationId && x.Entity.Source == "tunner/identity").Select(x => x.Entity.EventId).ToHashSet();
            if (database.Entry(set).State != EntityState.Modified || database.Entry(claim).State != EntityState.Modified || applied.OutboxEventIds.Count == 0 ||
                applied.OutboxEventIds.Any(x => x == Guid.Empty || !ownerEvents.Contains(x)) || applied.OutboxEventIds.Distinct().Count() != applied.OutboxEventIds.Count)
                return await RollbackConsumption(tx, "VERSION_CONFLICT", ct).ConfigureAwait(false);
            return await Finish(tx, new RecoveryCodeConsumptionResult(true, false, null, applied.OutcomeReference), ct).ConfigureAwait(false);
        }
        catch { database.ChangeTracker.Clear(); throw; }
    }
    private async Task<RecoveryCodeConsumptionResult> RollbackConsumption(IDbContextTransaction tx, string error, CancellationToken ct)
    {
        await tx.RollbackAsync(ct).ConfigureAwait(false); database.ChangeTracker.Clear(); return new(false, false, error);
    }
}
