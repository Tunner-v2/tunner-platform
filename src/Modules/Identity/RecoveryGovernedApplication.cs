using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Tunner.Database.Outbox;

namespace Tunner.Identity.Persistence;

internal sealed partial class RecoveryCaseApplication
{
    internal bool GovernedAvailable(string origin, DateTimeOffset at) => PasswordAvailable && governedDispositions is not null &&
        PasswordCurrent(origin, at).Values.GovernedRecoveryProfile is not null;
    private async Task<IReadOnlyList<RecoveryGovernedContactFacts>> GovernedContacts(string accountId, CancellationToken ct)
    {
        var contacts = await database.VerifiedContacts.FromSqlInterpolated($"SELECT * FROM accounts.verified_contacts WHERE account_id={accountId} ORDER BY contact_id FOR SHARE")
            .ToArrayAsync(ct).ConfigureAwait(false);
        foreach (var row in contacts) await database.Entry(row).ReloadAsync(ct).ConfigureAwait(false);
        return GovernedContactFacts(accountId);
    }
    private System.Collections.ObjectModel.ReadOnlyCollection<RecoveryGovernedContactFacts> GovernedContactFacts(string accountId) => Array.AsReadOnly(database.VerifiedContacts.Local
        .Where(x => x.AccountId == accountId).OrderBy(x => x.ContactId).Select(x => new RecoveryGovernedContactFacts(x.ContactId, x.Version,
            x.State.ToString().ToUpperInvariant(), x.AnonymizedAtUtc is not null, x.NormalizedEmail is null ? null : AuthenticationIdentity.Digest(x.NormalizedEmail))).ToArray());
    private static string GovernedContactsDigest(IReadOnlyList<RecoveryGovernedContactFacts> contacts) => AuthenticationIdentity.Digest(JsonSerializer.Serialize(contacts));
    private RecoveryCaseRow? PrepareGovernedPasswordCase(RecoveryCaseRow row, RecoveryEntryCaller caller, RecoveryEntryRow entry,
        RecoveryCaseLockedFacts facts, IReadOnlyList<RecoveryCaseCodeSetFacts> sets, RecoveryPasswordPolicyVersion policy,
        RecoveryCaseHierarchyFacts posture, IReadOnlyList<RecoveryGovernedContactFacts>? contacts, RecoveryCaseHierarchyDecision decision, DateTimeOffset at)
    {
        var profile = policy.Values.GovernedRecoveryProfile;
        if (profile is null || contacts is null || !GovernedAvailable(caller.Origin, at) || !GovernedSelected(decision, profile, posture, at)) return null;
        row.ProofKind = "GOVERNED_REVIEW"; row.State = "PENDING_GOVERNED_REVIEW"; row.SetReference = null; row.ExpectedSetLifecycleVersion = null;
        row.ExpectedUsageVersion = null; row.UsageAdmission = null; row.GovernedContactsDigest = GovernedContactsDigest(contacts);
        row.ContextDigest = Context(row); row.HierarchySnapshot = decision.Snapshot();
        row.ExpiresAt = IdentityPersistenceTime.Normalize(Clip(row.CreatedAt + policy.Values.CaseLifetime, entry.ExpiresAt, policy.Metadata.EffectiveInterval.Until, decision.EffectiveInterval.Until));
        row.RecoveryExpiresAt = entry.RecoveryExpiresAt;
        // Pending is this RecoveryCase's process state. No review lifecycle, approval or identity-verification outcome is created here.
        return at < row.ExpiresAt && GovernedAuthorityCurrent(caller, entry, row, facts, sets, at, false) ? row : null;
    }
    private static bool GovernedSelected(RecoveryCaseHierarchyDecision decision, RecoveryGovernedProfile profile, RecoveryCaseHierarchyFacts posture, DateTimeOffset at) =>
        decision.StrongAuthenticatorUnavailable && !decision.RecoveryCodeAllowed && decision.CodeSelection is null && decision.PasskeySelection is null && decision.ContactSelection is null &&
        decision.GovernedSelection is { RecoveryCodesUnavailable: true, SafeContactUnavailable: true } selection && selection.ProfileVersion == profile.Version &&
        !string.IsNullOrWhiteSpace(selection.UnavailabilityEvidenceReference) && decision.FactsDigest == posture.Digest() &&
        !string.IsNullOrWhiteSpace(decision.PolicyVersion) && !string.IsNullOrWhiteSpace(decision.EvidenceReference) && decision.EffectiveInterval.Contains(at);
    private bool GovernedAuthorityCurrent(RecoveryEntryCaller caller, RecoveryEntryRow entry, RecoveryCaseRow row, RecoveryCaseLockedFacts facts,
        IReadOnlyList<RecoveryCaseCodeSetFacts> sets, DateTimeOffset at, bool repaired)
    {
        var policy = PasswordCurrent(caller.Origin, at); var profile = policy.Values.GovernedRecoveryProfile;
        if (profile is null || !GovernedAvailable(caller.Origin, at)) return false;
        var contacts = GovernedContactFacts(row.AccountId); var posture = facts.Value() with { SecurityStampDigest = row.PriorSecurityStampDigest!, PasswordAvailable = true, CodeSets = sets };
        RecoveryCaseHierarchyDecision decision;
        try { decision = hierarchy!.RequireCurrent(new(row.CaseId, row.RequestId, PasswordSelectionContext(entry), row.PolicyVersion, RecoveryCaseGoal.RepairPassword, posture)
            { GovernedContacts = contacts }, at); }
        catch (Exception error) when (error is InvalidOperationException or ArgumentException) { return false; }
        return decision is not null && row.ProofKind == "GOVERNED_REVIEW" && row.State == (repaired ? "REPAIRED" : "PENDING_GOVERNED_REVIEW") &&
            row.Goal == nameof(RecoveryCaseGoal.RepairPassword) && row.SessionEffect == nameof(RecoveryPasswordSessionEffect.InvalidateExistingTunnerSessions) &&
            row.SetReference is null && row.ExpectedSetLifecycleVersion is null && row.ExpectedUsageVersion is null && row.UsageAdmission is null &&
            row.PasskeyCredentialReference is null && row.PasskeyCeremonyId is null && row.ContactReference is null && row.ContactChallengeId is null &&
            row.GovernedContactsDigest == GovernedContactsDigest(contacts) && database.VerifiedContacts.Local.Where(x => x.AccountId == row.AccountId).All(x => database.Entry(x).State == EntityState.Unchanged) &&
            facts.OtherSourcesUnmutated(database) && row.UserSourceDigest == UserSourceDigest(facts.User) &&
            (repaired || facts.User.PasswordHash is not null && AuthenticationIdentity.Digest(facts.User.PasswordHash) == row.PriorPasswordDigest &&
                AuthenticationIdentity.Digest(facts.User.SecurityStamp!) == row.PriorSecurityStampDigest) &&
            facts.Account.AccountId == row.AccountId && facts.Account.Version == row.ExpectedAccountVersion && facts.Account.State.ToString() == row.AccountState &&
            posture.Digest() == row.FactsDigest && GovernedSelected(decision, profile, posture, at) && decision.Snapshot() == row.HierarchySnapshot &&
            EntryCurrent(caller, entry, at) && row.EntryId == entry.EntryId && row.RequestId == entry.RequestId && row.RecoveryExpiresAt == entry.RecoveryExpiresAt &&
            row.PolicySnapshot == policy.Snapshot && Context(row) == row.ContextDigest && row.OriginalBrowserDigest == caller.BrowserDigest && row.Origin == caller.Origin &&
            row.NetworkDigest == caller.NetworkDigest && row.TargetSessionId is null && row.ExpectedSessionVersion is null && at >= row.CreatedAt &&
            row.ExpiresAt == IdentityPersistenceTime.Normalize(Clip(row.CreatedAt + policy.Values.CaseLifetime, entry.ExpiresAt, policy.Metadata.EffectiveInterval.Until, decision.EffectiveInterval.Until));
    }
    private static string GovernedEffectField(string value) => Encoding.UTF8.GetByteCount(value).ToString(CultureInfo.InvariantCulture) + ":" + value;
    private static string GovernedEffectsDigest(RecoveryCaseRow row) => AuthenticationIdentity.Digest(
        GovernedEffectField(row.Goal) + GovernedEffectField(row.SessionEffect) + GovernedEffectField(row.UnresolvedEffectsJson));
    private static RecoveryGovernedDispositionRequest GovernedRequest(RecoveryCaseRow row, RecoveryGovernedProfile profile) => new(row.CaseId, row.RequestId, row.AccountId, 1,
        row.ExpectedAccountVersion, row.ContextDigest, row.FactsDigest, row.UserSourceDigest!, row.GovernedContactsDigest!, row.Goal, row.SessionEffect, row.PolicyVersion, profile.Version,
        profile.DispositionContractReference, row.UnresolvedEffectsJson, RecoveryGovernedDispositionRequest.RequiredEffectsEncoding, GovernedEffectsDigest(row), row.ProcessingReference, row.RetentionReference);
    private static bool GovernedDispositionBound(RecoveryGovernedDisposition decision, RecoveryCaseRow row, RecoveryGovernedProfile profile) =>
        decision.AuthorizesExactEffect && decision.EffectiveInterval is not null && decision.CaseId == row.CaseId && decision.RequestId == row.RequestId && decision.AccountId == row.AccountId && decision.OriginalCaseVersion == 1 &&
        decision.AccountVersion == row.ExpectedAccountVersion && decision.ContextDigest == row.ContextDigest && decision.FactsDigest == row.FactsDigest &&
        decision.UserSourceDigest == row.UserSourceDigest && decision.ContactsDigest == row.GovernedContactsDigest && decision.Goal == row.Goal && decision.SessionEffect == row.SessionEffect && decision.RecoveryPolicyVersion == row.PolicyVersion &&
        decision.ProfileVersion == profile.Version && decision.DispositionContractReference == profile.DispositionContractReference &&
        decision.UnresolvedEffectsJson == row.UnresolvedEffectsJson && decision.EffectsEncoding == RecoveryGovernedDispositionRequest.RequiredEffectsEncoding && decision.EffectsDigest == GovernedEffectsDigest(row) &&
        decision.DispositionVersion >= 1 && new[] { decision.DispositionReference, decision.AuthorityPolicyVersion, decision.ControlEvidenceReference }.All(x => !string.IsNullOrWhiteSpace(x));
    private RecoveryGovernedDisposition? CurrentGovernedDisposition(RecoveryCaseRow row, RecoveryGovernedProfile profile, DateTimeOffset at)
    {
        try
        {
            var decision = governedDispositions!.RequireCurrent(GovernedRequest(row, profile), at);
            return decision is not null && GovernedDispositionBound(decision, row, profile) && decision.EffectiveInterval.Contains(at) ? decision : null;
        }
        catch (Exception error) when (error is InvalidOperationException or ArgumentException) { return null; }
    }
    internal async Task<RecoveryEntryResult> RepairPasswordWithGovernedDispositionAsync(RecoveryEntryCaller caller, Guid requestId, long expectedVersion,
        string? password, string? confirmation, CancellationToken ct = default)
    {
        Clean(); if (!caller.Valid || requestId == Guid.Empty) return new("VALIDATION_ERROR");
        if (!GovernedAvailable(caller.Origin, clock.GetUtcNow())) return new("RECOVERY_UNAVAILABLE");
        var policy = PasswordCurrent(caller.Origin, clock.GetUtcNow()); var profile = policy.Values.GovernedRecoveryProfile!;
        var original = await database.Set<RecoveryEntryRow>().AsNoTracking().SingleOrDefaultAsync(x => x.RequestId == requestId && x.BrowserDigest == caller.BrowserDigest, ct).ConfigureAwait(false);
        if (original is null || !EntryActor(caller, original)) return new("NOT_FOUND_OR_NOT_DISCLOSABLE");
        if (original.State == "REPAIRED")
        {
            var completed = await database.Set<RecoveryCaseRow>().AsNoTracking().SingleOrDefaultAsync(x => x.EntryId == original.EntryId, ct).ConfigureAwait(false);
            return completed?.ProofKind == "GOVERNED_REVIEW" ? await ReadPasswordEntryAsync(caller, requestId, ct).ConfigureAwait(false) : new("RECOVERY_PROOF_REJECTED", original);
        }
        if (original.State != "OPEN" || original.Version != expectedVersion || !EntryCurrent(caller, original, clock.GetUtcNow())) return new("RECOVERY_PROOF_REJECTED", original);
        // Charge and prepare exactly the existing anonymous password path before optional actual-case lookup.
        var prepared = await PrepareRecoveryPasswordAsync(caller, original, policy, password, confirmation, ct).ConfigureAwait(false);
        if (prepared.Error is not null) return new(prepared.Error, original);
        var seed = await database.Set<RecoveryCaseRow>().AsNoTracking().SingleOrDefaultAsync(x => x.EntryId == original.EntryId && x.ProofKind == "GOVERNED_REVIEW", ct).ConfigureAwait(false);
        if (seed is null) return new("RECOVERY_PROOF_REJECTED", original);
        await using var tx = await database.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        try
        {
            await LockEntry(caller, requestId, ct).ConfigureAwait(false); var entry = await Entry(caller, requestId, ct).ConfigureAwait(false);
            var actor = new RecoveryCaseCaller(seed.AccountId, caller.BrowserDigest, caller.Origin, caller.NetworkDigest);
            await LockOriginal(actor, requestId, ct).ConfigureAwait(false); var row = await Original(actor, requestId, ct).ConfigureAwait(false);
            var facts = row is null ? null : await LoadAccountFacts(row.AccountId, null, ct).ConfigureAwait(false);
            if (row is null || facts is null || entry is null || row.CaseId != seed.CaseId) return new("RECOVERY_PROOF_REJECTED");
            await GovernedContacts(row.AccountId, ct).ConfigureAwait(false); var sets = await CodeSets(row.AccountId, ct).ConfigureAwait(false);
            if (row.State == "REPAIRED" && entry.State == "REPAIRED") return await DescribeGovernedEntry(caller, entry, row, facts, tx, ct).ConfigureAwait(false);
            var at = clock.GetUtcNow();
            if (entry.State != "OPEN" || entry.Version != expectedVersion || row.Version != 1 || row.ResultId is not null || at >= row.ExpiresAt ||
                policy.Snapshot != PasswordCurrent(caller.Origin, at).Snapshot || !GovernedAuthorityCurrent(caller, entry, row, facts, sets, at, false)) return new("RECOVERY_PROOF_REJECTED");
            var disposition = CurrentGovernedDisposition(row, profile, at);
            if (disposition is null) return new("RECOVERY_PROOF_REJECTED", entry);
            var ended = IdentityPersistenceTime.Normalize(at);
            if (!disposition.EffectiveInterval.Contains(ended)) return new("RECOVERY_PROOF_REJECTED", entry);
            var concurrency = facts.User.ConcurrencyStamp; ApplyPassword(row, facts.User, prepared.Hash!); var eventId = Guid.NewGuid();
            var result = new RecoveryResultRow { ResultId = Guid.NewGuid(), CaseId = row.CaseId, AccountId = row.AccountId, Outcome = "PASSWORD_REPAIRED_NO_SESSION", ProofKind = "GOVERNED_REVIEW",
                ContextDigest = row.ContextDigest, PolicyVersion = row.PolicyVersion, PolicySnapshot = row.PolicySnapshot, HierarchySnapshot = row.HierarchySnapshot,
                RecordedAt = ended, UnresolvedEffectsJson = row.UnresolvedEffectsJson, ProcessingReference = row.ProcessingReference, RetentionReference = row.RetentionReference,
                RepairOutboxEventId = eventId, PriorPasswordDigest = row.PriorPasswordDigest, RepairedPasswordDigest = AuthenticationIdentity.Digest(prepared.Hash!),
                PriorSecurityStampDigest = row.PriorSecurityStampDigest, RepairedSecurityStampDigest = AuthenticationIdentity.Digest(facts.User.SecurityStamp!), SessionEffect = row.SessionEffect,
                GovernedDispositionReference = disposition.DispositionReference, GovernedDispositionVersion = disposition.DispositionVersion, GovernedDispositionSnapshot = disposition.Snapshot() };
            row.State = "REPAIRED"; row.Version++; row.ResultId = result.ResultId; row.EndedAt = ended; entry.State = "REPAIRED"; entry.Version++; entry.EndedAt = ended; database.Add(result);
            database.Add(new OutboxMessage(eventId, "1.0", "IdentitySecurityActivityRequested", "1", ended, "tunner/identity", row.CaseId.ToString("N"), "RESTRICTED_SECURITY",
                JsonSerializer.Serialize(new { reference = result.ResultId.ToString("N") }), row.Version.ToString(CultureInfo.InvariantCulture), OperationId(row), requestId.ToString("N")));
            database.ChangeTracker.DetectChanges(); at = clock.GetUtcNow();
            if (!GovernedCompleted(entry, row, result, facts.User, profile, at) || facts.User.PasswordHash != prepared.Hash || facts.User.ConcurrencyStamp == concurrency ||
                database.ChangeTracker.Entries().Any(x => (x.State is EntityState.Added or EntityState.Modified or EntityState.Deleted) &&
                    !ReferenceEquals(x.Entity, facts.User) && !ReferenceEquals(x.Entity, row) && !ReferenceEquals(x.Entity, entry) && !ReferenceEquals(x.Entity, result) &&
                    !(x.Entity is OutboxMessage intent && intent.EventId == eventId && x.State == EntityState.Added)) ||
                database.Entry(facts.User).State != EntityState.Modified || database.Entry(facts.User).Properties.Where(x => x.IsModified).Any(x =>
                    x.Metadata.Name is not (nameof(IdentityUserAdapter.PasswordHash) or nameof(IdentityUserAdapter.SecurityStamp) or nameof(IdentityUserAdapter.ConcurrencyStamp))) ||
                database.Entry(row).State != EntityState.Modified || database.Entry(entry).State != EntityState.Modified || database.Entry(result).State != EntityState.Added ||
                !database.ChangeTracker.Entries<OutboxMessage>().Any(x => x.State == EntityState.Added && x.Entity.EventId == eventId &&
                    x.Entity.EventType == "IdentitySecurityActivityRequested" && x.Entity.OperationId == OperationId(row) && x.Entity.CorrelationId == requestId.ToString("N") &&
                    x.Entity.Payload == JsonSerializer.Serialize(new { reference = result.ResultId.ToString("N") })) ||
                !GovernedAuthorityCurrent(caller, entry, row, facts, sets, at, true) || CurrentGovernedDisposition(row, profile, at)?.Snapshot() != result.GovernedDispositionSnapshot || at >= row.ExpiresAt)
                throw new InvalidOperationException("Current exact governed disposition/password/result witnesses are required.");
            await database.SaveChangesAsync(ct).ConfigureAwait(false); at = clock.GetUtcNow();
            if (!GovernedCompleted(entry, row, result, facts.User, profile, at) || !GovernedAuthorityCurrent(caller, entry, row, facts, sets, at, true) ||
                CurrentGovernedDisposition(row, profile, at)?.Snapshot() != result.GovernedDispositionSnapshot || at >= row.ExpiresAt || database.ChangeTracker.HasChanges())
                throw new InvalidOperationException("Governed recovery authorization changed before final commit.");
            await tx.CommitAsync(ct).ConfigureAwait(false); return new(null, entry, row, result);
        }
        catch { database.ChangeTracker.Clear(); throw; }
    }
    private static bool GovernedCompleted(RecoveryEntryRow entry, RecoveryCaseRow row, RecoveryResultRow result, IdentityUserAdapter user, RecoveryGovernedProfile profile, DateTimeOffset at)
    {
        if (!PasswordResultCurrent(entry, row, result, user, at) || result.ProofKind != "GOVERNED_REVIEW" || result.GovernedDispositionSnapshot is null) return false;
        try
        {
            var recorded = JsonSerializer.Deserialize<RecoveryGovernedDisposition>(result.GovernedDispositionSnapshot);
            return recorded is not null && GovernedDispositionBound(recorded, row, profile) && recorded.DispositionReference == result.GovernedDispositionReference &&
                recorded.DispositionVersion == result.GovernedDispositionVersion && recorded.EffectiveInterval.Contains(result.RecordedAt);
        }
        catch (JsonException) { return false; }
    }
    private async Task<RecoveryEntryResult> DescribeGovernedEntry(RecoveryEntryCaller caller, RecoveryEntryRow entry, RecoveryCaseRow row,
        RecoveryCaseLockedFacts? facts, IDbContextTransaction tx, CancellationToken ct)
    {
        await GovernedContacts(row.AccountId, ct).ConfigureAwait(false); var sets = await CodeSets(row.AccountId, ct).ConfigureAwait(false);
        var result = await database.Set<RecoveryResultRow>().SingleOrDefaultAsync(x => x.CaseId == row.CaseId, ct).ConfigureAwait(false);
        var eventExists = result is not null && await database.Set<OutboxMessage>().AnyAsync(x => x.EventId == result.RepairOutboxEventId && x.EventType == "IdentitySecurityActivityRequested" &&
            x.OperationId == OperationId(row) && x.CorrelationId == row.RequestId.ToString("N"), ct).ConfigureAwait(false);
        var at = clock.GetUtcNow(); var profile = PasswordCurrent(caller.Origin, at).Values.GovernedRecoveryProfile;
        // Reconcile the actual committed original outcome. It does not execute or reapprove a now-consumed disposition.
        if (facts is null || result is null || profile is null || !eventExists || !GovernedCompleted(entry, row, result, facts.User, profile, at) ||
            !GovernedAuthorityCurrent(caller, entry, row, facts, sets, at, true)) return await EntryFinish(tx, new("NOT_FOUND_OR_NOT_DISCLOSABLE"), ct).ConfigureAwait(false);
        return await EntryFinish(tx, new(null, entry, row, result, true), ct).ConfigureAwait(false);
    }
}
