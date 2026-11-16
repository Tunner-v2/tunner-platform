using System.Text.Json;
using Tunner.Database.Audit;
using Tunner.Database.Outbox;
using Tunner.Platform.Policies;

namespace Tunner.Identity.Persistence;

/// <summary>Explicit subscription/generation and lease inputs; no production subscription or timing is selected here.</summary>
public sealed record IdentityPostCommitConsumer(string SubscriptionId, string HandlerGeneration, TimeSpan LeaseDuration)
{
    internal string Key
    {
        get
        {
            if (string.IsNullOrWhiteSpace(SubscriptionId) || string.IsNullOrWhiteSpace(HandlerGeneration) || LeaseDuration <= TimeSpan.Zero)
                throw new ArgumentException("A qualified Identity subscription, generation and positive lease are required.");
            return JsonSerializer.Serialize(new[] { SubscriptionId, HandlerGeneration });
        }
    }
}

/// <summary>Historical owner evidence only. References must contain no credential material or recipient address.</summary>
public sealed record IdentityPostCommitOwnerFacts(string OwnerKind, string OwnerReference, string? ActorAccountId,
    string? SessionReference, string PolicyVersion, string ProcessingReference, string RetentionReference,
    IReadOnlyDictionary<string, string> References)
{
    public override string ToString() => nameof(IdentityPostCommitOwnerFacts);
}

/// <summary>Trusted owner adapter. Read immutable committed result/original facts, not current credential eligibility.
/// More than one matching owner is an invalid composition. The processor independently verifies the entire committed outbox record.</summary>
public interface IIdentityPostCommitOwnerSource
{
    bool Owns(PendingOutboxMessage message);
    ValueTask<IdentityPostCommitOwnerFacts?> ReadCommittedAsync(PendingOutboxMessage message, CancellationToken cancellationToken);
}

public sealed record IdentityPostCommitFacts(PendingOutboxMessage Event, IdentityPostCommitOwnerFacts Owner, string Digest)
{
    public override string ToString() => nameof(IdentityPostCommitFacts);
}

/// <summary>Qualified safe Notification-domain handoff. IntentId is the original business delivery identity where one exists.
/// Body, address, credential/challenge material and inferred recipient/class/route defaults do not belong here.</summary>
public sealed record IdentitySecurityNotification(Guid IntentId, Guid SourceEventId, string FactsDigest,
    string ContractVersion, string NotificationClass, string TemplateVersion, string ContentVersionReference,
    string Locale, string RecipientReference, string RecipientEvidenceReference, string RouteResolutionReference,
    string ProcessingReference, string RetentionReference, string EvidenceReference)
{
    public override string ToString() => nameof(IdentitySecurityNotification);
}

public enum IdentitySecurityNotificationOutcome { Accepted, NoLongerDispatchable }

/// <summary>The Notification/owner adapter durably deduplicates IntentId and rechecks recipient safety/current dispatchability.
/// Accepted is queue handoff, never delivery. Unavailability throws so the inbox remains retryable.</summary>
public interface IIdentitySecurityNotificationDispatcher
{
    Task<IdentitySecurityNotificationOutcome> QueueAsync(IdentitySecurityNotification notification, CancellationToken cancellationToken);
}

/// <summary>Server-owned action/actor/class/retention/content authority bound to the exact historical facts.
/// Audit identity/time/op/correlation/causation must equal the original event; metadata remains immutable across retries.</summary>
public sealed record IdentityPostCommitQualification(Guid EventId, string FactsDigest, string ContractVersion,
    string EvidenceReference, EffectivePolicyInterval EffectiveInterval, AuditEvent Audit,
    IdentitySecurityNotification? Notification = null);

public interface IIdentityPostCommitContractSource
{
    ValueTask<IdentityPostCommitQualification?> RequireCurrentAsync(IdentityPostCommitFacts facts, DateTimeOffset at,
        CancellationToken cancellationToken);
}

public enum IdentityPostCommitOutcome { Completed, Deferred, Rejected, Unavailable, LeaseLost }
