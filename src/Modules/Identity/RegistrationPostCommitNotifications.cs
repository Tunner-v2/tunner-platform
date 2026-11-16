using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tunner.Database.Outbox;

namespace Tunner.Identity.Persistence;

/// <summary>Reference-only registration notification facts. Current proof/address eligibility remains with Registration.
/// Superseded deliveries still have historical owner facts; the dispatch entry rejects their current delivery identity.</summary>
public sealed class RegistrationPostCommitOwnerSource(IDbContextFactory<IdentityPersistenceDbContext> contexts) : IIdentityPostCommitOwnerSource
{
    public bool Owns(PendingOutboxMessage message) => message.EventType is "RegistrationNotificationRequested" or
        "AccountRegistered" or "VerifiedContactChanged" or "AccountAgeEvaluated" or "AccountActivated" ||
        message.EventType == "ExternalIdentityLinked" && message.Payload == JsonSerializer.Serialize(new { reference = message.Subject });

    public async ValueTask<IdentityPostCommitOwnerFacts?> ReadCommittedAsync(PendingOutboxMessage message, CancellationToken cancellationToken)
    {
        var ct = cancellationToken;
        if (!Owns(message) || message.DataClassification != "RESTRICTED_SECURITY" ||
            message.OperationId is not { } operation || !operation.StartsWith("OP-", StringComparison.Ordinal) ||
            !Guid.TryParseExact(operation.AsSpan(3), "N", out var operationId) ||
            !long.TryParse(message.ResourceVersion, NumberStyles.None, CultureInfo.InvariantCulture, out var version) || version < 1) return null;
        using var json = JsonDocument.Parse(message.Payload);
        var payload = json.RootElement;
        if (payload.ValueKind != JsonValueKind.Object || payload.EnumerateObject().Count() != 1 ||
            !payload.TryGetProperty("reference", out var reference) || reference.ValueKind != JsonValueKind.String || reference.GetString() != message.Subject) return null;
        await using var db = await contexts.CreateDbContextAsync(ct).ConfigureAwait(false);
        var row = await db.Set<RegistrationOperationRow>().AsNoTracking().SingleOrDefaultAsync(x => x.OperationId == operationId, ct).ConfigureAwait(false);
        if (row is null || row.CorrelationId != message.CorrelationId || row.CreatedAt > message.OccurredAtUtc) return null;
        var references = new Dictionary<string, string>(StringComparer.Ordinal) { ["registrationReference"] = operationId.ToString("N") };
        if (row.AccountId is { } account) references.Add("targetAccountReference", account);
        switch (message.EventType)
        {
            case "RegistrationNotificationRequested":
                if (!Guid.TryParseExact(message.Subject, "N", out var deliveryId) || deliveryId == Guid.Empty || row.Version < version) return null;
                references.Add("deliveryReference", deliveryId.ToString("N"));
                references.Add("templateVersion", row.TemplateVersion);
                break;
            case "AccountRegistered":
                if (message.Subject != row.AccountId || version != 1 || message.OccurredAtUtc != row.CreatedAt) return null;
                break;
            case "VerifiedContactChanged":
                if (row.ContactId is not { } contactId || message.Subject != contactId.ToString("N") ||
                    !await db.VerifiedContacts.AsNoTracking().AnyAsync(x => x.ContactId == contactId && x.AccountId == row.AccountId && x.Version >= version, ct).ConfigureAwait(false)) return null;
                references.Add("contactReference", contactId.ToString("N"));
                break;
            case "AccountAgeEvaluated":
                if (version != 1 || message.Subject != row.AgeEvidenceReference || row.AgePolicyVersion is not { } agePolicy) return null;
                references.Add("agePolicyReference", agePolicy);
                references.Add("ageEvidenceReference", message.Subject);
                break;
            case "AccountActivated":
                if (row.Status != "SUCCEEDED" || row.Stage != "COMPLETE" || message.Subject != row.AccountId) return null;
                break;
            case "ExternalIdentityLinked":
                if (row.Mode != "FEDERATED" || row.FederationLinkId is not { } link || message.Subject != link.ToString("N")) return null;
                references.Add("linkReference", link.ToString("N"));
                break;
            default: return null;
        }
        // Initiating registration is anonymous; a pending Account binding is not an authenticated actor.
        return new(message.EventType == "RegistrationNotificationRequested" ? "REGISTRATION_NOTIFICATION" : "REGISTRATION_EVENT",
            operationId.ToString("N"), null, null, row.PolicyVersion, row.ProcessingReference, row.RetentionReference, references);
    }
}

/// <summary>Qualified handoff into the existing Registration dispatcher. No address/code leaves the owning application.
/// The underlying dispatcher must deduplicate the original DeliveryId; acceptance means queue handoff, never delivery.</summary>
public sealed class RegistrationPostCommitNotificationDispatcher(IDbContextFactory<IdentityPersistenceDbContext> contexts,
    IServiceScopeFactory scopes) : IIdentitySecurityNotificationDispatcher
{
    public async Task<IdentitySecurityNotificationOutcome> QueueAsync(IdentitySecurityNotification notification, CancellationToken cancellationToken)
    {
        var ct = cancellationToken;
        await using (var db = await contexts.CreateDbContextAsync(ct).ConfigureAwait(false))
        {
            var source = await db.Set<OutboxMessage>().AsNoTracking().SingleOrDefaultAsync(x => x.EventId == notification.SourceEventId, ct).ConfigureAwait(false);
            if (source is null || source.EventType != "RegistrationNotificationRequested" || source.Source != "tunner/identity" ||
                source.Subject != notification.IntentId.ToString("N")) throw new InvalidOperationException("Registration notification source binding is invalid.");
            var owner = new RegistrationPostCommitOwnerSource(contexts);
            var facts = await owner.ReadCommittedAsync(IdentityPostCommitProcessor.Project(source), ct).ConfigureAwait(false);
            if (facts is null || facts.References["templateVersion"] != notification.TemplateVersion ||
                notification.RecipientReference != facts.OwnerReference || notification.ProcessingReference != facts.ProcessingReference ||
                notification.RetentionReference != facts.RetentionReference) throw new InvalidOperationException("Registration notification qualification is invalid.");
        }
        await using var scope = scopes.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<RegistrationApplication>().DispatchNotificationAsync(notification.IntentId, ct).ConfigureAwait(false)
            ? IdentitySecurityNotificationOutcome.Accepted : IdentitySecurityNotificationOutcome.NoLongerDispatchable;
    }
}
