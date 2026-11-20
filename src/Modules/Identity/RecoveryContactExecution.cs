using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Tunner.Database.Outbox;

namespace Tunner.Identity.Persistence;

internal sealed partial class RecoveryCaseApplication
{
    internal async Task<RecoveryContactOptionsResult> StartPasswordContactAsync(RecoveryEntryCaller caller, Guid requestId, CancellationToken ct = default)
    {
        Clean(); if (!caller.Valid || requestId == Guid.Empty) return new("VALIDATION_ERROR");
        if (!ContactAvailable(caller.Origin, clock.GetUtcNow())) return new("RECOVERY_UNAVAILABLE");
        var initial = PasswordCurrent(caller.Origin, clock.GetUtcNow());
        var original = await database.Set<RecoveryEntryRow>().AsNoTracking().SingleOrDefaultAsync(x => x.RequestId == requestId && x.BrowserDigest == caller.BrowserDigest, ct).ConfigureAwait(false);
        if (original is null || !EntryActor(caller, original) || !EntryCurrent(caller, original, clock.GetUtcNow()) || original.State != "OPEN" || clock.GetUtcNow() >= original.ExpiresAt)
            return new("RECOVERY_PROOF_REJECTED");
        if (!await RecoveryEntryBudgetAsync(caller, original, initial, ct).ConfigureAwait(false)) return new("RATE_OR_ABUSE_LIMITED", original);
        await using var tx = await database.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        try
        {
            await LockEntry(caller, requestId, ct).ConfigureAwait(false); var entry = await Entry(caller, requestId, ct).ConfigureAwait(false);
            var at = clock.GetUtcNow(); var policy = PasswordCurrent(caller.Origin, at); var profile = policy.Values.ContactRecoveryProfile;
            if (entry is null || profile is null || initial.Snapshot != policy.Snapshot || !EntryCurrent(caller, entry, at) || entry.State != "OPEN" || at >= entry.ExpiresAt)
                return new("RECOVERY_PROOF_REJECTED");
            if (entry.ContactChallengeId is { } prior)
            {
                if (entry.ContactProfileVersion != profile.Version || entry.ContactExpiresAt is null) return new("RECOVERY_UNAVAILABLE");
                await tx.CommitAsync(ct).ConfigureAwait(false); return new(null, entry, new(prior, entry.ContactExpiresAt.Value));
            }
            var publicId = Guid.NewGuid(); var expires = IdentityPersistenceTime.Normalize(Clip(at + profile.ProofLifetime, entry.ExpiresAt));
            RecoveryContactChallengeRow? challenge = null; RecoveryCaseRow? row = null; RecoveryCaseLockedFacts? facts = null;
            IReadOnlyList<RecoveryCaseCodeSetFacts> sets = [];
            var snapshot = await database.Set<RecoveryCaseRow>().AsNoTracking().SingleOrDefaultAsync(x => x.EntryId == entry.EntryId && x.ProofKind == "CONTACT_RISK", ct).ConfigureAwait(false);
            if (snapshot is not null)
            {
                var actor = new RecoveryCaseCaller(snapshot.AccountId, caller.BrowserDigest, caller.Origin, caller.NetworkDigest);
                await LockOriginal(actor, requestId, ct).ConfigureAwait(false); row = await Original(actor, requestId, ct).ConfigureAwait(false);
                facts = row is null ? null : await LoadAccountFacts(row.AccountId, null, ct).ConfigureAwait(false);
                if (row is not null) { await LockRecoveryContact(row, ct).ConfigureAwait(false); sets = await CodeSets(row.AccountId, ct).ConfigureAwait(false); }
            }
            // The same cryptographic preparation runs for neutral entries. Only a current actual case can retain a verifier or notify a verified destination.
            var prepared = IdentityContactChallenge.Create(ContactProofProtection, row is null ? entry.EntryId.ToString("N") + ":" + policy.Values.Version : ContactProofBinding(row), profile.ProofDigits);
            if (row is not null && facts is not null)
            {
                try
                {
                    at = clock.GetUtcNow();
                    if (row.State == "PENDING_CONTACT_RISK" && row.Version == 1 && at < row.ExpiresAt && ContactPasswordAuthorityCurrent(caller, entry, row, facts, sets, at, false, out _))
                    {
                        var notification = NotificationCurrent(row, profile, at); var eventId = Guid.NewGuid();
                        var actualExpiry = IdentityPersistenceTime.Normalize(Clip(expires, row.ExpiresAt, notification.EffectiveInterval.Until));
                        if (at < actualExpiry)
                        {
                            challenge = new RecoveryContactChallengeRow { ChallengeId = row.ContactChallengeId!.Value, CaseId = row.CaseId, EntryId = entry.EntryId,
                                ContactReference = row.ContactReference!.Value, ContactVersion = row.ExpectedContactVersion!.Value, ContextDigest = row.ContextDigest,
                                DestinationDigest = row.ContactDestinationDigest!, ProfileVersion = profile.Version, ProtectedCode = prepared.Code, ProtectedDigest = prepared.Digest,
                                ProtectedAddress = ContactAddressProtection.Protect(facts.User.Email!), MaximumAttempts = profile.MaximumProofAttempts,
                                DeliveryId = Guid.NewGuid(), NotificationOutboxEventId = eventId, NotificationSnapshot = notification.Snapshot(),
                                CreatedAt = IdentityPersistenceTime.Normalize(at), ExpiresAt = actualExpiry, PurgeAfter = IdentityPersistenceTime.Normalize(at + profile.PendingMaterialRetention),
                                ProcessingReference = row.ProcessingReference, RetentionReference = row.RetentionReference };
                            publicId = challenge.ChallengeId; database.Add(challenge);
                            database.Add(new OutboxMessage(eventId, "1.0", "IdentitySecurityNotificationRequested", "1", at, "tunner/identity", challenge.DeliveryId.ToString("N"),
                                "RESTRICTED_SECURITY", JsonSerializer.Serialize(new { reference = challenge.DeliveryId.ToString("N") }), "1", OperationId(row), requestId.ToString("N")));
                        }
                    }
                }
                catch (Exception error) when (error is InvalidOperationException or ArgumentException) { if (challenge is not null) throw; }
            }
            entry.ContactChallengeId = publicId; entry.ContactProfileVersion = profile.Version; entry.ContactExpiresAt = expires;
            at = clock.GetUtcNow();
            if (!EntryCurrent(caller, entry, at) || policy.Snapshot != PasswordCurrent(caller.Origin, at).Snapshot || at >= expires ||
                challenge is not null && (row is null || facts is null || !ContactPasswordAuthorityCurrent(caller, entry, row, facts, sets, at, false, out _) ||
                    NotificationCurrent(row, profile, at).Snapshot() != challenge.NotificationSnapshot))
                throw new InvalidOperationException("Recovery contact presentation/notification authority changed before commit.");
            database.ChangeTracker.DetectChanges();
            if (database.ChangeTracker.Entries().Any(x => (x.State is EntityState.Added or EntityState.Modified or EntityState.Deleted) &&
                !ReferenceEquals(x.Entity, entry) && !ReferenceEquals(x.Entity, challenge) && !(x.Entity is OutboxMessage o && o.EventId == challenge?.NotificationOutboxEventId && x.State == EntityState.Added)))
                throw new InvalidOperationException("Contact challenge cannot mutate another owner source.");
            await database.SaveChangesAsync(ct).ConfigureAwait(false); at = clock.GetUtcNow();
            if (!EntryCurrent(caller, entry, at) || policy.Snapshot != PasswordCurrent(caller.Origin, at).Snapshot || at >= expires ||
                challenge is not null && (row is null || facts is null || !ContactPasswordAuthorityCurrent(caller, entry, row, facts, sets, at, false, out _) ||
                    NotificationCurrent(row, profile, at).Snapshot() != challenge.NotificationSnapshot) || database.ChangeTracker.HasChanges())
                throw new InvalidOperationException("Recovery contact presentation authority changed after persistence.");
            await tx.CommitAsync(ct).ConfigureAwait(false); return new(null, entry, new(publicId, expires));
        }
        catch { database.ChangeTracker.Clear(); throw; }
    }
    internal async Task<RecoveryEntryResult> RepairPasswordWithContactAsync(RecoveryEntryCaller caller, Guid requestId, long expectedVersion,
        Guid challengeId, string? code, string? password, string? confirmation, CancellationToken ct = default)
    {
        Clean(); if (!caller.Valid || requestId == Guid.Empty || challengeId == Guid.Empty) return new("VALIDATION_ERROR");
        if (!ContactAvailable(caller.Origin, clock.GetUtcNow())) return new("RECOVERY_UNAVAILABLE");
        var policy = PasswordCurrent(caller.Origin, clock.GetUtcNow()); var profile = policy.Values.ContactRecoveryProfile!;
        if (code is null || code.Length != profile.ProofDigits || code.Any(x => x is < '0' or > '9')) return new("VALIDATION_ERROR");
        var original = await database.Set<RecoveryEntryRow>().AsNoTracking().SingleOrDefaultAsync(x => x.RequestId == requestId && x.BrowserDigest == caller.BrowserDigest, ct).ConfigureAwait(false);
        if (original is null || !EntryActor(caller, original)) return new("NOT_FOUND_OR_NOT_DISCLOSABLE");
        if (original.State == "REPAIRED")
        {
            var completed = await database.Set<RecoveryCaseRow>().AsNoTracking().SingleOrDefaultAsync(x => x.EntryId == original.EntryId, ct).ConfigureAwait(false);
            if (completed?.ProofKind != "CONTACT_RISK" || completed.ContactChallengeId != challengeId || original.ContactChallengeId != challengeId)
                return new("RECOVERY_PROOF_REJECTED", original);
            return await ReadPasswordEntryAsync(caller, requestId, ct).ConfigureAwait(false);
        }
        if (original.State != "OPEN" || original.Version != expectedVersion || !EntryCurrent(caller, original, clock.GetUtcNow())) return new("RECOVERY_PROOF_REJECTED", original);
        var prepared = await PrepareRecoveryPasswordAsync(caller, original, policy, password, confirmation, ct).ConfigureAwait(false);
        if (prepared.Error is not null) return new(prepared.Error, original);
        var snapshot = await database.Set<RecoveryCaseRow>().AsNoTracking().SingleOrDefaultAsync(x => x.EntryId == original.EntryId && x.ProofKind == "CONTACT_RISK", ct).ConfigureAwait(false);
        if (snapshot is null || snapshot.ContactChallengeId != challengeId || original.ContactChallengeId != challengeId) return new("RECOVERY_PROOF_REJECTED", original);
        await using var tx = await database.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        try
        {
            await LockEntry(caller, requestId, ct).ConfigureAwait(false); var entry = await Entry(caller, requestId, ct).ConfigureAwait(false);
            var actor = new RecoveryCaseCaller(snapshot.AccountId, caller.BrowserDigest, caller.Origin, caller.NetworkDigest);
            await LockOriginal(actor, requestId, ct).ConfigureAwait(false); var row = await Original(actor, requestId, ct).ConfigureAwait(false);
            var facts = row is null ? null : await LoadAccountFacts(row.AccountId, null, ct).ConfigureAwait(false);
            if (row is null || facts is null || entry is null || row.CaseId != snapshot.CaseId) return new("RECOVERY_PROOF_REJECTED");
            await LockRecoveryContact(row, ct).ConfigureAwait(false); var sets = await CodeSets(row.AccountId, ct).ConfigureAwait(false);
            var challenge = await ContactChallenge(challengeId, ct).ConfigureAwait(false);
            if (row.State == "REPAIRED" && entry.State == "REPAIRED") return await DescribeContactEntry(caller, entry, row, facts, tx, ct).ConfigureAwait(false);
            var at = clock.GetUtcNow();
            if (challenge is null || challenge.State != "PENDING" || challenge.Attempts >= profile.MaximumProofAttempts || at >= challenge.ExpiresAt || at >= row.ExpiresAt ||
                entry.State != "OPEN" || entry.Version != expectedVersion || entry.ContactChallengeId != challengeId || entry.ContactProfileVersion != profile.Version ||
                entry.ContactExpiresAt is not { } publicExpiry || at >= publicExpiry || challenge.CaseId != row.CaseId || challenge.EntryId != entry.EntryId ||
                challenge.ContextDigest != row.ContextDigest || challenge.ProfileVersion != profile.Version || challenge.MaximumAttempts != profile.MaximumProofAttempts ||
                challenge.ContactReference != row.ContactReference || challenge.ContactVersion != row.ExpectedContactVersion || challenge.DestinationDigest != row.ContactDestinationDigest ||
                policy.Snapshot != PasswordCurrent(caller.Origin, at).Snapshot || !ContactPasswordAuthorityCurrent(caller, entry, row, facts, sets, at, false, out var risk) || risk is null ||
                !ContactNotificationMatches(row, profile, challenge, at)) return new("RECOVERY_PROOF_REJECTED");
            if (!IdentityContactChallenge.Verify(ContactProofProtection, ContactProofBinding(row), challenge.ProtectedDigest, code))
            {
                challenge.Attempts++; challenge.Version++;
                if (challenge.Attempts >= challenge.MaximumAttempts) { challenge.State = "REJECTED"; challenge.EndedAt = IdentityPersistenceTime.Normalize(at); challenge.ProtectedCode = null; challenge.ProtectedDigest = null; }
                await database.SaveChangesAsync(ct).ConfigureAwait(false); await tx.CommitAsync(ct).ConfigureAwait(false); return new("RECOVERY_PROOF_REJECTED", entry);
            }
            var concurrency = facts.User.ConcurrencyStamp; ApplyPassword(row, facts.User, prepared.Hash!);
            var ended = IdentityPersistenceTime.Normalize(at); var eventId = Guid.NewGuid();
            var result = new RecoveryResultRow { ResultId = Guid.NewGuid(), CaseId = row.CaseId, AccountId = row.AccountId, Outcome = "PASSWORD_REPAIRED_NO_SESSION", ProofKind = "CONTACT_RISK",
                ContextDigest = row.ContextDigest, PolicyVersion = row.PolicyVersion, PolicySnapshot = row.PolicySnapshot, HierarchySnapshot = row.HierarchySnapshot,
                RecordedAt = ended, UnresolvedEffectsJson = row.UnresolvedEffectsJson, ProcessingReference = row.ProcessingReference, RetentionReference = row.RetentionReference,
                RepairOutboxEventId = eventId, PriorPasswordDigest = row.PriorPasswordDigest, RepairedPasswordDigest = AuthenticationIdentity.Digest(prepared.Hash!),
                PriorSecurityStampDigest = row.PriorSecurityStampDigest, RepairedSecurityStampDigest = AuthenticationIdentity.Digest(facts.User.SecurityStamp!), SessionEffect = row.SessionEffect,
                ContactReference = row.ContactReference, ContactVersion = row.ExpectedContactVersion, ContactChallengeId = challengeId,
                ContactDestinationDigest = row.ContactDestinationDigest, ContactRiskSnapshot = risk.Snapshot(), ContactNotificationDeliveryId = challenge.DeliveryId };
            challenge.State = "CONSUMED"; challenge.Version++; challenge.EndedAt = ended; challenge.ResultId = result.ResultId; challenge.ProtectedCode = null; challenge.ProtectedDigest = null;
            row.State = "REPAIRED"; row.Version++; row.ResultId = result.ResultId; row.EndedAt = ended;
            entry.State = "REPAIRED"; entry.Version++; entry.EndedAt = ended; database.Add(result);
            database.Add(new OutboxMessage(eventId, "1.0", "IdentitySecurityActivityRequested", "1", ended, "tunner/identity", row.CaseId.ToString("N"), "RESTRICTED_SECURITY",
                JsonSerializer.Serialize(new { reference = result.ResultId.ToString("N") }), row.Version.ToString(CultureInfo.InvariantCulture), OperationId(row), requestId.ToString("N")));
            database.ChangeTracker.DetectChanges(); at = clock.GetUtcNow();
            if (!ContactCompleted(entry, row, result, challenge, facts.User, at) || facts.User.PasswordHash != prepared.Hash || facts.User.ConcurrencyStamp == concurrency ||
                database.Entry(facts.User).State != EntityState.Modified || database.Entry(facts.User).Properties.Where(x => x.IsModified).Any(x =>
                    x.Metadata.Name is not (nameof(IdentityUserAdapter.PasswordHash) or nameof(IdentityUserAdapter.SecurityStamp) or nameof(IdentityUserAdapter.ConcurrencyStamp))) ||
                !ContactPasswordAuthorityCurrent(caller, entry, row, facts, sets, at, true, out var finalRisk) || finalRisk?.Snapshot() != result.ContactRiskSnapshot ||
                NotificationCurrent(row, profile, at).Snapshot() != challenge.NotificationSnapshot || at >= row.ExpiresAt || at >= challenge.ExpiresAt)
                throw new InvalidOperationException("Current verified contact/risk/native password effects lost their original witnesses.");
            await database.SaveChangesAsync(ct).ConfigureAwait(false); at = clock.GetUtcNow();
            if (!ContactCompleted(entry, row, result, challenge, facts.User, at) || !ContactPasswordAuthorityCurrent(caller, entry, row, facts, sets, at, true, out finalRisk) ||
                finalRisk?.Snapshot() != result.ContactRiskSnapshot || NotificationCurrent(row, profile, at).Snapshot() != challenge.NotificationSnapshot ||
                at >= row.ExpiresAt || at >= challenge.ExpiresAt || database.ChangeTracker.HasChanges())
                throw new InvalidOperationException("Recovery contact/risk authority changed before final commit.");
            await tx.CommitAsync(ct).ConfigureAwait(false); return new(null, entry, row, result);
        }
        catch { database.ChangeTracker.Clear(); throw; }
    }
    private async Task<RecoveryContactChallengeRow?> ContactChallenge(Guid id, CancellationToken ct)
    {
        var row = await database.Set<RecoveryContactChallengeRow>().FromSqlInterpolated($"SELECT * FROM identity.recovery_contact_challenges WHERE challenge_id={id} FOR UPDATE")
            .SingleOrDefaultAsync(ct).ConfigureAwait(false);
        if (row is not null) await database.Entry(row).ReloadAsync(ct).ConfigureAwait(false); return row;
    }
    private static bool ContactCompleted(RecoveryEntryRow entry, RecoveryCaseRow row, RecoveryResultRow result, RecoveryContactChallengeRow challenge, IdentityUserAdapter user, DateTimeOffset at) =>
        PasswordResultCurrent(entry, row, result, user, at) && result.ProofKind == "CONTACT_RISK" && entry.ContactChallengeId == row.ContactChallengeId &&
        result.ContactChallengeId == challenge.ChallengeId && result.ContactChallengeId == row.ContactChallengeId && challenge.CaseId == row.CaseId && challenge.EntryId == entry.EntryId &&
        challenge.State == "CONSUMED" && challenge.Version >= challenge.Attempts + 2 && challenge.ResultId == result.ResultId && challenge.EndedAt == row.EndedAt &&
        challenge.ProtectedCode is null && challenge.ProtectedDigest is null && result.ContactReference == row.ContactReference && result.ContactVersion == row.ExpectedContactVersion &&
        result.ContactDestinationDigest == row.ContactDestinationDigest && result.ContactNotificationDeliveryId == challenge.DeliveryId &&
        challenge.ContactReference == row.ContactReference && challenge.ContactVersion == row.ExpectedContactVersion && challenge.DestinationDigest == row.ContactDestinationDigest &&
        entry.ContactProfileVersion == challenge.ProfileVersion && challenge.ProcessingReference == row.ProcessingReference && challenge.RetentionReference == row.RetentionReference &&
        result.ContactRiskSnapshot is not null && challenge.ContextDigest == row.ContextDigest && row.EndedAt >= challenge.CreatedAt && row.EndedAt < challenge.ExpiresAt &&
        entry.ContactExpiresAt is { } publicExpiry && row.EndedAt < publicExpiry;
    private async Task<RecoveryEntryResult> DescribeContactEntry(RecoveryEntryCaller caller, RecoveryEntryRow entry, RecoveryCaseRow row,
        RecoveryCaseLockedFacts? facts, IDbContextTransaction tx, CancellationToken ct)
    {
        await LockRecoveryContact(row, ct).ConfigureAwait(false); var sets = await CodeSets(row.AccountId, ct).ConfigureAwait(false);
        var challenge = row.ContactChallengeId is { } id ? await ContactChallenge(id, ct).ConfigureAwait(false) : null;
        var result = await database.Set<RecoveryResultRow>().SingleOrDefaultAsync(x => x.CaseId == row.CaseId, ct).ConfigureAwait(false);
        var outbox = result is not null && await database.Set<OutboxMessage>().AnyAsync(x => x.EventId == result.RepairOutboxEventId && x.EventType == "IdentitySecurityActivityRequested" &&
            x.OperationId == OperationId(row) && x.CorrelationId == row.RequestId.ToString("N"), ct).ConfigureAwait(false);
        var at = clock.GetUtcNow();
        if (facts is null || challenge is null || result is null || !outbox || !ContactCompleted(entry, row, result, challenge, facts.User, at) ||
            !ContactPasswordAuthorityCurrent(caller, entry, row, facts, sets, at, true, out _)) return await EntryFinish(tx, new("NOT_FOUND_OR_NOT_DISCLOSABLE"), ct).ConfigureAwait(false);
        return await EntryFinish(tx, new(null, entry, row, result, true), ct).ConfigureAwait(false);
    }
    internal async Task<bool> DispatchContactNotificationAsync(Guid deliveryId, CancellationToken ct = default)
    {
        Clean(); if (deliveryId == Guid.Empty || contactDispatcher is null) return false;
        var seed = await database.Set<RecoveryContactChallengeRow>().AsNoTracking().SingleOrDefaultAsync(x => x.DeliveryId == deliveryId, ct).ConfigureAwait(false);
        if (seed is null) return false;
        var original = await database.Set<RecoveryEntryRow>().AsNoTracking().SingleAsync(x => x.EntryId == seed.EntryId, ct).ConfigureAwait(false);
        var caller = new RecoveryEntryCaller(original.BrowserDigest, original.PreviousCookieDigest, original.Origin, original.NetworkDigest);
        RecoveryContactNotification? notice = null;
        await using (var tx = await database.Database.BeginTransactionAsync(ct).ConfigureAwait(false))
        {
            await LockEntry(caller, original.RequestId, ct).ConfigureAwait(false); var entry = await Entry(caller, original.RequestId, ct).ConfigureAwait(false);
            var snapshot = await database.Set<RecoveryCaseRow>().AsNoTracking().SingleAsync(x => x.CaseId == seed.CaseId, ct).ConfigureAwait(false);
            var actor = new RecoveryCaseCaller(snapshot.AccountId, caller.BrowserDigest, caller.Origin, caller.NetworkDigest);
            await LockOriginal(actor, original.RequestId, ct).ConfigureAwait(false); var row = await Original(actor, original.RequestId, ct).ConfigureAwait(false);
            var facts = row is null ? null : await LoadAccountFacts(row.AccountId, null, ct).ConfigureAwait(false);
            if (row is null || facts is null || entry is null) return false;
            await LockRecoveryContact(row, ct).ConfigureAwait(false); var sets = await CodeSets(row.AccountId, ct).ConfigureAwait(false);
            var challenge = await ContactChallenge(seed.ChallengeId, ct).ConfigureAwait(false);
            var eventExists = challenge is not null && await database.Set<OutboxMessage>().AnyAsync(x => x.EventId == challenge.NotificationOutboxEventId &&
                x.EventType == "IdentitySecurityNotificationRequested" && x.OperationId == OperationId(row) && x.CorrelationId == row.RequestId.ToString("N"), ct).ConfigureAwait(false);
            var at = clock.GetUtcNow(); var profile = PasswordCurrent(caller.Origin, at).Values.ContactRecoveryProfile;
            if (challenge is null || profile is null || challenge.DeliveryId != deliveryId || challenge.State != "PENDING" || challenge.ProtectedAddress is null ||
                challenge.ProtectedCode is null || at >= challenge.ExpiresAt || at >= challenge.PurgeAfter || !eventExists ||
                !ContactPasswordAuthorityCurrent(caller, entry, row, facts, sets, at, false, out _)) return false;
            var authority = NotificationCurrent(row, profile, at);
            if (authority.Snapshot() != challenge.NotificationSnapshot) return false;
            notice = new(deliveryId, row.CaseId, challenge.ChallengeId, ContactAddressProtection.Unprotect(challenge.ProtectedAddress),
                ContactProofProtection.Unprotect(challenge.ProtectedCode), authority, challenge.ExpiresAt);
            await tx.CommitAsync(ct).ConfigureAwait(false);
        }
        // NotificationIntent queue handoff is outside every Identity owner transaction; adapter owns deduplication and delivery evidence.
        if (notice is null || clock.GetUtcNow() >= notice.ExpiresAt || !notice.Authority.EffectiveInterval.Contains(clock.GetUtcNow())) return false;
        await contactDispatcher.QueueAsync(notice, ct).ConfigureAwait(false); return true;
    }
    internal async Task<int> PurgeExpiredContactMaterialAsync(int maximum, CancellationToken ct = default)
    {
        Clean(); ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximum); var at = clock.GetUtcNow();
        var ids = await database.Set<RecoveryContactChallengeRow>().AsNoTracking().Where(x => x.PurgeAfter <= at && x.MaterialPurgedAt == null)
            .OrderBy(x => x.PurgeAfter).Take(maximum).Select(x => x.ChallengeId).ToArrayAsync(ct).ConfigureAwait(false); var count = 0;
        foreach (var id in ids)
        {
            await using var tx = await database.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
            var row = await ContactChallenge(id, ct).ConfigureAwait(false); at = clock.GetUtcNow();
            if (row is not null && row.PurgeAfter <= at && row.MaterialPurgedAt is null)
            {
                row.ProtectedCode = null; row.ProtectedDigest = null; row.ProtectedAddress = null; row.MaterialPurgedAt = IdentityPersistenceTime.Normalize(at); row.Version++;
                if (row.State == "PENDING") { row.State = "EXPIRED"; row.EndedAt = row.MaterialPurgedAt; }
                await database.SaveChangesAsync(ct).ConfigureAwait(false); count++;
            }
            await tx.CommitAsync(ct).ConfigureAwait(false);
        }
        return count;
    }
}
