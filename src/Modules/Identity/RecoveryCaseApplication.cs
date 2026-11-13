using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Tunner.Identity.Persistence;

/// <summary>Uncomposed private owner slice. No endpoint, session issuance, credential repair or policy defaults.</summary>
internal sealed partial class RecoveryCaseApplication(IdentityPersistenceDbContext database, RecoveryCodeApplication codes,
    AuthenticationApplication authentication, TimeProvider clock, IRecoveryCasePolicySource? policies = null,
    IRecoveryCaseHierarchySource? hierarchy = null, IRecoveryPasswordPolicySource? passwordPolicies = null,
    IRegistrationPasswordBlocklist? passwordBlocklist = null, IAuthenticationPolicySource? authenticationPolicies = null,
    AuthenticationPartitionKey? partitions = null)
{
    internal static string OperationId(RecoveryCaseRow row) => "OP-RECOVERY-CASE-" + row.CaseId.ToString("N");
    internal async Task<RecoveryCaseResult> StartAsync(RecoveryCaseCaller caller, RecoveryCaseStartCommand command, CancellationToken ct = default)
    {
        Clean();
        if (!caller.Valid || command.RequestId == Guid.Empty || command.TargetSessionId == Guid.Empty || command.SetReference == Guid.Empty ||
            command.ExpectedAccountVersion < 1 || command.ExpectedSessionVersion < 1 || command.ExpectedSetLifecycleVersion < 1 || command.ExpectedUsageVersion is < 0)
            return new("VALIDATION_ERROR");
        if (policies is null || hierarchy is null) return new("RECOVERY_UNAVAILABLE");
        var initial = Current(caller, clock.GetUtcNow());
        if (initial.Values.UsageAdmission == RecoveryCodeUsageAdmission.ExactUsageSnapshot ? command.ExpectedUsageVersion is null : command.ExpectedUsageVersion is not null)
            return new("VALIDATION_ERROR");
        await using var tx = await database.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        try
        {
            await LockOriginal(caller, command.RequestId, ct).ConfigureAwait(false);
            var prior = await Original(caller, command.RequestId, ct).ConfigureAwait(false);
            if (prior is not null)
            {
                if (!SameCommand(prior, command)) return await Finish(tx, new("VERSION_CONFLICT"), ct).ConfigureAwait(false);
                return await ReadLocked(caller, prior, tx, ct).ConfigureAwait(false);
            }
            var facts = await LoadFacts(caller, command.TargetSessionId, ct).ConfigureAwait(false);
            if (facts is null || facts.Account.Version != command.ExpectedAccountVersion || facts.Session.Version != command.ExpectedSessionVersion)
                return await Finish(tx, new("RECOVERY_UNAVAILABLE"), ct).ConfigureAwait(false);
            var set = await database.Set<RecoveryCodeSetRow>().AsNoTracking().SingleOrDefaultAsync(x => x.AccountId == caller.AccountId && x.SetReference == command.SetReference, ct).ConfigureAwait(false);
            var pending = await database.Set<RecoveryCaseRow>().CountAsync(x => x.AccountId == caller.AccountId && x.State == "PENDING_CODE" && x.ExpiresAt > clock.GetUtcNow(), ct).ConfigureAwait(false);
            var now = clock.GetUtcNow(); var policy = Current(caller, now);
            if (policy.Snapshot != initial.Snapshot || pending >= policy.Values.MaximumPendingCases || set is null || set.State != "ACTIVE" ||
                set.LifecycleVersion != command.ExpectedSetLifecycleVersion || command.ExpectedUsageVersion is { } usage && set.UsageVersion != usage ||
                facts.Session.State != "ACTIVE" || facts.Session.ExpiresAt <= now || facts.Session.Scheme != AuthenticationIdentity.Scheme || facts.Session.Origin != caller.Origin)
                return await Finish(tx, new("RECOVERY_UNAVAILABLE"), ct).ConfigureAwait(false);
            var row = new RecoveryCaseRow
            {
                CaseId = Guid.NewGuid(), RequestId = command.RequestId, AccountId = caller.AccountId, OriginalBrowserDigest = caller.OriginalBrowserDigest,
                Origin = caller.Origin, NetworkDigest = caller.NetworkDigest, Goal = policy.Values.Goal.ToString(), SessionEffect = policy.Values.SessionEffect.ToString(),
                TargetSessionId = command.TargetSessionId, ExpectedSessionVersion = command.ExpectedSessionVersion, ExpectedAccountVersion = command.ExpectedAccountVersion,
                AccountState = facts.Account.State.ToString(), SetReference = command.SetReference, ExpectedSetLifecycleVersion = command.ExpectedSetLifecycleVersion,
                ExpectedUsageVersion = command.ExpectedUsageVersion, UsageAdmission = policy.Values.UsageAdmission.ToString(), PolicyVersion = policy.Values.Version,
                PolicySnapshot = policy.Snapshot, FactsDigest = facts.Value().Digest(), ProcessingReference = policy.Values.ProcessingReference,
                RetentionReference = policy.Values.RetentionReference, UnresolvedEffectsJson = JsonSerializer.Serialize(policy.Values.UnresolvedDownstreamEffects), CreatedAt = IdentityPersistenceTime.Normalize(now)
            };
            row.ContextDigest = Context(row);
            var decision = Decision(row, facts, now); row.HierarchySnapshot = decision.Snapshot();
            row.ExpiresAt = IdentityPersistenceTime.Normalize(Clip(row.CreatedAt + policy.Values.CaseLifetime, policy.Metadata.EffectiveInterval.Until, decision.EffectiveInterval.Until, facts.Session.ExpiresAt));
            row.RecoveryExpiresAt = IdentityPersistenceTime.Normalize(row.ExpiresAt + policy.Values.ResultRecoveryLifetime);
            now = clock.GetUtcNow();
            if (!PendingCurrent(caller, row, facts, now)) return await Finish(tx, new("RECOVERY_UNAVAILABLE"), ct).ConfigureAwait(false);
            database.Add(row);
            return await Finish(tx, new(null, row), ct).ConfigureAwait(false);
        }
        catch { database.ChangeTracker.Clear(); throw; }
    }

    internal async Task<RecoveryCaseResult> ReadOriginalAsync(RecoveryCaseCaller caller, Guid requestId, CancellationToken ct = default)
    {
        Clean(); if (!caller.Valid || requestId == Guid.Empty) return new("VALIDATION_ERROR");
        if (policies is null || hierarchy is null) return new("RECOVERY_UNAVAILABLE");
        await using var tx = await database.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        try
        {
            await LockOriginal(caller, requestId, ct).ConfigureAwait(false);
            var row = await Original(caller, requestId, ct).ConfigureAwait(false);
            return row is null ? await Finish(tx, new("NOT_FOUND_OR_NOT_DISCLOSABLE"), ct).ConfigureAwait(false) : await ReadLocked(caller, row, tx, ct).ConfigureAwait(false);
        }
        catch { database.ChangeTracker.Clear(); throw; }
    }

    internal async Task<RecoveryCaseResult> ConsumeCodeAsync(RecoveryCaseCaller caller, Guid requestId, string? input, CancellationToken ct = default)
    {
        Clean(); if (!caller.Valid || requestId == Guid.Empty) return new("VALIDATION_ERROR");
        if (policies is null || hierarchy is null) return new("RECOVERY_UNAVAILABLE");
        var snapshot = await database.Set<RecoveryCaseRow>().AsNoTracking().SingleOrDefaultAsync(x => x.AccountId == caller.AccountId &&
            x.OriginalBrowserDigest == caller.OriginalBrowserDigest && x.RequestId == requestId, ct).ConfigureAwait(false);
        if (snapshot is null || !Actor(snapshot, caller)) return new("NOT_FOUND_OR_NOT_DISCLOSABLE");
        var request = new RecoveryCodeConsumptionRequest(caller.AccountId, OperationId(snapshot), requestId.ToString("N"), snapshot.ContextDigest, caller.Origin, caller.NetworkDigest);
        var owner = new RecoveryCaseCodeOwner(this, database, authentication, clock, caller, snapshot.CaseId, requestId);
        var consumed = await codes.ExecuteConsumptionOutcomeAsync(request, input, owner, ct).ConfigureAwait(false);
        return new(consumed.ErrorCode, consumed.ErrorCode is null ? owner.Case : null,
            consumed.ErrorCode is null ? owner.Result : null, consumed.Reconciled);
    }

    internal RecoveryCasePolicyVersion Current(RecoveryCaseCaller caller, DateTimeOffset at)
    {
        var selected = policies?.RequireEffective(at) ?? throw new InvalidOperationException("Recovery policy is unavailable.");
        selected.Require(caller.Origin, at); return selected;
    }
    internal RecoveryCaseHierarchyDecision Decision(RecoveryCaseRow row, RecoveryCaseLockedFacts facts, DateTimeOffset at)
    {
        var current = facts.Value();
        var decision = hierarchy?.RequireCurrent(new(row.CaseId, row.RequestId, row.ContextDigest, row.PolicyVersion,
            Enum.Parse<RecoveryCaseGoal>(row.Goal, false), current), at) ?? throw new InvalidOperationException("Current recovery hierarchy is unavailable.");
        if (!decision.RecoveryCodeAllowed || !decision.StrongAuthenticatorUnavailable || string.IsNullOrWhiteSpace(decision.PolicyVersion) ||
            string.IsNullOrWhiteSpace(decision.EvidenceReference) || decision.FactsDigest != current.Digest() || !decision.EffectiveInterval.Contains(at))
            throw new InvalidOperationException("Qualified current recovery-code hierarchy admission is required.");
        return decision;
    }
    internal bool AuthorityCurrent(RecoveryCaseCaller caller, RecoveryCaseRow row, RecoveryCaseLockedFacts facts, DateTimeOffset at)
    {
        var policy = Current(caller, at); var decision = Decision(row, facts, at);
        return Actor(row, caller) && Context(row) == row.ContextDigest && policy.Snapshot == row.PolicySnapshot &&
            facts.Account.AccountId == row.AccountId && facts.Account.Version == row.ExpectedAccountVersion && facts.Account.State.ToString() == row.AccountState &&
            facts.Unmutated(database) && facts.Value().Digest() == row.FactsDigest && decision.Snapshot() == row.HierarchySnapshot &&
            row.ExpiresAt == IdentityPersistenceTime.Normalize(Clip(row.CreatedAt + policy.Values.CaseLifetime, policy.Metadata.EffectiveInterval.Until,
                decision.EffectiveInterval.Until, facts.Session.ExpiresAt)) &&
            row.RecoveryExpiresAt == IdentityPersistenceTime.Normalize(row.ExpiresAt + policy.Values.ResultRecoveryLifetime);
    }
    internal bool PendingCurrent(RecoveryCaseCaller caller, RecoveryCaseRow row, RecoveryCaseLockedFacts facts, DateTimeOffset at) =>
        at >= row.CreatedAt && at < row.ExpiresAt && row.State == "PENDING_CODE" && row.Version == 1 && row.ResultId is null && row.EndedAt is null &&
        facts.Session.SessionId == row.TargetSessionId && facts.Session.AccountId == row.AccountId && facts.Session.State == "ACTIVE" &&
        facts.Session.Version == row.ExpectedSessionVersion && facts.Session.Origin == row.Origin && facts.Session.Scheme == AuthenticationIdentity.Scheme &&
        facts.Session.AuthenticatedAt <= at && facts.Session.ExpiresAt > at && AuthorityCurrent(caller, row, facts, at);

    internal Task<int> LockOriginal(RecoveryCaseCaller caller, Guid requestId, CancellationToken ct) => database.Database.ExecuteSqlInterpolatedAsync(
        $"SELECT pg_advisory_xact_lock(hashtextextended({"recovery-case-original:" + AuthenticationIdentity.Digest(JsonSerializer.Serialize(new { caller.AccountId, caller.OriginalBrowserDigest, requestId }))},0))", ct);
    internal async Task<RecoveryCaseRow?> Original(RecoveryCaseCaller caller, Guid requestId, CancellationToken ct)
    {
        var row = await database.Set<RecoveryCaseRow>().FromSqlInterpolated($"SELECT * FROM identity.recovery_cases WHERE account_id={caller.AccountId} AND original_browser_digest={caller.OriginalBrowserDigest} AND request_id={requestId} FOR UPDATE").SingleOrDefaultAsync(ct).ConfigureAwait(false);
        if (row is not null) await database.Entry(row).ReloadAsync(ct).ConfigureAwait(false);
        return row;
    }
    internal async Task<RecoveryCaseLockedFacts?> LoadFacts(RecoveryCaseCaller caller, Guid sessionId, CancellationToken ct)
    {
        await authentication.LockSessionAsync(sessionId, ct).ConfigureAwait(false);
        var session = await database.Set<AuthenticationSessionRow>().FromSqlInterpolated($"SELECT * FROM identity.tunner_sessions WHERE session_id={sessionId} FOR UPDATE").SingleOrDefaultAsync(ct).ConfigureAwait(false);
        if (session is null || session.AccountId != caller.AccountId) return null;
        await database.Entry(session).ReloadAsync(ct).ConfigureAwait(false);
        return await LoadAccountFacts(caller.AccountId, session, ct).ConfigureAwait(false);
    }
    internal async Task<RecoveryCaseLockedFacts?> LoadAccountFacts(string accountId, AuthenticationSessionRow? session, CancellationToken ct)
    {
        var account = await database.Accounts.FromSqlInterpolated($"SELECT * FROM accounts.accounts WHERE account_id={accountId} FOR NO KEY UPDATE").SingleOrDefaultAsync(ct).ConfigureAwait(false);
        if (account is null) return null;
        await database.Entry(account).ReloadAsync(ct).ConfigureAwait(false);
        var user = await database.Users.FromSqlInterpolated($"SELECT * FROM identity.\"AspNetUsers\" WHERE \"AccountId\"={accountId} FOR NO KEY UPDATE").SingleOrDefaultAsync(ct).ConfigureAwait(false);
        if (user is null || string.IsNullOrWhiteSpace(user.SecurityStamp)) return null;
        await database.Entry(user).ReloadAsync(ct).ConfigureAwait(false);
        var passkeys = await database.Set<PasskeyCredentialRow>().FromSqlInterpolated($"SELECT * FROM identity.passkey_credentials WHERE account_id={accountId} ORDER BY credential_reference FOR SHARE").ToArrayAsync(ct).ConfigureAwait(false);
        var factors = await database.Set<TotpAuthenticatorRow>().FromSqlInterpolated($"SELECT * FROM identity.totp_authenticators WHERE account_id={accountId} ORDER BY authenticator_reference FOR SHARE").ToArrayAsync(ct).ConfigureAwait(false);
        var links = await database.ExternalIdentityLinks.FromSqlInterpolated($"SELECT * FROM identity.external_identity_links WHERE account_id={accountId} ORDER BY link_id FOR SHARE").ToArrayAsync(ct).ConfigureAwait(false);
        foreach (var path in passkeys) await database.Entry(path).ReloadAsync(ct).ConfigureAwait(false);
        foreach (var path in factors) await database.Entry(path).ReloadAsync(ct).ConfigureAwait(false);
        foreach (var path in links) await database.Entry(path).ReloadAsync(ct).ConfigureAwait(false);
        return new(account, session, user, passkeys, factors, links);
    }
    private async Task<RecoveryCaseResult> ReadLocked(RecoveryCaseCaller caller, RecoveryCaseRow row, IDbContextTransaction tx, CancellationToken ct)
    {
        if (row.Goal != nameof(RecoveryCaseGoal.ContainSelectedTunnerSession) || row.TargetSessionId is not { } target)
            return await Finish(tx, new("NOT_FOUND_OR_NOT_DISCLOSABLE"), ct).ConfigureAwait(false);
        var facts = await LoadFacts(caller, target, ct).ConfigureAwait(false);
        var result = await database.Set<RecoveryResultRow>().SingleOrDefaultAsync(x => x.CaseId == row.CaseId, ct).ConfigureAwait(false);
        var claim = await database.Set<RecoveryCodeEntryRow>().AsNoTracking().SingleOrDefaultAsync(x => x.AccountId == row.AccountId && x.ConsumedByOperationId == OperationId(row), ct).ConfigureAwait(false);
        var at = clock.GetUtcNow();
        if (facts is null || at >= row.RecoveryExpiresAt || !AuthorityCurrent(caller, row, facts, at) ||
            (row.State == "PENDING_CODE" ? !PendingCurrent(caller, row, facts, at) || result is not null || claim is not null :
                !CompletedCurrent(row, facts.Session, result, at) || claim is null || claim.ConsumptionOutcomeReference != result!.ResultId.ToString("N") ||
                claim.Version != 2 || claim.ConsumedAt is null || claim.ConsumedAt < row.CreatedAt || claim.ConsumedAt > row.EndedAt ||
                claim.ConsumptionContextDigest != row.ContextDigest || claim.ConsumptionCorrelationId != row.RequestId.ToString("N") ||
                claim.ConsumptionOwnerPolicyVersion != row.PolicyVersion || claim.SetReference != row.SetReference || claim.ConsumptionSetLifecycleVersion != row.ExpectedSetLifecycleVersion))
            return await Finish(tx, new("NOT_FOUND_OR_NOT_DISCLOSABLE"), ct).ConfigureAwait(false);
        return await Finish(tx, new(null, row, result, result is not null), ct).ConfigureAwait(false);
    }
    internal static bool CompletedCurrent(RecoveryCaseRow row, AuthenticationSessionRow session, RecoveryResultRow? result, DateTimeOffset at) =>
        result is not null && row.State == "CONTAINED" && row.Version == 2 && row.ResultId == result.ResultId && row.EndedAt == result.RecordedAt &&
        row.EndedAt >= row.CreatedAt && row.EndedAt < row.ExpiresAt && row.EndedAt <= at && at < row.RecoveryExpiresAt &&
        result.CaseId == row.CaseId && result.AccountId == row.AccountId && result.Outcome == "CONTAINED_PENDING_RESTORATION" &&
        result.ContextDigest == row.ContextDigest && result.PolicyVersion == row.PolicyVersion && result.PolicySnapshot == row.PolicySnapshot &&
        result.HierarchySnapshot == row.HierarchySnapshot && result.UnresolvedEffectsJson == row.UnresolvedEffectsJson &&
        result.ProcessingReference == row.ProcessingReference && result.RetentionReference == row.RetentionReference &&
        result.SetReference == row.SetReference && result.SetLifecycleVersion == row.ExpectedSetLifecycleVersion &&
        result.TargetSessionId == row.TargetSessionId && result.PriorSessionVersion == row.ExpectedSessionVersion && result.EndedSessionVersion == row.ExpectedSessionVersion + 1 &&
        session.SessionId == row.TargetSessionId && session.AccountId == row.AccountId && session.State == "REVOKED" && session.Version == result.EndedSessionVersion &&
        session.RevokedAt == row.EndedAt && session.ProtectedKey.Length == 0 && session.ProtectedTicket.Length == 0 &&
        session.OperationId == OperationId(row) && session.CorrelationId == row.RequestId.ToString("N");
    private static bool SameCommand(RecoveryCaseRow row, RecoveryCaseStartCommand command) => row.RequestId == command.RequestId &&
        row.TargetSessionId == command.TargetSessionId && row.ExpectedSessionVersion == command.ExpectedSessionVersion && row.ExpectedAccountVersion == command.ExpectedAccountVersion &&
        row.SetReference == command.SetReference && row.ExpectedSetLifecycleVersion == command.ExpectedSetLifecycleVersion && row.ExpectedUsageVersion == command.ExpectedUsageVersion;
    internal static bool Actor(RecoveryCaseRow row, RecoveryCaseCaller caller) => caller.Valid && row.AccountId == caller.AccountId &&
        row.OriginalBrowserDigest == caller.OriginalBrowserDigest && row.Origin == caller.Origin && row.NetworkDigest == caller.NetworkDigest;
    private static string Context(RecoveryCaseRow row)
    {
        var original = AuthenticationIdentity.Digest(JsonSerializer.Serialize(new { row.CaseId, row.RequestId,
        row.AccountId, row.OriginalBrowserDigest, row.Origin, row.NetworkDigest, row.Goal, row.SessionEffect, row.TargetSessionId,
        row.ExpectedSessionVersion, row.ExpectedAccountVersion, row.AccountState, row.SetReference, row.ExpectedSetLifecycleVersion,
        row.ExpectedUsageVersion, row.UsageAdmission, row.PolicyVersion, row.PolicySnapshot, row.FactsDigest, row.ProcessingReference, row.RetentionReference, row.UnresolvedEffectsJson }));
        return row.EntryId is null ? original : AuthenticationIdentity.Digest(JsonSerializer.Serialize(new
            { original, row.EntryId, row.PriorPasswordDigest, row.PriorSecurityStampDigest, row.UserSourceDigest }));
    }
    private static DateTimeOffset Clip(DateTimeOffset value, params DateTimeOffset?[] bounds)
    { foreach (var bound in bounds) if (bound is { } end && end < value) value = end; return value; }
    private void Clean() { if (database.Database.CurrentTransaction is not null || database.ChangeTracker.HasChanges()) throw new InvalidOperationException("Recovery cases require a clean owner boundary."); }
    private async Task<RecoveryCaseResult> Finish(IDbContextTransaction tx, RecoveryCaseResult result, CancellationToken ct)
    { await database.SaveChangesAsync(ct).ConfigureAwait(false); await tx.CommitAsync(ct).ConfigureAwait(false); return result; }
}

internal sealed class RecoveryCaseLockedFacts(AccountRow account, AuthenticationSessionRow? session, IdentityUserAdapter user,
    PasskeyCredentialRow[] passkeys, TotpAuthenticatorRow[] factors, ExternalIdentityLinkRow[] links)
{
    internal AccountRow Account { get; } = account;
    internal AuthenticationSessionRow Session => session ?? throw new InvalidOperationException("This recovery goal has no actor session.");
    internal IdentityUserAdapter User { get; } = user;
    internal bool OtherSourcesUnmutated(IdentityPersistenceDbContext database)
    {
        database.ChangeTracker.DetectChanges();
        return database.Entry(Account).State == EntityState.Unchanged && passkeys.All(x => database.Entry(x).State == EntityState.Unchanged) &&
            factors.All(x => database.Entry(x).State == EntityState.Unchanged) && links.All(x => database.Entry(x).State == EntityState.Unchanged);
    }
    internal bool Unmutated(IdentityPersistenceDbContext database)
    {
        database.ChangeTracker.DetectChanges();
        return database.Entry(Account).State == EntityState.Unchanged && database.Entry(User).State == EntityState.Unchanged &&
            passkeys.All(x => database.Entry(x).State == EntityState.Unchanged) && factors.All(x => database.Entry(x).State == EntityState.Unchanged) &&
            links.All(x => database.Entry(x).State == EntityState.Unchanged);
    }
    internal RecoveryCaseHierarchyFacts Value() => new(Account.AccountId, Account.State.ToString(), Account.Version, User.PasswordHash is not null, User.EmailConfirmed,
        AuthenticationIdentity.Digest(User.SecurityStamp!), Array.AsReadOnly(passkeys.Select(x => new RecoveryCaseAuthenticationPath("PASSKEY", x.CredentialReference, x.State, x.Version, x.PolicyVersion, null))
            .Concat(factors.Select(x => new RecoveryCaseAuthenticationPath("TOTP", x.AuthenticatorReference, x.State, x.Version, x.PolicyVersion, null)))
            .Concat(links.Select(x => new RecoveryCaseAuthenticationPath("FEDERATION", x.LinkId, x.LinkState, x.Version, x.QualificationProfileVersion ?? string.Empty, x.QualificationEvidenceReference)))
            .OrderBy(x => x.Kind, StringComparer.Ordinal).ThenBy(x => x.Reference).ToArray()));
}
