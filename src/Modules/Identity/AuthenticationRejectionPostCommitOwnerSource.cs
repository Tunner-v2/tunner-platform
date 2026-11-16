using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Tunner.Database.Outbox;
using Tunner.Platform.Policies;

namespace Tunner.Identity.Persistence;

/// <summary>Explicit historical audit authority for the anonymous rejection producer, not its unrecorded sign-in policy.
/// Classification, processing, retention and approval evidence are owner inputs; no production profile is selected here.</summary>
public sealed record AuthenticationRejectionAuditProfile(PolicyVersionCandidate<string> Metadata,
    string ProcessingReference, string RetentionReference, string DataClassification)
{
    public const string Scope = "identity.authentication-rejection.audit";
}

public interface IAuthenticationRejectionAuditProfileSource
{
    /// <summary>Return the uniquely qualified profile for the original event instant; absent authority fails closed.</summary>
    AuthenticationRejectionAuditProfile? RequireEffective(DateTimeOffset eventOccurredAt);
}

/// <summary>The producer records no Account/attempt owner row. Its independently committed, reference-only outbox event
/// is the historical witness. Current generic audit qualification remains required by IdentityPostCommitProcessor.</summary>
public sealed class AuthenticationRejectionPostCommitOwnerSource(IDbContextFactory<IdentityPersistenceDbContext> contexts,
    IAuthenticationRejectionAuditProfileSource profiles) : IIdentityPostCommitOwnerSource
{
    public bool Owns(PendingOutboxMessage message) => message.EventType == "AuthenticationRejected";

    public async ValueTask<IdentityPostCommitOwnerFacts?> ReadCommittedAsync(PendingOutboxMessage message, CancellationToken cancellationToken)
    {
        var e = message; var ct = cancellationToken;
        if (!Owns(e) || e.EventId == Guid.Empty || e.Source != "tunner/identity" || e.EnvelopeVersion != "1.0" ||
            e.EventSchemaVersion != "1" || e.ResourceVersion != "1" || e.DataClassification != "RESTRICTED_SECURITY" || e.CausationEventId is not null ||
            !Guid.TryParseExact(e.Subject, "N", out var reference) || reference == Guid.Empty ||
            e.OperationId != "OP-" + e.Subject || !Guid.TryParseExact(e.CorrelationId, "N", out var correlation) || correlation == Guid.Empty) return null;
        await using var db = await contexts.CreateDbContextAsync(ct).ConfigureAwait(false);
        if (db.Database.CurrentTransaction is not null || db.ChangeTracker.HasChanges())
            throw new InvalidOperationException("An independent clean Identity rejection read context is required.");
        var committed = await db.Set<OutboxMessage>().AsNoTracking().SingleOrDefaultAsync(x => x.EventId == e.EventId, ct).ConfigureAwait(false);
        if (committed is null || IdentityPostCommitProcessor.Project(committed) != e) return null;
        try
        {
            using var json = JsonDocument.Parse(e.Payload);
            var payload = json.RootElement;
            if (payload.ValueKind != JsonValueKind.Object || payload.EnumerateObject().Count() != 1 ||
                !payload.TryGetProperty("reference", out var value) || value.ValueKind != JsonValueKind.String || value.GetString() != e.OperationId) return null;
        }
        catch (JsonException) { return null; }
        var profile = profiles.RequireEffective(e.OccurredAtUtc);
        if (profile?.Metadata is not { } metadata || metadata.Status != "ACTIVE" || metadata.Scope != AuthenticationRejectionAuditProfile.Scope ||
            !metadata.EffectiveInterval.Contains(e.OccurredAtUtc) || string.IsNullOrWhiteSpace(profile.ProcessingReference) ||
            string.IsNullOrWhiteSpace(profile.RetentionReference) || profile.DataClassification != e.DataClassification) return null;
        return new("AUTHENTICATION_REJECTION", e.Subject, null, null, metadata.VersionId, profile.ProcessingReference, profile.RetentionReference,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["auditProfileEvidenceReferences"] = JsonSerializer.Serialize(metadata.ApprovalEvidenceReferences.Order(StringComparer.Ordinal)),
                ["auditProfileClassification"] = profile.DataClassification
            });
    }
}
