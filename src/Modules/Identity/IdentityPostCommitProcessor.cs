using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Tunner.Database;
using Tunner.Database.Audit;
using Tunner.Database.DurableWork;
using Tunner.Database.Outbox;

namespace Tunner.Identity.Persistence;

/// <summary>Finite Identity post-commit join. Audit, downstream handoff and inbox completion are separate commits.
/// A retry verifies the exact prior audit and reuses the original downstream intent identity.</summary>
public sealed class IdentityPostCommitProcessor(IDbContextFactory<IdentityPersistenceDbContext> identityContexts,
    IDbContextFactory<TunnerMigrationsDbContext> platformContexts, IConsumerInboxStore inbox,
    IEnumerable<IIdentityPostCommitOwnerSource> owners, IIdentityPostCommitContractSource contracts,
    IdentityPostCommitConsumer consumer, TimeProvider clock, IIdentitySecurityNotificationDispatcher? notifications = null)
{
    public async Task<IdentityPostCommitOutcome> ProcessAsync(PendingOutboxMessage message, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        var key = consumer.Key;
        // A fresh connection observes only committed producer records, even when the caller has a tracked/uncommitted event.
        await using (var identity = await identityContexts.CreateDbContextAsync(ct).ConfigureAwait(false))
        {
            if (identity.Database.CurrentTransaction is not null || identity.ChangeTracker.HasChanges())
                throw new InvalidOperationException("An independent clean Identity read context is required.");
            var original = await identity.Set<OutboxMessage>().AsNoTracking().SingleOrDefaultAsync(x => x.EventId == message.EventId, ct).ConfigureAwait(false);
            if (original is null || Project(original) != message || message.Source != "tunner/identity" ||
                message.EnvelopeVersion != "1.0" || message.EventSchemaVersion != "1" ||
                string.IsNullOrWhiteSpace(message.OperationId) || string.IsNullOrWhiteSpace(message.CorrelationId))
                return IdentityPostCommitOutcome.Rejected;
        }
        var receipt = await ReadReceiptAsync(key, message.EventId, ct).ConfigureAwait(false);
        // An exact committed event with this subscription/generation's completed receipt is already processed.
        // Owner policy and dispatch eligibility may legitimately have changed since that historical completion.
        if (receipt?.CompletedAtUtc is not null) return IdentityPostCommitOutcome.Completed;
        if (receipt?.LeaseToken is not null && receipt.LeaseExpiresAtUtc > clock.GetUtcNow()) return IdentityPostCommitOutcome.Deferred;
        var matching = owners.Where(x => x.Owns(message)).Take(2).ToArray();
        if (matching.Length != 1) return IdentityPostCommitOutcome.Unavailable;
        var owner = await matching[0].ReadCommittedAsync(message, ct).ConfigureAwait(false);
        if (owner is null || !OwnerShape(owner)) return IdentityPostCommitOutcome.Rejected;
        // Canonical reference order is mechanical and has no policy precedence.
        var snapshot = owner with { References = new ReadOnlyDictionary<string, string>(
            new SortedDictionary<string, string>(owner.References.ToDictionary(x => x.Key, x => x.Value), StringComparer.Ordinal)) };
        var digest = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { message, owner = snapshot }))));
        var facts = new IdentityPostCommitFacts(message, snapshot, digest);
        var qualification = await contracts.RequireCurrentAsync(facts, clock.GetUtcNow(), ct).ConfigureAwait(false);
        if (qualification is null) return IdentityPostCommitOutcome.Unavailable;
        if (!Qualified(facts, qualification, clock.GetUtcNow())) return IdentityPostCommitOutcome.Rejected;
        if (qualification.Notification is not null && notifications is null) return IdentityPostCommitOutcome.Unavailable;
        var token = Guid.NewGuid(); var reservedAt = clock.GetUtcNow(); var expiresAt = reservedAt + consumer.LeaseDuration;
        if (!await inbox.TryReserveAsync(key, message.EventId, token, reservedAt, expiresAt, ct).ConfigureAwait(false))
            return (await ReadReceiptAsync(key, message.EventId, ct).ConfigureAwait(false))?.CompletedAtUtc is not null
                ? IdentityPostCommitOutcome.Completed : IdentityPostCommitOutcome.Deferred;
        try
        {
            if (!Qualified(facts, qualification, clock.GetUtcNow())) return IdentityPostCommitOutcome.Rejected;
            if (!await AppendAuditAsync(BoundAudit(facts, qualification), ct).ConfigureAwait(false)) return IdentityPostCommitOutcome.Rejected;
            if (!await OwnsLeaseAsync(key, message.EventId, token, ct).ConfigureAwait(false)) return IdentityPostCommitOutcome.LeaseLost;
            if (qualification.Notification is { } notice)
            {
                // Requalify current dispatch authority before the external/domain boundary. Historical audit never upgrades to a new version.
                var current = await contracts.RequireCurrentAsync(facts, clock.GetUtcNow(), ct).ConfigureAwait(false);
                if (current is null) return IdentityPostCommitOutcome.Unavailable;
                if (!Qualified(facts, current, clock.GetUtcNow()) || current.Audit.ToEnvelope() != qualification.Audit.ToEnvelope() || current.Notification != notice)
                    return IdentityPostCommitOutcome.Rejected;
                // This observes the stored token as well as expiry. A later loss is still possible: downstream IntentId deduplication remains mandatory.
                if (!await OwnsLeaseAsync(key, message.EventId, token, ct).ConfigureAwait(false)) return IdentityPostCommitOutcome.LeaseLost;
                var outcome = await notifications!.QueueAsync(notice, ct).ConfigureAwait(false);
                if (outcome is not (IdentitySecurityNotificationOutcome.Accepted or IdentitySecurityNotificationOutcome.NoLongerDispatchable))
                    throw new InvalidOperationException("Unknown Identity notification handoff outcome.");
            }
            return await inbox.TryCompleteAsync(key, message.EventId, token, clock.GetUtcNow(), ct).ConfigureAwait(false)
                ? IdentityPostCommitOutcome.Completed : IdentityPostCommitOutcome.LeaseLost;
        }
        finally
        {
            // Release is lease-token guarded; it cannot clear a replacement worker's reservation. Cancellation/failure never completes the inbox.
            await inbox.TryReleaseAsync(key, message.EventId, token, clock.GetUtcNow(), CancellationToken.None).ConfigureAwait(false);
        }
    }

    private async Task<ConsumerInboxEntry?> ReadReceiptAsync(string key, Guid eventId, CancellationToken ct)
    {
        await using var context = await platformContexts.CreateDbContextAsync(ct).ConfigureAwait(false);
        if (context.Database.CurrentTransaction is not null || context.ChangeTracker.HasChanges())
            throw new InvalidOperationException("An independent clean platform inbox read context is required.");
        return await context.ConsumerInboxEntries.AsNoTracking().SingleOrDefaultAsync(x => x.ConsumerKey == key && x.MessageId == eventId, ct).ConfigureAwait(false);
    }

    private async Task<bool> OwnsLeaseAsync(string key, Guid eventId, Guid token, CancellationToken ct)
    {
        var receipt = await ReadReceiptAsync(key, eventId, ct).ConfigureAwait(false);
        return receipt is { CompletedAtUtc: null } && receipt.LeaseToken == token && receipt.LeaseExpiresAtUtc > clock.GetUtcNow();
    }

    private async Task<bool> AppendAuditAsync(AuditEvent audit, CancellationToken ct)
    {
        await using var context = await platformContexts.CreateDbContextAsync(ct).ConfigureAwait(false);
        if (context.Database.CurrentTransaction is not null || context.ChangeTracker.HasChanges())
            throw new InvalidOperationException("An independent clean platform audit context is required.");
        await using var tx = await context.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        // Serialize this stable audit identity across lease expiry and concurrent redelivery; no distributed-transaction claim.
        await context.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({"identity-post-commit-audit:" + audit.EventId.ToString("N")}, 0))", ct).ConfigureAwait(false);
        var prior = await context.AuditEvents.AsNoTracking().SingleOrDefaultAsync(x => x.EventId == audit.EventId, ct).ConfigureAwait(false);
        if (prior is not null)
        {
            if (!prior.VerifyIntegrity() || prior.Envelope != audit.ToEnvelope()) return false;
        }
        else
        {
            EfAuditAppender.StageAppend(context, audit);
            await context.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        await tx.CommitAsync(ct).ConfigureAwait(false); return true;
    }

    internal static PendingOutboxMessage Project(OutboxMessage row) => new(row.EventId, row.EnvelopeVersion, row.EventType,
        row.EventSchemaVersion, row.OccurredAtUtc, row.Source, row.Subject, row.DataClassification, row.Payload,
        row.ResourceVersion, row.OperationId, row.CorrelationId, row.CausationEventId);

    private static AuditEvent BoundAudit(IdentityPostCommitFacts facts, IdentityPostCommitQualification q)
    {
        var a = q.Audit;
        var binding = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
        { facts.Digest, q.ContractVersion, q.EvidenceReference, audit = a.ToEnvelope(), q.Notification }))));
        return new(a.EventId, a.OccurredAtUtc, a.Actor, a.Action, a.OperationId, a.CorrelationId, a.Targets, a.Result,
            a.DataClassification, a.RetentionClassReference, a.CausationEventId, a.Reason, a.PermissionVersionReferences,
            a.ScopeVersionReferences, a.PolicyVersionReferences, a.StepUpReferences, a.ApprovalReferences, a.BeforeVersions,
            a.AfterVersions, a.ProviderReferences, a.EvidenceReferences.Append("identity-post-commit-binding:sha256:" + binding));
    }

    private static bool OwnerShape(IdentityPostCommitOwnerFacts facts) =>
        new[] { facts.OwnerKind, facts.OwnerReference, facts.PolicyVersion, facts.ProcessingReference, facts.RetentionReference }.All(x => !string.IsNullOrWhiteSpace(x)) &&
        facts.ActorAccountId is not "" && facts.SessionReference is not "" && facts.References is not null &&
        facts.References.All(x => !string.IsNullOrWhiteSpace(x.Key) && !string.IsNullOrWhiteSpace(x.Value));

    private static bool Qualified(IdentityPostCommitFacts facts, IdentityPostCommitQualification q, DateTimeOffset at)
    {
        var e = facts.Event; var a = q.Audit;
        if (q.EventId != e.EventId || q.FactsDigest != facts.Digest || string.IsNullOrWhiteSpace(q.ContractVersion) ||
            string.IsNullOrWhiteSpace(q.EvidenceReference) || q.EffectiveInterval is null || !q.EffectiveInterval.Contains(at) || a is null ||
            a.EventId != e.EventId || a.OccurredAtUtc != e.OccurredAtUtc || a.OperationId != e.OperationId || a.CorrelationId != e.CorrelationId ||
            a.CausationEventId != e.EventId.ToString("N") || a.DataClassification != e.DataClassification ||
            !a.Targets.Any(x => x.ResourceId == e.Subject && x.Version == e.ResourceVersion) ||
            !a.PolicyVersionReferences.Contains(q.ContractVersion) || !a.EvidenceReferences.Contains(q.EvidenceReference) ||
            (facts.Owner.ActorAccountId is { } account && a.Actor.PrincipalId != account)) return false;
        if (q.Notification is not { } n) return true;
        var intentId = facts.Owner.References.TryGetValue("deliveryReference", out var delivery) && Guid.TryParse(delivery, out var businessId) ? businessId : e.EventId;
        return n.IntentId == intentId && n.IntentId != Guid.Empty && n.SourceEventId == e.EventId && n.FactsDigest == facts.Digest &&
            new[] { n.ContractVersion, n.NotificationClass, n.TemplateVersion, n.ContentVersionReference, n.Locale,
                n.RecipientReference, n.RecipientEvidenceReference, n.RouteResolutionReference, n.ProcessingReference,
                n.RetentionReference, n.EvidenceReference }.All(x => !string.IsNullOrWhiteSpace(x));
    }
}
