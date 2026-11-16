using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Tunner.Database.Outbox;
using Tunner.Platform.Policies;

namespace Tunner.Identity.Persistence;

/// <summary>Explicit historical audit authority for rejection producers, not their unrecorded credential/assurance policy.
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

/// <summary>The independently committed, reference-only outbox event is the historical rejection witness.
/// Only step-up has an authenticated session witness. Current generic audit qualification remains separately required.</summary>
public sealed class AuthenticationRejectionPostCommitOwnerSource(IDbContextFactory<IdentityPersistenceDbContext> contexts,
    IAuthenticationRejectionAuditProfileSource profiles) : IIdentityPostCommitOwnerSource
{
    public bool Owns(PendingOutboxMessage message) => message.EventType is "AuthenticationRejected" or "StepUpAuthenticationRejected";

    public async ValueTask<IdentityPostCommitOwnerFacts?> ReadCommittedAsync(PendingOutboxMessage message, CancellationToken cancellationToken)
    {
        var e = message; var ct = cancellationToken;
        var stepUp = e.EventType == "StepUpAuthenticationRejected";
        if (!Owns(e) || e.EventId == Guid.Empty || e.Source != "tunner/identity" || e.EnvelopeVersion != "1.0" ||
            e.EventSchemaVersion != "1" || e.ResourceVersion != "1" || e.DataClassification != "RESTRICTED_SECURITY" || e.CausationEventId is not null ||
            !Guid.TryParseExact(e.Subject, "N", out var reference) || reference == Guid.Empty ||
            string.IsNullOrWhiteSpace(e.OperationId) || string.IsNullOrWhiteSpace(e.CorrelationId) ||
            !stepUp && (e.OperationId != "OP-" + e.Subject || !Guid.TryParseExact(e.CorrelationId, "N", out var correlation) || correlation == Guid.Empty)) return null;
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
        AuthenticationSessionRow? session = null;
        if (stepUp)
        {
            session = await db.Set<AuthenticationSessionRow>().AsNoTracking().SingleOrDefaultAsync(x => x.SessionId == reference, ct).ConfigureAwait(false);
            if (session is null || session.Scheme != AuthenticationIdentity.Scheme || string.IsNullOrWhiteSpace(session.AccountId)) return null;
            // Eligibility/stamp/version may have changed since failure. Session identity and Account binding are immutable.
        }
        return new(stepUp ? "STEP_UP_AUTHENTICATION_REJECTION" : "AUTHENTICATION_REJECTION", e.Subject, session?.AccountId, session?.SessionId.ToString("N"),
            metadata.VersionId, profile.ProcessingReference, profile.RetentionReference,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["auditProfileEvidenceReferences"] = JsonSerializer.Serialize(metadata.ApprovalEvidenceReferences.Order(StringComparer.Ordinal)),
                ["auditProfileClassification"] = profile.DataClassification
            });
    }
}
