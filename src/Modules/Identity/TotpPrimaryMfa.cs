using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore.Storage;
using Tunner.Accounts;

namespace Tunner.Identity.Persistence;

internal sealed partial class AuthenticationApplication
{
    internal async Task<AuthenticationAttempt> RecoverCompletedTotpMfaAsync(AuthenticationCaller caller, Guid pendingId, Guid requestId, CancellationToken ct)
    {
        caller.Validate(); if (database.Database.CurrentTransaction is not null || database.ChangeTracker.HasChanges()) throw new InvalidOperationException("MFA recovery requires a clean owner boundary.");
        var pending = await FindTotpMfaAsync(caller, requestId, ct).ConfigureAwait(false);
        if (pending is null || pending.PendingId != pendingId || pending.PreviousSessionId != caller.PreviousSessionId || pending.State != "COMPLETED" || pending.AcceptedAuthenticatorReference is not { } factor)
            return new("VERSION_CONFLICT", null);
        return await RecoverTotpMfaAsync(caller, pending, factor, ct).ConfigureAwait(false);
    }
    internal Task<TotpPendingAuthenticationRow?> FindTotpMfaAsync(AuthenticationCaller caller, Guid request, CancellationToken ct) =>
        database.Set<TotpPendingAuthenticationRow>().AsNoTracking().SingleOrDefaultAsync(x => x.BrowserDigest == caller.BrowserDigest && x.RequestId == request && x.Origin == caller.Origin, ct);
    private async Task<AuthenticationAttempt?> PrepareTotpMfaAsync(AuthenticationCaller caller, Guid request, bool remember, IdentityUserAdapter user,
        string identifier, string method, string primaryAssurance, DateTimeOffset verifiedAt, CancellationToken ct,
        PasskeyCredentialRow? passkey = null, string? passkeyPolicy = null, string? passkeyFacts = null)
    {
        var policy = CurrentTotpPolicy(clock.GetUtcNow()); if (policy is null) return null;
        if (policy.Origin != caller.Origin) throw new InvalidOperationException("TOTP origin is unavailable.");
        var source = mfaRequirements?.SingleOrDefault() ?? throw new InvalidOperationException("An explicit current MFA requirement source is required.");
        if (caller.PreviousSessionId is { } previous) await LockSessionAsync(previous, ct).ConfigureAwait(false);
        var account = await database.Accounts.FromSqlInterpolated($"SELECT * FROM accounts.accounts WHERE account_id={user.AccountId} FOR NO KEY UPDATE").SingleAsync(ct).ConfigureAwait(false);
        await database.Entry(account).ReloadAsync(ct).ConfigureAwait(false);
        // Primary proof was checked above. Freeze its exact persisted source for final-MFA freshness checks.
        _ = await database.Users.FromSqlInterpolated($"SELECT * FROM identity.\"AspNetUsers\" WHERE \"AccountId\"={user.AccountId} FOR NO KEY UPDATE").SingleAsync(ct).ConfigureAwait(false);
        var now = clock.GetUtcNow(); var requirement = await source.RequireAsync(new(user.AccountId, account.Version, method, primaryAssurance, now), ct).ConfigureAwait(false);
        ValidateRequirement(requirement, user.AccountId, account.Version, method, primaryAssurance, now);
        if (!requirement.Required) return null;
        if (!policy.MfaAssurances.ContainsKey(method)) return new("STEP_UP_REQUIRED", null);
        var primaryDigest = method == "pwd" ? AuthenticationIdentity.Digest(user.PasswordHash ?? string.Empty) : AuthenticationIdentity.Digest(passkey!.CredentialDigest + "|" + passkey.Version.ToString(System.Globalization.CultureInfo.InvariantCulture) + "|" + passkeyFacts);
        var prior = await database.Set<TotpPendingAuthenticationRow>().SingleOrDefaultAsync(x => x.BrowserDigest == caller.BrowserDigest && x.RequestId == request, ct).ConfigureAwait(false);
        if (prior is not null)
        {
            await database.Entry(prior).ReloadAsync(ct).ConfigureAwait(false);
            return prior.AccountId == user.AccountId && prior.Origin == caller.Origin && prior.RememberMe == remember && prior.PreviousSessionId == caller.PreviousSessionId &&
                prior.PrimaryMethod == method && prior.PrimarySourceDigest == primaryDigest && prior.SecurityStampDigest == AuthenticationIdentity.Digest(user.SecurityStamp!) && prior.State == "PENDING" && prior.ExpiresAt > now
                ? new(null, null, prior) : new("VERSION_CONFLICT", null);
        }
        var auth = policies.RequireEffective(now); auth.Validate();
        var expires = verifiedAt + requirement.PrimaryEvidenceLifetime; if (requirement.EffectiveInterval.Until is { } until && until < expires) expires = until;
        if (now < verifiedAt || expires <= now) return new("AUTHENTICATION_FAILED", null);
        var pending = new TotpPendingAuthenticationRow { PendingId = Guid.NewGuid(), RequestId = request, BrowserDigest = caller.BrowserDigest, AccountId = user.AccountId,
            Origin = caller.Origin, RememberMe = remember, PreviousSessionId = caller.PreviousSessionId, IdentifierDigest = identifier, PrimaryMethod = method,
            PrimaryAssurance = primaryAssurance, PrimarySourceDigest = primaryDigest, SecurityStampDigest = AuthenticationIdentity.Digest(user.SecurityStamp!),
            PasskeyCredentialReference = passkey?.CredentialReference, PasskeyCredentialVersion = passkey?.Version, PasskeyPolicyVersion = passkeyPolicy, PasskeyAssuranceKey = passkeyFacts,
            AuthenticationPolicyVersion = auth.Version, TotpPolicyVersion = policy.Version, RequirementPolicyVersion = requirement.PolicyVersion, RequirementEvidenceReference = requirement.EvidenceReference,
            PrimaryVerifiedAt = verifiedAt, ExpiresAt = expires, RecoveryExpiresAt = expires + policy.ResultRecoveryLifetime };
        database.Add(pending); return new(null, null, pending);
    }
    internal async Task<AuthenticationAttempt> CompleteTotpMfaAsync(AuthenticationCaller caller, Guid pendingId, TotpCodeCommand command,
        ITotpSeedStore seeds, TotpNativeVerifier verifier, CancellationToken ct)
    {
        caller.Validate(); if (database.Database.CurrentTransaction is not null || database.ChangeTracker.HasChanges()) throw new InvalidOperationException("MFA requires a clean Identity owner boundary.");
        if (command.RequestId == Guid.Empty || command.AuthenticatorReference == Guid.Empty || TotpNativeVerifier.CanonicalCode(command.Code) is null) return new("VALIDATION_ERROR", null);
        // The exact protected browser attempt recovers its original result before any new code acceptance.
        var snapshot = await FindTotpMfaAsync(caller, command.RequestId, ct).ConfigureAwait(false);
        if (snapshot is null || snapshot.PendingId != pendingId || snapshot.PreviousSessionId != caller.PreviousSessionId) return new("VERSION_CONFLICT", null);
        if (snapshot.State == "COMPLETED") return await RecoverTotpMfaAsync(caller, snapshot, command.AuthenticatorReference, ct).ConfigureAwait(false);
        var selected = await database.Set<TotpAuthenticatorRow>().AsNoTracking().SingleOrDefaultAsync(x => x.AuthenticatorReference == command.AuthenticatorReference && x.AccountId == snapshot.AccountId && x.State == "ACTIVE", ct).ConfigureAwait(false);
        if (selected is null) return new("AUTHENTICATION_FAILED", null);
        using var seed = await seeds.ReadAsync(selected.Binding, selected.Reference, ct).ConfigureAwait(false); if (seed is null) return new("DEPENDENCY_UNAVAILABLE", null);
        await using var transaction = await database.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        await LockAsync("auth-browser:" + caller.BrowserDigest, ct).ConfigureAwait(false);
        if (caller.PreviousSessionId is { } previous) await LockSessionAsync(previous, ct).ConfigureAwait(false);
        var account = await database.Accounts.FromSqlInterpolated($"SELECT * FROM accounts.accounts WHERE account_id={snapshot.AccountId} FOR NO KEY UPDATE").SingleAsync(ct).ConfigureAwait(false); await database.Entry(account).ReloadAsync(ct).ConfigureAwait(false);
        var user = await database.Users.FromSqlInterpolated($"SELECT * FROM identity.\"AspNetUsers\" WHERE \"AccountId\"={snapshot.AccountId} FOR NO KEY UPDATE").SingleAsync(ct).ConfigureAwait(false); await database.Entry(user).ReloadAsync(ct).ConfigureAwait(false);
        _ = await database.VerifiedContacts.FromSqlInterpolated($"SELECT * FROM accounts.verified_contacts WHERE account_id={snapshot.AccountId} ORDER BY contact_id FOR SHARE").ToArrayAsync(ct).ConfigureAwait(false);
        await LockAsync("totp-owner:" + snapshot.AccountId, ct).ConfigureAwait(false);
        var pending = await database.Set<TotpPendingAuthenticationRow>().SingleAsync(x => x.PendingId == pendingId, ct).ConfigureAwait(false); await database.Entry(pending).ReloadAsync(ct).ConfigureAwait(false);
        if (pending.State == "COMPLETED")
        {
            await transaction.RollbackAsync(ct).ConfigureAwait(false); database.ChangeTracker.Clear(); return await RecoverTotpMfaAsync(caller, pending, command.AuthenticatorReference, ct).ConfigureAwait(false);
        }
        var factor = await database.Set<TotpAuthenticatorRow>().SingleAsync(x => x.AuthenticatorReference == selected.AuthenticatorReference, ct).ConfigureAwait(false); await database.Entry(factor).ReloadAsync(ct).ConfigureAwait(false);
        var now = clock.GetUtcNow(); var auth = policies.RequireEffective(now); auth.Validate(); var policy = CurrentTotpPolicy(now);
        var accessDecision = await AccessAsync(account, "SIGN_IN", now, ct).ConfigureAwait(false);
        var requirement = await RequirePendingAuthority(pending, user, account, caller, ct).ConfigureAwait(false);
        if (policy is null || accessDecision?.Allowed != true || requirement is null || factor.State != "ACTIVE" || factor.Generation != selected.Generation || factor.Version != selected.Version || factor.Reference != selected.Reference ||
            !PendingCurrent(pending, caller, user, auth, policy, now) || !policy.MfaAssurances.TryGetValue(pending.PrimaryMethod, out var assurance))
            return await FinishMfa(transaction, new("AUTHENTICATION_FAILED", null), ct).ConfigureAwait(false);
        if (!await AllowAsync("totp-mfa:N:" + caller.NetworkDigest, auth.NetworkAttemptLimit, auth, now, ct).ConfigureAwait(false) ||
            !await AllowAsync("totp-mfa:A:" + pending.AccountId, auth.IdentifierAttemptLimit, auth, now, ct).ConfigureAwait(false))
            return await FinishMfa(transaction, new("RATE_OR_ABUSE_LIMITED", null), ct).ConfigureAwait(false);
        await database.SaveChangesAsync(ct).ConfigureAwait(false); await transaction.CreateSavepointAsync("totp_mfa", ct).ConfigureAwait(false);
        var error = await verifier.AcceptAsync(factor, policy, seed.Require(), command.Code, ct).ConfigureAwait(false);
        if (error is not null) return await FinishMfa(transaction, new(error, null), ct).ConfigureAwait(false);
        // All asynchronous authority work completes before the final time/policy decision.
        requirement = await RequirePendingAuthority(pending, user, account, caller, ct).ConfigureAwait(false);
        accessDecision = await AccessAsync(account, "SIGN_IN", clock.GetUtcNow(), ct).ConfigureAwait(false); now = clock.GetUtcNow();
        var finalAuth = policies.RequireEffective(now); finalAuth.Validate(); var finalPolicy = CurrentTotpPolicy(now);
        if (requirement is null || !requirement.EffectiveInterval.Contains(now) || accessDecision?.Allowed != true || !accessDecision.EffectiveInterval.Contains(now) || finalPolicy is null ||
            !PendingCurrent(pending, caller, user, finalAuth, finalPolicy, now) || !finalPolicy.MfaAssurances.TryGetValue(pending.PrimaryMethod, out var finalAssurance) || finalAssurance != assurance)
        {
            await transaction.RollbackToSavepointAsync("totp_mfa", ct).ConfigureAwait(false); database.ChangeTracker.Clear(); return await FinishMfa(transaction, new("AUTHENTICATION_FAILED", null), ct).ConfigureAwait(false);
        }
        if (caller.PreviousSessionId is { } oldId)
        {
            var old = await database.Set<AuthenticationSessionRow>().SingleOrDefaultAsync(x => x.SessionId == oldId && x.Origin == caller.Origin, ct).ConfigureAwait(false); if (old is not null) Revoke(old, now);
        }
        var result = CreateSession(caller, pending.RequestId, pending.RememberMe, user, pending.IdentifierDigest, pending.PrimaryMethod + "+totp", assurance, finalAuth, accessDecision, now,
            pending.PasskeyCredentialReference, pending.PasskeyPolicyVersion, pending.PasskeyAssuranceKey, factor, finalPolicy.Version, pending.RequirementPolicyVersion);
        pending.State = "COMPLETED"; pending.ResultSessionId = AuthenticationIdentity.SessionId(result.Grant!.Ticket.Principal);
        pending.AcceptedAuthenticatorReference = factor.AuthenticatorReference; pending.AcceptedGeneration = factor.Generation; pending.Version++;
        return await FinishMfa(transaction, result, ct).ConfigureAwait(false);
    }
    private async Task<TotpMfaRequirementDecision?> RequirePendingAuthority(TotpPendingAuthenticationRow pending, IdentityUserAdapter user, AccountRow account, AuthenticationCaller caller, CancellationToken ct)
    {
        var now = clock.GetUtcNow(); var source = mfaRequirements?.SingleOrDefault() ?? throw new InvalidOperationException("MFA authority is unavailable.");
        var decision = await source.RequireAsync(new(pending.AccountId, account.Version, pending.PrimaryMethod, pending.PrimaryAssurance, now), ct).ConfigureAwait(false);
        ValidateRequirement(decision, pending.AccountId, account.Version, pending.PrimaryMethod, pending.PrimaryAssurance, now);
        if (!decision.Required || decision.PolicyVersion != pending.RequirementPolicyVersion || decision.EvidenceReference != pending.RequirementEvidenceReference ||
            now - pending.PrimaryVerifiedAt >= decision.PrimaryEvidenceLifetime || !user.EmailConfirmed ||
            !await database.VerifiedContacts.AnyAsync(x => x.AccountId == user.AccountId && x.State == ContactVerificationState.Verified && x.AnonymizedAtUtc == null && x.NormalizedEmail == user.NormalizedEmail, ct).ConfigureAwait(false)) return null;
        if (pending.PrimaryMethod == "pwd") return pending.PrimarySourceDigest == AuthenticationIdentity.Digest(user.PasswordHash ?? string.Empty) ? decision : null;
        var credential = await database.Set<PasskeyCredentialRow>().SingleOrDefaultAsync(x => x.CredentialReference == pending.PasskeyCredentialReference && x.AccountId == pending.AccountId, ct).ConfigureAwait(false);
        if (credential is null) return null; await database.Entry(credential).ReloadAsync(ct).ConfigureAwait(false);
        var passkey = passkeyPolicies.SingleOrDefault()?.RequireEffective(clock.GetUtcNow());
        return credential.State == "ACTIVE" && credential.Version == pending.PasskeyCredentialVersion && passkey is not null && passkey.Version == pending.PasskeyPolicyVersion && passkey.Origin == caller.Origin && pending.PasskeyAssuranceKey is not null &&
            passkey.RequireAssurance(pending.PasskeyAssuranceKey) == pending.PrimaryAssurance ? decision : null;
    }
    private static bool PendingCurrent(TotpPendingAuthenticationRow pending, AuthenticationCaller caller, IdentityUserAdapter user, AuthenticationPolicy auth, TotpPolicy policy, DateTimeOffset now) =>
        pending.State == "PENDING" && pending.BrowserDigest == caller.BrowserDigest && pending.Origin == caller.Origin && pending.PreviousSessionId == caller.PreviousSessionId &&
        pending.AccountId == user.AccountId && !string.IsNullOrWhiteSpace(user.SecurityStamp) && pending.SecurityStampDigest == AuthenticationIdentity.Digest(user.SecurityStamp) &&
        pending.AuthenticationPolicyVersion == auth.Version && pending.TotpPolicyVersion == policy.Version && now >= pending.PrimaryVerifiedAt && now < pending.ExpiresAt &&
        (pending.PrimaryMethod != "pwd" || pending.PrimaryAssurance == auth.PasswordAssurance);
    private static void ValidateRequirement(TotpMfaRequirementDecision decision, string account, long version, string method, string assurance, DateTimeOffset now)
    {
        if (decision.AccountId != account || decision.AccountVersion != version || decision.PrimaryMethod != method || decision.PrimaryAssurance != assurance ||
            string.IsNullOrWhiteSpace(decision.PolicyVersion) || string.IsNullOrWhiteSpace(decision.EvidenceReference) || !decision.EffectiveInterval.Contains(now) || decision.PrimaryEvidenceLifetime <= TimeSpan.Zero)
            throw new InvalidOperationException("Current MFA requirement authority is unavailable.");
    }
    private async Task<AuthenticationAttempt> RecoverTotpMfaAsync(AuthenticationCaller caller, TotpPendingAuthenticationRow pending, Guid factor, CancellationToken ct)
    {
        await using var transaction = await database.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        if (pending.ResultSessionId is not { } id || pending.AcceptedAuthenticatorReference != factor || pending.RecoveryExpiresAt <= clock.GetUtcNow()) return await FinishMfa(transaction, new("VERSION_CONFLICT", null), ct).ConfigureAwait(false);
        await LockSessionAsync(id, ct).ConfigureAwait(false);
        var session = await database.Set<AuthenticationSessionRow>().SingleOrDefaultAsync(x => x.SessionId == id && x.BrowserDigest == caller.BrowserDigest && x.IdempotencyKey == pending.RequestId && x.AccountId == pending.AccountId, ct).ConfigureAwait(false);
        var ticket = session is null ? null : await ValidateAsync(session, caller.Origin, clock.GetUtcNow(), ct).ConfigureAwait(false);
        return await FinishMfa(transaction, ticket is null ? new("AUTHENTICATION_FAILED", null) : new(null, new(keyProtection.Unprotect(session!.ProtectedKey), ticket)), ct).ConfigureAwait(false);
    }
    private async Task<AuthenticationAttempt> FinishMfa(IDbContextTransaction transaction, AuthenticationAttempt result, CancellationToken ct)
    { await database.SaveChangesAsync(ct).ConfigureAwait(false); await transaction.CommitAsync(ct).ConfigureAwait(false); return result; }
}
