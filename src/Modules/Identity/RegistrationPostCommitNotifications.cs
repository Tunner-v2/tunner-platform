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
    public bool Owns(PendingOutboxMessage message) => message.EventType == "RegistrationNotificationRequested";

    public async ValueTask<IdentityPostCommitOwnerFacts?> ReadCommittedAsync(PendingOutboxMessage message, CancellationToken cancellationToken)
    {
        var ct = cancellationToken;
        if (!Owns(message) || message.DataClassification != "RESTRICTED_SECURITY" ||
            message.OperationId is not { } operation || !operation.StartsWith("OP-", StringComparison.Ordinal) ||
            !Guid.TryParseExact(operation.AsSpan(3), "N", out var operationId) ||
            !Guid.TryParseExact(message.Subject, "N", out var deliveryId) || deliveryId == Guid.Empty ||
            !long.TryParse(message.ResourceVersion, NumberStyles.None, CultureInfo.InvariantCulture, out var version) || version < 1) return null;
        using var json = JsonDocument.Parse(message.Payload);
        var payload = json.RootElement;
        if (payload.ValueKind != JsonValueKind.Object || payload.EnumerateObject().Count() != 1 ||
            !payload.TryGetProperty("reference", out var reference) || reference.ValueKind != JsonValueKind.String || reference.GetString() != message.Subject) return null;
        await using var db = await contexts.CreateDbContextAsync(ct).ConfigureAwait(false);
        var row = await db.Set<RegistrationOperationRow>().AsNoTracking().SingleOrDefaultAsync(x => x.OperationId == operationId, ct).ConfigureAwait(false);
        if (row is null || row.CorrelationId != message.CorrelationId || row.Version < version || row.CreatedAt > message.OccurredAtUtc) return null;
        // Initiating registration is anonymous; a pending Account binding is not an authenticated actor.
        return new("REGISTRATION_NOTIFICATION", operationId.ToString("N"), null, null, row.PolicyVersion,
            row.ProcessingReference, row.RetentionReference, new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["deliveryReference"] = deliveryId.ToString("N"),
                ["registrationReference"] = operationId.ToString("N"),
                ["templateVersion"] = row.TemplateVersion
            });
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
