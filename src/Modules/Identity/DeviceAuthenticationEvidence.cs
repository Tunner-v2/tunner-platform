using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace Tunner.Identity.Persistence;

internal sealed partial class DeviceApplication
{
    /// <summary>Called only after the real ticket-store grant guard. A separate atomic evidence transaction cannot establish a session.</summary>
    internal async Task<bool> ObserveAuthenticatedAsync(StepUpCaller caller, AuthenticationGrant grant, string installationToken,
        IReadOnlyDictionary<string, string> characteristics, CancellationToken ct)
    {
        Clean();
        if (installationToken is not { Length: 64 } || !installationToken.All(Uri.IsHexDigit) ||
            AuthenticationIdentity.SessionId(grant.Ticket.Principal) != caller.SessionId ||
            grant.Ticket.AuthenticationScheme != AuthenticationIdentity.Scheme)
            throw new ArgumentException("Current issued authentication and an independent opaque installation correlation are required.");
        var at = clock.GetUtcNow(); var initial = Policy(caller.Origin, at); var safeJson = initial.Characteristics(characteristics);
        var correlation = Correlation(caller.AccountId, caller.Origin, installationToken);
        try
        {
            await using var tx = await database.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
            var session = await authentication.LockForOperationAsync(caller, ObserveOperation, ct).ConfigureAwait(false);
            if (session is null)
            {
                // Ordinary authentication validation may have ended an expired or invalid-method session.
                // Preserve that authoritative transition even though no device evidence is admitted.
                await database.SaveChangesAsync(ct).ConfigureAwait(false); await tx.CommitAsync(ct).ConfigureAwait(false); return false;
            }
            if (session.KeyDigest != AuthenticationIdentity.Digest(grant.Key) ||
                session.AuthenticatedAt != grant.Ticket.Properties.IssuedUtc || session.ExpiresAt != grant.Ticket.Properties.ExpiresUtc)
            { await tx.RollbackAsync(ct).ConfigureAwait(false); database.ChangeTracker.Clear(); return false; }
            var account = await database.Accounts.SingleAsync(x => x.AccountId == caller.AccountId, ct).ConfigureAwait(false);
            var priorObservation = await database.Set<DeviceSessionEvidenceRow>().AsNoTracking().SingleOrDefaultAsync(x => x.SessionId == caller.SessionId, ct).ConfigureAwait(false);
            if (priorObservation is not null)
            {
                // A retried authentication result cannot overwrite original device/trust evidence or create a second record.
                var original = await database.Set<DeviceRecordRow>().AsNoTracking().SingleAsync(x => x.DeviceId == priorObservation.DeviceId, ct).ConfigureAwait(false);
                var matches = original.AccountId == caller.AccountId && original.Origin == caller.Origin && original.CorrelationDigest == correlation;
                await tx.RollbackAsync(ct).ConfigureAwait(false); database.ChangeTracker.Clear(); return matches;
            }
            var record = await database.Set<DeviceRecordRow>().SingleOrDefaultAsync(x => x.AccountId == caller.AccountId &&
                x.Origin == caller.Origin && x.CorrelationDigest == correlation, ct).ConfigureAwait(false);
            if (record is not null)
            {
                await LockDevice(record.DeviceId, ct).ConfigureAwait(false); await database.Entry(record).ReloadAsync(ct).ConfigureAwait(false);
                if (record.Version == long.MaxValue) throw new InvalidOperationException("Device evidence version is exhausted.");
            }
            at = clock.GetUtcNow(); var policy = Policy(caller.Origin, at);
            if (initial.Snapshot != policy.Snapshot) throw new InvalidOperationException("Device qualification changed during collection.");
            var fresh = record is null;
            record ??= new DeviceRecordRow { DeviceId = Guid.NewGuid(), AccountId = caller.AccountId, Origin = caller.Origin,
                CorrelationDigest = correlation, CreatedAt = IdentityPersistenceTime.Normalize(at), Version = 0 };
            var priorTrust = fresh ? null : await LatestTrust(record.DeviceId, ct).ConfigureAwait(false);
            if (!fresh && priorTrust is null || priorTrust?.Version == long.MaxValue) throw new InvalidOperationException("Device trust lineage is unavailable.");
            var facts = new DeviceTrustEligibilityFacts(caller.AccountId, account.Version, caller.Origin, record.DeviceId,
                record.Version + 1, priorTrust?.Version ?? 0, priorTrust?.State, session.SessionId, session.AuthenticationMethods,
                session.Assurance, session.AuthenticatedAt, AuthenticationIdentity.Digest(safeJson), policy.Metadata.VersionId);
            var decision = Decision(facts, policy, clock.GetUtcNow());
            var operation = new StepUpOperation("OP-DEVICE-OBSERVE-" + session.SessionId.ToString("N"), session.SessionId.ToString("N"),
                new(ObserveOperation, "USER_BFF"), record.DeviceId.ToString("N"), record.Version, facts.Digest);
            var authPolicy = authenticationPolicies.RequireEffective(clock.GetUtcNow()); authPolicy.Validate();
            var method = await authentication.MethodIsCurrentAsync(session, authPolicy, clock.GetUtcNow(), ct).ConfigureAwait(false);
            var access = await authentication.VerifyContinuedOperationAsync(caller, operation, session, ct).ConfigureAwait(false);
            at = clock.GetUtcNow(); policy = Policy(caller.Origin, at);
            var finalAuth = authenticationPolicies.RequireEffective(at); finalAuth.Validate(); var finalDecision = Decision(facts, policy, at);
            if (initial.Snapshot != policy.Snapshot || authPolicy != finalAuth || !method || session.State != "ACTIVE" ||
                AuthenticationApplication.Expired(session, finalAuth, at) || !authentication.MethodFactsCurrentAt(session, finalAuth, at) ||
                access is null || !access.SessionUseInterval.Contains(at) || !access.OperationInterval.Contains(at) || !SameDecision(decision, finalDecision, at))
                throw new InvalidOperationException("Current qualified device collection authority is unavailable.");
            var recordedAt = IdentityPersistenceTime.Normalize(at);
            record.Version++; record.LastObservedAt = recordedAt; record.CharacteristicsJson = safeJson;
            record.PolicyVersion = policy.Metadata.VersionId; record.PolicySnapshot = policy.Snapshot;
            record.CollectionReference = policy.CollectionReference; record.InventoryReference = policy.InventoryReference;
            record.ProcessingReference = policy.ProcessingReference; record.RetentionReference = policy.RetentionReference;
            record.Classification = policy.Classification;
            if (fresh) database.Add(record);
            var trustEvent = Guid.NewGuid(); var observationEvent = Guid.NewGuid();
            var trust = new DeviceTrustRow { DeviceId = record.DeviceId, AccountId = record.AccountId, Origin = record.Origin,
                Version = (priorTrust?.Version ?? 0) + 1, DeviceVersion = record.Version,
                State = decision.Allowed ? "TRUSTED" : "UNTRUSTED", SourceSessionId = session.SessionId,
                EligibilityFactsJson = JsonSerializer.Serialize(facts), EligibilityDecisionJson = JsonSerializer.Serialize(decision),
                PolicyVersion = policy.Metadata.VersionId, PolicySnapshot = policy.Snapshot, RecordedAt = recordedAt,
                OperationId = operation.OperationId, CorrelationId = operation.CorrelationId, OutboxEventId = trustEvent };
            database.Add(trust);
            database.Add(new DeviceSessionEvidenceRow { SessionId = session.SessionId, AccountId = record.AccountId, Origin = record.Origin,
                DeviceId = record.DeviceId, DeviceVersion = record.Version, TrustVersion = trust.Version, ObservedAt = recordedAt,
                PolicySnapshot = policy.Snapshot, CharacteristicsJson = safeJson, OutboxEventId = observationEvent });
            Event(trustEvent, "DeviceTrustEvaluated", record.DeviceId, trust.Version, operation.OperationId, operation.CorrelationId, policy.Classification, recordedAt);
            Event(observationEvent, "DeviceEvidenceObserved", record.DeviceId, record.Version, operation.OperationId, operation.CorrelationId, policy.Classification, recordedAt);
            await database.SaveChangesAsync(ct).ConfigureAwait(false); await tx.CommitAsync(ct).ConfigureAwait(false); return true;
        }
        catch { database.ChangeTracker.Clear(); throw; }
    }
}
