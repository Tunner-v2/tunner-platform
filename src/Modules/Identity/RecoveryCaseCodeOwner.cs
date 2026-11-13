using Microsoft.EntityFrameworkCore;
using Tunner.Database.Outbox;

namespace Tunner.Identity.Persistence;

/// <summary>The actual011 case owner inside009's verifier/claim transaction. Never an authenticated StepUp adapter.</summary>
internal sealed class RecoveryCaseCodeOwner(RecoveryCaseApplication application, IdentityPersistenceDbContext database,
    AuthenticationApplication authentication, TimeProvider clock, RecoveryCaseCaller caller, Guid caseId, Guid requestId) : IRecoveryCodeConsumptionOwner
{
    internal RecoveryCaseRow? Case { get; private set; }
    internal RecoveryResultRow? Result { get; private set; }
    private RecoveryCaseLockedFacts? facts;
    private bool originalOutboxObserved;

    public async Task<RecoveryCodeConsumptionAdmission?> AdmitAsync(IdentityPersistenceDbContext db, RecoveryCodeConsumptionRequest request, CancellationToken ct)
    {
        if (!ReferenceEquals(db, database) || db.Database.CurrentTransaction is null || request.AccountId != caller.AccountId ||
            request.Origin != caller.Origin || request.NetworkDigest != caller.NetworkDigest || request.CorrelationId != requestId.ToString("N")) return null;
        await application.LockOriginal(caller, requestId, ct).ConfigureAwait(false);
        Case = await application.Original(caller, requestId, ct).ConfigureAwait(false);
        if (Case is null || Case.ProofKind != "CODE" || Case.CaseId != caseId || !RecoveryCaseApplication.Actor(Case, caller) ||
            request.OwnerOperationId != RecoveryCaseApplication.OperationId(Case) || request.ContextDigest != Case.ContextDigest) return null;
        if (Case.Goal != nameof(RecoveryCaseGoal.ContainSelectedTunnerSession) || Case.TargetSessionId is not { } target) return null;
        facts = await application.LoadFacts(caller, target, ct).ConfigureAwait(false);
        Result = await db.Set<RecoveryResultRow>().SingleOrDefaultAsync(x => x.CaseId == caseId, ct).ConfigureAwait(false);
        originalOutboxObserved = Result is not null && await db.Set<OutboxMessage>().AnyAsync(x => x.EventId == Result.SessionOutboxEventId &&
            x.EventType == "TunnerSessionRevoked" && x.OperationId == request.OwnerOperationId && x.CorrelationId == request.CorrelationId, ct).ConfigureAwait(false);
        var at = clock.GetUtcNow();
        if (facts is null || !application.AuthorityCurrent(caller, Case, facts, at) ||
            (Case.State == "PENDING_CODE" ? !application.PendingCurrent(caller, Case, facts, at) || Result is not null :
                !originalOutboxObserved || !RecoveryCaseApplication.CompletedCurrent(Case, facts.Session, Result, at))) return null;
        var policy = application.Current(caller, at); var decision = application.Decision(Case, facts, at);
        return new(db, db.Database.CurrentTransaction, request, facts.Account, Case.SetReference!.Value, Case.ExpectedSetLifecycleVersion!.Value,
            Enum.Parse<RecoveryCodeUsageAdmission>(Case.UsageAdmission!, false), Case.ExpectedUsageVersion, Case.PolicyVersion,
            decision.EvidenceReference, policy.Metadata.EffectiveInterval, Result?.ResultId.ToString("N"));
    }

    public Task<RecoveryCodeConsumptionOwnerOutcome> ApplyAsync(IdentityPersistenceDbContext db, RecoveryCodeConsumptionRequest request,
        RecoveryCodeConsumptionAdmission admission, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var at = clock.GetUtcNow();
        if (!ReferenceEquals(db, database) || !ReferenceEquals(db.Database.CurrentTransaction, admission.Transaction) ||
            Case is null || facts is null || Result is not null || !application.PendingCurrent(caller, Case, facts, at))
            throw new InvalidOperationException("The original current recovery case is required at its effect frontier.");
        var effectAt = IdentityPersistenceTime.Normalize(at);
        var eventId = authentication.ContainRecoveryCaseSession(Case, facts.Session, effectAt);
        Result = new RecoveryResultRow
        {
            ResultId = Guid.NewGuid(), CaseId = Case.CaseId, AccountId = Case.AccountId, Outcome = "CONTAINED_PENDING_RESTORATION",
            TargetSessionId = Case.TargetSessionId, PriorSessionVersion = Case.ExpectedSessionVersion, EndedSessionVersion = facts.Session.Version,
            ContextDigest = Case.ContextDigest, PolicyVersion = Case.PolicyVersion, PolicySnapshot = Case.PolicySnapshot,
            HierarchySnapshot = Case.HierarchySnapshot, SetReference = Case.SetReference, SetLifecycleVersion = Case.ExpectedSetLifecycleVersion,
            RecordedAt = effectAt, UnresolvedEffectsJson = Case.UnresolvedEffectsJson, ProcessingReference = Case.ProcessingReference,
            RetentionReference = Case.RetentionReference, SessionOutboxEventId = eventId
        };
        Case.State = "CONTAINED"; Case.Version++; Case.ResultId = Result.ResultId; Case.EndedAt = effectAt;
        db.Add(Result);
        return Task.FromResult(new RecoveryCodeConsumptionOwnerOutcome(db, admission.Transaction, admission, true, Result.ResultId.ToString("N"),
            facts.Account.Version, facts.Account.State, Case.PolicyVersion, admission.EffectiveInterval, [eventId]));
    }

    public bool CurrentAt(IdentityPersistenceDbContext db, RecoveryCodeConsumptionRequest request, RecoveryCodeConsumptionAdmission admission,
        RecoveryCodeConsumptionOwnerOutcome? outcome, DateTimeOffset at)
    {
        if (!ReferenceEquals(db, database) || !ReferenceEquals(db.Database.CurrentTransaction, admission.Transaction) ||
            !ReferenceEquals(admission.Request, request) || Case is null || facts is null || Result is null ||
            !ReferenceEquals(admission.Account, facts.Account) || Case.CaseId != caseId || Case.RequestId != requestId ||
            request.OwnerOperationId != RecoveryCaseApplication.OperationId(Case) || request.ContextDigest != Case.ContextDigest ||
            request.CorrelationId != requestId.ToString("N") || request.AccountId != caller.AccountId ||
            request.Origin != caller.Origin || request.NetworkDigest != caller.NetworkDigest ||
            !RecoveryCaseApplication.CompletedCurrent(Case, facts.Session, Result, at) || !application.AuthorityCurrent(caller, Case, facts, at)) return false;
        if (outcome is null) return originalOutboxObserved && db.Entry(Case).State == EntityState.Unchanged &&
            db.Entry(Result).State == EntityState.Unchanged && db.Entry(facts.Session).State == EntityState.Unchanged;
        db.ChangeTracker.DetectChanges();
        return ReferenceEquals(outcome.Admission, admission) && outcome.OutcomeReference == Result.ResultId.ToString("N") &&
            outcome.OutboxEventIds.Count == 1 && outcome.OutboxEventIds[0] == Result.SessionOutboxEventId &&
            db.Entry(Case).State == EntityState.Modified && db.Entry(Result).State == EntityState.Added && db.Entry(facts.Session).State == EntityState.Modified &&
            db.ChangeTracker.Entries<OutboxMessage>().Any(x => x.State == EntityState.Added && x.Entity.EventId == Result.SessionOutboxEventId &&
                x.Entity.EventType == "TunnerSessionRevoked" && x.Entity.OperationId == request.OwnerOperationId && x.Entity.CorrelationId == request.CorrelationId);
    }
}
