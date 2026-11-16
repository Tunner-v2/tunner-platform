using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Tunner.Database.Outbox;

namespace Tunner.Identity.Persistence;

/// <summary>Existing local Identity producers only. Terminal owner records, original event versions and safe typed payloads
/// establish historical facts; later authenticator/session eligibility does not erase an already committed mutation.</summary>
public sealed class IdentitySecurityPostCommitOwnerSource(IDbContextFactory<IdentityPersistenceDbContext> contexts) : IIdentityPostCommitOwnerSource
{
    public bool Owns(PendingOutboxMessage message) => (message.EventType is "IdentitySecurityActivityRequested" or "IdentitySecurityNotificationRequested" or
        "AuthenticatorAdded" or "AuthenticatorRevoked" or "ExternalIdentityLinked" or "ExternalIdentityUnlinked" or
        "RecoveryCodeSetGenerated" or "RecoveryCodeSetReplaced" or "TunnerSessionAuthenticated" or "TunnerSessionRevoked" or "TunnerSessionExpired" or
        "StepUpProofIssued" or "StepUpProofConsumed" or "StepUpProofExpired" or "DeviceTrustEvaluated" or "DeviceEvidenceObserved" or "DeviceTrustRevoked") &&
        !(message.EventType == "ExternalIdentityLinked" && message.Payload == JsonSerializer.Serialize(new { reference = message.Subject }));

    public async ValueTask<IdentityPostCommitOwnerFacts?> ReadCommittedAsync(PendingOutboxMessage message, CancellationToken cancellationToken)
    {
        var e = message; var ct = cancellationToken;
        if (!Owns(e) || !long.TryParse(e.ResourceVersion, NumberStyles.None, CultureInfo.InvariantCulture, out var version) || version < 1) return null;
        if (e.EventType is not ("DeviceTrustEvaluated" or "DeviceEvidenceObserved" or "DeviceTrustRevoked") && e.DataClassification != "RESTRICTED_SECURITY") return null;
        await using var db = await contexts.CreateDbContextAsync(ct).ConfigureAwait(false);
        using var document = JsonDocument.Parse(e.Payload); var p = document.RootElement;
        if (p.ValueKind != JsonValueKind.Object || p.EnumerateObject().Any(x => x.Value.ValueKind is not (JsonValueKind.String or JsonValueKind.Null))) return null;
        if (p.TryGetProperty("SchemaVersion", out _)) return await Passkey(db, e, p, version, ct).ConfigureAwait(false);
        if (p.TryGetProperty("authenticatorType", out _)) return await Totp(db, e, p, version, ct).ConfigureAwait(false);
        if (p.TryGetProperty("linkReference", out _)) return await Federation(db, e, p, version, ct).ConfigureAwait(false);
        if (e.EventType is "RecoveryCodeSetGenerated" or "RecoveryCodeSetReplaced") return await RecoveryCodes(db, e, p, version, ct).ConfigureAwait(false);
        if (!Properties(p, "reference") || !Guid.TryParseExact(p.GetProperty("reference").GetString(), "N", out var reference)) return null;
        if (e.EventType is "TunnerSessionAuthenticated" or "TunnerSessionRevoked" or "TunnerSessionExpired")
        {
            var row = await db.Set<AuthenticationSessionRow>().AsNoTracking().SingleOrDefaultAsync(x => x.SessionId == reference, ct).ConfigureAwait(false);
            if (row is null || e.Subject != reference.ToString("N") || version > row.Version ||
                e.EventType != "TunnerSessionAuthenticated" && (row.Version != version || row.OperationId != e.OperationId || row.CorrelationId != e.CorrelationId || row.RevokedAt != e.OccurredAtUtc) ||
                e.EventType == "TunnerSessionAuthenticated" && version != 1) return null;
            var facts = Facts("SESSION", e.Subject, row.AccountId, row.SessionId, row.PolicyVersion, row.ProcessingReference, row.RetentionReference);
            // End can also be invoked by maintenance or credential retirement; its target is not an acting user witness.
            return e.EventType == "TunnerSessionAuthenticated" ? facts : TargetOnly(facts);
        }
        if (e.EventType is "StepUpProofIssued" or "StepUpProofConsumed" or "StepUpProofExpired")
        {
            var row = await db.Set<StepUpProofRow>().AsNoTracking().SingleOrDefaultAsync(x => x.ProofId == reference, ct).ConfigureAwait(false);
            if (row is null || e.Subject != reference.ToString("N") || row.Version < version || row.OperationId != e.OperationId || row.CorrelationId != e.CorrelationId ||
                (e.EventType == "StepUpProofIssued" ? version != 1 : row.Version != version || row.EndedAt != e.OccurredAtUtc)) return null;
            var facts = Facts("STEP_UP", e.Subject, row.AccountId, row.SessionId, row.PolicyVersion, row.ProcessingReference, row.RetentionReference);
            return e.EventType == "StepUpProofExpired" ? TargetOnly(facts) : facts;
        }
        if (e.EventType is "DeviceTrustEvaluated" or "DeviceTrustRevoked")
        {
            var row = await db.Set<DeviceTrustRow>().AsNoTracking().SingleOrDefaultAsync(x => x.OutboxEventId == e.EventId, ct).ConfigureAwait(false);
            if (row is null || row.DeviceId != reference || e.Subject != reference.ToString("N") || row.Version != version || row.OperationId != e.OperationId ||
                row.CorrelationId != e.CorrelationId || row.RecordedAt != e.OccurredAtUtc || (e.EventType == "DeviceTrustRevoked") != (row.State == "REVOKED")) return null;
            if (e.EventType == "DeviceTrustRevoked")
            {
                // SourceSessionId witnesses the original device observation, not the later authenticated remover.
                var removal = await db.Set<DeviceOperationRow>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == row.RemovalOperationId, ct).ConfigureAwait(false);
                if (removal is null || removal.State != "COMPLETED" || removal.Version != 2 || removal.AccountId != row.AccountId ||
                    removal.Origin != row.Origin || removal.DeviceId != row.DeviceId || removal.ExpectedDeviceVersion != row.DeviceVersion ||
                    removal.ResultTrustVersion != row.Version || removal.ExpectedTrustVersion != row.Version - 1 ||
                    removal.OutboxEventId != e.EventId || removal.CompletedAt != row.RecordedAt ||
                    removal.PolicyVersion != row.PolicyVersion || removal.PolicySnapshot != row.PolicySnapshot ||
                    e.OperationId != "OP-DEVICE-TRUST-REVOKE-" + removal.Id.ToString("N") || removal.CorrelationId != e.CorrelationId ||
                    removal.ProofId is not { } proofId) return null;
                var proof = await db.Set<StepUpProofRow>().AsNoTracking().SingleOrDefaultAsync(x => x.ProofId == proofId, ct).ConfigureAwait(false);
                if (proof is null || proof.State != "CONSUMED" || proof.Version != 2 || proof.AccountId != removal.AccountId ||
                    proof.SessionId != removal.SessionId || proof.Origin != removal.Origin || proof.Scheme != AuthenticationIdentity.Scheme ||
                    proof.OperationId != e.OperationId || proof.CorrelationId != e.CorrelationId || proof.Operation != DeviceApplication.RevokeScope.Operation ||
                    proof.Context != DeviceApplication.RevokeScope.Context || proof.ResourceId != e.Subject || proof.ExpectedVersion != removal.ExpectedTrustVersion ||
                    proof.ContextDigest != removal.ContextDigest || proof.PolicyVersion != removal.AssurancePolicyVersion || proof.ProtectedKey.Length != 0 ||
                    row.RecordedAt < proof.AuthenticatedAt || proof.EndedAt is not { } consumed || consumed < row.RecordedAt || consumed >= proof.ExpiresAt) return null;
                var facts = DeviceFacts(e, "DEVICE_TRUST", removal.AccountId, removal.SessionId, row.PolicySnapshot, row.PolicyVersion);
                return facts is null ? null : facts with { References = new Dictionary<string, string>(StringComparer.Ordinal) {
                    ["sourceSessionReference"] = row.SourceSessionId.ToString("N"), ["removalOperationReference"] = removal.Id.ToString("N") } };
            }
            return DeviceFacts(e, "DEVICE_TRUST", row.AccountId, row.SourceSessionId, row.PolicySnapshot, row.PolicyVersion);
        }
        if (e.EventType == "DeviceEvidenceObserved")
        {
            var row = await db.Set<DeviceSessionEvidenceRow>().AsNoTracking().SingleOrDefaultAsync(x => x.OutboxEventId == e.EventId, ct).ConfigureAwait(false);
            if (row is null || row.DeviceId != reference || e.Subject != reference.ToString("N") || row.DeviceVersion != version || row.ObservedAt != e.OccurredAtUtc) return null;
            var trust = await db.Set<DeviceTrustRow>().AsNoTracking().SingleAsync(x => x.DeviceId == reference && x.Version == row.TrustVersion, ct).ConfigureAwait(false);
            if (trust.OperationId != e.OperationId || trust.CorrelationId != e.CorrelationId || trust.SourceSessionId != row.SessionId || trust.PolicySnapshot != row.PolicySnapshot) return null;
            return DeviceFacts(e, "DEVICE_EVIDENCE", row.AccountId, row.SessionId, row.PolicySnapshot, trust.PolicyVersion);
        }
        if (e.EventType == "IdentitySecurityActivityRequested")
        {
            var result = await db.Set<RecoveryResultRow>().AsNoTracking().SingleOrDefaultAsync(x => x.ResultId == reference && x.RepairOutboxEventId == e.EventId, ct).ConfigureAwait(false);
            if (result is null || e.Subject != result.CaseId.ToString("N") || e.OperationId != "OP-RECOVERY-CASE-" + result.CaseId.ToString("N") || result.RecordedAt != e.OccurredAtUtc) return null;
            var recovery = await db.Set<RecoveryCaseRow>().AsNoTracking().SingleAsync(x => x.CaseId == result.CaseId, ct).ConfigureAwait(false);
            if (e.CorrelationId != recovery.RequestId.ToString("N") || version != recovery.Version) return null;
            return TargetOnly(Facts("RECOVERY_RESULT", reference.ToString("N"), result.AccountId, result.TargetSessionId,
                result.PolicyVersion, result.ProcessingReference, result.RetentionReference, ("outcome", result.Outcome)));
        }
        if (e.EventType == "IdentitySecurityNotificationRequested")
        {
            var challenge = await db.Set<RecoveryContactChallengeRow>().AsNoTracking().SingleOrDefaultAsync(x => x.DeliveryId == reference && x.NotificationOutboxEventId == e.EventId, ct).ConfigureAwait(false);
            if (challenge is null || e.Subject != reference.ToString("N") || version != 1 || e.OperationId != "OP-RECOVERY-CASE-" + challenge.CaseId.ToString("N")) return null;
            var recovery = await db.Set<RecoveryCaseRow>().AsNoTracking().SingleAsync(x => x.CaseId == challenge.CaseId, ct).ConfigureAwait(false);
            if (e.CorrelationId != recovery.RequestId.ToString("N")) return null;
            return TargetOnly(Facts("RECOVERY_CONTACT", reference.ToString("N"), recovery.AccountId, null, recovery.PolicyVersion,
                challenge.ProcessingReference, challenge.RetentionReference, ("deliveryReference", reference.ToString("N")), ("challengeReference", challenge.ChallengeId.ToString("N"))));
        }
        return null;
    }

    private static async Task<IdentityPostCommitOwnerFacts?> Passkey(IdentityPersistenceDbContext db, PendingOutboxMessage e, JsonElement p, long version, CancellationToken ct)
    {
        if (!Properties(p, "SchemaVersion", "OperationReference", "AuthenticatorReference", "AuthenticatorType", "Operation", "EventType")) return null;
        var intent = p.Deserialize<PasskeySecurityIntent>();
        if (intent is null || intent.SchemaVersion != "1" || intent.AuthenticatorType != "PASSKEY" || intent.EventType != e.EventType) return null;
        var row = await db.Set<PasskeyCeremonyRow>().AsNoTracking().SingleOrDefaultAsync(x => x.CeremonyId == intent.OperationReference, ct).ConfigureAwait(false);
        if (row is null || row.AccountId is null || row.CredentialReference is null || row.State != "COMPLETED" || row.Version != version || row.Kind is not ("ENROLL" or "REVOKE") ||
            e.EventType is not ("IdentitySecurityActivityRequested" or "IdentitySecurityNotificationRequested") && e.EventType != (row.Kind == "ENROLL" ? "AuthenticatorAdded" : "AuthenticatorRevoked") ||
            intent.AuthenticatorReference != row.CredentialReference || intent.Operation != (row.Kind == "ENROLL" ? "REGISTER_PASSKEY" : "REVOKE_PASSKEY") ||
            e.Subject != (row.Kind == "REVOKE" ? row.CredentialReference.Value : row.CeremonyId).ToString("N") ||
            e.OperationId != "OP-PASSKEY-" + row.CeremonyId.ToString("N") || e.CorrelationId != row.CeremonyId.ToString("N")) return null;
        return Facts("PASSKEY", row.CeremonyId.ToString("N"), row.AccountId, row.SessionId, row.PolicyVersion,
            row.ProcessingReference, row.RetentionReference, ("authenticatorReference", row.CredentialReference.Value.ToString("N")), ("operation", intent.Operation));
    }

    private static async Task<IdentityPostCommitOwnerFacts?> Totp(IdentityPersistenceDbContext db, PendingOutboxMessage e, JsonElement p, long version, CancellationToken ct)
    {
        if (!Properties(p, "schemaVersion", "reference", "authenticatorReference", "authenticatorType", "operation") ||
            p.GetProperty("schemaVersion").GetString() != "1" || p.GetProperty("authenticatorType").GetString() != "TOTP" || !p.GetProperty("reference").TryGetGuid(out var id)) return null;
        var row = await db.Set<TotpOperationRow>().AsNoTracking().SingleOrDefaultAsync(x => x.EnrollmentId == id, ct).ConfigureAwait(false);
        if (row is null || row.State != "COMPLETED" || row.Version != version || e.Subject != row.AuthenticatorReference.ToString("N") ||
            !p.GetProperty("authenticatorReference").TryGetGuid(out var authenticator) || authenticator != row.AuthenticatorReference) return null;
        var op = TotpApplication.Operation(row);
        if (p.GetProperty("operation").GetString() != op.Scope.Operation || e.OperationId != op.OperationId || e.CorrelationId != op.CorrelationId ||
            e.EventType is not ("IdentitySecurityActivityRequested" or "IdentitySecurityNotificationRequested") && e.EventType != (row.Kind == "ENROLL" ? "AuthenticatorAdded" : "AuthenticatorRevoked")) return null;
        return Facts("TOTP", id.ToString("N"), row.AccountId, row.SessionId, row.PolicyVersion, row.ProcessingReference, row.RetentionReference,
            ("authenticatorReference", authenticator.ToString("N")), ("operation", op.Scope.Operation));
    }

    private static async Task<IdentityPostCommitOwnerFacts?> Federation(IdentityPersistenceDbContext db, PendingOutboxMessage e, JsonElement p, long version, CancellationToken ct)
    {
        if (!Properties(p, "reference", "linkReference", "operation") || !p.GetProperty("reference").TryGetGuid(out var id)) return null;
        var row = await db.Set<FederationLinkOperationRow>().AsNoTracking().SingleOrDefaultAsync(x => x.OperationId == id, ct).ConfigureAwait(false);
        if (row is null || row.State != "COMPLETED" || row.Version != version || row.ResultLinkId is not { } link ||
            !p.GetProperty("linkReference").TryGetGuid(out var actualLink) || actualLink != link || e.Subject != link.ToString("N")) return null;
        var op = FederationLinkApplication.Operation(row);
        if (p.GetProperty("operation").GetString() != op.Scope.Operation || e.OperationId != op.OperationId || e.CorrelationId != op.CorrelationId ||
            e.EventType is not ("IdentitySecurityActivityRequested" or "IdentitySecurityNotificationRequested") && e.EventType != (row.Kind == "LINK" ? "ExternalIdentityLinked" : "ExternalIdentityUnlinked")) return null;
        return Facts("FEDERATION_LINK", id.ToString("N"), row.AccountId, row.SessionId, row.PolicyVersion, row.ProcessingReference, row.RetentionReference,
            ("linkReference", link.ToString("N")), ("operation", op.Scope.Operation));
    }

    private static async Task<IdentityPostCommitOwnerFacts?> RecoveryCodes(IdentityPersistenceDbContext db, PendingOutboxMessage e, JsonElement p, long version, CancellationToken ct)
    {
        if (!Properties(p, "reference", "setReference", "operation") || !Guid.TryParseExact(p.GetProperty("reference").GetString(), "N", out var id)) return null;
        var row = await db.Set<RecoveryCodeOperationRow>().AsNoTracking().SingleOrDefaultAsync(x => x.OperationId == id, ct).ConfigureAwait(false);
        if (row is null || row.State != "COMPLETED" || row.Version != version || e.Subject != row.SetReference.ToString("N") ||
            !p.GetProperty("setReference").TryGetGuid(out var set) || set != row.SetReference ||
            e.EventType != (row.Kind == "GENERATE" ? "RecoveryCodeSetGenerated" : "RecoveryCodeSetReplaced")) return null;
        var op = RecoveryCodeApplication.Operation(row);
        if (p.GetProperty("operation").GetString() != op.Scope.Operation || e.OperationId != op.OperationId || e.CorrelationId != op.CorrelationId) return null;
        return Facts("RECOVERY_CODE_SET", id.ToString("N"), row.AccountId, row.SessionId, row.PolicyVersion, row.ProcessingReference, row.RetentionReference,
            ("setReference", set.ToString("N")), ("operation", op.Scope.Operation));
    }

    private static bool Properties(JsonElement value, params string[] names)
    {
        var actual = value.EnumerateObject().Select(x => x.Name).ToArray();
        return actual.Length == names.Length && actual.Distinct(StringComparer.Ordinal).Count() == actual.Length && names.All(x => actual.Contains(x, StringComparer.Ordinal));
    }
    private static IdentityPostCommitOwnerFacts? DeviceFacts(PendingOutboxMessage e, string kind, string account, Guid session, string snapshot, string version)
    {
        using var document = JsonDocument.Parse(snapshot); var policy = document.RootElement;
        if (policy.GetProperty("Metadata").GetProperty("VersionId").GetString() != version || policy.GetProperty("Classification").GetString() != e.DataClassification) return null;
        return Facts(kind, e.Subject, account, session, version, policy.GetProperty("ProcessingReference").GetString()!, policy.GetProperty("RetentionReference").GetString()!);
    }
    private static IdentityPostCommitOwnerFacts Facts(string kind, string reference, string account, Guid? session,
        string policy, string processing, string retention, params (string Key, string Value)[] references) =>
        new(kind, reference, account, session?.ToString("N"), policy, processing, retention, references.ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal));

    private static IdentityPostCommitOwnerFacts TargetOnly(IdentityPostCommitOwnerFacts facts)
    {
        var references = facts.References.ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal);
        references.Add("targetAccountReference", facts.ActorAccountId!);
        return facts with { ActorAccountId = null, References = references };
    }
}
