using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;
using Tunner.Database.Outbox;

namespace Tunner.Identity.Persistence;

internal sealed partial class RecoveryCaseApplication
{
    private async Task<(string? Error, string? Hash)> PrepareRecoveryPasswordAsync(RecoveryEntryCaller caller, RecoveryEntryRow entry,
        RecoveryPasswordPolicyVersion profile, string? password, string? confirmation, CancellationToken ct)
    {
        if (!await RecoveryEntryBudgetAsync(caller, entry, profile, ct).ConfigureAwait(false)) return ("RATE_OR_ABUSE_LIMITED", null);
        if (password is null || password != confirmation || password.EnumerateRunes().Count() < profile.Values.PasswordMinimumLength ||
            password.EnumerateRunes().Count() > profile.Values.PasswordMaximumLength || await passwordBlocklist!.IsBlockedAsync(password, ct).ConfigureAwait(false))
            return ("VALIDATION_ERROR", null);
        var hasher = new PasswordHasher<IdentityUserAdapter>(Options.Create(new PasswordHasherOptions
            { CompatibilityMode = PasswordHasherCompatibilityMode.IdentityV3, IterationCount = profile.Values.PasswordHashIterations }));
        var preparedHash = hasher.HashPassword(new IdentityUserAdapter(), password);

        return (null, preparedHash);
    }
    private async Task<bool> RecoveryEntryBudgetAsync(RecoveryEntryCaller caller, RecoveryEntryRow entry,
        RecoveryPasswordPolicyVersion profile, CancellationToken ct)
    {
        // The same explicit budget applies before native options or password preparation, including neutral entries.
        // This short abuse transaction closes before the sole owner-effect transaction begins.
        await using var budget = await database.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        var auth = authenticationPolicies!.RequireEffective(clock.GetUtcNow()); auth.Validate();
        var allowed = await authentication.AllowAsync("recovery-entry:N:" + caller.NetworkDigest, profile.Values.EntryNetworkLimit, auth, clock.GetUtcNow(), ct).ConfigureAwait(false) &&
            await authentication.AllowAsync("recovery-entry:I:" + entry.IdentifierDigest, profile.Values.EntryIdentifierLimit, auth, clock.GetUtcNow(), ct).ConfigureAwait(false);
        await database.SaveChangesAsync(ct).ConfigureAwait(false); await budget.CommitAsync(ct).ConfigureAwait(false); return allowed;
    }
    internal RecoveryPasskeyCapability PasswordPasskeyCapability(string origin, DateTimeOffset at)
    {
        var profile = PasswordCurrent(origin, at).Values.PasskeyRecoveryProfile;
        if (profile is null || passkeys is null) return new(false, null);
        try { return passkeys.RecoveryCapability(origin, profile, at); }
        catch (InvalidOperationException) { return new(false, null); }
    }
    private RecoveryCaseRow? PreparePasskeyPasswordCase(RecoveryCaseRow row, RecoveryEntryCaller caller, RecoveryEntryRow entry,
        RecoveryCaseLockedFacts facts, RecoveryPasswordPolicyVersion profile, RecoveryCaseHierarchyFacts posture,
        RecoveryCaseHierarchyDecision decision, DateTimeOffset at)
    {
        var nativeProfile = profile.Values.PasskeyRecoveryProfile;
        var choice = decision.PasskeySelection;
        var credential = facts.Passkey(choice?.CredentialReference);
        if (passkeys is null || nativeProfile is null || choice is null || credential is null || credential.State != "ACTIVE" ||
            credential.Version != choice.Version || credential.AccountId != row.AccountId || choice.ProfileVersion != nativeProfile.Version ||
            !PasskeyAdmitted(decision, posture, at)) return null;
        row.ProofKind = "PASSKEY"; row.State = "PENDING_PASSKEY"; row.SetReference = null; row.ExpectedSetLifecycleVersion = null;
        row.ExpectedUsageVersion = null; row.UsageAdmission = null; row.PasskeyCredentialReference = choice.CredentialReference;
        row.ExpectedPasskeyVersion = choice.Version; row.ExpectedPasskeyUsageVersion = credential.UsageVersion; row.PasskeyCeremonyId = Guid.NewGuid();
        row.ContextDigest = Context(row); row.HierarchySnapshot = decision.Snapshot();
        row.ExpiresAt = IdentityPersistenceTime.Normalize(Clip(row.CreatedAt + profile.Values.CaseLifetime, entry.ExpiresAt,
            profile.Metadata.EffectiveInterval.Until, decision.EffectiveInterval.Until));
        row.RecoveryExpiresAt = entry.RecoveryExpiresAt;
        return at < row.ExpiresAt && passkeys.RecoveryNativeAvailable(row, nativeProfile, at) ? row : null;
    }
    private static bool PasskeyAdmitted(RecoveryCaseHierarchyDecision decision, RecoveryCaseHierarchyFacts posture, DateTimeOffset at) =>
        !decision.RecoveryCodeAllowed && !decision.StrongAuthenticatorUnavailable && decision.CodeSelection is null &&
        decision.PasskeySelection is { CredentialReference: var reference, Version: > 0 } && reference != Guid.Empty &&
        decision.FactsDigest == posture.Digest() && !string.IsNullOrWhiteSpace(decision.PolicyVersion) &&
        !string.IsNullOrWhiteSpace(decision.EvidenceReference) && decision.EffectiveInterval.Contains(at);
    private bool PasskeyPasswordAuthorityCurrent(RecoveryEntryCaller caller, RecoveryEntryRow entry, RecoveryCaseRow row,
        RecoveryCaseLockedFacts facts, DateTimeOffset at, bool repaired)
    {
        var profile = PasswordCurrent(caller.Origin, at); var nativeProfile = profile.Values.PasskeyRecoveryProfile;
        var credential = facts.Passkey(row.PasskeyCredentialReference);
        var posture = facts.Value() with { SecurityStampDigest = row.PriorSecurityStampDigest!, PasswordAvailable = true };
        var decision = hierarchy!.RequireCurrent(new(row.CaseId, row.RequestId, PasswordSelectionContext(entry), row.PolicyVersion,
            RecoveryCaseGoal.RepairPassword, posture), at);
        return passkeys is not null && nativeProfile is not null && credential is not null && row.ProofKind == "PASSKEY" &&
            row.SetReference is null && row.ExpectedSetLifecycleVersion is null && row.ExpectedUsageVersion is null && row.UsageAdmission is null &&
            row.PasskeyCeremonyId is not null && credential.AccountId == row.AccountId && credential.State == "ACTIVE" &&
            credential.Version == row.ExpectedPasskeyVersion && credential.UsageVersion == row.ExpectedPasskeyUsageVersion + (repaired ? 1 : 0) &&
            decision.PasskeySelection == new RecoveryCasePasskeySelection(credential.CredentialReference, credential.Version, nativeProfile.Version) &&
            PasskeyAdmitted(decision, posture, at) && decision.Snapshot() == row.HierarchySnapshot && posture.Digest() == row.FactsDigest &&
            EntryCurrent(caller, entry, at) && Context(row) == row.ContextDigest && row.EntryId == entry.EntryId && row.RequestId == entry.RequestId &&
            row.Goal == nameof(RecoveryCaseGoal.RepairPassword) && row.TargetSessionId is null && row.ExpectedSessionVersion is null &&
            row.OriginalBrowserDigest == caller.BrowserDigest && row.Origin == caller.Origin && row.NetworkDigest == caller.NetworkDigest &&
            row.AccountId == facts.Account.AccountId && row.PolicySnapshot == profile.Snapshot &&
            row.SessionEffect == nameof(RecoveryPasswordSessionEffect.InvalidateExistingTunnerSessions) &&
            row.ExpectedAccountVersion == facts.Account.Version && row.AccountState == facts.Account.State.ToString() &&
            row.UserSourceDigest == UserSourceDigest(facts.User) && facts.SourcesExceptPasskeyUnmutated(database, credential) &&
            passkeys.RecoveryNativeAvailable(row, nativeProfile, at) &&
            (repaired || facts.User.PasswordHash is not null && AuthenticationIdentity.Digest(facts.User.PasswordHash) == row.PriorPasswordDigest &&
                AuthenticationIdentity.Digest(facts.User.SecurityStamp!) == row.PriorSecurityStampDigest) &&
            row.RecoveryExpiresAt == entry.RecoveryExpiresAt && row.ExpiresAt == IdentityPersistenceTime.Normalize(Clip(row.CreatedAt + profile.Values.CaseLifetime, entry.ExpiresAt,
                profile.Metadata.EffectiveInterval.Until, decision.EffectiveInterval.Until));
    }
    internal async Task<RecoveryPasskeyOptionsResult> StartPasswordPasskeyAsync(RecoveryEntryCaller caller, Guid requestId,
        HttpContext http, CancellationToken ct = default)
    {
        Clean(); if (!caller.Valid || requestId == Guid.Empty) return new("VALIDATION_ERROR");
        if (!PasswordAvailable || passkeys is null) return new("RECOVERY_UNAVAILABLE");
        var initial = PasswordCurrent(caller.Origin, clock.GetUtcNow());
        if (!PasswordPasskeyCapability(caller.Origin, clock.GetUtcNow()).Available) return new("RECOVERY_UNAVAILABLE");
        var originalEntry = await database.Set<RecoveryEntryRow>().AsNoTracking().SingleOrDefaultAsync(x => x.BrowserDigest == caller.BrowserDigest && x.RequestId == requestId, ct).ConfigureAwait(false);
        if (originalEntry is null || !EntryActor(caller, originalEntry) || !EntryCurrent(caller, originalEntry, clock.GetUtcNow()) || originalEntry.State != "OPEN" || clock.GetUtcNow() >= originalEntry.ExpiresAt)
            return new("RECOVERY_PROOF_REJECTED");
        if (!await RecoveryEntryBudgetAsync(caller, originalEntry, initial, ct).ConfigureAwait(false)) return new("RATE_OR_ABUSE_LIMITED", originalEntry);
        await using var tx = await database.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        try
        {
            await LockEntry(caller, requestId, ct).ConfigureAwait(false); var entry = await Entry(caller, requestId, ct).ConfigureAwait(false);
            if (entry is null || !EntryCurrent(caller, entry, clock.GetUtcNow()) || entry.State != "OPEN") return new("RECOVERY_PROOF_REJECTED");
            var at = clock.GetUtcNow(); var profile = PasswordCurrent(caller.Origin, at);
            if (initial.Snapshot != profile.Snapshot || profile.Values.PasskeyRecoveryProfile is not { } nativeProfile) return new("RECOVERY_UNAVAILABLE");
            if (entry.PasskeyCeremonyId is { } originalId)
            {
                if (entry.PasskeyProfileVersion != nativeProfile.Version || entry.PasskeyPolicyVersion != nativeProfile.NativePolicyVersion ||
                    entry.PasskeyOptionsJson is null || entry.PasskeyExpiresAt is null) return new("RECOVERY_UNAVAILABLE");
                var original = await database.Set<PasskeyCeremonyRow>().AsNoTracking().SingleOrDefaultAsync(x => x.CeremonyId == originalId && x.Kind == "RECOVERY", ct).ConfigureAwait(false);
                at = clock.GetUtcNow();
                if (!EntryCurrent(caller, entry, at) || initial.Snapshot != PasswordCurrent(caller.Origin, at).Snapshot || !PasswordPasskeyCapability(caller.Origin, at).Available)
                    return new("RECOVERY_UNAVAILABLE");
                await tx.CommitAsync(ct).ConfigureAwait(false);
                return new(null, entry, original, new(originalId, entry.PasskeyOptionsJson, entry.PasskeyExpiresAt.Value));
            }
            var preparedOptions = await passkeys.PrepareRecoveryOptionsAsync(entry, nativeProfile, http).ConfigureAwait(false);
            var publicId = Guid.NewGuid(); PasskeyCeremonyRow? ceremony = null;
            var snapshot = await database.Set<RecoveryCaseRow>().AsNoTracking().SingleOrDefaultAsync(x => x.EntryId == entry.EntryId && x.ProofKind == "PASSKEY", ct).ConfigureAwait(false);
            if (snapshot is not null)
            {
                var actor = new RecoveryCaseCaller(snapshot.AccountId, caller.BrowserDigest, caller.Origin, caller.NetworkDigest);
                await LockOriginal(actor, requestId, ct).ConfigureAwait(false); var row = await Original(actor, requestId, ct).ConfigureAwait(false);
                var facts = row is null ? null : await LoadAccountFacts(row.AccountId, null, ct).ConfigureAwait(false);
                try
                {
                    at = clock.GetUtcNow();
                    if (row is not null && facts is not null && row.State == "PENDING_PASSKEY" && row.Version == 1 && at < row.ExpiresAt &&
                        PasskeyPasswordAuthorityCurrent(caller, entry, row, facts, at, false))
                    {
                        ceremony = await passkeys.StartRecoveryOptionsAsync(row, facts.User, nativeProfile, preparedOptions, ct).ConfigureAwait(false);
                        if (ceremony is not null && !PasskeyPasswordAuthorityCurrent(caller, entry, row, facts, clock.GetUtcNow(), false))
                        {
                            database.Entry(ceremony).State = EntityState.Detached; ceremony = null;
                        }
                    }
                }
                catch (Exception error) when (error is InvalidOperationException or ArgumentException)
                {
                    if (ceremony is not null && database.Entry(ceremony).State == EntityState.Added) database.Entry(ceremony).State = EntityState.Detached;
                    ceremony = null;
                }
            }
            if (ceremony is not null) publicId = ceremony.CeremonyId;
            entry.PasskeyCeremonyId = publicId; entry.PasskeyOptionsJson = preparedOptions.OptionsJson; entry.PasskeyExpiresAt = preparedOptions.ExpiresAt;
            entry.PasskeyProfileVersion = nativeProfile.Version; entry.PasskeyPolicyVersion = nativeProfile.NativePolicyVersion;
            at = clock.GetUtcNow();
            if (!EntryCurrent(caller, entry, at) || initial.Snapshot != PasswordCurrent(caller.Origin, at).Snapshot ||
                !PasswordPasskeyCapability(caller.Origin, at).Available || at >= preparedOptions.ExpiresAt)
                throw new InvalidOperationException("Original recovery presentation authority changed before commit.");
            database.ChangeTracker.DetectChanges();
            if (database.ChangeTracker.Entries().Any(x => (x.State is EntityState.Added or EntityState.Modified or EntityState.Deleted) &&
                    !ReferenceEquals(x.Entity, entry) && !(ReferenceEquals(x.Entity, ceremony) && x.State == EntityState.Added)) ||
                database.Entry(entry).Properties.Where(x => x.IsModified).Any(x => x.Metadata.Name is not (nameof(RecoveryEntryRow.PasskeyCeremonyId) or
                    nameof(RecoveryEntryRow.PasskeyOptionsJson) or nameof(RecoveryEntryRow.PasskeyProfileVersion) or nameof(RecoveryEntryRow.PasskeyPolicyVersion) or nameof(RecoveryEntryRow.PasskeyExpiresAt))))
                throw new InvalidOperationException("Native recovery presentation cannot mutate another owner source.");
            await database.SaveChangesAsync(ct).ConfigureAwait(false);
            at = clock.GetUtcNow();
            if (!EntryCurrent(caller, entry, at) || initial.Snapshot != PasswordCurrent(caller.Origin, at).Snapshot ||
                !PasswordPasskeyCapability(caller.Origin, at).Available || at >= preparedOptions.ExpiresAt || database.ChangeTracker.HasChanges())
                throw new InvalidOperationException("Recovery presentation authority changed after persistence.");
            await tx.CommitAsync(ct).ConfigureAwait(false);
            return new(null, entry, ceremony, new(publicId, preparedOptions.OptionsJson, preparedOptions.ExpiresAt));
        }
        catch { database.ChangeTracker.Clear(); throw; }
    }
    internal async Task<RecoveryEntryResult> RepairPasswordWithPasskeyAsync(RecoveryEntryCaller caller, Guid requestId, long expectedVersion,
        Guid ceremonyId, JsonElement response, string? password, string? confirmation, HttpContext http, CancellationToken ct = default)
    {
        Clean(); if (!caller.Valid || requestId == Guid.Empty || ceremonyId == Guid.Empty) return new("VALIDATION_ERROR");
        if (!PasswordAvailable || passkeys is null) return new("RECOVERY_UNAVAILABLE");
        var profile = PasswordCurrent(caller.Origin, clock.GetUtcNow());
        var capability = PasswordPasskeyCapability(caller.Origin, clock.GetUtcNow());
        if (!capability.Available) return new("RECOVERY_UNAVAILABLE");
        if (!PasskeyApplication.RecoveryCredentialShapeValid(response, capability.MaximumCredentialJsonBytes!.Value))
            return new("VALIDATION_ERROR");
        var entry = await database.Set<RecoveryEntryRow>().AsNoTracking().SingleOrDefaultAsync(x => x.BrowserDigest == caller.BrowserDigest && x.RequestId == requestId, ct).ConfigureAwait(false);
        if (entry is null || !EntryActor(caller, entry)) return new("NOT_FOUND_OR_NOT_DISCLOSABLE");
        var snapshot = await database.Set<RecoveryCaseRow>().AsNoTracking().SingleOrDefaultAsync(x => x.EntryId == entry.EntryId, ct).ConfigureAwait(false);
        if (entry.State == "REPAIRED")
        {
            if (snapshot is null || snapshot.ProofKind != "PASSKEY" || snapshot.PasskeyCeremonyId != ceremonyId) return new("RECOVERY_PROOF_REJECTED", entry);
            var original = await database.Set<PasskeyCeremonyRow>().AsNoTracking().SingleOrDefaultAsync(x => x.CeremonyId == ceremonyId, ct).ConfigureAwait(false);
            if (original is null || original.RequestDigest != AuthenticationIdentity.Digest(response.GetRawText())) return new("RECOVERY_PROOF_REJECTED", entry);
            return await ReadPasswordEntryAsync(caller, requestId, ct).ConfigureAwait(false);
        }
        if (entry.Version != expectedVersion || entry.State != "OPEN" || !EntryCurrent(caller, entry, clock.GetUtcNow())) return new("RECOVERY_PROOF_REJECTED", entry);
        var preparation = await PrepareRecoveryPasswordAsync(caller, entry, profile, password, confirmation, ct).ConfigureAwait(false);
        if (preparation.Error is not null) return new(preparation.Error, entry);
        if (snapshot is null || snapshot.ProofKind != "PASSKEY" || snapshot.PasskeyCeremonyId != ceremonyId || profile.Values.PasskeyRecoveryProfile is null)
            return new("RECOVERY_PROOF_REJECTED", entry);
        if (!PasskeyEntryBinding(entry, snapshot, profile.Values.PasskeyRecoveryProfile, clock.GetUtcNow())) return new("RECOVERY_PROOF_REJECTED", entry);
        var prepared = await passkeys.PrepareRecoveryAssertionAsync(snapshot, profile.Values.PasskeyRecoveryProfile, ceremonyId, response, http, ct).ConfigureAwait(false);
        if (prepared is null) return new("RECOVERY_PROOF_REJECTED", entry);
        await using var tx = await database.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        try
        {
            await LockEntry(caller, requestId, ct).ConfigureAwait(false); entry = await Entry(caller, requestId, ct).ConfigureAwait(false);
            if (entry is null) return new("RECOVERY_PROOF_REJECTED");
            var actor = new RecoveryCaseCaller(snapshot.AccountId, caller.BrowserDigest, caller.Origin, caller.NetworkDigest);
            await LockOriginal(actor, requestId, ct).ConfigureAwait(false); var row = await Original(actor, requestId, ct).ConfigureAwait(false);
            var facts = row is null ? null : await LoadAccountFacts(row.AccountId, null, ct).ConfigureAwait(false);
            var at = clock.GetUtcNow();
            if (row is not null && facts is not null && row.CaseId == snapshot.CaseId && row.ContextDigest == prepared.Context &&
                row.State == "REPAIRED" && entry.State == "REPAIRED")
            {
                var original = await database.Set<PasskeyCeremonyRow>().SingleOrDefaultAsync(x => x.CeremonyId == prepared.CeremonyId, ct).ConfigureAwait(false);
                return original?.RequestDigest == prepared.RequestDigest ? await DescribePasskeyEntry(caller, entry, row, facts, tx, ct).ConfigureAwait(false)
                    : new("RECOVERY_PROOF_REJECTED", entry);
            }
            if (row is null || row.CaseId != snapshot.CaseId || row.ContextDigest != prepared.Context || facts is null || row.State != "PENDING_PASSKEY" ||
                row.Version != 1 || row.ResultId is not null || entry.State != "OPEN" || entry.Version != expectedVersion || at >= row.ExpiresAt ||
                profile.Snapshot != PasswordCurrent(caller.Origin, at).Snapshot || !PasskeyPasswordAuthorityCurrent(caller, entry, row, facts, at, false))
                return new("RECOVERY_PROOF_REJECTED");
            var ceremony = await passkeys.LockRecoveryAssertionAsync(row, facts.User, profile.Values.PasskeyRecoveryProfile!, prepared, ct).ConfigureAwait(false);
            at = clock.GetUtcNow(); var credential = facts.Passkey(row.PasskeyCredentialReference);
            if (ceremony is null || credential is null || !PasskeyPasswordAuthorityCurrent(caller, entry, row, facts, at, false) || at >= row.ExpiresAt)
                return new("RECOVERY_PROOF_REJECTED");
            if (!PasskeyEntryBinding(entry, row, profile.Values.PasskeyRecoveryProfile!, at) || entry.PasskeyOptionsJson != ceremony.OptionsJson)
                return new("RECOVERY_PROOF_REJECTED");
            var nativeAfter = await passkeys.ApplyRecoveryAssertionAsync(row, facts.User, credential, prepared, ct).ConfigureAwait(false);
            at = clock.GetUtcNow();
            if (!PasskeyPasswordAuthorityCurrent(caller, entry, row, facts, at, false) || at >= row.ExpiresAt || at >= ceremony.ExpiresAt)
                throw new InvalidOperationException("Strong recovery authority changed after native counter update.");
            var concurrencyBefore = facts.User.ConcurrencyStamp;
            ApplyPassword(row, facts.User, preparation.Hash!);
            var ended = IdentityPersistenceTime.Normalize(at); var eventId = Guid.NewGuid();
            credential.LastUsedAt = ended; credential.UsageVersion++;
            var result = new RecoveryResultRow
            {
                ResultId = Guid.NewGuid(), CaseId = row.CaseId, AccountId = row.AccountId, Outcome = "PASSWORD_REPAIRED_NO_SESSION", ProofKind = "PASSKEY",
                ContextDigest = row.ContextDigest, PolicyVersion = row.PolicyVersion, PolicySnapshot = row.PolicySnapshot, HierarchySnapshot = row.HierarchySnapshot,
                RecordedAt = ended, UnresolvedEffectsJson = row.UnresolvedEffectsJson, ProcessingReference = row.ProcessingReference, RetentionReference = row.RetentionReference,
                RepairOutboxEventId = eventId, PriorPasswordDigest = row.PriorPasswordDigest, RepairedPasswordDigest = AuthenticationIdentity.Digest(preparation.Hash!),
                PriorSecurityStampDigest = row.PriorSecurityStampDigest, RepairedSecurityStampDigest = AuthenticationIdentity.Digest(facts.User.SecurityStamp!), SessionEffect = row.SessionEffect,
                PasskeyCredentialReference = credential.CredentialReference, PasskeyVersion = credential.Version, PasskeyUsageVersion = credential.UsageVersion,
                PasskeyCeremonyId = ceremony.CeremonyId, PasskeyMethod = prepared.Method, PasskeyAssurance = prepared.Assurance,
                NativeBeforeDigest = prepared.NativeBeforeDigest, NativeAfterDigest = nativeAfter
            };
            row.State = "REPAIRED"; row.Version++; row.ResultId = result.ResultId; row.EndedAt = ended;
            entry.State = "REPAIRED"; entry.Version++; entry.EndedAt = ended;
            passkeys.CompleteRecoveryCeremony(ceremony, prepared, result.ResultId, ended);
            database.Add(result);
            database.Add(new OutboxMessage(eventId, "1.0", "IdentitySecurityActivityRequested", "1", ended, "tunner/identity", row.CaseId.ToString("N"),
                "RESTRICTED_SECURITY", JsonSerializer.Serialize(new { reference = result.ResultId.ToString("N") }), row.Version.ToString(CultureInfo.InvariantCulture), OperationId(row), requestId.ToString("N")));
            database.ChangeTracker.DetectChanges(); at = clock.GetUtcNow();
            // Causal postconditions before the save and again after every awaited persistence operation.
            if (!PasswordResultCurrent(entry, row, result, facts.User, at) || !PasskeyPasswordAuthorityCurrent(caller, entry, row, facts, at, true) ||
                facts.User.PasswordHash != preparation.Hash || facts.User.ConcurrencyStamp == concurrencyBefore ||
                database.Entry(facts.User).State != EntityState.Modified || database.Entry(facts.User).Properties.Where(x => x.IsModified).Any(x =>
                    x.Metadata.Name is not (nameof(IdentityUserAdapter.PasswordHash) or nameof(IdentityUserAdapter.SecurityStamp) or nameof(IdentityUserAdapter.ConcurrencyStamp))) ||
                database.Entry(credential).Properties.Where(x => x.IsModified).Any(x => x.Metadata.Name is not (nameof(PasskeyCredentialRow.LastUsedAt) or nameof(PasskeyCredentialRow.UsageVersion))) ||
                database.Entry(result).State != EntityState.Added || database.Entry(row).State != EntityState.Modified || database.Entry(entry).State != EntityState.Modified ||
                database.Entry(ceremony).State != EntityState.Modified || !database.ChangeTracker.Entries<OutboxMessage>().Any(x => x.State == EntityState.Added && x.Entity.EventId == eventId))
                throw new InvalidOperationException("Recovery native/password/result effects lost their causal witnesses.");
            await database.SaveChangesAsync(ct).ConfigureAwait(false);
            var persistedNative = await passkeys.RecoveryNativeDigest(facts.User.Id, credential.CredentialId, ct).ConfigureAwait(false);
            at = clock.GetUtcNow();
            if (persistedNative != result.NativeAfterDigest || !PasswordResultCurrent(entry, row, result, facts.User, at) ||
                !PasskeyPasswordAuthorityCurrent(caller, entry, row, facts, at, true) || at >= row.ExpiresAt || at >= ceremony.ExpiresAt ||
                !PasskeyEntryBinding(entry, row, profile.Values.PasskeyRecoveryProfile!, at) || !passkeys.CurrentBinding(ceremony, passkeys.Current(row.Origin), at) || database.ChangeTracker.HasChanges())
                throw new InvalidOperationException("Recovery authority changed before final commit.");
            await tx.CommitAsync(ct).ConfigureAwait(false); return new(null, entry, row, result);
        }
        catch { database.ChangeTracker.Clear(); throw; }
    }
    private async Task<RecoveryEntryResult> DescribePasskeyEntry(RecoveryEntryCaller caller, RecoveryEntryRow entry, RecoveryCaseRow row,
        RecoveryCaseLockedFacts? facts, IDbContextTransaction tx, CancellationToken ct)
    {
        var result = await database.Set<RecoveryResultRow>().SingleOrDefaultAsync(x => x.CaseId == row.CaseId, ct).ConfigureAwait(false);
        var ceremony = await database.Set<PasskeyCeremonyRow>().SingleOrDefaultAsync(x => x.CeremonyId == row.PasskeyCeremonyId, ct).ConfigureAwait(false);
        var credential = facts?.Passkey(row.PasskeyCredentialReference); var profile = PasswordCurrent(caller.Origin, clock.GetUtcNow()).Values.PasskeyRecoveryProfile;
        var native = facts is null || credential is null || passkeys is null ? null : await passkeys.RecoveryNativeDigest(facts.User.Id, credential.CredentialId, ct).ConfigureAwait(false);
        var outbox = result is not null && await database.Set<OutboxMessage>().AnyAsync(x => x.EventId == result.RepairOutboxEventId && x.EventType == "IdentitySecurityActivityRequested" &&
            x.OperationId == OperationId(row) && x.CorrelationId == row.RequestId.ToString("N"), ct).ConfigureAwait(false);
        var at = clock.GetUtcNow();
        if (facts is null || result is null || credential is null || ceremony is null || profile is null || passkeys is null ||
            !PasswordResultCurrent(entry, row, result, facts.User, at) || !PasskeyPasswordAuthorityCurrent(caller, entry, row, facts, at, true) ||
            !PasskeyEntryBinding(entry, row, profile, row.EndedAt!.Value) ||
            !PasskeyApplication.RecoveryBinding(ceremony, row, profile) || ceremony.State != "COMPLETED" || ceremony.Version != 2 || ceremony.ProofReference != result.ResultId ||
            ceremony.EndedAt != row.EndedAt || ceremony.RequestDigest.Length != 64 || ceremony.ProtectedState.Length != 0 || ceremony.OptionsJson.Length != 0 ||
            result.PasskeyCeremonyId != ceremony.CeremonyId || result.PasskeyCredentialReference != credential.CredentialReference || result.PasskeyVersion != credential.Version ||
            result.PasskeyUsageVersion != credential.UsageVersion || credential.LastUsedAt != row.EndedAt || result.NativeAfterDigest != native ||
            result.NativeBeforeDigest?.Length != 64 || result.PasskeyMethod is null || result.PasskeyAssurance is null || !profile.Admits(result.PasskeyMethod, result.PasskeyAssurance) || !outbox)
            return await EntryFinish(tx, new("NOT_FOUND_OR_NOT_DISCLOSABLE"), ct).ConfigureAwait(false);
        return await EntryFinish(tx, new(null, entry, row, result, true), ct).ConfigureAwait(false);
    }
    private static bool PasskeyEntryBinding(RecoveryEntryRow entry, RecoveryCaseRow row, RecoveryPasswordPasskeyProfile profile, DateTimeOffset at) =>
        entry.PasskeyCeremonyId == row.PasskeyCeremonyId && entry.PasskeyOptionsJson is not null && entry.PasskeyProfileVersion == profile.Version &&
        entry.PasskeyPolicyVersion == profile.NativePolicyVersion && entry.PasskeyExpiresAt is { } expires && at < expires && expires <= entry.ExpiresAt;
}
