using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Tunner.Accounts;
using Tunner.Database.Outbox;

namespace Tunner.Identity.Persistence;

internal sealed class AuthenticationPasswordWork
{
    private readonly ConcurrentDictionary<int, Lazy<string>> dummies = new();
    internal static PasswordHasher<IdentityUserAdapter> Hasher(AuthenticationPolicy policy) => new(Options.Create(new PasswordHasherOptions
        { CompatibilityMode = PasswordHasherCompatibilityMode.IdentityV3, IterationCount = policy.PasswordHashIterations }));
    internal string Dummy(AuthenticationPolicy policy) => dummies.GetOrAdd(policy.PasswordHashIterations,
        _ => new Lazy<string>(() => Hasher(policy).HashPassword(new IdentityUserAdapter(), Convert.ToHexString(RandomNumberGenerator.GetBytes(32))))).Value;
    internal PasswordVerificationResult Verify(AuthenticationPolicy policy, IdentityUserAdapter? user, string password)
    {
        try { return Hasher(policy).VerifyHashedPassword(user ?? new IdentityUserAdapter(), user?.PasswordHash ?? Dummy(policy), password); }
        catch (FormatException) { return PasswordVerificationResult.Failed; }
    }

}

/// <summary>Identity owner; transactions contain state plus outbox, never HTTP or Product sessions.</summary>
internal sealed partial class AuthenticationApplication(IdentityPersistenceDbContext database, IAuthenticationPolicySource policies,
    IAccountAuthenticationAccessSource access, AuthenticationPartitionKey partitions, IDataProtectionProvider protection,
    AuthenticationPasswordWork passwordWork, TimeProvider clock, IEnumerable<IPasskeyPolicySource> passkeyPolicies,
    IEnumerable<ITotpPolicySource>? totpPolicies = null, IEnumerable<ITotpMfaRequirementSource>? mfaRequirements = null) : IAuthenticationSessionMaintenance
{
    private readonly IDataProtector ticketProtection = protection.CreateProtector("Tunner.User.Session.Ticket.v1");
    private readonly IDataProtector keyProtection = protection.CreateProtector("Tunner.User.Session.RetryKey.v1");
    private static readonly UpperInvariantLookupNormalizer Normalizer = new();

    internal async Task<AuthenticationAttempt> SignInAsync(AuthenticationCaller caller, PasswordSignInCommand command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(caller); ArgumentNullException.ThrowIfNull(command); caller.Validate();
        var now = clock.GetUtcNow(); var policy = policies.RequireEffective(now); policy.Validate();
        if (command.IdempotencyKey == Guid.Empty || !IdentityEmail.IsValid(command.Email) || string.IsNullOrEmpty(command.Password) ||
            command.Password.EnumerateRunes().Count() > policy.PasswordMaximumLength)
            return new("VALIDATION_ERROR", null);
        var normalized = Normalizer.NormalizeEmail(command.Email.Trim())!;
        var identifier = partitions.Identifier(normalized);
        var hasher = AuthenticationPasswordWork.Hasher(policy); _ = passwordWork.Dummy(policy);
        await using var transaction = await database.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        if (!await AllowAsync("N:" + caller.NetworkDigest, policy.NetworkAttemptLimit, policy, now, ct).ConfigureAwait(false) ||
            !await AllowAsync("I:" + identifier, policy.IdentifierAttemptLimit, policy, now, ct).ConfigureAwait(false))
        {
            await database.SaveChangesAsync(ct).ConfigureAwait(false); await transaction.CommitAsync(ct).ConfigureAwait(false);
            return new("RATE_OR_ABUSE_LIMITED", null);
        }
        await LockAsync("auth-browser:" + caller.BrowserDigest, ct).ConfigureAwait(false);
        var prior = await database.Set<AuthenticationSessionRow>().SingleOrDefaultAsync(x => x.BrowserDigest == caller.BrowserDigest && x.IdempotencyKey == command.IdempotencyKey, ct).ConfigureAwait(false);
        var user = await (from contact in database.VerifiedContacts join candidate in database.Users on contact.AccountId equals candidate.AccountId
            where contact.NormalizedEmail == normalized && contact.AnonymizedAtUtc == null && contact.State == ContactVerificationState.Verified &&
                candidate.EmailConfirmed && candidate.NormalizedEmail == normalized select candidate).SingleOrDefaultAsync(ct).ConfigureAwait(false);
        if (user is not null && CurrentTotpPolicy(clock.GetUtcNow()) is not null)
        {
            if (caller.PreviousSessionId is { } previousSession) await LockSessionAsync(previousSession, ct).ConfigureAwait(false);
            _ = await database.Accounts.FromSqlInterpolated($"SELECT * FROM accounts.accounts WHERE account_id={user.AccountId} FOR NO KEY UPDATE").SingleAsync(ct).ConfigureAwait(false);
            _ = await database.Users.FromSqlInterpolated($"SELECT * FROM identity.\"AspNetUsers\" WHERE \"AccountId\"={user.AccountId} FOR NO KEY UPDATE").SingleAsync(ct).ConfigureAwait(false);
            await database.Entry(user).ReloadAsync(ct).ConfigureAwait(false);
            _ = await database.VerifiedContacts.FromSqlInterpolated($"SELECT * FROM accounts.verified_contacts WHERE account_id={user.AccountId} ORDER BY contact_id FOR SHARE").ToArrayAsync(ct).ConfigureAwait(false);
        }
        var verification = passwordWork.Verify(policy, user, command.Password);
        if (user is null || verification == PasswordVerificationResult.Failed || string.IsNullOrWhiteSpace(user.SecurityStamp))
            return await RejectAsync(transaction, now, ct).ConfigureAwait(false);
        var account = await database.Accounts.SingleAsync(x => x.AccountId == user.AccountId, ct).ConfigureAwait(false);
        var decision = await AccessAsync(account, "SIGN_IN", now, ct).ConfigureAwait(false);
        if (decision is null || !decision.Allowed) return await RejectAsync(transaction, now, ct).ConfigureAwait(false);
        if (prior is not null)
        {
            if (prior.IdentifierDigest != identifier || prior.AccountId != user.AccountId || prior.RememberMe != command.RememberMe)
            {
                await database.SaveChangesAsync(ct).ConfigureAwait(false); await transaction.CommitAsync(ct).ConfigureAwait(false);
                return new("VERSION_CONFLICT", null);
            }
            await LockSessionAsync(prior.SessionId, ct).ConfigureAwait(false);
            await database.Entry(prior).ReloadAsync(ct).ConfigureAwait(false);
            var valid = await ValidateAsync(prior, caller.Origin, now, ct).ConfigureAwait(false);
            await database.SaveChangesAsync(ct).ConfigureAwait(false); await transaction.CommitAsync(ct).ConfigureAwait(false);
            return valid is null ? new("AUTHENTICATION_FAILED", null) : new(null, new AuthenticationGrant(keyProtection.Unprotect(prior.ProtectedKey), valid));
        }
        if (verification == PasswordVerificationResult.SuccessRehashNeeded)
        {
            user.PasswordHash = hasher.HashPassword(user, command.Password); user.ConcurrencyStamp = Guid.NewGuid().ToString("N");
        }
        var mfa = await PrepareTotpMfaAsync(caller, command.IdempotencyKey, command.RememberMe, user, identifier, "pwd", policy.PasswordAssurance, now, ct).ConfigureAwait(false);
        if (mfa is not null)
        {
            await database.SaveChangesAsync(ct).ConfigureAwait(false); await transaction.CommitAsync(ct).ConfigureAwait(false); return mfa;
        }
        if (caller.PreviousSessionId is { } previous)
        {
            await LockSessionAsync(previous, ct).ConfigureAwait(false);
            var old = await database.Set<AuthenticationSessionRow>().SingleOrDefaultAsync(x => x.SessionId == previous && x.Origin == caller.Origin && x.Scheme == AuthenticationIdentity.Scheme, ct).ConfigureAwait(false);
            if (old is not null) Revoke(old, now);
        }
        var result = CreateSession(caller, command.IdempotencyKey, command.RememberMe, user, identifier, "pwd", policy.PasswordAssurance, policy, decision, now);
        await database.SaveChangesAsync(ct).ConfigureAwait(false); await transaction.CommitAsync(ct).ConfigureAwait(false);
        return result;
    }

    private AuthenticationAttempt CreateSession(AuthenticationCaller caller, Guid idempotencyKey, bool rememberMe, IdentityUserAdapter user,
        string identifier, string method, string assurance, AuthenticationPolicy policy, AccountAuthenticationAccessDecision decision, DateTimeOffset now,
        Guid? credentialReference = null, string? passkeyPolicyVersion = null, string? passkeyAssuranceKey = null,
        TotpAuthenticatorRow? totp = null, string? totpPolicyVersion = null, string? requirementPolicyVersion = null)
    {
        var sessionId = Guid.NewGuid(); var key = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var absolute = rememberMe ? policy.RememberAbsoluteLifetime : policy.AbsoluteLifetime;
        var idle = rememberMe ? policy.RememberIdleLifetime : policy.IdleLifetime;
        var principal = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim(ClaimTypes.NameIdentifier, user.AccountId), new Claim(AuthenticationIdentity.SessionClaim, sessionId.ToString("N")),
            new Claim(AuthenticationIdentity.OriginClaim, caller.Origin), new Claim("amr", method), new Claim("acr", assurance)
        ], AuthenticationIdentity.Scheme));
        var ticket = new AuthenticationTicket(principal, new AuthenticationProperties { IssuedUtc = now, ExpiresUtc = now + absolute,
            IsPersistent = rememberMe, AllowRefresh = false }, AuthenticationIdentity.Scheme);
        var row = new AuthenticationSessionRow
        {
            SessionId = sessionId, KeyDigest = AuthenticationIdentity.Digest(key), ProtectedKey = keyProtection.Protect(key),
            ProtectedTicket = ticketProtection.Protect(Convert.ToBase64String(TicketSerializer.Default.Serialize(ticket))),
            BrowserDigest = caller.BrowserDigest, IdempotencyKey = idempotencyKey, IdentifierDigest = identifier, RememberMe = rememberMe,
            AccountId = user.AccountId, SecurityStampDigest = AuthenticationIdentity.Digest(user.SecurityStamp!), Scheme = AuthenticationIdentity.Scheme,
            Origin = caller.Origin, AuthenticationMethods = method, Assurance = assurance,
            PasskeyCredentialReference = credentialReference, PasskeyPolicyVersion = passkeyPolicyVersion, PasskeyAssuranceKey = passkeyAssuranceKey, AuthenticatedAt = ticket.Properties.IssuedUtc!.Value, LastSeenAt = ticket.Properties.IssuedUtc.Value,
            TotpAuthenticatorReference = totp?.AuthenticatorReference, TotpGeneration = totp?.Generation, TotpAuthenticatorVersion = totp?.Version, TotpPolicyVersion = totpPolicyVersion, MfaRequirementPolicyVersion = requirementPolicyVersion,
            ExpiresAt = ticket.Properties.ExpiresUtc!.Value, IdleTicks = idle.Ticks, PolicyVersion = policy.Version, AccessPolicyVersion = decision.PolicyVersion,
            AccessEvidenceReference = decision.EvidenceReference, ProcessingReference = policy.ProcessingReference, RetentionReference = policy.RetentionReference,
            OperationId = "OP-" + Guid.NewGuid().ToString("N"), CorrelationId = Guid.NewGuid().ToString("N"), Version = 1
        };
        database.Add(row); Event(row, "TunnerSessionAuthenticated", now);
        return new(null, new AuthenticationGrant(key, ticket));
    }

    // Caller owns the same Identity transaction as framework credential counter update and ceremony consumption.
    internal async Task<AuthenticationAttempt> IssuePasskeySessionAsync(AuthenticationCaller caller, Guid requestId, bool rememberMe,
        IdentityUserAdapter user, PasskeyCredentialRow credential, PasskeyPolicy passkeyPolicy, string assuranceKey, CancellationToken ct)
    {
        if (caller.PreviousSessionId is { } priorSession) await LockSessionAsync(priorSession, ct).ConfigureAwait(false);
        var account = await database.Accounts.FromSqlInterpolated($"SELECT * FROM accounts.accounts WHERE account_id={user.AccountId} FOR NO KEY UPDATE").SingleAsync(ct).ConfigureAwait(false);
        await database.Entry(account).ReloadAsync(ct).ConfigureAwait(false);
        await database.Entry(user).ReloadAsync(ct).ConfigureAwait(false);
        var now = clock.GetUtcNow(); var policy = policies.RequireEffective(now); policy.Validate();
        var decision = await AccessAsync(account, "SIGN_IN", now, ct).ConfigureAwait(false);
        if (decision?.Allowed != true || !user.EmailConfirmed || string.IsNullOrWhiteSpace(user.SecurityStamp) ||
            !await database.VerifiedContacts.AnyAsync(x => x.AccountId == user.AccountId && x.State == ContactVerificationState.Verified &&
                x.AnonymizedAtUtc == null && x.NormalizedEmail == user.NormalizedEmail, ct).ConfigureAwait(false)) return new("AUTHENTICATION_FAILED", null);
        var mfa = await PrepareTotpMfaAsync(caller, requestId, rememberMe, user, credential.CredentialDigest, "passkey", passkeyPolicy.RequireAssurance(assuranceKey), now, ct,
            credential, passkeyPolicy.Version, assuranceKey).ConfigureAwait(false);
        if (mfa is not null) return mfa;
        if (caller.PreviousSessionId is { } previous)
        {
            await LockSessionAsync(previous, ct).ConfigureAwait(false);
            var old = await database.Set<AuthenticationSessionRow>().SingleOrDefaultAsync(x => x.SessionId == previous && x.Origin == caller.Origin && x.Scheme == AuthenticationIdentity.Scheme, ct).ConfigureAwait(false);
            if (old is not null) Revoke(old, now);
        }
        return CreateSession(caller, requestId, rememberMe, user, credential.CredentialDigest, "passkey", passkeyPolicy.RequireAssurance(assuranceKey),
            policy, decision, now, credential.CredentialReference, passkeyPolicy.Version, assuranceKey);
    }
    internal async Task<bool> MethodIsCurrentAsync(AuthenticationSessionRow row, AuthenticationPolicy policy, DateTimeOffset now, CancellationToken ct)
    {
        if (row.TotpAuthenticatorReference is not null) return await TotpSessionCurrentAsync(row, policy, now, ct).ConfigureAwait(false);
        if (row.AuthenticationMethods == "pwd") return row.PasskeyCredentialReference is null && row.Assurance == policy.PasswordAssurance;
        if (row.AuthenticationMethods != "passkey" || row.PasskeyCredentialReference is not { } credential || row.PasskeyAssuranceKey is null) return false;
        var source = passkeyPolicies.SingleOrDefault(); if (source is null) return false;
        var current = source.RequireEffective(now);
        var metadata = await database.Set<PasskeyCredentialRow>().SingleOrDefaultAsync(x => x.CredentialReference == credential && x.AccountId == row.AccountId, ct).ConfigureAwait(false);
        return current.Origin == row.Origin && current.RequireAssurance(row.PasskeyAssuranceKey) == row.Assurance &&
            metadata is not null && (metadata.State == "ACTIVE" || metadata.State == "REVOKED" && metadata.RevocationSessionEffect == nameof(PasskeyRevocationSessionEffect.PreserveExistingSessions));
    }
    internal async Task<AuthenticationSessionTerminationReceipt?> EndCredentialActorSessionAsync(StepUpCaller caller, StepUpOperation operation,
        PasskeyCredentialRow credential, PasskeyPolicy policy, CancellationToken ct)
    {
        var row = await database.Set<AuthenticationSessionRow>().SingleAsync(x => x.SessionId == caller.SessionId, ct).ConfigureAwait(false);
        if (row.PasskeyCredentialReference != credential.CredentialReference) return null;
        if (operation.Scope != PasskeyApplication.RevocationScope || operation.ResourceId != credential.CredentialReference.ToString("N") ||
            operation.ExpectedVersion + 1 != credential.Version || row.State != "ACTIVE" || row.AccountId != caller.AccountId || row.Origin != caller.Origin || row.Scheme != AuthenticationIdentity.Scheme ||
            policy.RevocationSessionEffect != PasskeyRevocationSessionEffect.RevokeCredentialSessions || credential.State != "REVOKED")
            throw new InvalidOperationException("An exact current credential-session transition is required.");
        // Persist the independently validated touch first. The immutable session trigger enforces one version per write.
        await database.SaveChangesAsync(ct).ConfigureAwait(false); var prior = row.Version;
        row.TerminationCause = "PASSKEY_REVOKED"; row.TerminationPolicyVersion = policy.Version; row.TerminationCredentialVersion = credential.Version;
        End(row, "REVOKED", "TunnerSessionRevoked", clock.GetUtcNow(), operation.OperationId, operation.CorrelationId);
        await database.SaveChangesAsync(ct).ConfigureAwait(false);
        return new(row.SessionId, row.AccountId, row.Origin, operation.OperationId, operation.CorrelationId, operation.ContextDigest,
            credential.CredentialReference, credential.Version, policy.Version, prior, row.Version);
    }
    internal async Task<AuthenticationTerminalVerification?> VerifyTerminationAsync(StepUpCaller caller, StepUpOperation operation, AuthenticationSessionTerminationReceipt receipt,
        long validatedSessionVersion, AuthenticationPolicy policy, DateTimeOffset now, CancellationToken ct)
    {
        if (receipt.AuthenticatorType == "TOTP") return await VerifyTotpTerminationAsync(caller, operation, receipt, validatedSessionVersion, policy, now, ct).ConfigureAwait(false);
        var row = await database.Set<AuthenticationSessionRow>().AsNoTracking().SingleOrDefaultAsync(x => x.SessionId == caller.SessionId, ct).ConfigureAwait(false);
        if (operation.Scope != PasskeyApplication.RevocationScope || operation.ResourceId != receipt.CredentialReference.ToString("N") ||
            operation.ExpectedVersion + 1 != receipt.CredentialVersion || receipt.PriorSessionVersion != validatedSessionVersion ||
            row is null || receipt.SessionId != caller.SessionId || receipt.AccountId != caller.AccountId || receipt.Origin != caller.Origin ||
            receipt.OperationId != operation.OperationId || receipt.CorrelationId != operation.CorrelationId || receipt.ContextDigest != operation.ContextDigest ||
            receipt.EndedSessionVersion != receipt.PriorSessionVersion + 1 || row.Version != receipt.EndedSessionVersion || row.State != "REVOKED" ||
            row.AccountId != caller.AccountId || row.Origin != caller.Origin || row.Scheme != AuthenticationIdentity.Scheme || row.ProtectedKey.Length != 0 || row.ProtectedTicket.Length != 0 ||
            row.PasskeyCredentialReference != receipt.CredentialReference || row.TerminationCause != "PASSKEY_REVOKED" || row.OperationId != operation.OperationId ||
            row.CorrelationId != operation.CorrelationId || row.TerminationPolicyVersion != receipt.PasskeyPolicyVersion || row.TerminationCredentialVersion != receipt.CredentialVersion ||
            row.RevokedAt is not { } ended || ended < row.AuthenticatedAt || ended > now || Expired(row, policy, now)) return null;
        var credential = await database.Set<PasskeyCredentialRow>().AsNoTracking().SingleOrDefaultAsync(x => x.CredentialReference == receipt.CredentialReference, ct).ConfigureAwait(false);
        var passkey = passkeyPolicies.SingleOrDefault()?.RequireEffective(now);
        if (credential is null || credential.AccountId != caller.AccountId || credential.State != "REVOKED" || credential.Version != receipt.CredentialVersion ||
            credential.RevocationSessionEffect != nameof(PasskeyRevocationSessionEffect.RevokeCredentialSessions) || credential.RevocationPolicyVersion != receipt.PasskeyPolicyVersion ||
            credential.RevocationOperationId != operation.OperationId || credential.RevocationCorrelationId != operation.CorrelationId || passkey is null ||
            passkey.Version != receipt.PasskeyPolicyVersion || passkey.RevocationSessionEffect != PasskeyRevocationSessionEffect.RevokeCredentialSessions ||
            row.PasskeyAssuranceKey is null || passkey.RequireAssurance(row.PasskeyAssuranceKey) != row.Assurance) return null;
        var account = await database.Accounts.SingleAsync(x => x.AccountId == caller.AccountId, ct).ConfigureAwait(false);
        await database.Entry(account).ReloadAsync(ct).ConfigureAwait(false);
        var user = await database.Users.SingleAsync(x => x.AccountId == caller.AccountId, ct).ConfigureAwait(false); await database.Entry(user).ReloadAsync(ct).ConfigureAwait(false);
        if (!user.EmailConfirmed || string.IsNullOrWhiteSpace(user.SecurityStamp) || AuthenticationIdentity.Digest(user.SecurityStamp) != row.SecurityStampDigest ||
            !await database.VerifiedContacts.AnyAsync(x => x.AccountId == caller.AccountId && x.State == ContactVerificationState.Verified && x.AnonymizedAtUtc == null && x.NormalizedEmail == user.NormalizedEmail, ct).ConfigureAwait(false)) return null;
        var decision = await AccessAsync(account, operation.Scope.Operation, now, ct).ConfigureAwait(false);
        var decisionAt = clock.GetUtcNow();
        return decision?.Allowed == true && decision.EffectiveInterval.Contains(decisionAt) && passkeyPolicies.Single().RequireEffective(decisionAt).Version == receipt.PasskeyPolicyVersion
            ? new AuthenticationTerminalVerification(decision.EffectiveInterval) : null;
    }
    internal async Task<AuthenticatorProofSource?> ReadProofCredentialAsync(StepUpProofRow proof, CancellationToken ct, bool refresh = false)
    {
        if (proof.TotpAuthenticatorReference is { } factorId)
        {
            var factor = await database.Set<TotpAuthenticatorRow>().SingleOrDefaultAsync(x => x.AuthenticatorReference == factorId && x.AccountId == proof.AccountId, ct).ConfigureAwait(false);
            if (refresh && factor is not null) { await database.Entry(factor).ReloadAsync(ct).ConfigureAwait(false); if (database.Entry(factor).State == EntityState.Detached) return null; }
            return factor is null ? null : new(null, factor);
        }
        if (proof.PasskeyCredentialReference is not { } id) return null;
        var credential = await database.Set<PasskeyCredentialRow>().SingleOrDefaultAsync(x => x.CredentialReference == id && x.AccountId == proof.AccountId, ct).ConfigureAwait(false);
        // Issuance and execution use separate transactions on one scoped context. Refresh at the pre-action
        // boundary; after the owner callback retain its tracked, possibly unflushed causal revocation.
        if (refresh && credential is not null)
        {
            await database.Entry(credential).ReloadAsync(ct).ConfigureAwait(false);
            if (database.Entry(credential).State == EntityState.Detached) return null;
        }
        return credential is null ? null : new(credential, null);
    }
    internal bool ProofMethodCurrentAt(StepUpProofRow proof, PasskeyCredentialRow? credential, StepUpOperation operation, DateTimeOffset decisionAt, bool appliedOwnRevocation) =>
        ProofMethodCurrentAt(proof, credential is null ? null : new AuthenticatorProofSource(credential, null), operation, decisionAt, appliedOwnRevocation);
    internal bool ProofMethodCurrentAt(StepUpProofRow proof, AuthenticatorProofSource? source, StepUpOperation operation, DateTimeOffset decisionAt, bool appliedOwnRevocation)
    {
        if (proof.AuthenticationMethod == "totp") return TotpProofCurrentAt(proof, source?.Totp, operation, decisionAt, appliedOwnRevocation);
        if (proof.TotpAuthenticatorReference is not null || proof.TotpGeneration is not null || proof.TotpAuthenticatorVersion is not null || proof.TotpPolicyVersion is not null) return false;
        if (proof.AuthenticationMethod == "pwd") return proof.PasskeyCredentialReference is null && proof.PasskeyPolicyVersion is null && proof.PasskeyAssuranceKey is null;
        var credential = source?.Passkey;
        var current = passkeyPolicies.SingleOrDefault()?.RequireEffective(decisionAt);
        if (current is null || current.Version != proof.PasskeyPolicyVersion || current.Origin != proof.Origin || proof.PasskeyAssuranceKey is null ||
            proof.AuthenticationMethod != "passkey:" + proof.PasskeyAssuranceKey || credential is null || credential.AccountId != proof.AccountId || credential.CredentialReference != proof.PasskeyCredentialReference) return false;
        if (credential.State == "ACTIVE") return true;
        return appliedOwnRevocation && operation.Scope == PasskeyApplication.RevocationScope && operation.ResourceId == credential.CredentialReference.ToString("N") &&
            operation.ExpectedVersion + 1 == credential.Version && credential.State == "REVOKED" && credential.RevocationOperationId == operation.OperationId &&
            credential.RevocationCorrelationId == operation.CorrelationId && credential.RevocationPolicyVersion == current.Version;
    }
    internal async Task<AuthenticationOperationVerification?> VerifyContinuedOperationAsync(StepUpCaller caller, StepUpOperation operation,
        AuthenticationSessionRow session, CancellationToken ct)
    {
        if (session.State != "ACTIVE" || session.AccountId != caller.AccountId || session.SessionId != caller.SessionId || session.Origin != caller.Origin || session.Scheme != AuthenticationIdentity.Scheme) return null;
        var account = await database.Accounts.SingleAsync(x => x.AccountId == caller.AccountId, ct).ConfigureAwait(false); await database.Entry(account).ReloadAsync(ct).ConfigureAwait(false);
        var user = await database.Users.SingleAsync(x => x.AccountId == caller.AccountId, ct).ConfigureAwait(false); await database.Entry(user).ReloadAsync(ct).ConfigureAwait(false);
        if (!user.EmailConfirmed || string.IsNullOrWhiteSpace(user.SecurityStamp) || AuthenticationIdentity.Digest(user.SecurityStamp) != session.SecurityStampDigest ||
            !await database.VerifiedContacts.AnyAsync(x => x.AccountId == caller.AccountId && x.State == ContactVerificationState.Verified && x.AnonymizedAtUtc == null && x.NormalizedEmail == user.NormalizedEmail, ct).ConfigureAwait(false)) return null;
        var use = await AccessAsync(account, "SESSION_USE", clock.GetUtcNow(), ct).ConfigureAwait(false);
        var current = await AccessAsync(account, operation.Scope.Operation, clock.GetUtcNow(), ct).ConfigureAwait(false);
        return use?.Allowed == true && current?.Allowed == true ? new(use.EffectiveInterval, current.EffectiveInterval) : null;
    }
    internal bool MethodFactsCurrentAt(AuthenticationSessionRow row, AuthenticationPolicy policy, DateTimeOffset decisionAt)
    {
        if (row.TotpAuthenticatorReference is not null) return TotpSessionFactsCurrentAt(row, policy, decisionAt);
        if (row.AuthenticationMethods == "pwd") return row.PasskeyCredentialReference is null && row.Assurance == policy.PasswordAssurance;
        var current = passkeyPolicies.SingleOrDefault()?.RequireEffective(decisionAt);
        return row.AuthenticationMethods == "passkey" && current is not null && current.Origin == row.Origin && row.PasskeyAssuranceKey is not null && current.RequireAssurance(row.PasskeyAssuranceKey) == row.Assurance;
    }
    internal bool PasskeyOutcomeCurrentAt(UserStepUpOperationApplyOutcome outcome, StepUpCaller caller, StepUpOperation operation, DateTimeOffset decisionAt)
    {
        if (outcome.TotpValidity is not null) return TotpOutcomeCurrentAt(outcome, caller, operation, decisionAt);
        if (outcome.PasskeyValidity is not { } validity) return outcome.Receipt is null;
        var row = database.ChangeTracker.Entries<PasskeyCeremonyRow>().Select(x => x.Entity).SingleOrDefault(x => x.CeremonyId == validity.CeremonyId);
        var current = passkeyPolicies.SingleOrDefault()?.RequireEffective(decisionAt);
        if (row is null || current is null || current.Version != validity.PolicyVersion || current.Origin != caller.Origin || row.PolicyVersion != validity.PolicyVersion ||
            row.AccountId != caller.AccountId || row.SessionId != caller.SessionId || row.Origin != caller.Origin || validity.AccountId != caller.AccountId ||
            validity.SessionId != caller.SessionId || validity.Origin != caller.Origin || validity.OperationId != operation.OperationId || validity.ContextDigest != operation.ContextDigest ||
            PasskeyApplication.Operation(row).ContextDigest != operation.ContextDigest || row.ExpiresAt != validity.ExpiresAt || decisionAt >= validity.ExpiresAt) return false;
        if (operation.Scope == PasskeyApplication.EnrollmentScope) return row.State == "AUTHORIZED" && validity.EligibilityInterval is null;
        return operation.Scope == PasskeyApplication.RevocationScope && row.State == "COMPLETED" && validity.EligibilityInterval is { } interval && interval.Contains(decisionAt) &&
            !string.IsNullOrWhiteSpace(validity.EligibilityPolicyVersion) && !string.IsNullOrWhiteSpace(validity.EligibilityEvidenceReference) &&
            row.EligibilityPolicyVersion == validity.EligibilityPolicyVersion && row.EligibilityEvidenceReference == validity.EligibilityEvidenceReference &&
            row.EligibilityFrom == interval.From && row.EligibilityIncludesFrom == interval.IncludesFrom && row.EligibilityUntil == interval.Until && row.EligibilityIncludesUntil == interval.IncludesUntil;
    }
    private async Task RevokeInvalidMethodAsync(AuthenticationSessionRow row, DateTimeOffset now, CancellationToken ct)
    {
        if (row.TotpAuthenticatorReference is { } factorId)
        {
            var factor = await database.Set<TotpAuthenticatorRow>().AsNoTracking().SingleOrDefaultAsync(x => x.AuthenticatorReference == factorId && x.AccountId == row.AccountId, ct).ConfigureAwait(false);
            if (factor is { State: "DISABLED", DisableSessionEffect: nameof(TotpSessionEffect.RevokeFactorSessions) })
            {
                row.TerminationCause = "TOTP_DISABLED"; row.TerminationPolicyVersion = factor.DisablePolicyVersion; row.TerminationCredentialVersion = factor.Version;
                End(row, "REVOKED", "TunnerSessionRevoked", now, factor.DisableOperationId, factor.DisableCorrelationId); return;
            }
        }
        var credential = row.PasskeyCredentialReference is { } id
            ? await database.Set<PasskeyCredentialRow>().AsNoTracking().SingleOrDefaultAsync(x => x.CredentialReference == id && x.AccountId == row.AccountId, ct).ConfigureAwait(false) : null;
        if (credential is { State: "REVOKED", RevocationSessionEffect: nameof(PasskeyRevocationSessionEffect.RevokeCredentialSessions) })
        {
            row.TerminationCause = "PASSKEY_REVOKED"; row.TerminationPolicyVersion = credential.RevocationPolicyVersion; row.TerminationCredentialVersion = credential.Version;
            End(row, "REVOKED", "TunnerSessionRevoked", now, credential.RevocationOperationId, credential.RevocationCorrelationId);
        }
        else Revoke(row, now);
    }
    internal async Task<AuthenticationGrant?> RecoverPasskeyGrantAsync(PasskeyCeremonyRow ceremony, AuthenticationCaller caller, CancellationToken ct)
    {
        var finalSession = ceremony.ResultSessionId;
        if (finalSession is null)
        {
            var pending = await FindTotpMfaAsync(caller, ceremony.RequestId, ct).ConfigureAwait(false);
            if (pending?.State == "COMPLETED" && pending.PrimaryMethod == "passkey" && pending.PasskeyCredentialReference == ceremony.CredentialReference) finalSession = pending.ResultSessionId;
        }
        if (finalSession is not { } sessionId) return null;
        await LockSessionAsync(sessionId, ct).ConfigureAwait(false);
        var row = await database.Set<AuthenticationSessionRow>().SingleOrDefaultAsync(x => x.SessionId == sessionId && x.BrowserDigest == caller.BrowserDigest && x.IdempotencyKey == ceremony.RequestId, ct).ConfigureAwait(false);
        if (row is null) return null;
        await database.Entry(row).ReloadAsync(ct).ConfigureAwait(false);
        var ticket = await ValidateAsync(row, caller.Origin, clock.GetUtcNow(), ct).ConfigureAwait(false);
        return ticket is null ? null : new(keyProtection.Unprotect(row.ProtectedKey), ticket);
    }

    internal Task<bool> ConfirmPasswordOutcomeAsync(string browserDigest, AuthenticationOutcomeCommand command, Guid sessionId, string accountId, string origin, CancellationToken ct)
    {
        if (!IdentityEmail.IsValid(command.Email)) return Task.FromResult(false);
        var identifier = partitions.Identifier(Normalizer.NormalizeEmail(command.Email.Trim())!);
        return ConfirmOutcomeAsync(browserDigest, command.IdempotencyKey, sessionId, accountId, origin, "pwd", ct, identifier, command.RememberMe);
    }
    // Read-only authentication outcome: validates the exact current result and never creates or recovers a bearer grant.
    internal async Task<bool> ConfirmOutcomeAsync(string browserDigest, Guid idempotencyKey, Guid currentSessionId, string accountId, string origin, string method, CancellationToken ct, string? expectedIdentifier = null, bool? expectedRememberMe = null)
    {
        if (idempotencyKey == Guid.Empty || currentSessionId == Guid.Empty || browserDigest.Length != 64) return false;
        await using var transaction = await database.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        await LockSessionAsync(currentSessionId, ct).ConfigureAwait(false);
        var row = await database.Set<AuthenticationSessionRow>().SingleOrDefaultAsync(x => x.SessionId == currentSessionId && x.AccountId == accountId && x.BrowserDigest == browserDigest &&
            x.IdempotencyKey == idempotencyKey && x.Origin == origin && x.Scheme == AuthenticationIdentity.Scheme && x.AuthenticationMethods == method, ct).ConfigureAwait(false);
        if (row is null || expectedIdentifier is not null && row.IdentifierDigest != expectedIdentifier || expectedRememberMe is not null && row.RememberMe != expectedRememberMe) return false;
        await database.Entry(row).ReloadAsync(ct).ConfigureAwait(false);
        var valid = await ValidateAsync(row, origin, clock.GetUtcNow(), ct).ConfigureAwait(false);
        await database.SaveChangesAsync(ct).ConfigureAwait(false); await transaction.CommitAsync(ct).ConfigureAwait(false);
        return valid is not null;
    }
    internal async Task<AuthenticationTicket?> RetrieveAsync(string key, string origin, CancellationToken ct)
    {
        if (key is not { Length: 64 } || !key.All(Uri.IsHexDigit)) return null;
        var digest = AuthenticationIdentity.Digest(key);
        await using var transaction = await database.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        var id = await database.Set<AuthenticationSessionRow>().Where(x => x.KeyDigest == digest).Select(x => (Guid?)x.SessionId).SingleOrDefaultAsync(ct).ConfigureAwait(false);
        if (id is null) return null;
        await LockSessionAsync(id.Value, ct).ConfigureAwait(false);
        var row = await database.Set<AuthenticationSessionRow>().SingleAsync(x => x.SessionId == id, ct).ConfigureAwait(false);
        var ticket = await ValidateAsync(row, origin, clock.GetUtcNow(), ct).ConfigureAwait(false);
        await database.SaveChangesAsync(ct).ConfigureAwait(false); await transaction.CommitAsync(ct).ConfigureAwait(false);
        return ticket;
    }

    internal async Task RemoveAsync(string key, string origin, CancellationToken ct)
    {
        if (key is not { Length: 64 } || !key.All(Uri.IsHexDigit)) return;
        var digest = AuthenticationIdentity.Digest(key);
        await using var transaction = await database.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        var id = await database.Set<AuthenticationSessionRow>().Where(x => x.KeyDigest == digest && x.Origin == origin && x.Scheme == AuthenticationIdentity.Scheme).Select(x => (Guid?)x.SessionId).SingleOrDefaultAsync(ct).ConfigureAwait(false);
        if (id is null) return;
        await LockSessionAsync(id.Value, ct).ConfigureAwait(false);
        var row = await database.Set<AuthenticationSessionRow>().SingleAsync(x => x.SessionId == id, ct).ConfigureAwait(false);
        Revoke(row, clock.GetUtcNow());
        await database.SaveChangesAsync(ct).ConfigureAwait(false); await transaction.CommitAsync(ct).ConfigureAwait(false);
    }

    public async Task<int> ExpireSessionsAsync(CancellationToken cancellationToken = default)
    {
        var now = clock.GetUtcNow(); var policy = policies.RequireEffective(now); policy.Validate();
        // Bounded technical batch. The worker may repeat until zero; this is not a policy lifetime or retention default.
        var candidates = await database.Set<AuthenticationSessionRow>().FromSqlInterpolated($"""
            SELECT * FROM identity.tunner_sessions WHERE state='ACTIVE' AND (
              expires_at <= {now} OR last_seen_at + idle_ticks * interval '1 microsecond' / 10 <= {now} OR
              last_seen_at + CASE WHEN remember_me THEN {policy.RememberIdleLifetime} ELSE {policy.IdleLifetime} END <= {now} OR
              authenticated_at + CASE WHEN remember_me THEN {policy.RememberAbsoluteLifetime} ELSE {policy.AbsoluteLifetime} END <= {now})
            ORDER BY authenticated_at, session_id LIMIT 100
            """).AsNoTracking().Select(x => x.SessionId).ToArrayAsync(cancellationToken).ConfigureAwait(false);
        var ended = 0;
        foreach (var id in candidates)
        {
            await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            await LockSessionAsync(id, cancellationToken).ConfigureAwait(false);
            var row = await database.Set<AuthenticationSessionRow>().SingleAsync(x => x.SessionId == id, cancellationToken).ConfigureAwait(false);
            // A previous iteration may have tracked a row; always reload after acquiring its lifecycle lock.
            await database.Entry(row).ReloadAsync(cancellationToken).ConfigureAwait(false);
            if (row.State == "ACTIVE" && Expired(row, policy, now))
            {
                End(row, "EXPIRED", "TunnerSessionExpired", now); ended++;
                await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        return ended;
    }
    public Task<int> PurgeExpiredAbuseAsync(CancellationToken cancellationToken = default)
    {
        var now = clock.GetUtcNow();
        return database.Set<AuthenticationAbuseRow>().Where(x => x.ExpiresAt <= now).ExecuteDeleteAsync(cancellationToken);
    }
    internal static bool Expired(AuthenticationSessionRow row, AuthenticationPolicy policy, DateTimeOffset now)
    {
        var absolute = row.RememberMe ? policy.RememberAbsoluteLifetime : policy.AbsoluteLifetime;
        var idle = row.RememberMe ? policy.RememberIdleLifetime : policy.IdleLifetime;
        return now < row.AuthenticatedAt || now < row.LastSeenAt || now >= row.ExpiresAt || now - row.AuthenticatedAt >= absolute ||
            now - row.LastSeenAt >= TimeSpan.FromTicks(Math.Min(row.IdleTicks, idle.Ticks));
    }

    private async Task<AuthenticationTicket?> ValidateAsync(AuthenticationSessionRow row, string origin, DateTimeOffset now, CancellationToken ct)
    {
        if (row.State != "ACTIVE" || row.Scheme != AuthenticationIdentity.Scheme || row.Origin != origin) return null;
        var policy = policies.RequireEffective(now); policy.Validate();
        if (Expired(row, policy, now))
        { End(row, "EXPIRED", "TunnerSessionExpired", now); return null; }
        var account = await database.Accounts.SingleAsync(x => x.AccountId == row.AccountId, ct).ConfigureAwait(false);
        var user = await database.Users.SingleOrDefaultAsync(x => x.AccountId == row.AccountId, ct).ConfigureAwait(false);
        if (user is null || !user.EmailConfirmed || string.IsNullOrWhiteSpace(user.SecurityStamp) ||
            AuthenticationIdentity.Digest(user.SecurityStamp) != row.SecurityStampDigest || !await MethodIsCurrentAsync(row, policy, now, ct).ConfigureAwait(false) ||
            !await database.VerifiedContacts.AnyAsync(x => x.AccountId == row.AccountId && x.State == ContactVerificationState.Verified &&
                x.AnonymizedAtUtc == null && x.NormalizedEmail == user.NormalizedEmail, ct).ConfigureAwait(false))
        { await RevokeInvalidMethodAsync(row, now, ct).ConfigureAwait(false); return null; }
        var decision = await AccessAsync(account, "SESSION_USE", now, ct).ConfigureAwait(false);
        if (decision is null || !decision.Allowed) { Revoke(row, now); return null; }
        var ticket = TicketSerializer.Default.Deserialize(Convert.FromBase64String(ticketProtection.Unprotect(row.ProtectedTicket)));
        if (ticket is null || ticket.AuthenticationScheme != row.Scheme || AuthenticationIdentity.SessionId(ticket.Principal) != row.SessionId ||
            ticket.Principal.FindFirstValue(ClaimTypes.NameIdentifier) != row.AccountId || ticket.Principal.FindFirstValue(AuthenticationIdentity.OriginClaim) != row.Origin ||
            ticket.Properties.IssuedUtc != row.AuthenticatedAt || ticket.Properties.ExpiresUtc != row.ExpiresAt)
        { Revoke(row, now); return null; }
        row.LastSeenAt = now; row.AccessPolicyVersion = decision.PolicyVersion; row.AccessEvidenceReference = decision.EvidenceReference; row.Version++;
        return ticket;
    }

    // Caller owns the transaction. Session lock matches retrieval/revocation; Account lock freezes restriction state.
    internal async Task<AuthenticationSessionRow?> LockForOperationAsync(StepUpCaller caller, string operation, CancellationToken ct)
    {
        await LockSessionAsync(caller.SessionId, ct).ConfigureAwait(false);
        var row = await database.Set<AuthenticationSessionRow>().FromSqlInterpolated($"SELECT * FROM identity.tunner_sessions WHERE session_id={caller.SessionId} FOR UPDATE").SingleOrDefaultAsync(ct).ConfigureAwait(false);
        if (row is null) return null;
        await database.Entry(row).ReloadAsync(ct).ConfigureAwait(false);
        if (row.AccountId != caller.AccountId || row.Origin != caller.Origin || row.Scheme != AuthenticationIdentity.Scheme) return null;
        var account = await database.Accounts.FromSqlInterpolated($"SELECT * FROM accounts.accounts WHERE account_id={caller.AccountId} FOR NO KEY UPDATE").SingleAsync(ct).ConfigureAwait(false);
        await database.Entry(account).ReloadAsync(ct).ConfigureAwait(false);
        var user = await database.Users.FromSqlInterpolated($"""SELECT * FROM identity."AspNetUsers" WHERE "AccountId"={caller.AccountId} FOR NO KEY UPDATE""").SingleOrDefaultAsync(ct).ConfigureAwait(false);
        if (user is not null) await database.Entry(user).ReloadAsync(ct).ConfigureAwait(false);
        // Freeze the current verified contact against concurrent revocation during the local mutation.
        _ = await database.VerifiedContacts.FromSqlInterpolated($"SELECT * FROM accounts.verified_contacts WHERE account_id={caller.AccountId} ORDER BY contact_id FOR SHARE").ToArrayAsync(ct).ConfigureAwait(false);
        var now = clock.GetUtcNow();
        if (await ValidateAsync(row, caller.Origin, now, ct).ConfigureAwait(false) is null) return null;
        var decision = await AccessAsync(account, operation, now, ct).ConfigureAwait(false);
        return decision?.Allowed == true ? row : null;
    }

    private async ValueTask<AccountAuthenticationAccessDecision?> AccessAsync(AccountRow account, string operation, DateTimeOffset now, CancellationToken ct)
    {
        if (account.State is AccountState.Closed or AccountState.VerificationPending) return null;
        var decision = await access.RequireAsync(new(account.AccountId, account.State, account.Version, operation, now), ct).ConfigureAwait(false);
        if (decision.AccountId != account.AccountId || decision.StateVersion != account.Version || decision.Operation != operation ||
            string.IsNullOrWhiteSpace(decision.PolicyVersion) || string.IsNullOrWhiteSpace(decision.EvidenceReference) || !decision.EffectiveInterval.Contains(now))
            throw new InvalidOperationException("Current Account authentication authority is unavailable.");
        return decision;
    }
    internal async Task<bool> AllowAsync(string partition, int limit, AuthenticationPolicy policy, DateTimeOffset now, CancellationToken ct)
    {
        var window = now.UtcTicks / policy.AbuseWindow.Ticks;
        await LockAsync("auth-abuse:" + partition, ct).ConfigureAwait(false);
        var row = await database.Set<AuthenticationAbuseRow>().SingleOrDefaultAsync(x => x.Partition == partition && x.PolicyVersion == policy.Version && x.Window == window, ct).ConfigureAwait(false);
        if (row is null)
        {
            row = new AuthenticationAbuseRow { Partition = partition, PolicyVersion = policy.Version, Window = window,
                ExpiresAt = new DateTimeOffset(checked((window + 1) * policy.AbuseWindow.Ticks), TimeSpan.Zero) };
            database.Add(row);
        }
        if (row.Count >= limit) return false;
        row.Count++; return true;
    }
    private async Task<AuthenticationAttempt> RejectAsync(Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction, DateTimeOffset now, CancellationToken ct)
    {
        var id = Guid.NewGuid(); var operation = "OP-" + id.ToString("N");
        database.Add(new OutboxMessage(Guid.NewGuid(), "1.0", "AuthenticationRejected", "1", now, "tunner/identity", id.ToString("N"),
            "RESTRICTED_SECURITY", JsonSerializer.Serialize(new { reference = operation }), "1", operation, Guid.NewGuid().ToString("N")));
        await database.SaveChangesAsync(ct).ConfigureAwait(false); await transaction.CommitAsync(ct).ConfigureAwait(false);
        return new("AUTHENTICATION_FAILED", null);
    }
    private void Revoke(AuthenticationSessionRow row, DateTimeOffset now) => End(row, "REVOKED", "TunnerSessionRevoked", now);
    private void End(AuthenticationSessionRow row, string state, string eventType, DateTimeOffset now, string? operationId = null, string? correlationId = null)
    {
        if (row.State != "ACTIVE") return;
        row.State = state; row.RevokedAt = now; row.OperationId = operationId ?? "OP-" + Guid.NewGuid().ToString("N"); row.CorrelationId = correlationId ?? Guid.NewGuid().ToString("N"); row.ProtectedKey = string.Empty; row.ProtectedTicket = string.Empty; row.Version++;
        Event(row, eventType, now);
    }
    private void Event(AuthenticationSessionRow row, string type, DateTimeOffset now) => database.Add(new OutboxMessage(Guid.NewGuid(), "1.0", type, "1", now,
        "tunner/identity", row.SessionId.ToString("N"), "RESTRICTED_SECURITY", JsonSerializer.Serialize(new { reference = row.SessionId.ToString("N") }),
        row.Version.ToString(CultureInfo.InvariantCulture), row.OperationId, row.CorrelationId));
    private Task<int> LockAsync(string resource, CancellationToken ct) => database.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({resource}, 0))", ct);
    internal Task<int> LockSessionAsync(Guid id, CancellationToken ct) => LockAsync("auth-session:" + id.ToString("N"), ct);
}
