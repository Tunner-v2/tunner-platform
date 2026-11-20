using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tunner.Database.Outbox;

namespace Tunner.Identity.Persistence;

/// <summary>Reference-only handoff into the existing Recovery owner. Its dispatcher retains address/proof custody,
/// current safety checks and DeliveryId deduplication; acceptance means queue handoff, never delivery.</summary>
public sealed class RecoveryContactPostCommitNotificationDispatcher(IDbContextFactory<IdentityPersistenceDbContext> contexts,
    IServiceScopeFactory scopes) : IIdentitySecurityNotificationDispatcher
{
    public async Task<IdentitySecurityNotificationOutcome> QueueAsync(IdentitySecurityNotification notification, CancellationToken cancellationToken)
    {
        var ct = cancellationToken;
        await using (var db = await contexts.CreateDbContextAsync(ct).ConfigureAwait(false))
        {
            if (db.Database.CurrentTransaction is not null || db.ChangeTracker.HasChanges())
                throw new InvalidOperationException("An independent clean Recovery notification read context is required.");
            var source = await db.Set<OutboxMessage>().AsNoTracking().SingleOrDefaultAsync(x => x.EventId == notification.SourceEventId, ct).ConfigureAwait(false);
            var facts = source is null ? null : await RecoveryContactPostCommitEvidence.ReadAsync(db, IdentityPostCommitProcessor.Project(source), ct).ConfigureAwait(false);
            if (facts is null || notification.IntentId != facts.Challenge.DeliveryId)
                throw new InvalidOperationException("Recovery contact notification source binding is invalid.");
            var decision = facts.Decision;
            if (notification.ContractVersion != decision.ContractReference || notification.NotificationClass != decision.NotificationClass ||
                notification.TemplateVersion != decision.TemplateVersion || notification.Locale != decision.Locale ||
                notification.RecipientReference != decision.ContactReference.ToString("N") || notification.RecipientEvidenceReference != decision.RecipientEvidenceReference ||
                notification.RouteResolutionReference != decision.RouteResolutionReference || notification.ProcessingReference != decision.ProcessingReference ||
                notification.RetentionReference != decision.RetentionReference || notification.EvidenceReference != decision.EvidenceReference)
                throw new InvalidOperationException("Recovery contact notification qualification is invalid.");
        }
        await using var scope = scopes.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<RecoveryCaseApplication>().DispatchContactNotificationAsync(notification.IntentId, ct).ConfigureAwait(false)
            ? IdentitySecurityNotificationOutcome.Accepted : IdentitySecurityNotificationOutcome.NoLongerDispatchable;
    }
}

/// <summary>One typed historical join shared by owner facts and the dispatch adapter. Challenge state/version and current
/// contact eligibility may change later; the original committed notification decision remains immutable evidence.</summary>
internal sealed record RecoveryContactPostCommitEvidence(RecoveryContactChallengeRow Challenge, RecoveryCaseRow Case,
    RecoveryContactNotificationDecision Decision)
{
    internal static async Task<RecoveryContactPostCommitEvidence?> ReadAsync(IdentityPersistenceDbContext db, PendingOutboxMessage e, CancellationToken ct)
    {
        if (e.EventType != "IdentitySecurityNotificationRequested" || e.Source != "tunner/identity" || e.EnvelopeVersion != "1.0" || e.EventSchemaVersion != "1" ||
            e.ResourceVersion != "1" || e.DataClassification != "RESTRICTED_SECURITY" || e.CausationEventId is not null ||
            !Guid.TryParseExact(e.Subject, "N", out var delivery) || delivery == Guid.Empty) return null;
        var committed = await db.Set<OutboxMessage>().AsNoTracking().SingleOrDefaultAsync(x => x.EventId == e.EventId, ct).ConfigureAwait(false);
        if (committed is null || IdentityPostCommitProcessor.Project(committed) != e) return null;
        var challenge = await db.Set<RecoveryContactChallengeRow>().AsNoTracking().SingleOrDefaultAsync(x => x.DeliveryId == delivery && x.NotificationOutboxEventId == e.EventId, ct).ConfigureAwait(false);
        if (challenge is null || challenge.CreatedAt != e.OccurredAtUtc) return null;
        var recovery = await db.Set<RecoveryCaseRow>().AsNoTracking().SingleOrDefaultAsync(x => x.CaseId == challenge.CaseId, ct).ConfigureAwait(false);
        if (recovery is null || recovery.ProofKind != "CONTACT_RISK" || recovery.ContactChallengeId != challenge.ChallengeId || recovery.EntryId != challenge.EntryId ||
            recovery.ContactReference != challenge.ContactReference || recovery.ExpectedContactVersion != challenge.ContactVersion ||
            recovery.ContactDestinationDigest != challenge.DestinationDigest || recovery.ContextDigest != challenge.ContextDigest ||
            e.OperationId != "OP-RECOVERY-CASE-" + recovery.CaseId.ToString("N") || e.CorrelationId != recovery.RequestId.ToString("N")) return null;
        try
        {
            using var payload = JsonDocument.Parse(e.Payload);
            var p = payload.RootElement;
            if (p.ValueKind != JsonValueKind.Object || p.EnumerateObject().Count() != 1 || !p.TryGetProperty("reference", out var reference) ||
                reference.ValueKind != JsonValueKind.String || reference.GetString() != e.Subject) return null;
            var decision = JsonSerializer.Deserialize<RecoveryContactNotificationDecision>(challenge.NotificationSnapshot);
            using var policy = JsonDocument.Parse(recovery.PolicySnapshot);
            var profile = policy.RootElement.GetProperty("Values").GetProperty("ContactRecoveryProfile");
            if (decision is null || decision.CaseId != recovery.CaseId || decision.ChallengeId != challenge.ChallengeId || decision.AccountId != recovery.AccountId ||
                decision.ContactReference != challenge.ContactReference || decision.ContactVersion != challenge.ContactVersion ||
                decision.DestinationDigest != challenge.DestinationDigest || decision.ContextDigest != challenge.ContextDigest ||
                decision.ContractReference != profile.GetProperty("NotificationContractReference").GetString() || challenge.ProfileVersion != profile.GetProperty("Version").GetString() ||
                decision.ProcessingReference != challenge.ProcessingReference || decision.RetentionReference != challenge.RetentionReference ||
                decision.ProcessingReference != recovery.ProcessingReference || decision.RetentionReference != recovery.RetentionReference ||
                decision.Channel != "EMAIL" || decision.EffectiveInterval is null || !decision.EffectiveInterval.Contains(e.OccurredAtUtc) ||
                new[] { decision.ContractReference, decision.NotificationClass, decision.TemplateVersion, decision.CodeVariableReference, decision.Locale,
                    decision.RecipientEvidenceReference, decision.RouteResolutionReference, decision.EvidenceReference }.Any(string.IsNullOrWhiteSpace)) return null;
            return new(challenge, recovery, decision);
        }
        catch (Exception error) when (error is JsonException or KeyNotFoundException or InvalidOperationException) { return null; }
    }
}
