using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Tunner.Database.Outbox;

namespace Tunner.Identity.Persistence;

internal sealed class RecoveryPasswordCodeOwner(RecoveryCaseApplication application, IdentityPersistenceDbContext database,
    RecoveryEntryCaller caller, Guid entryId, Guid caseId, string preparedHash, string preparedPolicySnapshot, TimeProvider clock) : IRecoveryCodeConsumptionOwner
{
    internal RecoveryEntryRow? Entry { get; private set; }
    internal RecoveryCaseRow? Case { get; private set; }
    internal RecoveryResultRow? Result { get; private set; }
    private RecoveryCaseLockedFacts? facts;
    private IReadOnlyList<RecoveryCaseCodeSetFacts> sets = [];
    private bool originalOutbox;
    private string? originalConcurrencyStamp;
    public async Task<RecoveryCodeConsumptionAdmission?> AdmitAsync(IdentityPersistenceDbContext db, RecoveryCodeConsumptionRequest request, CancellationToken ct)
    {
        if (!ReferenceEquals(database, db) || db.Database.CurrentTransaction is null || request.Origin != caller.Origin || request.NetworkDigest != caller.NetworkDigest ||
            !Guid.TryParseExact(request.CorrelationId, "N", out var requestId)) return null;
        await application.LockEntry(caller, requestId, ct).ConfigureAwait(false);
        Entry = await application.Entry(caller, requestId, ct).ConfigureAwait(false);
        if (Entry is null || Entry.EntryId != entryId || !RecoveryCaseApplication.EntryActor(caller, Entry)) return null;
        var snapshot = await db.Set<RecoveryCaseRow>().AsNoTracking().SingleOrDefaultAsync(x => x.EntryId == entryId, ct).ConfigureAwait(false);
        if (snapshot is null || snapshot.CaseId != caseId || snapshot.AccountId != request.AccountId) return null;
        var actor = new RecoveryCaseCaller(snapshot.AccountId, caller.BrowserDigest, caller.Origin, caller.NetworkDigest);
        await application.LockOriginal(actor, requestId, ct).ConfigureAwait(false);
        Case = await application.Original(actor, requestId, ct).ConfigureAwait(false);
        if (Case is null || Case.ProofKind != "CODE" || Case.EntryId != entryId || Case.CaseId != caseId || Case.ContextDigest != request.ContextDigest ||
            RecoveryCaseApplication.OperationId(Case) != request.OwnerOperationId || Case.Goal != nameof(RecoveryCaseGoal.RepairPassword)) return null;
        facts = await application.LoadAccountFacts(Case.AccountId, null, ct).ConfigureAwait(false);
        sets = await application.CodeSets(Case.AccountId, ct).ConfigureAwait(false);
        Result = await db.Set<RecoveryResultRow>().SingleOrDefaultAsync(x => x.CaseId == caseId, ct).ConfigureAwait(false);
        originalOutbox = Result?.RepairOutboxEventId is { } eventId && await db.Set<OutboxMessage>().AnyAsync(x => x.EventId == eventId &&
            x.EventType == "IdentitySecurityActivityRequested" && x.OperationId == request.OwnerOperationId && x.CorrelationId == request.CorrelationId, ct).ConfigureAwait(false);
        var priorConsumption = Result is null ? null : await db.Set<RecoveryCodeEntryRow>().SingleOrDefaultAsync(x => x.AccountId == request.AccountId &&
            x.ConsumedByOperationId == request.OwnerOperationId, ct).ConfigureAwait(false);
        var at = clock.GetUtcNow();
        if (facts is null || Case.PolicySnapshot != preparedPolicySnapshot || !application.PasswordAuthorityCurrent(caller, Entry, Case, facts, sets, at, Result is not null, priorConsumption) ||
            (Result is null ? Entry.State != "OPEN" || Case.State != "PENDING_CODE" || Case.Version != 1 || at >= Case.ExpiresAt || at >= Entry.ExpiresAt :
                !originalOutbox || !RecoveryCaseApplication.PasswordResultCurrent(Entry, Case, Result, facts.User, at))) return null;
        originalConcurrencyStamp = facts.User.ConcurrencyStamp;
        var profile = application.PasswordCurrent(caller.Origin, at);
        return new(db, db.Database.CurrentTransaction, request, facts.Account, Case.SetReference!.Value, Case.ExpectedSetLifecycleVersion!.Value,
            Enum.Parse<RecoveryCodeUsageAdmission>(Case.UsageAdmission!, false), Case.ExpectedUsageVersion, Case.PolicyVersion,
            profile.Metadata.ApprovalEvidenceReferences[0], profile.Metadata.EffectiveInterval, Result?.ResultId.ToString("N"));
    }
    public Task<RecoveryCodeConsumptionOwnerOutcome> ApplyAsync(IdentityPersistenceDbContext db, RecoveryCodeConsumptionRequest request,
        RecoveryCodeConsumptionAdmission admission, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested(); var at = clock.GetUtcNow();
        if (!ReferenceEquals(db, database) || !ReferenceEquals(db.Database.CurrentTransaction, admission.Transaction) || Entry is null || Case is null ||
            facts is null || Result is not null || Entry.State != "OPEN" || Case.State != "PENDING_CODE" || at >= Case.ExpiresAt ||
            !application.PasswordAuthorityCurrent(caller, Entry, Case, facts, sets, at, false)) throw new InvalidOperationException("Original recovery repair authority changed.");
        application.ApplyPassword(Case, facts.User, preparedHash);
        var eventId = Guid.NewGuid(); var ended = IdentityPersistenceTime.Normalize(at);
        Result = new RecoveryResultRow
        {
            ResultId = Guid.NewGuid(), CaseId = caseId, AccountId = Case.AccountId, Outcome = "PASSWORD_REPAIRED_NO_SESSION",
            ContextDigest = Case.ContextDigest, PolicyVersion = Case.PolicyVersion, PolicySnapshot = Case.PolicySnapshot, HierarchySnapshot = Case.HierarchySnapshot,
            SetReference = Case.SetReference, SetLifecycleVersion = Case.ExpectedSetLifecycleVersion, RecordedAt = ended,
            UnresolvedEffectsJson = Case.UnresolvedEffectsJson, ProcessingReference = Case.ProcessingReference, RetentionReference = Case.RetentionReference,
            RepairOutboxEventId = eventId, PriorPasswordDigest = Case.PriorPasswordDigest, RepairedPasswordDigest = AuthenticationIdentity.Digest(preparedHash),
            PriorSecurityStampDigest = Case.PriorSecurityStampDigest, RepairedSecurityStampDigest = AuthenticationIdentity.Digest(facts.User.SecurityStamp!), SessionEffect = Case.SessionEffect
        };
        Case.State = "REPAIRED"; Case.Version++; Case.ResultId = Result.ResultId; Case.EndedAt = ended;
        Entry.State = "REPAIRED"; Entry.Version++; Entry.EndedAt = ended;
        db.Add(Result);
        db.Add(new OutboxMessage(eventId, "1.0", "IdentitySecurityActivityRequested", "1", ended, "tunner/identity", Case.CaseId.ToString("N"),
            "RESTRICTED_SECURITY", JsonSerializer.Serialize(new { reference = Result.ResultId.ToString("N") }), Case.Version.ToString(CultureInfo.InvariantCulture),
            request.OwnerOperationId, request.CorrelationId));
        return Task.FromResult(new RecoveryCodeConsumptionOwnerOutcome(db, admission.Transaction, admission, true, Result.ResultId.ToString("N"),
            facts.Account.Version, facts.Account.State, Case.PolicyVersion, admission.EffectiveInterval, [eventId]));
    }
    public bool CurrentAt(IdentityPersistenceDbContext db, RecoveryCodeConsumptionRequest request, RecoveryCodeConsumptionAdmission admission,
        RecoveryCodeConsumptionOwnerOutcome? outcome, DateTimeOffset at)
    {
        if (!ReferenceEquals(db, database) || !ReferenceEquals(db.Database.CurrentTransaction, admission.Transaction) || !ReferenceEquals(admission.Request, request) ||
            Entry is null || Case is null || Result is null || facts is null || Case.ContextDigest != request.ContextDigest || Case.CaseId != caseId ||
            request.AccountId != Case.AccountId || request.OwnerOperationId != RecoveryCaseApplication.OperationId(Case) || request.CorrelationId != Entry.RequestId.ToString("N") ||
            !RecoveryCaseApplication.PasswordResultCurrent(Entry, Case, Result, facts.User, at) ||
            !application.PasswordAuthorityCurrent(caller, Entry, Case, facts, sets, at, true,
                db.Set<RecoveryCodeEntryRow>().Local.SingleOrDefault(x => x.ConsumedByOperationId == request.OwnerOperationId && x.AccountId == request.AccountId))) return false;
        db.ChangeTracker.DetectChanges();
        if (outcome is null) return originalOutbox && !db.ChangeTracker.HasChanges();
        return outcome.OutcomeReference == Result.ResultId.ToString("N") && facts.User.PasswordHash == preparedHash &&
            facts.User.ConcurrencyStamp != originalConcurrencyStamp && db.Entry(facts.User).State == EntityState.Modified &&
            db.Entry(facts.User).Properties.Where(x => x.IsModified).All(x => x.Metadata.Name is nameof(IdentityUserAdapter.PasswordHash) or nameof(IdentityUserAdapter.SecurityStamp) or nameof(IdentityUserAdapter.ConcurrencyStamp)) &&
            db.Entry(Entry).State == EntityState.Modified && db.Entry(Case).State == EntityState.Modified && db.Entry(Result).State == EntityState.Added &&
            outcome.OutboxEventIds.Count == 1 && outcome.OutboxEventIds[0] == Result.RepairOutboxEventId &&
            db.ChangeTracker.Entries<OutboxMessage>().Any(x => x.State == EntityState.Added && x.Entity.EventId == Result.RepairOutboxEventId &&
                x.Entity.OperationId == request.OwnerOperationId && x.Entity.CorrelationId == request.CorrelationId);
    }
}
