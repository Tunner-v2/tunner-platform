using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Tunner.Identity.Persistence;

internal sealed partial class PasskeyApplication
{
    /// <summary>Local verified preparation only; never a bearer grant or authenticated session.</summary>
    internal sealed class PreparedRecoveryAssertion(PasskeyApplication owner, Guid caseId, Guid ceremonyId, string context,
        string requestDigest, string nativeBeforeDigest, long credentialUsageVersion, UserPasskeyInfo verified, string method, string assurance)
    {
        internal PasskeyApplication Owner { get; } = owner;
        internal Guid CaseId { get; } = caseId;
        internal Guid CeremonyId { get; } = ceremonyId;
        internal string Context { get; } = context;
        internal string RequestDigest { get; } = requestDigest;
        internal string NativeBeforeDigest { get; } = nativeBeforeDigest;
        internal long CredentialUsageVersion { get; } = credentialUsageVersion;
        internal UserPasskeyInfo Verified { get; } = verified;
        internal string Method { get; } = method;
        internal string Assurance { get; } = assurance;
        public override string ToString() => nameof(PreparedRecoveryAssertion);
    }
    internal bool RecoveryNativeAvailable(RecoveryCaseRow row, RecoveryPasswordPasskeyProfile profile, DateTimeOffset at)
    {
        var policy = policies.RequireEffective(at);
        return policy.Version == profile.NativePolicyVersion && policy.Origin == row.Origin;
    }
    internal RecoveryPasskeyCapability RecoveryCapability(string origin, RecoveryPasswordPasskeyProfile profile, DateTimeOffset at)
    {
        var policy = policies.RequireEffective(at);
        return policy.Version == profile.NativePolicyVersion && policy.Origin == origin
            ? new(true, policy.MaximumCredentialJsonBytes) : new(false, null);
    }
    internal sealed record PreparedRecoveryOptions(PasskeyApplication Owner, Guid EntryId, string Origin, string ProfileVersion, string PolicyVersion, string AuthenticationPolicyVersion,
        string OptionsJson, string ProtectedState, DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt)
    {
        public override string ToString() => nameof(PreparedRecoveryOptions);
    }
    internal async Task<PreparedRecoveryOptions> PrepareRecoveryOptionsAsync(RecoveryEntryRow entry, RecoveryPasswordPasskeyProfile profile, HttpContext http)
    {
        var policy = Current(entry.Origin);
        if (policy.Version != profile.NativePolicyVersion) throw new InvalidOperationException("Current native recovery capability is unavailable.");
        // Every entry receives the SAME discoverable native options shape. Only an actual case retains protected assertion state.
        var options = await Handler(policy).MakeRequestOptionsAsync(null, http).ConfigureAwait(false);
        var at = clock.GetUtcNow();
        if (!RecoveryCapability(entry.Origin, profile, at).Available || at >= entry.ExpiresAt)
            throw new InvalidOperationException("Native recovery capability changed during presentation preparation.");
        var auth = authenticationPolicies.RequireEffective(at); auth.Validate();
        return new(this, entry.EntryId, entry.Origin, profile.Version, policy.Version, auth.Version, options.RequestOptionsJson,
            stateProtection.Protect(options.AssertionState ?? throw new InvalidOperationException("Native assertion state is unavailable.")),
            IdentityPersistenceTime.Normalize(at), Min(entry.ExpiresAt, IdentityPersistenceTime.Normalize(at + policy.CeremonyLifetime)));
    }
    internal static bool RecoveryCredentialShapeValid(JsonElement response, int maximumBytes)
    {
        if (response.ValueKind != JsonValueKind.Object || System.Text.Encoding.UTF8.GetByteCount(response.GetRawText()) > maximumBytes) return false;
        try { _ = ReadCredentialId(response); return true; }
        catch (ArgumentException) { return false; }
    }
    internal async Task<PasskeyCeremonyRow?> StartRecoveryOptionsAsync(RecoveryCaseRow recovery, IdentityUserAdapter user,
        RecoveryPasswordPasskeyProfile profile, PreparedRecoveryOptions options, CancellationToken ct)
    {
        if (database.Database.CurrentTransaction is null || recovery.ProofKind != "PASSKEY" || recovery.State != "PENDING_PASSKEY" ||
            recovery.AccountId != user.AccountId || recovery.PasskeyCeremonyId is not { } id || !ReferenceEquals(options.Owner, this) || options.Origin != recovery.Origin ||
            options.EntryId != recovery.EntryId || options.ProfileVersion != profile.Version) return null;
        var policy = Current(recovery.Origin);
        if (policy.Version != profile.NativePolicyVersion || options.PolicyVersion != policy.Version) return null;
        await LockCredentialOwner(recovery.AccountId, ct).ConfigureAwait(false);
        await LockCeremony(id, ct).ConfigureAwait(false);
        var prior = await database.Set<PasskeyCeremonyRow>().SingleOrDefaultAsync(x => x.CeremonyId == id, ct).ConfigureAwait(false);
        if (prior is not null)
        {
            await database.Entry(prior).ReloadAsync(ct).ConfigureAwait(false);
            return RecoveryBinding(prior, recovery, profile) && prior.State == "PENDING" && CurrentBinding(prior, policy, clock.GetUtcNow()) ? prior : null;
        }
        var now = clock.GetUtcNow();
        if (await database.Set<PasskeyCeremonyRow>().CountAsync(x => x.AccountId == recovery.AccountId && x.Kind == "RECOVERY" &&
            x.State == "PENDING" && x.ExpiresAt > now, ct).ConfigureAwait(false) >= policy.MaximumPendingCeremonies) return null;
        var credential = await database.Set<PasskeyCredentialRow>().SingleOrDefaultAsync(x => x.CredentialReference == recovery.PasskeyCredentialReference &&
            x.AccountId == recovery.AccountId && x.State == "ACTIVE", ct).ConfigureAwait(false);
        if (credential is null || credential.Version != recovery.ExpectedPasskeyVersion || credential.UsageVersion != recovery.ExpectedPasskeyUsageVersion) return null;
        var native = (await users.GetPasskeysAsync(user).ConfigureAwait(false)).SingleOrDefault(x => x.CredentialId.AsSpan().SequenceEqual(credential.CredentialId));
        // The signed assertion will determine actual BS; this only checks that the selected native method can be qualified.
        if (native is null || !(Admitted(native.IsBackupEligible, false) || native.IsBackupEligible && Admitted(true, true))) return null;
        bool Admitted(bool eligible, bool backed) => policy.AssuranceMappings.TryGetValue(PasskeyPolicy.AssuranceKey(eligible, backed), out var assurance) &&
            profile.Admits(PasskeyPolicy.MethodKey(eligible, backed), assurance);
        now = clock.GetUtcNow();
        if (!RecoveryNativeAvailable(recovery, profile, now) || now >= recovery.ExpiresAt) return null;
        var auth = authenticationPolicies.RequireEffective(now); auth.Validate();
        var expires = Min(recovery.ExpiresAt, options.ExpiresAt);
        if (expires <= now || options.AuthenticationPolicyVersion != auth.Version) return null;
        var row = new PasskeyCeremonyRow
        {
            CeremonyId = id, RequestId = recovery.RequestId, Kind = "RECOVERY", BrowserDigest = recovery.OriginalBrowserDigest,
            AccountId = recovery.AccountId, Origin = recovery.Origin, PolicyVersion = policy.Version, AuthenticationPolicyVersion = auth.Version,
            SecurityStampDigest = recovery.PriorSecurityStampDigest!, AssurancePolicyVersion = profile.Version,
            BoundOperationId = RecoveryCaseApplication.OperationId(recovery), BoundCorrelationId = recovery.RequestId.ToString("N"),
            BoundOperation = recovery.Goal, BoundContext = "ACCOUNT_RECOVERY", BoundResourceId = recovery.CaseId.ToString("N"),
            BoundExpectedVersion = 1, BoundContextDigest = recovery.ContextDigest, ExpectedVersion = recovery.ExpectedPasskeyVersion!.Value,
            CredentialReference = recovery.PasskeyCredentialReference, CreatedAt = options.CreatedAt, ExpiresAt = expires,
            RecoveryExpiresAt = recovery.RecoveryExpiresAt, OptionsJson = options.OptionsJson, ProtectedState = options.ProtectedState,
            ProcessingReference = recovery.ProcessingReference, RetentionReference = recovery.RetentionReference
        };
        database.Add(row); return row;
    }
    internal async Task<PreparedRecoveryAssertion?> PrepareRecoveryAssertionAsync(RecoveryCaseRow recovery,
        RecoveryPasswordPasskeyProfile profile, Guid ceremonyId, JsonElement response, HttpContext http, CancellationToken ct)
    {
        Clean();
        try
        {
            var prepared = await PrepareRecoveryAssertionCoreAsync(recovery, profile, ceremonyId, response, http, ct).ConfigureAwait(false);
            if (database.ChangeTracker.HasChanges()) throw new InvalidOperationException("Native recovery preparation mutated persisted state.");
            return prepared;
        }
        catch (JsonException) { return null; }
        catch (ArgumentException) { return null; }
        catch (System.Security.Cryptography.CryptographicException) { return null; }
        catch (FormatException) { return null; }
        finally { database.ChangeTracker.Clear(); }
    }
    private async Task<PreparedRecoveryAssertion?> PrepareRecoveryAssertionCoreAsync(RecoveryCaseRow recovery,
        RecoveryPasswordPasskeyProfile profile, Guid ceremonyId, JsonElement response, HttpContext http, CancellationToken ct)
    {
            var policy = Current(recovery.Origin);
            var json = CredentialJson(response, policy); var id = ReadCredentialId(response); var digest = CredentialDigest(id);
            var ceremony = await database.Set<PasskeyCeremonyRow>().AsNoTracking().SingleOrDefaultAsync(x => x.CeremonyId == ceremonyId, ct).ConfigureAwait(false);
            if (ceremony is null || !RecoveryBinding(ceremony, recovery, profile) || ceremony.State != "PENDING" ||
                !CurrentBinding(ceremony, policy, clock.GetUtcNow()) || !RecoveryNativeAvailable(recovery, profile, clock.GetUtcNow())) return null;
            var credential = await database.Set<PasskeyCredentialRow>().AsNoTracking().SingleOrDefaultAsync(x => x.CredentialReference == recovery.PasskeyCredentialReference &&
                x.AccountId == recovery.AccountId && x.State == "ACTIVE" && x.CredentialDigest == digest, ct).ConfigureAwait(false);
            var user = await database.Users.AsNoTracking().SingleOrDefaultAsync(x => x.AccountId == recovery.AccountId, ct).ConfigureAwait(false);
            if (credential is null || user is null || credential.Version != recovery.ExpectedPasskeyVersion ||
                credential.UsageVersion != recovery.ExpectedPasskeyUsageVersion || AuthenticationIdentity.Digest(user.SecurityStamp!) != recovery.PriorSecurityStampDigest) return null;
            var before = await RecoveryNativeDigest(user.Id, id, ct).ConfigureAwait(false);
            if (before is null) return null;
            var verified = await Handler(policy).PerformAssertionAsync(new PasskeyAssertionContext
                { HttpContext = http, CredentialJson = json, AssertionState = stateProtection.Unprotect(ceremony.ProtectedState) }).ConfigureAwait(false);
            if (!verified.Succeeded || verified.User.AccountId != recovery.AccountId || verified.User.Id != user.Id ||
                !verified.Passkey.CredentialId.AsSpan().SequenceEqual(id) || !verified.Passkey.IsUserVerified) return null;
            var method = PasskeyPolicy.MethodKey(verified.Passkey.IsBackupEligible, verified.Passkey.IsBackedUp);
            var assurance = policy.RequireAssurance(PasskeyPolicy.AssuranceKey(verified.Passkey.IsBackupEligible, verified.Passkey.IsBackedUp));
            var after = await RecoveryNativeDigest(user.Id, id, ct).ConfigureAwait(false);
            var at = clock.GetUtcNow();
            if (before != after || !profile.Admits(method, assurance) || !CurrentBinding(ceremony, Current(recovery.Origin), at) ||
                !RecoveryNativeAvailable(recovery, profile, at) || at >= recovery.ExpiresAt || database.ChangeTracker.HasChanges()) return null;
            return new(this, recovery.CaseId, ceremonyId, recovery.ContextDigest, AuthenticationIdentity.Digest(json), before,
                credential.UsageVersion, verified.Passkey, method, assurance);
    }
    internal async Task<PasskeyCeremonyRow?> LockRecoveryAssertionAsync(RecoveryCaseRow recovery, IdentityUserAdapter user,
        RecoveryPasswordPasskeyProfile profile, PreparedRecoveryAssertion prepared, CancellationToken ct)
    {
        if (!ReferenceEquals(prepared.Owner, this) || database.Database.CurrentTransaction is null || prepared.CaseId != recovery.CaseId ||
            prepared.CeremonyId != recovery.PasskeyCeremonyId || prepared.Context != recovery.ContextDigest) return null;
        await LockCredentialOwner(recovery.AccountId, ct).ConfigureAwait(false);
        await LockCeremony(prepared.CeremonyId, ct).ConfigureAwait(false);
        await LockCredential(prepared.Verified.CredentialId, ct).ConfigureAwait(false);
        var ceremony = await database.Set<PasskeyCeremonyRow>().SingleOrDefaultAsync(x => x.CeremonyId == prepared.CeremonyId, ct).ConfigureAwait(false);
        if (ceremony is null) return null;
        await database.Entry(ceremony).ReloadAsync(ct).ConfigureAwait(false);
        var native = await database.Set<IdentityUserPasskey<string>>().FromSqlInterpolated(
            $"SELECT * FROM identity.\"AspNetUserPasskeys\" WHERE \"CredentialId\"={prepared.Verified.CredentialId} AND \"UserId\"={user.Id} FOR UPDATE").SingleOrDefaultAsync(ct).ConfigureAwait(false);
        var digest = await RecoveryNativeDigest(user.Id, prepared.Verified.CredentialId, ct).ConfigureAwait(false);
        var at = clock.GetUtcNow();
        return native is not null && digest == prepared.NativeBeforeDigest && RecoveryBinding(ceremony, recovery, profile) && ceremony.State == "PENDING" &&
            CurrentBinding(ceremony, Current(recovery.Origin), at) && RecoveryNativeAvailable(recovery, profile, at) && at < recovery.ExpiresAt &&
            profile.Admits(prepared.Method, prepared.Assurance) ? ceremony : null;
    }
    internal async Task<string> ApplyRecoveryAssertionAsync(RecoveryCaseRow recovery, IdentityUserAdapter user,
        PasskeyCredentialRow credential, PreparedRecoveryAssertion prepared, CancellationToken ct)
    {
        if (database.Database.CurrentTransaction is null || !ReferenceEquals(prepared.Owner, this) || prepared.CaseId != recovery.CaseId ||
            prepared.CeremonyId != recovery.PasskeyCeremonyId || prepared.Context != recovery.ContextDigest || credential.AccountId != recovery.AccountId ||
            credential.CredentialReference != recovery.PasskeyCredentialReference || credential.State != "ACTIVE" || credential.Version != recovery.ExpectedPasskeyVersion ||
            credential.UsageVersion != recovery.ExpectedPasskeyUsageVersion || credential.UsageVersion != prepared.CredentialUsageVersion ||
            !credential.CredentialId.AsSpan().SequenceEqual(prepared.Verified.CredentialId) ||
            await RecoveryNativeDigest(user.Id, credential.CredentialId, ct).ConfigureAwait(false) != prepared.NativeBeforeDigest)
            throw new InvalidOperationException("The exact current verified native recovery source is required.");
        // UserManager persists the native counter using this SAME DbContext and transaction. No session or StepUp proof is issued.
        var stored = await users.AddOrUpdatePasskeyAsync(user, prepared.Verified).ConfigureAwait(false);
        if (!stored.Succeeded) throw new InvalidOperationException("Native recovery counter update failed.");
        return await RecoveryNativeDigest(user.Id, credential.CredentialId, ct).ConfigureAwait(false) ??
            throw new InvalidOperationException("Native recovery credential disappeared.");
    }
    internal Task<string?> RecoveryNativeDigest(string userId, byte[] credentialId, CancellationToken ct) => database.Database.SqlQuery<string>(
        $"SELECT upper(encode(sha256(convert_to(\"Data\"::text,'UTF8')),'hex')) AS \"Value\" FROM identity.\"AspNetUserPasskeys\" WHERE \"UserId\"={userId} AND \"CredentialId\"={credentialId}")
        .SingleOrDefaultAsync(ct);
    internal static bool RecoveryBinding(PasskeyCeremonyRow ceremony, RecoveryCaseRow recovery, RecoveryPasswordPasskeyProfile profile) =>
        recovery.ProofKind == "PASSKEY" && ceremony.Kind == "RECOVERY" && ceremony.CeremonyId == recovery.PasskeyCeremonyId &&
        ceremony.RequestId == recovery.RequestId && ceremony.AccountId == recovery.AccountId && ceremony.SessionId is null && ceremony.StepUpRequestId is null &&
        ceremony.BrowserDigest == recovery.OriginalBrowserDigest && ceremony.Origin == recovery.Origin && ceremony.SecurityStampDigest == recovery.PriorSecurityStampDigest &&
        ceremony.PolicyVersion == profile.NativePolicyVersion && ceremony.AssurancePolicyVersion == profile.Version &&
        ceremony.BoundOperationId == RecoveryCaseApplication.OperationId(recovery) && ceremony.BoundCorrelationId == recovery.RequestId.ToString("N") &&
        ceremony.BoundOperation == recovery.Goal && ceremony.BoundContext == "ACCOUNT_RECOVERY" && ceremony.BoundResourceId == recovery.CaseId.ToString("N") &&
        ceremony.BoundExpectedVersion == 1 && ceremony.BoundContextDigest == recovery.ContextDigest && ceremony.ExpectedVersion == recovery.ExpectedPasskeyVersion &&
        ceremony.CredentialReference == recovery.PasskeyCredentialReference && ceremony.ResultSessionId is null && ceremony.PreviousSessionId is null &&
        ceremony.ProcessingReference == recovery.ProcessingReference && ceremony.RetentionReference == recovery.RetentionReference &&
        ceremony.CreatedAt >= recovery.CreatedAt && ceremony.ExpiresAt <= recovery.ExpiresAt && ceremony.RecoveryExpiresAt == recovery.RecoveryExpiresAt;
    internal void CompleteRecoveryCeremony(PasskeyCeremonyRow ceremony, PreparedRecoveryAssertion prepared, Guid resultId, DateTimeOffset at)
    {
        if (!ReferenceEquals(prepared.Owner, this) || ceremony.State != "PENDING" || ceremony.CeremonyId != prepared.CeremonyId)
            throw new InvalidOperationException("Only the verified original recovery ceremony may complete.");
        ceremony.RequestDigest = prepared.RequestDigest; ceremony.ProofReference = resultId; End(ceremony, "COMPLETED", at);
    }
}
