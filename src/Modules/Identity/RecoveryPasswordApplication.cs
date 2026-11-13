using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;

namespace Tunner.Identity.Persistence;

internal sealed partial class RecoveryCaseApplication
{
    internal void ApplyPassword(RecoveryCaseRow row, IdentityUserAdapter user, string preparedHash) => authentication.ApplyRecoveryPassword(row, user, preparedHash);
    internal bool PasswordAvailable => passwordPolicies is not null && hierarchy is not null && passwordBlocklist is not null &&
        authenticationPolicies is not null && partitions is not null;
    internal RecoveryPasswordPolicyVersion PasswordCurrent(string origin, DateTimeOffset at)
    {
        if (!PasswordAvailable) throw new InvalidOperationException("Recovery password owner is unavailable.");
        var current = passwordPolicies!.RequireEffective(at); current.Require(origin, at); return current;
    }
    internal async Task<RecoveryEntryResult> StartPasswordEntryAsync(RecoveryEntryCaller caller, Guid requestId, string email,
        string goalReference, CancellationToken ct = default)
    {
        Clean(); if (!caller.Valid || requestId == Guid.Empty || !IdentityEmail.IsValid(email)) return new("VALIDATION_ERROR");
        if (!PasswordAvailable) return new("RECOVERY_UNAVAILABLE");
        var initial = PasswordCurrent(caller.Origin, clock.GetUtcNow());
        if (goalReference != initial.Values.GoalReference) return new("RECOVERY_UNAVAILABLE");
        var normalized = new UpperInvariantLookupNormalizer().NormalizeEmail(email.Trim())!;
        var identifier = partitions!.Identifier(normalized);
        await using var tx = await database.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        try
        {
            await LockEntry(caller, requestId, ct).ConfigureAwait(false);
            var prior = await Entry(caller, requestId, ct).ConfigureAwait(false);
            if (prior is not null)
            {
                if (prior.IdentifierDigest != identifier || prior.GoalReference != goalReference) return await EntryFinish(tx, new("VERSION_CONFLICT"), ct).ConfigureAwait(false);
                return await DescribeEntry(caller, prior, tx, ct).ConfigureAwait(false);
            }
            var auth = authenticationPolicies!.RequireEffective(clock.GetUtcNow()); auth.Validate();
            if (!await authentication.AllowAsync("recovery-entry:N:" + caller.NetworkDigest, initial.Values.EntryNetworkLimit, auth, clock.GetUtcNow(), ct).ConfigureAwait(false) ||
                !await authentication.AllowAsync("recovery-entry:I:" + identifier, initial.Values.EntryIdentifierLimit, auth, clock.GetUtcNow(), ct).ConfigureAwait(false))
                return await EntryFinish(tx, new("RATE_OR_ABUSE_LIMITED"), ct).ConfigureAwait(false);
            var pending = await database.Set<RecoveryEntryRow>().CountAsync(x => x.BrowserDigest == caller.BrowserDigest && x.State == "OPEN" && x.ExpiresAt > clock.GetUtcNow(), ct).ConfigureAwait(false);
            var now = clock.GetUtcNow(); var profile = PasswordCurrent(caller.Origin, now);
            if (profile.Snapshot != initial.Snapshot || pending >= profile.Values.MaximumPendingEntries) return await EntryFinish(tx, new("RECOVERY_UNAVAILABLE"), ct).ConfigureAwait(false);
            var expires = IdentityPersistenceTime.Normalize(Clip(now + profile.Values.EntryLifetime, profile.Metadata.EffectiveInterval.Until));
            var entry = new RecoveryEntryRow
            {
                EntryId = Guid.NewGuid(), RequestId = requestId, BrowserDigest = caller.BrowserDigest, PreviousCookieDigest = caller.PreviousCookieDigest,
                Origin = caller.Origin, NetworkDigest = caller.NetworkDigest, IdentifierDigest = identifier, GoalReference = goalReference,
                PolicyVersion = profile.Values.Version, PolicySnapshot = profile.Snapshot, ProcessingReference = profile.Values.ProcessingReference,
                RetentionReference = profile.Values.RetentionReference, CreatedAt = IdentityPersistenceTime.Normalize(now), ExpiresAt = expires,
                RecoveryExpiresAt = IdentityPersistenceTime.Normalize(expires + profile.Values.ResultRecoveryLifetime)
            };
            if (expires <= now) return await EntryFinish(tx, new("RECOVERY_UNAVAILABLE"), ct).ConfigureAwait(false);
            database.Add(entry);
            // The neutral entry exists before its optional real-owner FK, inside this SAME transaction.
            await database.SaveChangesAsync(ct).ConfigureAwait(false);
            var accountId = await (from user in database.Users
                join contact in database.VerifiedContacts on user.AccountId equals contact.AccountId
                where user.NormalizedEmail == normalized && user.EmailConfirmed && contact.NormalizedEmail == normalized &&
                    contact.State == Tunner.Accounts.ContactVerificationState.Verified && contact.AnonymizedAtUtc == null
                select user.AccountId).SingleOrDefaultAsync(ct).ConfigureAwait(false);
            RecoveryCaseRow? recovery = null;
            RecoveryCaseLockedFacts? preparedFacts = null;
            IReadOnlyList<RecoveryCaseCodeSetFacts> preparedSets = [];
            if (accountId is not null)
            {
                var facts = await LoadAccountFacts(accountId, null, ct).ConfigureAwait(false);
                var sets = await CodeSets(accountId, ct).ConfigureAwait(false);
                now = clock.GetUtcNow(); profile = PasswordCurrent(caller.Origin, now);
                var currentContact = await database.VerifiedContacts.SingleOrDefaultAsync(x => x.AccountId == accountId && x.NormalizedEmail == normalized &&
                    x.State == Tunner.Accounts.ContactVerificationState.Verified && x.AnonymizedAtUtc == null, ct).ConfigureAwait(false);
                now = clock.GetUtcNow(); profile = PasswordCurrent(caller.Origin, now);
                if (facts?.User.PasswordHash is not null && facts.User.NormalizedEmail == normalized && facts.User.EmailConfirmed && currentContact is not null)
                {
                    var governedContacts = profile.Values.GovernedRecoveryProfile is null ? null : await GovernedContacts(accountId, ct).ConfigureAwait(false);
                    now = clock.GetUtcNow(); profile = PasswordCurrent(caller.Origin, now);
                    recovery = PreparePasswordCase(caller, entry, facts, sets, profile, now, currentContact, governedContacts);
                    if (recovery is not null) { preparedFacts = facts; preparedSets = sets; database.Add(recovery); }
                }
            }
            // Any final authority failure after the first save MUST roll back, never commit a failure return.
            now = clock.GetUtcNow();
            if (!EntryCurrent(caller, entry, now) || PasswordCurrent(caller.Origin, now).Snapshot != initial.Snapshot ||
                recovery is not null && (preparedFacts is null || !PasswordAuthorityCurrent(caller, entry, recovery, preparedFacts, preparedSets, now, false)))
                throw new InvalidOperationException("Recovery entry authority changed before commit.");
            return await EntryFinish(tx, new(null, entry, recovery), ct).ConfigureAwait(false);
        }
        catch { database.ChangeTracker.Clear(); throw; }
    }

    internal async Task<RecoveryEntryResult> ReadPasswordEntryAsync(RecoveryEntryCaller caller, Guid requestId, CancellationToken ct = default)
    {
        Clean(); if (!caller.Valid || requestId == Guid.Empty) return new("VALIDATION_ERROR");
        if (!PasswordAvailable) return new("RECOVERY_UNAVAILABLE");
        await using var tx = await database.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        try
        {
            await LockEntry(caller, requestId, ct).ConfigureAwait(false);
            var entry = await Entry(caller, requestId, ct).ConfigureAwait(false);
            return entry is null ? await EntryFinish(tx, new("NOT_FOUND_OR_NOT_DISCLOSABLE"), ct).ConfigureAwait(false) : await DescribeEntry(caller, entry, tx, ct).ConfigureAwait(false);
        }
        catch { database.ChangeTracker.Clear(); throw; }
    }
    internal async Task<RecoveryEntryResult> CancelPasswordEntryAsync(RecoveryEntryCaller caller, Guid requestId, CancellationToken ct = default)
    {
        Clean(); if (!caller.Valid || requestId == Guid.Empty) return new("VALIDATION_ERROR");
        if (!PasswordAvailable) return new("RECOVERY_UNAVAILABLE");
        await using var tx = await database.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        try
        {
            await LockEntry(caller, requestId, ct).ConfigureAwait(false); var entry = await Entry(caller, requestId, ct).ConfigureAwait(false);
            var now = clock.GetUtcNow();
            if (entry is null || !EntryCurrent(caller, entry, now)) return await EntryFinish(tx, new("NOT_FOUND_OR_NOT_DISCLOSABLE"), ct).ConfigureAwait(false);
            if (entry.State == "OPEN" && now < entry.ExpiresAt) { entry.State = "CANCELLED"; entry.Version++; entry.EndedAt = IdentityPersistenceTime.Normalize(now); }
            return await DescribeEntry(caller, entry, tx, ct).ConfigureAwait(false);
        }
        catch { database.ChangeTracker.Clear(); throw; }
    }
    internal async Task<RecoveryEntryResult> RepairPasswordAsync(RecoveryEntryCaller caller, Guid requestId, long expectedVersion,
        string? code, string? password, string? confirmation, CancellationToken ct = default)
    {
        Clean(); if (!caller.Valid || requestId == Guid.Empty) return new("VALIDATION_ERROR");
        if (!PasswordAvailable) return new("RECOVERY_UNAVAILABLE");
        var profile = PasswordCurrent(caller.Origin, clock.GetUtcNow());
        var entry = await database.Set<RecoveryEntryRow>().AsNoTracking().SingleOrDefaultAsync(x => x.BrowserDigest == caller.BrowserDigest && x.RequestId == requestId, ct).ConfigureAwait(false);
        if (entry is null || !EntryActor(caller, entry)) return new("NOT_FOUND_OR_NOT_DISCLOSABLE");
        if (entry.State == "REPAIRED") return await ReadPasswordEntryAsync(caller, requestId, ct).ConfigureAwait(false);
        if (expectedVersion != entry.Version || entry.State != "OPEN" || !EntryCurrent(caller, entry, clock.GetUtcNow())) return new("RECOVERY_PROOF_REJECTED", entry);
        var preparation = await PrepareRecoveryPasswordAsync(caller, entry, profile, password, confirmation, ct).ConfigureAwait(false);
        if (preparation.Error is not null) return new(preparation.Error, entry);
        var preparedHash = preparation.Hash!;
        var recovery = await database.Set<RecoveryCaseRow>().AsNoTracking().SingleOrDefaultAsync(x => x.EntryId == entry.EntryId, ct).ConfigureAwait(false);
        if (recovery is null || recovery.ProofKind != "CODE") return new("RECOVERY_PROOF_REJECTED", entry);
        var request = new RecoveryCodeConsumptionRequest(recovery.AccountId, OperationId(recovery), requestId.ToString("N"), recovery.ContextDigest, caller.Origin, caller.NetworkDigest);
        if (codes is null) return new("RECOVERY_UNAVAILABLE", entry);
        var owner = new RecoveryPasswordCodeOwner(this, database, caller, entry.EntryId, recovery.CaseId, preparedHash, profile.Snapshot, clock);
        var consumed = await codes.ExecuteConsumptionOutcomeAsync(request, code, owner, ct).ConfigureAwait(false);
        return new(consumed.ErrorCode is null ? null : "RECOVERY_PROOF_REJECTED", owner.Entry ?? entry,
            consumed.ErrorCode is null ? owner.Case : null, consumed.ErrorCode is null ? owner.Result : null, consumed.Reconciled);
    }

    private RecoveryCaseRow? PreparePasswordCase(RecoveryEntryCaller caller, RecoveryEntryRow entry, RecoveryCaseLockedFacts facts,
        IReadOnlyList<RecoveryCaseCodeSetFacts> sets, RecoveryPasswordPolicyVersion profile, DateTimeOffset at, VerifiedContactRow? verifiedContact = null, IReadOnlyList<RecoveryGovernedContactFacts>? governedContacts = null)
    {
        var posture = facts.Value() with { CodeSets = sets };
        var row = new RecoveryCaseRow
        {
            CaseId = Guid.NewGuid(), EntryId = entry.EntryId, RequestId = entry.RequestId, AccountId = facts.Account.AccountId,
            OriginalBrowserDigest = caller.BrowserDigest, Origin = caller.Origin, NetworkDigest = caller.NetworkDigest,
            Goal = nameof(RecoveryCaseGoal.RepairPassword), SessionEffect = profile.Values.SessionEffect.ToString(), ExpectedAccountVersion = facts.Account.Version,
            AccountState = facts.Account.State.ToString(), PolicyVersion = profile.Values.Version, PolicySnapshot = profile.Snapshot,
            FactsDigest = posture.Digest(), PriorPasswordDigest = AuthenticationIdentity.Digest(facts.User.PasswordHash!),
            PriorSecurityStampDigest = AuthenticationIdentity.Digest(facts.User.SecurityStamp!), UserSourceDigest = UserSourceDigest(facts.User),
            ProcessingReference = profile.Values.ProcessingReference, RetentionReference = profile.Values.RetentionReference,
            UnresolvedEffectsJson = JsonSerializer.Serialize(profile.Values.UnresolvedDownstreamEffects), CreatedAt = IdentityPersistenceTime.Normalize(at),
            UsageAdmission = profile.Values.RequireExactCodeUsageVersion ? nameof(RecoveryCodeUsageAdmission.ExactUsageSnapshot) : nameof(RecoveryCodeUsageAdmission.IndependentUnusedEntry)
        };
        // Selection is qualified by the same actual hierarchy owner; never infer a latest or single active set.
        RecoveryCaseHierarchyDecision decision;
        try { decision = hierarchy!.RequireCurrent(new(row.CaseId, row.RequestId, PasswordSelectionContext(entry), row.PolicyVersion, RecoveryCaseGoal.RepairPassword, posture)
            { VerifiedContact = profile.Values.ContactRecoveryProfile is not null && verifiedContact is not null ? ContactFacts(verifiedContact) : null, GovernedContacts = governedContacts }, at); }
        catch (InvalidOperationException) { return null; }
        catch (ArgumentException) { return null; }
        if (decision.GovernedSelection is not null)
            return PrepareGovernedPasswordCase(row, caller, entry, facts, sets, profile, posture, governedContacts, decision, at);
        if (decision.ContactSelection is not null)
            return PrepareContactPasswordCase(row, caller, entry, facts, sets, profile, posture, verifiedContact, decision, at);
        if (decision.PasskeySelection is not null)
            return PreparePasskeyPasswordCase(row, caller, entry, facts, profile, posture, decision, at);
        if (codes is null || !Admitted(decision, posture, at) || decision.CodeSelection is not { } choice || !sets.Any(x => x.SetReference == choice.SetReference &&
            x.State == "ACTIVE" && x.LifecycleVersion == choice.LifecycleVersion && (profile.Values.RequireExactCodeUsageVersion ? choice.UsageVersion == x.UsageVersion : choice.UsageVersion is null))) return null;
        row.SetReference = choice.SetReference; row.ExpectedSetLifecycleVersion = choice.LifecycleVersion; row.ExpectedUsageVersion = choice.UsageVersion;
        row.ContextDigest = Context(row); row.HierarchySnapshot = decision.Snapshot();
        row.ExpiresAt = IdentityPersistenceTime.Normalize(Clip(row.CreatedAt + profile.Values.CaseLifetime, entry.ExpiresAt, profile.Metadata.EffectiveInterval.Until, decision.EffectiveInterval.Until));
        row.RecoveryExpiresAt = entry.RecoveryExpiresAt;
        return at < row.ExpiresAt ? row : null;
    }
    internal bool PasswordAuthorityCurrent(RecoveryEntryCaller caller, RecoveryEntryRow entry, RecoveryCaseRow row, RecoveryCaseLockedFacts facts,
        IReadOnlyList<RecoveryCaseCodeSetFacts> sets, DateTimeOffset at, bool repaired, RecoveryCodeEntryRow? consumptionWitness = null)
    {
        if (row.ProofKind == "GOVERNED_REVIEW") return GovernedAuthorityCurrent(caller, entry, row, facts, sets, at, repaired);
        if (row.ProofKind == "CONTACT_RISK") return ContactPasswordAuthorityCurrent(caller, entry, row, facts, sets, at, repaired, out _);
        if (row.ProofKind == "PASSKEY") return PasskeyPasswordAuthorityCurrent(caller, entry, row, facts, at, repaired);
        if (row.ProofKind != "CODE") return false;
        var profile = PasswordCurrent(caller.Origin, at);
        var posture = facts.Value() with { SecurityStampDigest = row.PriorSecurityStampDigest!, PasswordAvailable = true, CodeSets = sets };
        var decision = hierarchy!.RequireCurrent(new(row.CaseId, row.RequestId, PasswordSelectionContext(entry), row.PolicyVersion, RecoveryCaseGoal.RepairPassword, posture), at);
        var selected = sets.SingleOrDefault(x => x.SetReference == row.SetReference);
        if (selected is null || selected.State != "ACTIVE" || selected.LifecycleVersion != row.ExpectedSetLifecycleVersion ||
            decision.CodeSelection is not { } choice || choice.SetReference != row.SetReference || choice.LifecycleVersion != row.ExpectedSetLifecycleVersion ||
            (profile.Values.RequireExactCodeUsageVersion ? choice.UsageVersion != selected.UsageVersion : choice.UsageVersion is not null)) return false;
        if (repaired)
        {
            if (!OwnConsumptionCurrent(row, consumptionWitness) || profile.Values.RequireExactCodeUsageVersion &&
                selected.UsageVersion != row.ExpectedUsageVersion && selected.UsageVersion != row.ExpectedUsageVersion + 1) return false;
            // Only this exact original claim's one usage increment may be reconciled. Never rewrite set identity/lifecycle.
            decision = decision with { CodeSelection = choice with { UsageVersion = row.ExpectedUsageVersion } };
        }
        return EntryCurrent(caller, entry, at) && Context(row) == row.ContextDigest && row.EntryId == entry.EntryId && row.RequestId == entry.RequestId &&
            row.Goal == nameof(RecoveryCaseGoal.RepairPassword) && row.TargetSessionId is null && row.ExpectedSessionVersion is null &&
            row.OriginalBrowserDigest == caller.BrowserDigest && row.Origin == caller.Origin && row.NetworkDigest == caller.NetworkDigest && row.AccountId == facts.Account.AccountId &&
            row.PolicySnapshot == profile.Snapshot && row.SessionEffect == nameof(RecoveryPasswordSessionEffect.InvalidateExistingTunnerSessions) &&
            row.ExpectedAccountVersion == facts.Account.Version && row.AccountState == facts.Account.State.ToString() &&
            row.UserSourceDigest == UserSourceDigest(facts.User) && facts.OtherSourcesUnmutated(database) &&
            posture.Digest() == row.FactsDigest && Admitted(decision, posture, at) && decision.Snapshot() == row.HierarchySnapshot &&
            (repaired || facts.User.PasswordHash is not null && AuthenticationIdentity.Digest(facts.User.PasswordHash) == row.PriorPasswordDigest &&
                AuthenticationIdentity.Digest(facts.User.SecurityStamp!) == row.PriorSecurityStampDigest) &&
            row.ExpiresAt == IdentityPersistenceTime.Normalize(Clip(row.CreatedAt + profile.Values.CaseLifetime, entry.ExpiresAt,
                profile.Metadata.EffectiveInterval.Until, decision.EffectiveInterval.Until));
    }
    internal Task<int> LockEntry(RecoveryEntryCaller caller, Guid requestId, CancellationToken ct) => database.Database.ExecuteSqlInterpolatedAsync(
        $"SELECT pg_advisory_xact_lock(hashtextextended({"recovery-entry:" + caller.BrowserDigest + ":" + requestId.ToString("N")},0))", ct);
    internal async Task<RecoveryEntryRow?> Entry(RecoveryEntryCaller caller, Guid requestId, CancellationToken ct)
    {
        var entry = await database.Set<RecoveryEntryRow>().FromSqlInterpolated($"SELECT * FROM identity.recovery_entries WHERE browser_digest={caller.BrowserDigest} AND request_id={requestId} FOR UPDATE").SingleOrDefaultAsync(ct).ConfigureAwait(false);
        if (entry is not null) await database.Entry(entry).ReloadAsync(ct).ConfigureAwait(false);
        return entry;
    }
    internal async Task<IReadOnlyList<RecoveryCaseCodeSetFacts>> CodeSets(string accountId, CancellationToken ct) => Array.AsReadOnly(await database.Set<RecoveryCodeSetRow>()
        .AsNoTracking().Where(x => x.AccountId == accountId).OrderBy(x => x.SetReference).Select(x => new RecoveryCaseCodeSetFacts(x.SetReference, x.State, x.LifecycleVersion, x.UsageVersion)).ToArrayAsync(ct).ConfigureAwait(false));
    internal bool EntryCurrent(RecoveryEntryCaller caller, RecoveryEntryRow entry, DateTimeOffset at) => EntryActor(caller, entry) && at >= entry.CreatedAt &&
        at < entry.RecoveryExpiresAt && PasswordCurrent(caller.Origin, at).Snapshot == entry.PolicySnapshot;
    internal static bool EntryActor(RecoveryEntryCaller caller, RecoveryEntryRow entry) => caller.Valid && entry.BrowserDigest == caller.BrowserDigest &&
        entry.PreviousCookieDigest == caller.PreviousCookieDigest && entry.Origin == caller.Origin && entry.NetworkDigest == caller.NetworkDigest;
    private static bool Admitted(RecoveryCaseHierarchyDecision decision, RecoveryCaseHierarchyFacts facts, DateTimeOffset at) =>
        decision.RecoveryCodeAllowed && decision.StrongAuthenticatorUnavailable && decision.FactsDigest == facts.Digest() &&
        !string.IsNullOrWhiteSpace(decision.PolicyVersion) && !string.IsNullOrWhiteSpace(decision.EvidenceReference) && decision.EffectiveInterval.Contains(at);
    private static string PasswordSelectionContext(RecoveryEntryRow entry) => AuthenticationIdentity.Digest(JsonSerializer.Serialize(new
        { entry.EntryId, entry.RequestId, entry.BrowserDigest, entry.PreviousCookieDigest, entry.Origin, entry.NetworkDigest, entry.IdentifierDigest, entry.GoalReference, entry.PolicySnapshot }));
    internal static string UserSourceDigest(IdentityUserAdapter user) => AuthenticationIdentity.Digest(JsonSerializer.Serialize(new
    {
        user.Id, user.AccountId, user.UserName, user.NormalizedUserName, user.Email, user.NormalizedEmail, user.EmailConfirmed,
        user.PhoneNumber, user.PhoneNumberConfirmed, user.TwoFactorEnabled, user.LockoutEnd, user.LockoutEnabled, user.AccessFailedCount
    }));
    private async Task<RecoveryEntryResult> DescribeEntry(RecoveryEntryCaller caller, RecoveryEntryRow entry, IDbContextTransaction tx, CancellationToken ct)
    {
        if (!EntryCurrent(caller, entry, clock.GetUtcNow())) return await EntryFinish(tx, new("NOT_FOUND_OR_NOT_DISCLOSABLE"), ct).ConfigureAwait(false);
        if (entry.State != "REPAIRED") return await EntryFinish(tx, new(null, entry), ct).ConfigureAwait(false);
        var row = await database.Set<RecoveryCaseRow>().SingleOrDefaultAsync(x => x.EntryId == entry.EntryId, ct).ConfigureAwait(false);
        if (row is null) return await EntryFinish(tx, new("NOT_FOUND_OR_NOT_DISCLOSABLE"), ct).ConfigureAwait(false);
        var facts = await LoadAccountFacts(row.AccountId, null, ct).ConfigureAwait(false);
        if (row.ProofKind == "GOVERNED_REVIEW") return await DescribeGovernedEntry(caller, entry, row, facts, tx, ct).ConfigureAwait(false);
        if (row.ProofKind == "CONTACT_RISK") return await DescribeContactEntry(caller, entry, row, facts, tx, ct).ConfigureAwait(false);
        if (row.ProofKind == "PASSKEY") return await DescribePasskeyEntry(caller, entry, row, facts, tx, ct).ConfigureAwait(false);
        var sets = await CodeSets(row.AccountId, ct).ConfigureAwait(false);
        var result = await database.Set<RecoveryResultRow>().SingleOrDefaultAsync(x => x.CaseId == row.CaseId, ct).ConfigureAwait(false);
        var consumed = await database.Set<RecoveryCodeEntryRow>().AsNoTracking().SingleOrDefaultAsync(x => x.AccountId == row.AccountId && x.ConsumedByOperationId == OperationId(row), ct).ConfigureAwait(false);
        var now = clock.GetUtcNow();
        if (facts is null || result is null || !PasswordResultCurrent(entry, row, result, facts.User, now) ||
            consumed?.ConsumptionOutcomeReference != result.ResultId.ToString("N") || consumed.ConsumptionContextDigest != row.ContextDigest ||
            consumed.ConsumptionOwnerPolicyVersion != row.PolicyVersion || consumed.ConsumptionCorrelationId != row.RequestId.ToString("N") ||
            !PasswordAuthorityCurrent(caller, entry, row, facts, sets, now, true, consumed))
            return await EntryFinish(tx, new("NOT_FOUND_OR_NOT_DISCLOSABLE"), ct).ConfigureAwait(false);
        return await EntryFinish(tx, new(null, entry, row, result, true), ct).ConfigureAwait(false);
    }
    internal static bool PasswordResultCurrent(RecoveryEntryRow entry, RecoveryCaseRow row, RecoveryResultRow result, IdentityUserAdapter user, DateTimeOffset at) =>
        entry.State == "REPAIRED" && entry.Version == 2 && row.State == "REPAIRED" && row.Version == 2 && row.ResultId == result.ResultId &&
        row.EntryId == entry.EntryId && result.CaseId == row.CaseId && result.AccountId == row.AccountId && result.Outcome == "PASSWORD_REPAIRED_NO_SESSION" &&
        row.EndedAt == result.RecordedAt && entry.EndedAt == row.EndedAt && row.EndedAt >= row.CreatedAt && row.EndedAt < row.ExpiresAt && row.EndedAt <= at &&
        result.TargetSessionId is null && result.SessionOutboxEventId is null && result.RepairOutboxEventId is not null && result.PolicySnapshot == row.PolicySnapshot &&
        result.PolicyVersion == row.PolicyVersion && result.ContextDigest == row.ContextDigest && result.HierarchySnapshot == row.HierarchySnapshot &&
        result.ProofKind == row.ProofKind && result.SetReference == row.SetReference && result.SetLifecycleVersion == row.ExpectedSetLifecycleVersion &&
        result.PriorPasswordDigest == row.PriorPasswordDigest && result.PriorSecurityStampDigest == row.PriorSecurityStampDigest &&
        user.PasswordHash is not null && AuthenticationIdentity.Digest(user.PasswordHash) == result.RepairedPasswordDigest &&
        !string.IsNullOrWhiteSpace(user.SecurityStamp) && AuthenticationIdentity.Digest(user.SecurityStamp) == result.RepairedSecurityStampDigest &&
        result.RepairedSecurityStampDigest != result.PriorSecurityStampDigest && result.SessionEffect == row.SessionEffect &&
        result.UnresolvedEffectsJson == row.UnresolvedEffectsJson && result.ProcessingReference == row.ProcessingReference && result.RetentionReference == row.RetentionReference;
    private static bool OwnConsumptionCurrent(RecoveryCaseRow row, RecoveryCodeEntryRow? claim) => claim is not null && claim.AccountId == row.AccountId &&
        claim.SetReference == row.SetReference && claim.ConsumptionSetLifecycleVersion == row.ExpectedSetLifecycleVersion && claim.Version == 2 &&
        claim.ConsumedByOperationId == OperationId(row) && claim.ConsumptionContextDigest == row.ContextDigest &&
        claim.ConsumptionCorrelationId == row.RequestId.ToString("N") && claim.ConsumptionOwnerPolicyVersion == row.PolicyVersion &&
        claim.ConsumptionOutcomeReference == row.ResultId?.ToString("N") && claim.ConsumedAt is { } consumedAt &&
        IdentityPersistenceTime.Normalize(consumedAt) >= row.CreatedAt && IdentityPersistenceTime.Normalize(consumedAt) <= row.EndedAt;
    private async Task<RecoveryEntryResult> EntryFinish(IDbContextTransaction tx, RecoveryEntryResult result, CancellationToken ct)
    { await database.SaveChangesAsync(ct).ConfigureAwait(false); await tx.CommitAsync(ct).ConfigureAwait(false); return result; }
}
