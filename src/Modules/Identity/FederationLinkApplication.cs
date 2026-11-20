using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Tunner.Database.Outbox;
using Tunner.Accounts;

namespace Tunner.Identity.Persistence;

/// <summary>Exact local management owner. Shared StepUp owns proof consumption/commit; native protocol remains in the one attempt ledger.</summary>
internal sealed class FederationLinkApplication(IdentityPersistenceDbContext database, AuthenticationApplication authentication,
    FederationAttemptApplication attempts, IFederationProfileSource profiles, IFederationLinkPolicySource policies,
    IFederationUnlinkPostureSource postures, IAuthenticationPolicySource authenticationPolicies, IAssurancePolicySource assurancePolicies, TimeProvider clock)
{
    internal static readonly AssurancePolicyScope LinkScope = new("LINK_EXTERNAL_IDENTITY", "USER_BFF");
    internal static readonly AssurancePolicyScope UnlinkScope = new("UNLINK_EXTERNAL_IDENTITY", "USER_BFF");
    internal static bool Owns(AssurancePolicyScope scope) => scope == LinkScope || scope == UnlinkScope;
    internal static StepUpOperation Operation(FederationLinkOperationRow row) => new("OP-FEDERATION-" + row.OperationId.ToString("N"),
        row.OperationId.ToString("N"), row.Kind == "LINK" ? LinkScope : UnlinkScope,
        (row.Kind == "UNLINK" ? row.TargetLinkId!.Value : row.OperationId).ToString("N"), row.ExpectedLinkVersion, row.ContextDigest);

    internal Task<FederationLinkOperationResult> StartLinkAsync(FederationCurrentBrowser browser, StepUpCaller caller, Guid requestId,
        FederationProviderKind provider, Guid? targetLinkId, long expectedLinkVersion, long expectedAccountVersion, CancellationToken ct) =>
        Start(caller, browser, requestId, provider, "LINK", targetLinkId, expectedLinkVersion, expectedAccountVersion, browser.ActualCookieDigest, ct);
    internal Task<FederationLinkOperationResult> StartUnlinkAsync(StepUpCaller caller, Guid requestId, FederationProviderKind provider,
        Guid linkId, long expectedLinkVersion, long expectedAccountVersion, CancellationToken ct, string? originalCookieDigest = null) =>
        Start(caller, null, requestId, provider, "UNLINK", linkId, expectedLinkVersion, expectedAccountVersion, originalCookieDigest, ct);

    private async Task<FederationLinkOperationResult> Start(StepUpCaller caller, FederationCurrentBrowser? browser, Guid requestId,
        FederationProviderKind provider, string kind, Guid? target, long expectedVersion, long accountVersion, string? originalCookieDigest, CancellationToken ct)
    {
        Clean();
        if (requestId == Guid.Empty || !Enum.IsDefined(provider) || target == Guid.Empty || accountVersion < 1 ||
            originalCookieDigest is not null && (originalCookieDigest.Length != 64 || !originalCookieDigest.All(Uri.IsHexDigit)) ||
            (target is null ? expectedVersion != 0 || kind != "LINK" : expectedVersion < 1) || kind == "LINK" && browser is null)
            return new("VALIDATION_ERROR");
        var now = clock.GetUtcNow(); var policy = Policy(caller.Origin, now); var profile = Profile(provider, caller.Origin, now);
        await using var tx = await database.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        // SignIn's order is browser -> session/Account. The shared proof Apply path never takes this browser lock.
        if (browser is not null) await attempts.LockOriginalBrowserAsync(browser, ct).ConfigureAwait(false);
        var session = await authentication.LockForOperationAsync(caller, kind == "LINK" ? LinkScope.Operation : UnlinkScope.Operation, ct).ConfigureAwait(false);
        if (session is null) return await Finish(tx, new("AUTHENTICATION_FAILED"), ct).ConfigureAwait(false);
        var prior = await database.Set<FederationLinkOperationRow>().SingleOrDefaultAsync(x => x.SessionId == caller.SessionId && x.RequestId == requestId, ct).ConfigureAwait(false);
        if (prior is not null)
        {
            await database.Entry(prior).ReloadAsync(ct).ConfigureAwait(false);
            if (prior.RecoveryExpiresAt <= clock.GetUtcNow()) return await Finish(tx, new("NOT_FOUND_OR_NOT_DISCLOSABLE"), ct).ConfigureAwait(false);
            var priorAttempt = prior.AttemptId is { } attemptId ? await database.Set<FederationAttemptRow>().SingleAsync(x => x.AttemptId == attemptId, ct).ConfigureAwait(false) : null;
            return await Finish(tx, new(Actor(prior, caller) && prior.Kind == kind && prior.Provider == provider.ToString() &&
                prior.TargetLinkId == target && prior.ExpectedLinkVersion == expectedVersion && prior.ExpectedAccountVersion == accountVersion &&
                (prior.EndedAt is not null || Current(prior, caller, clock.GetUtcNow()) &&
                    (priorAttempt is null || browser is not null && priorAttempt.BrowserKey == browser.BrowserKey && priorAttempt.ExpiresAt > clock.GetUtcNow()))
                ? null : "VERSION_CONFLICT", prior, priorAttempt), ct).ConfigureAwait(false);
        }
        var account = await database.Accounts.SingleAsync(x => x.AccountId == caller.AccountId, ct).ConfigureAwait(false);
        if (account.Version != accountVersion) return await Finish(tx, new("VERSION_CONFLICT"), ct).ConfigureAwait(false);
        var link = target is { } id ? await database.ExternalIdentityLinks.SingleOrDefaultAsync(x => x.LinkId == id && x.AccountId == caller.AccountId, ct).ConfigureAwait(false) : null;
        if (target is not null && (link is null || link.Version != expectedVersion || !Qualified(link, profile) ||
            link.LinkState != (kind == "LINK" ? "UNLINKED" : "ACTIVE"))) return await Finish(tx, new("VERSION_CONFLICT"), ct).ConfigureAwait(false);
        if (await database.Set<FederationLinkOperationRow>().CountAsync(x => x.AccountId == caller.AccountId && x.EndedAt == null && x.ExpiresAt > now, ct).ConfigureAwait(false) >= policy.Values.MaximumPendingOperations)
            return await Finish(tx, new("RATE_OR_ABUSE_LIMITED"), ct).ConfigureAwait(false);
        now = clock.GetUtcNow(); var finalPolicy = Policy(caller.Origin, now); var finalProfile = Profile(provider, caller.Origin, now);
        var auth = authenticationPolicies.RequireEffective(now); auth.Validate();
        if (policy.Snapshot != finalPolicy.Snapshot || profile.Snapshot != finalProfile.Snapshot || session.ExpiresAt <= now)
            return await Finish(tx, new("VERSION_CONFLICT"), ct).ConfigureAwait(false);
        var expires = Min(now + policy.Values.OperationLifetime, session.ExpiresAt);
        if (policy.Metadata.EffectiveInterval.Until is { } until) expires = Min(expires, until);
        expires = profile.Clip(expires); if (expires <= now) return await Finish(tx, new("VERSION_CONFLICT"), ct).ConfigureAwait(false);
        var row = new FederationLinkOperationRow { OperationId = Guid.NewGuid(), RequestId = requestId, Kind = kind, AccountId = caller.AccountId,
            SessionId = caller.SessionId, Origin = caller.Origin, Provider = provider.ToString(), ProfileVersion = profile.Values.Version,
            ProfileSnapshot = profile.Snapshot, AttemptId = kind == "LINK" ? Guid.NewGuid() : null, TargetLinkId = target,
            ExpectedLinkVersion = expectedVersion, ExpectedAccountVersion = accountVersion, PolicyVersion = policy.Values.Version, PolicySnapshot = policy.Snapshot,
            AssurancePolicyVersion = kind == "LINK" ? policy.Values.LinkAssurancePolicyVersion : policy.Values.UnlinkAssurancePolicyVersion,
            AuthenticationPolicyVersion = auth.Version, SecurityStampDigest = session.SecurityStampDigest, OriginalCookieDigest = originalCookieDigest, SessionEffect = policy.Values.UnlinkSessionEffect.ToString(),
            SessionUsePolicyVersion = policy.Values.SessionUsePolicyVersion, SessionEffectEvidenceReference = policy.Values.SessionEffectEvidenceReference,
            ProcessingReference = policy.Values.ProcessingReference, RetentionReference = policy.Values.RetentionReference,
            State = kind == "LINK" ? "PENDING_PROVIDER" : "PENDING_CONFIRMATION", CreatedAt = now, ExpiresAt = expires,
            RecoveryExpiresAt = expires + policy.Values.ResultRecoveryLifetime };
        row.ContextDigest = Context(row); _ = assurancePolicies.RequireEffective(Operation(row).Scope, now, row.AssurancePolicyVersion);
        FederationAttemptRow? attempt = null;
        if (browser is not null)
        {
            attempt = await attempts.PrepareLinkInTransactionAsync(browser, caller, row.AttemptId!.Value, requestId, provider,
                Operation(row), account.Version, session, expires, policy.Values.ContinuationPath, ct).ConfigureAwait(false);
            if (attempt is null) return await Finish(tx, new("VERSION_CONFLICT"), ct).ConfigureAwait(false);
        }
        // Preparation may await. Do not admit stale policy/profile/session authority into a new operation.
        now = clock.GetUtcNow();
        if (!Current(row, caller, now) || session.SecurityStampDigest != row.SecurityStampDigest || now >= expires)
        { await tx.RollbackAsync(ct).ConfigureAwait(false); database.ChangeTracker.Clear(); return new("VERSION_CONFLICT"); }
        database.Add(row); Event(row, "ExternalIdentityOperationStarted", now);
        return await Finish(tx, new(null, row, attempt), ct).ConfigureAwait(false);
    }

    internal async Task<FederationLinkOperationResult> ConfirmAsync(StepUpCaller caller, Guid id, Guid requestId, long expectedVersion, bool confirmed, CancellationToken ct)
    {
        Clean(); if (!confirmed) return new("VALIDATION_ERROR");
        await using var tx = await database.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        var row = await Locked(caller, id, requestId, ct).ConfigureAwait(false);
        if (row is null) return await Finish(tx, new("NOT_FOUND_OR_NOT_DISCLOSABLE"), ct).ConfigureAwait(false);
        var attempt = row.AttemptId is { } attemptId ? await LockedAttempt(attemptId, ct).ConfigureAwait(false) : null;
        if (row.ConfirmedAt is not null) return await Finish(tx, new(null, row, attempt), ct).ConfigureAwait(false);
        var now = clock.GetUtcNow();
        if (row.Version != expectedVersion || !Current(row, caller, now) || row.State is not ("PENDING_PROVIDER" or "PENDING_CONFIRMATION") ||
            row.Kind == "LINK" && !LinkEvidence(row, attempt, caller, now)) return await Finish(tx, new("VERSION_CONFLICT", row, attempt), ct).ConfigureAwait(false);
        row.ConfirmedAt = now; row.State = "PENDING_PROOF"; row.Version++; Event(row, "ExternalIdentityOperationConfirmed", now);
        return await Finish(tx, new(null, row, attempt), ct).ConfigureAwait(false);
    }

    internal async Task<UserStepUpOperationContext?> ResolveAsync(StepUpCaller caller, Guid id, Guid requestId, CancellationToken ct)
    {
        var row = await database.Set<FederationLinkOperationRow>().AsNoTracking().SingleOrDefaultAsync(x => x.OperationId == id && x.RequestId == requestId, ct).ConfigureAwait(false);
        if (row is null || !Actor(row, caller) || row.State is not ("PENDING_PROOF" or "COMPLETED") || !Current(row, caller, clock.GetUtcNow())) return null;
        var op = Operation(row);
        return new(new(caller), op.OperationId, op.CorrelationId, op.Scope, op.ResourceId, op.ExpectedVersion, op.ContextDigest, row.AssurancePolicyVersion, row.State == "COMPLETED");
    }

    // Only shared StepUp invokes this under its current actor/proof transaction. No protocol/provider/network work.
    internal async Task<UserStepUpOperationApplyOutcome> ApplyAsync(StepUpCaller caller, UserStepUpOperationContext context, CancellationToken ct)
    {
        if (database.Database.CurrentTransaction is null || !Owns(context.Binding.Scope) ||
            !Guid.TryParseExact(context.Binding.CorrelationId, "N", out var id)) return UserStepUpOperationApplyOutcome.Conflict();
        await LockOperation(id, ct).ConfigureAwait(false);
        var row = await database.Set<FederationLinkOperationRow>().SingleOrDefaultAsync(x => x.OperationId == id, ct).ConfigureAwait(false);
        if (row is null) return UserStepUpOperationApplyOutcome.Conflict(); await database.Entry(row).ReloadAsync(ct).ConfigureAwait(false);
        if (!Actor(row, caller) || row.State != "PENDING_PROOF" || row.ConfirmedAt is null || context.ExpectedPolicyVersion != row.AssurancePolicyVersion ||
            context.Binding.ContextDigest != row.ContextDigest || Operation(row).Scope != context.Binding.Scope) return UserStepUpOperationApplyOutcome.Conflict();
        var proof = await database.Set<StepUpProofRow>().SingleOrDefaultAsync(x => x.SessionId == caller.SessionId && x.OperationId == context.Binding.OperationId, ct).ConfigureAwait(false);
        var session = await database.Set<AuthenticationSessionRow>().SingleAsync(x => x.SessionId == caller.SessionId, ct).ConfigureAwait(false);
        var account = await database.Accounts.SingleAsync(x => x.AccountId == caller.AccountId, ct).ConfigureAwait(false);
        if (proof is null || proof.State != "ACTIVE" || proof.ContextDigest != row.ContextDigest || account.Version != row.ExpectedAccountVersion ||
            session.SecurityStampDigest != row.SecurityStampDigest) return UserStepUpOperationApplyOutcome.Conflict();
        var attempt = row.AttemptId is { } attemptId ? await LockedAttempt(attemptId, ct).ConfigureAwait(false) : null;
        var now = clock.GetUtcNow(); var profile = Profile(Enum.Parse<FederationProviderKind>(row.Provider, false), caller.Origin, now);
        if (!Current(row, caller, now)) return UserStepUpOperationApplyOutcome.Conflict();
        ExternalIdentityLinkRow? link;
        PostureFacts? facts = null; FederationUnlinkPostureRequest? request = null; FederationUnlinkPostureDecision? decision = null;
        if (row.Kind == "LINK")
        {
            if (!LinkEvidence(row, attempt, caller, now)) return UserStepUpOperationApplyOutcome.Conflict();
            var key = attempts.ReadLinkIdentity(attempt!, profile);
            await database.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({"federation-key:" + AuthenticationIdentity.Digest(JsonSerializer.Serialize(new[] { key.ProviderNamespace, key.Subject }))},0))", ct).ConfigureAwait(false);
            link = await database.ExternalIdentityLinks.FromSqlInterpolated($"SELECT * FROM identity.external_identity_links WHERE provider={key.ProviderNamespace} AND subject={key.Subject} FOR UPDATE").SingleOrDefaultAsync(ct).ConfigureAwait(false);
            if (link is null)
            {
                if (row.TargetLinkId is not null || row.ExpectedLinkVersion != 0) return UserStepUpOperationApplyOutcome.Conflict();
                link = new ExternalIdentityLinkRow { LinkId = Guid.NewGuid(), AccountId = caller.AccountId, Provider = key.ProviderNamespace,
                    Subject = key.Subject, Version = 1, LinkState = "ACTIVE", QualifiedClientId = profile.Values.ClientId,
                    QualificationProfileVersion = profile.Values.Version, IssuerMappingEvidenceReference = profile.Values.IssuerMappingEvidenceReference,
                    QualificationEvidenceReference = row.OperationId.ToString("N"), EvidenceReference = row.OperationId.ToString("N") };
                database.Add(link);
            }
            else
            {
                await database.Entry(link).ReloadAsync(ct).ConfigureAwait(false);
                if (link.AccountId != caller.AccountId || link.LinkId != row.TargetLinkId || link.Version != row.ExpectedLinkVersion ||
                    link.LinkState != "UNLINKED" || !Qualified(link, profile)) return UserStepUpOperationApplyOutcome.Conflict();
                link.LinkState = "ACTIVE"; link.Version++; link.EvidenceReference = row.OperationId.ToString("N");
                link.QualificationProfileVersion = profile.Values.Version; link.QualificationEvidenceReference = row.OperationId.ToString("N");
            }
        }
        else
        {
            link = await database.ExternalIdentityLinks.FromSqlInterpolated($"SELECT * FROM identity.external_identity_links WHERE link_id={row.TargetLinkId!.Value} AND account_id={caller.AccountId} FOR UPDATE").SingleOrDefaultAsync(ct).ConfigureAwait(false);
            if (link is null) return UserStepUpOperationApplyOutcome.Conflict(); await database.Entry(link).ReloadAsync(ct).ConfigureAwait(false);
            if (link.Version != row.ExpectedLinkVersion || link.LinkState != "ACTIVE" || !Qualified(link, profile)) return UserStepUpOperationApplyOutcome.Conflict();
            facts = await ReadPostureFacts(caller.AccountId, link.LinkId, ct).ConfigureAwait(false);
            request = facts.Request(account, link); now = clock.GetUtcNow(); decision = Decision(request, now);
            if (!decision.Allowed) return UserStepUpOperationApplyOutcome.Conflict();
            row.PostureFactsDigest = request.FactsDigest; row.PosturePolicyVersion = decision.PolicyVersion; row.PostureEvidenceReference = decision.EvidenceReference;
            row.PostureFrom = IdentityPersistenceTime.Normalize(decision.EffectiveInterval.From); row.PostureIncludesFrom = decision.EffectiveInterval.IncludesFrom;
            row.PostureUntil = decision.EffectiveInterval.Until is { } postureUntil ? IdentityPersistenceTime.Normalize(postureUntil) : null;
            row.PostureIncludesUntil = decision.EffectiveInterval.IncludesUntil;
            link.LinkState = "UNLINKED"; link.Version++; link.EvidenceReference = row.OperationId.ToString("N");
        }
        // All authority reads above precede the final mutation witness. Shared StepUp rechecks the same closed validity after its own awaits.
        now = clock.GetUtcNow();
        if (!Current(row, caller, now) || row.Kind == "LINK" && !LinkEvidence(row, attempt, caller, now) ||
            decision is not null && !SameDecision(decision, Decision(request!, now), now)) return UserStepUpOperationApplyOutcome.Conflict();
        row.State = "COMPLETED"; row.Version++; row.EndedAt = now; row.ProofReference = proof.ProofId;
        row.ResultLinkId = link.LinkId; row.ResultLinkVersion = link.Version;
        if (attempt is not null)
        {
            attempt.State = "COMPLETED"; attempt.Version++; attempt.EndedAt = now;
            attempt.ResultExpiresAt = now + TimeSpan.FromTicks(attempt.ResultRecoveryLifetimeTicks);
        }
        Event(row, row.Kind == "LINK" ? "ExternalIdentityLinked" : "ExternalIdentityUnlinked", now);
        Event(row, "IdentitySecurityActivityRequested", now); Event(row, "IdentitySecurityNotificationRequested", now);
        AuthenticationSessionTerminationReceipt? receipt = null;
        if (row.Kind == "UNLINK")
        {
            receipt = await authentication.ApplyFederationUnlinkSessionEffectAsync(new(caller, context.Binding, row.OperationId,
                Enum.Parse<FederationProviderKind>(row.Provider, false), link.LinkId, row.ExpectedLinkVersion, link.Version,
                Enum.Parse<FederationUnlinkSessionEffect>(row.SessionEffect, false), row.PolicyVersion, row.SessionUsePolicyVersion,
                row.SessionEffectEvidenceReference, decision!.PolicyVersion, decision.EvidenceReference, decision.EffectiveInterval), ct).ConfigureAwait(false);
        }
        var validity = new FederationLinkMutationValidity(this, database.Database.CurrentTransaction!.TransactionId,
            row, link, attempt, session, proof, facts, request, decision, receipt is not null);
        if (!validity.CurrentAt(database, caller, context.Binding, clock.GetUtcNow())) return UserStepUpOperationApplyOutcome.Conflict();
        return (receipt is null ? UserStepUpOperationApplyOutcome.SessionContinues() : UserStepUpOperationApplyOutcome.SessionEnded(receipt)).ForFederation(validity);
    }

    internal async Task<FederationLinkOperationResult> ReadAsync(StepUpCaller caller, Guid id, Guid requestId, CancellationToken ct)
    {
        Clean(); await using var tx = await database.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        var row = await Locked(caller, id, requestId, ct).ConfigureAwait(false);
        var attempt = row?.AttemptId is { } attemptId ? await LockedAttempt(attemptId, ct).ConfigureAwait(false) : null;
        return await Finish(tx, row is null || row.RecoveryExpiresAt <= clock.GetUtcNow() ? new("NOT_FOUND_OR_NOT_DISCLOSABLE") : new(null, row, attempt), ct).ConfigureAwait(false);
    }
    internal async Task<FederationLinkOperationResult> ReadOriginalByRequestAsync(StepUpCaller caller, Guid requestId, CancellationToken ct)
    {
        Clean(); if (requestId == Guid.Empty) return new("VALIDATION_ERROR");
        var id = await database.Set<FederationLinkOperationRow>().AsNoTracking().Where(x => x.AccountId == caller.AccountId &&
            x.SessionId == caller.SessionId && x.RequestId == requestId).Select(x => (Guid?)x.OperationId).SingleOrDefaultAsync(ct).ConfigureAwait(false);
        return id is { } operationId ? await ReadAsync(caller, operationId, requestId, ct).ConfigureAwait(false) : new("NOT_FOUND_OR_NOT_DISCLOSABLE");
    }
    internal async Task<FederationLinkOperationView> ViewAsync(StepUpCaller caller, FederationLinkOperationRow row, CancellationToken ct)
    {
        if (!Actor(row, caller)) throw new ArgumentException("The original owner must match.");
        var attempt = row.AttemptId is { } id ? await database.Set<FederationAttemptRow>().AsNoTracking().SingleOrDefaultAsync(x => x.AttemptId == id, ct).ConfigureAwait(false) : null;
        var accountVersion = await database.Accounts.AsNoTracking().Where(x => x.AccountId == caller.AccountId).Select(x => x.Version).SingleAsync(ct).ConfigureAwait(false);
        var target = row.TargetLinkId is { } targetId ? await database.ExternalIdentityLinks.AsNoTracking().SingleOrDefaultAsync(x => x.LinkId == targetId, ct).ConfigureAwait(false) : null;
        var at = clock.GetUtcNow(); var current = false;
        try { current = Current(row, caller, at) && accountVersion == row.ExpectedAccountVersion &&
            (row.TargetLinkId is null || target is not null && target.AccountId == caller.AccountId && target.Version == row.ExpectedLinkVersion &&
                target.LinkState == (row.Kind == "LINK" ? "UNLINKED" : "ACTIVE")); }
        catch (InvalidOperationException) { /* An unavailable current policy offers no mutation capability. */ }
        var verified = row.Kind == "LINK" && LinkEvidence(row, attempt, caller, at);
        return new(row.OperationId, row.RequestId, row.Kind, row.Provider, row.EndedAt is null && row.ExpiresAt <= at ? "EXPIRED" : row.State,
            row.Version, row.TargetLinkId, row.ExpectedLinkVersion, row.ExpectedAccountVersion, row.ResultLinkId, row.ResultLinkVersion,
            row.ErrorCode, verified, current && row.ConfirmedAt is null && (row.Kind == "UNLINK" || verified),
            current && row.State == "PENDING_PROOF" && (row.Kind == "UNLINK" || verified), row.EndedAt is null,
            row.Kind == "UNLINK" ? row.SessionEffect : null, row.Kind != "UNLINK" ? null :
                row.SessionEffect == nameof(FederationUnlinkSessionEffect.PreserveExistingSessions) ? "federation.unlink.impact.preserve" : "federation.unlink.impact.revokeLinked");
    }
    internal bool CanRestoreOriginal(FederationLinkOperationRow row, FederationAttemptRow? attempt, StepUpCaller caller, FederationCurrentBrowser browser, DateTimeOffset at)
    {
        var account = database.ChangeTracker.Entries<AccountRow>().Select(x => x.Entity).SingleOrDefault(x => x.AccountId == caller.AccountId);
        var session = database.ChangeTracker.Entries<AuthenticationSessionRow>().Select(x => x.Entity).SingleOrDefault(x => x.SessionId == caller.SessionId);
        if (!Actor(row, caller) || row.OriginalCookieDigest is null || row.OriginalCookieDigest != browser.ActualCookieDigest ||
            account?.Version != row.ExpectedAccountVersion || session?.State != "ACTIVE" || session.SecurityStampDigest != row.SecurityStampDigest ||
            row.RecoveryExpiresAt <= at || !Current(row, caller, at)) return false;
        return row.Kind == "UNLINK" || attempt is not null && attempt.OwnerAccountId == caller.AccountId && attempt.OwnerSessionId == caller.SessionId &&
            attempt.PreviousCookieDigest == browser.ActualCookieDigest && attempt.ProfileSnapshot == row.ProfileSnapshot && attempt.ExpiresAt > at;
    }
    internal async Task<FederationLinkOperationResult> CancelAsync(StepUpCaller caller, Guid id, Guid requestId, CancellationToken ct)
    {
        Clean(); await using var tx = await database.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        var row = await Locked(caller, id, requestId, ct).ConfigureAwait(false);
        if (row is null) return await Finish(tx, new("NOT_FOUND_OR_NOT_DISCLOSABLE"), ct).ConfigureAwait(false);
        if (row.EndedAt is not null) return await Finish(tx, new(null, row), ct).ConfigureAwait(false);
        var attempt = row.AttemptId is { } attemptId ? await LockedAttempt(attemptId, ct).ConfigureAwait(false) : null;
        var now = clock.GetUtcNow(); row.State = now >= row.ExpiresAt ? "EXPIRED" : "REJECTED"; row.ErrorCode = "AUTHENTICATION_FAILED";
        row.EndedAt = now; row.Version++;
        if (attempt is not null && attempt.State is ("PREPARED" or "CHALLENGE_ADMITTED" or "VERIFIED"))
        { attempt.State = row.State; attempt.ErrorCode = row.ErrorCode; attempt.EndedAt = now; attempt.ResultExpiresAt = now + TimeSpan.FromTicks(attempt.ResultRecoveryLifetimeTicks); attempt.Version++; }
        Event(row, "ExternalIdentityOperationCancelled", now); return await Finish(tx, new(null, row, attempt), ct).ConfigureAwait(false);
    }

    private async Task<FederationLinkOperationRow?> Locked(StepUpCaller caller, Guid id, Guid requestId, CancellationToken ct)
    {
        var snapshot = await database.Set<FederationLinkOperationRow>().AsNoTracking().SingleOrDefaultAsync(x => x.OperationId == id && x.RequestId == requestId, ct).ConfigureAwait(false);
        if (snapshot is null || !Actor(snapshot, caller) || await authentication.LockForOperationAsync(caller, Operation(snapshot).Scope.Operation, ct).ConfigureAwait(false) is null) return null;
        await LockOperation(id, ct).ConfigureAwait(false);
        var row = await database.Set<FederationLinkOperationRow>().SingleAsync(x => x.OperationId == id, ct).ConfigureAwait(false);
        await database.Entry(row).ReloadAsync(ct).ConfigureAwait(false); return Actor(row, caller) ? row : null;
    }
    private async Task<FederationAttemptRow?> LockedAttempt(Guid id, CancellationToken ct)
    {
        await database.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({"federation-attempt:" + id.ToString("N")},0))", ct).ConfigureAwait(false);
        var row = await database.Set<FederationAttemptRow>().SingleOrDefaultAsync(x => x.AttemptId == id, ct).ConfigureAwait(false);
        if (row is not null) await database.Entry(row).ReloadAsync(ct).ConfigureAwait(false); return row;
    }
    private static bool LinkEvidence(FederationLinkOperationRow row, FederationAttemptRow? attempt, StepUpCaller caller, DateTimeOffset at) => attempt is not null &&
        attempt.AttemptId == row.AttemptId && attempt.Purpose == nameof(FederationAttemptPurpose.Link) && attempt.State == "VERIFIED" &&
        attempt.OwnerAccountId == caller.AccountId && attempt.OwnerSessionId == caller.SessionId && attempt.Origin == caller.Origin &&
        attempt.OwnerAccountVersion == row.ExpectedAccountVersion && attempt.OwnerSecurityStampDigest == row.SecurityStampDigest &&
        attempt.OwnerOperationId == Operation(row).OperationId && attempt.OwnerContextDigest == row.ContextDigest &&
        attempt.ProfileSnapshot == row.ProfileSnapshot && attempt.VerifiedAt <= at && attempt.VerificationExpiresAt > at && attempt.ExpiresAt > at &&
        attempt.ProtectedLinkSubject is not null;
    private FederationLinkManagementPolicyVersion Policy(string origin, DateTimeOffset at) { var value = policies.RequireCurrent(origin, at); value.Require(origin, at); return value; }
    private FederationProfileVersion Profile(FederationProviderKind kind, string origin, DateTimeOffset at) { var value = profiles.RequireCurrent(kind, at); value.Require(origin, at); if (value.Values.Provider != kind) throw new InvalidOperationException("Provider profile scope changed."); return value; }
    private bool Current(FederationLinkOperationRow row, StepUpCaller caller, DateTimeOffset at) => Actor(row, caller) && row.CreatedAt <= at && at < row.ExpiresAt &&
        row.PolicySnapshot == Policy(caller.Origin, at).Snapshot && row.ProfileSnapshot == Profile(Enum.Parse<FederationProviderKind>(row.Provider, false), caller.Origin, at).Snapshot &&
        row.AuthenticationPolicyVersion == authenticationPolicies.RequireEffective(at).Version && row.ContextDigest == Context(row);
    internal static bool Qualified(ExternalIdentityLinkRow link, FederationProfileVersion profile) => link.LinkState is "ACTIVE" or "UNLINKED" &&
        link.Provider == profile.Values.StableNamespaceId && link.QualifiedClientId == profile.Values.ClientId &&
        link.IssuerMappingEvidenceReference == profile.Values.IssuerMappingEvidenceReference && !string.IsNullOrWhiteSpace(link.QualificationProfileVersion) && !string.IsNullOrWhiteSpace(link.QualificationEvidenceReference);
    private static bool Actor(FederationLinkOperationRow row, StepUpCaller caller) => row.AccountId == caller.AccountId && row.SessionId == caller.SessionId && row.Origin == caller.Origin;
    private static string Context(FederationLinkOperationRow row) => AuthenticationIdentity.Digest(JsonSerializer.Serialize(new { row.OperationId, row.RequestId, row.Kind,
        row.AccountId, row.SessionId, row.Origin, row.Provider, row.ProfileSnapshot, row.AttemptId, row.TargetLinkId, row.ExpectedLinkVersion, row.ExpectedAccountVersion,
        row.PolicySnapshot, row.AssurancePolicyVersion, row.AuthenticationPolicyVersion, row.SecurityStampDigest, row.OriginalCookieDigest, row.SessionEffect, row.SessionUsePolicyVersion, row.SessionEffectEvidenceReference }));
    private FederationUnlinkPostureDecision Decision(FederationUnlinkPostureRequest request, DateTimeOffset at)
    {
        var value = postures.RequireCurrent(request, at);
        if (value.AccountId != request.AccountId || value.AccountVersion != request.AccountVersion || value.LinkId != request.LinkId || value.LinkVersion != request.LinkVersion ||
            value.FactsDigest != request.FactsDigest || string.IsNullOrWhiteSpace(value.PolicyVersion) || string.IsNullOrWhiteSpace(value.EvidenceReference) || value.EffectiveInterval is null || !value.EffectiveInterval.Contains(at))
            throw new InvalidOperationException("Current remaining authentication/recovery authority is unavailable.");
        return value;
    }
    private static bool SameDecision(FederationUnlinkPostureDecision original, FederationUnlinkPostureDecision current, DateTimeOffset at) =>
        original.Allowed && current.Allowed && original.AccountId == current.AccountId && original.AccountVersion == current.AccountVersion &&
        original.LinkId == current.LinkId && original.LinkVersion == current.LinkVersion && original.FactsDigest == current.FactsDigest &&
        original.PolicyVersion == current.PolicyVersion && original.EvidenceReference == current.EvidenceReference &&
        original.EffectiveInterval.Contains(at) && current.EffectiveInterval.Contains(at);
    private Task<int> LockOperation(Guid id, CancellationToken ct) => database.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({"federation-management:" + id.ToString("N")},0))", ct);
    private void Clean() { if (database.Database.CurrentTransaction is not null || database.ChangeTracker.HasChanges()) throw new InvalidOperationException("Federation management requires a clean owner boundary."); }
    private async Task<FederationLinkOperationResult> Finish(IDbContextTransaction tx, FederationLinkOperationResult result, CancellationToken ct)
    { await database.SaveChangesAsync(ct).ConfigureAwait(false); await tx.CommitAsync(ct).ConfigureAwait(false); return result; }
    private static DateTimeOffset Min(DateTimeOffset a, DateTimeOffset b) => a < b ? a : b;
    private void Event(FederationLinkOperationRow row, string type, DateTimeOffset at) => database.Add(new OutboxMessage(Guid.NewGuid(), "1.0", type, "1", at,
        "tunner/identity", (row.ResultLinkId ?? row.TargetLinkId ?? row.OperationId).ToString("N"), "RESTRICTED_SECURITY",
        JsonSerializer.Serialize(new { reference = row.OperationId, linkReference = row.ResultLinkId ?? row.TargetLinkId, operation = Operation(row).Scope.Operation }),
        row.Version.ToString(CultureInfo.InvariantCulture), Operation(row).OperationId, row.OperationId.ToString("N")));

    internal async Task<FederationManagementView?> SettingsAsync(StepUpCaller caller, IReadOnlyCollection<FederationProviderKind> enabledTransports, CancellationToken ct)
    {
        Clean(); await using var tx = await database.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        if (await authentication.LockForOperationAsync(caller, "LIST_EXTERNAL_IDENTITIES", ct).ConfigureAwait(false) is null) return null;
        var account = await database.Accounts.SingleAsync(x => x.AccountId == caller.AccountId, ct).ConfigureAwait(false);
        var facts = await ReadPostureFacts(caller.AccountId, null, ct).ConfigureAwait(false);
        var links = new List<FederationManagedLinkView>(); var available = new HashSet<FederationProviderKind>();
        foreach (var kind in Enum.GetValues<FederationProviderKind>())
        {
            FederationProfileVersion profile;
            try { profile = Profile(kind, caller.Origin, clock.GetUtcNow()); _ = Policy(caller.Origin, clock.GetUtcNow()); }
            catch (InvalidOperationException) { continue; }
            if (enabledTransports.Contains(kind)) available.Add(kind);
            foreach (var link in facts.Links.Where(link => Qualified(link, profile)))
            {
                var linkedAt = await database.Set<FederationLinkOperationRow>().Where(x => x.AccountId == caller.AccountId && x.Kind == "LINK" &&
                    x.State == "COMPLETED" && x.ResultLinkId == link.LinkId).OrderByDescending(x => x.EndedAt).Select(x => x.EndedAt).FirstOrDefaultAsync(ct).ConfigureAwait(false);
                links.Add(new(link.LinkId, link.Version, kind, link.LinkState == "ACTIVE" ? FederationManagedLinkState.Active : FederationManagedLinkState.Unlinked, linkedAt, false));
            }
        }
        // Queries above may await. Re-resolve advisory capabilities at the last read frontier too.
        var finalAt = clock.GetUtcNow();
        foreach (var kind in Enum.GetValues<FederationProviderKind>())
        {
            try
            {
                var current = Profile(kind, caller.Origin, finalAt); _ = Policy(caller.Origin, finalAt);
                links.RemoveAll(view => view.Provider == kind && !Qualified(facts.Links.Single(link => link.LinkId == view.LinkReference), current));
            }
            catch (InvalidOperationException) { available.Remove(kind); links.RemoveAll(link => link.Provider == kind); }
        }
        for (var index = 0; index < links.Count; index++)
        {
            var view = links[index]; var link = facts.Links.Single(x => x.LinkId == view.LinkReference); var canUnlink = false;
            if (link.LinkState == "ACTIVE")
            {
                try { canUnlink = Decision(facts.Request(account, link), finalAt).Allowed; }
                catch (InvalidOperationException) { /* Fail closed for this advisory capability. */ }
            }
            links[index] = view with { CanStartUnlink = canUnlink };
        }
        var result = new FederationManagementView(account.Version, available.Contains(FederationProviderKind.Google),
            available.Contains(FederationProviderKind.Microsoft), available.Count != 0, Array.AsReadOnly(links.OrderBy(x => x.LinkReference).ToArray()));
        await database.SaveChangesAsync(ct).ConfigureAwait(false); await tx.CommitAsync(ct).ConfigureAwait(false); return result;
    }

    internal sealed class PostureFacts(IdentityUserAdapter user, VerifiedContactRow[] contacts, PasskeyCredentialRow[] passkeys,
        TotpAuthenticatorRow[] totp, ExternalIdentityLinkRow[] links, RecoveryCodeSetRow[] sets, RecoveryCodeEntryRow[] entries)
    {
        internal IdentityUserAdapter User { get; } = user;
        internal ExternalIdentityLinkRow[] Links { get; } = links;
        internal FederationUnlinkPostureRequest Request(AccountRow account, ExternalIdentityLinkRow target)
        {
            var paths = contacts.Select(x => new FederationAuthenticationPath(FederationAuthenticationPathKind.VerifiedContact, x.ContactId.ToString("N"),
                x.AnonymizedAtUtc is null ? x.State.ToString() : "ANONYMIZED", x.Version, 0, string.Empty, x.EvidenceReference, null))
                .Concat(passkeys.Select(x => new FederationAuthenticationPath(FederationAuthenticationPathKind.Passkey, x.CredentialReference.ToString("N"),
                    x.State, x.Version, x.UsageVersion, x.PolicyVersion, x.CredentialDigest, null)))
                .Concat(totp.Select(x => new FederationAuthenticationPath(FederationAuthenticationPathKind.Totp, x.AuthenticatorReference.ToString("N"),
                    x.State, x.Version, x.UsageVersion, x.PolicyVersion, x.Generation.ToString("N"), null)))
                .Concat(Links.Where(x => x.LinkId != target.LinkId).Select(x => new FederationAuthenticationPath(FederationAuthenticationPathKind.Federation,
                    x.LinkId.ToString("N"), x.LinkState, x.Version, 0, x.QualificationProfileVersion ?? string.Empty,
                    AuthenticationIdentity.Digest(JsonSerializer.Serialize(new { x.Provider, x.QualifiedClientId, x.IssuerMappingEvidenceReference, x.QualificationEvidenceReference })), null)))
                .Concat(sets.Select(x => new FederationAuthenticationPath(FederationAuthenticationPathKind.RecoveryCodeSet, x.SetReference.ToString("N"),
                    x.State, x.LifecycleVersion, x.UsageVersion, x.PolicyVersion, x.CryptoProfileVersion, entries.Count(e => e.SetReference == x.SetReference && e.ConsumedAt is null))));
            return new(account.AccountId, account.Version, target.LinkId, target.Version == 0 ? 0 : target.Version,
                User.EmailConfirmed && User.PasswordHash is not null, AuthenticationIdentity.Digest(User.SecurityStamp!), paths);
        }
    }
    private async Task<PostureFacts> ReadPostureFacts(string account, Guid? target, CancellationToken ct)
    {
        _ = target; // Target is excluded only when constructing a request, so one complete locked snapshot serves settings collections too.
        var user = await database.Users.SingleAsync(x => x.AccountId == account, ct).ConfigureAwait(false);
        var contacts = await database.VerifiedContacts.FromSqlInterpolated($"SELECT * FROM accounts.verified_contacts WHERE account_id={account} ORDER BY contact_id FOR SHARE").AsNoTracking().ToArrayAsync(ct).ConfigureAwait(false);
        var passkeys = await database.Set<PasskeyCredentialRow>().FromSqlInterpolated($"SELECT * FROM identity.passkey_credentials WHERE account_id={account} ORDER BY credential_reference FOR SHARE").AsNoTracking().ToArrayAsync(ct).ConfigureAwait(false);
        var totp = await database.Set<TotpAuthenticatorRow>().FromSqlInterpolated($"SELECT * FROM identity.totp_authenticators WHERE account_id={account} ORDER BY authenticator_reference FOR SHARE").AsNoTracking().ToArrayAsync(ct).ConfigureAwait(false);
        var links = await database.ExternalIdentityLinks.FromSqlInterpolated($"SELECT * FROM identity.external_identity_links WHERE account_id={account} ORDER BY link_id FOR SHARE").AsNoTracking().ToArrayAsync(ct).ConfigureAwait(false);
        var sets = await database.Set<RecoveryCodeSetRow>().FromSqlInterpolated($"SELECT * FROM identity.recovery_code_sets WHERE account_id={account} ORDER BY set_reference FOR SHARE").AsNoTracking().ToArrayAsync(ct).ConfigureAwait(false);
        var entries = await database.Set<RecoveryCodeEntryRow>().FromSqlInterpolated($"SELECT * FROM identity.recovery_code_entries WHERE account_id={account} ORDER BY entry_reference FOR SHARE").AsNoTracking().ToArrayAsync(ct).ConfigureAwait(false);
        return new(user, contacts, passkeys, totp, links, sets, entries);
    }

    internal bool MutationCurrentAt(FederationLinkMutationValidity value, IdentityPersistenceDbContext db, StepUpCaller caller, StepUpOperation operation, DateTimeOffset at)
    {
        if (!ReferenceEquals(db, database) || db.Database.CurrentTransaction?.TransactionId != value.TransactionId || !Actor(value.Row, caller) ||
            value.Row.State != "COMPLETED" || value.Row.Version != value.OperationVersion || value.Row.ProofReference != value.Proof.ProofId ||
            value.Row.ResultLinkId != value.Link.LinkId || value.Row.ResultLinkVersion != value.LinkVersion || value.Link.Version != value.LinkVersion ||
            value.Row.EndedAt is null || value.Row.EndedAt > at || value.Row.ConfirmedAt is null || value.Row.ConfirmedAt > value.Row.EndedAt ||
            Operation(value.Row).OperationId != operation.OperationId || value.Row.ContextDigest != operation.ContextDigest || !Current(value.Row, caller, at) ||
            value.Proof.State != "ACTIVE" || value.Proof.AccountId != caller.AccountId || value.Proof.SessionId != caller.SessionId ||
            value.Proof.ContextDigest != operation.ContextDigest || value.Proof.OperationId != operation.OperationId ||
            value.Session.AccountId != caller.AccountId || value.Session.SessionId != caller.SessionId || value.Session.Origin != caller.Origin ||
            value.Session.SecurityStampDigest != value.Row.SecurityStampDigest || value.Session.State != (value.ActorEnded ? "REVOKED" : "ACTIVE") || value.Link.AccountId != caller.AccountId ||
            value.Link.LinkState != (value.Row.Kind == "LINK" ? "ACTIVE" : "UNLINKED")) return false;
        var account = db.ChangeTracker.Entries<AccountRow>().Select(x => x.Entity).SingleOrDefault(x => x.AccountId == caller.AccountId);
        if (account is null || account.Version != value.Row.ExpectedAccountVersion || !Qualified(value.Link, Profile(Enum.Parse<FederationProviderKind>(value.Row.Provider, false), caller.Origin, at))) return false;
        if (value.Row.Kind == "LINK")
        {
            var attempt = value.Attempt;
            if (attempt is null || attempt.State != "COMPLETED" || attempt.OwnerAccountId != caller.AccountId || attempt.OwnerSessionId != caller.SessionId ||
                attempt.OwnerContextDigest != operation.ContextDigest || attempt.ProfileSnapshot != value.Row.ProfileSnapshot ||
                attempt.VerifiedAt > at || attempt.VerificationExpiresAt <= at || attempt.ExpiresAt <= at) return false;
            var key = attempts.ReadLinkIdentity(attempt, Profile(Enum.Parse<FederationProviderKind>(value.Row.Provider, false), caller.Origin, at));
            return key.ProviderNamespace == value.Link.Provider && key.Subject == value.Link.Subject;
        }
        if (value.Facts is null || value.PostureRequest is null || value.PostureDecision is null) return false;
        // Target's own version advances once; the request retains its original version and all remaining facts.
        var currentFacts = value.Facts.Request(account, value.Link);
        var originalVersionFacts = new FederationUnlinkPostureRequest(currentFacts.AccountId, currentFacts.AccountVersion,
            currentFacts.LinkId, value.Row.ExpectedLinkVersion, currentFacts.PasswordAvailable, currentFacts.SecurityStampDigest, currentFacts.Paths);
        return originalVersionFacts.FactsDigest == value.PostureRequest.FactsDigest &&
            SameDecision(value.PostureDecision, Decision(value.PostureRequest, at), at) && value.Row.PostureFactsDigest == value.PostureRequest.FactsDigest &&
            value.Row.PosturePolicyVersion == value.PostureDecision.PolicyVersion && value.Row.PostureEvidenceReference == value.PostureDecision.EvidenceReference &&
            value.Row.PostureFrom == IdentityPersistenceTime.Normalize(value.PostureDecision.EffectiveInterval.From) &&
            value.Row.PostureIncludesFrom == value.PostureDecision.EffectiveInterval.IncludesFrom &&
            value.Row.PostureUntil == (value.PostureDecision.EffectiveInterval.Until is { } until ? IdentityPersistenceTime.Normalize(until) : (DateTimeOffset?)null) &&
            value.Row.PostureIncludesUntil == value.PostureDecision.EffectiveInterval.IncludesUntil;
    }
}

/// <summary>Fixed transaction-local management witness consumed only by shared StepUp's final frontier.</summary>
internal sealed class FederationLinkMutationValidity
{
    private readonly FederationLinkApplication application;
    internal FederationLinkMutationValidity(FederationLinkApplication application, Guid transactionId,
        FederationLinkOperationRow row, ExternalIdentityLinkRow link, FederationAttemptRow? attempt,
        AuthenticationSessionRow session, StepUpProofRow proof, FederationLinkApplication.PostureFacts? facts,
        FederationUnlinkPostureRequest? postureRequest, FederationUnlinkPostureDecision? postureDecision, bool actorEnded)
    {
        this.application = application; TransactionId = transactionId; Row = row; Link = link;
        Attempt = attempt; Session = session; Proof = proof; Facts = facts; PostureRequest = postureRequest; PostureDecision = postureDecision;
        OperationVersion = row.Version; LinkVersion = link.Version;
        ActorEnded = actorEnded;
    }
    internal Guid TransactionId { get; }
    internal FederationLinkOperationRow Row { get; }
    internal ExternalIdentityLinkRow Link { get; }
    internal FederationAttemptRow? Attempt { get; }
    internal AuthenticationSessionRow Session { get; }
    internal StepUpProofRow Proof { get; }
    internal FederationLinkApplication.PostureFacts? Facts { get; }
    internal FederationUnlinkPostureRequest? PostureRequest { get; }
    internal FederationUnlinkPostureDecision? PostureDecision { get; }
    internal long OperationVersion { get; }
    internal long LinkVersion { get; }
    internal bool ActorEnded { get; }
    internal bool CurrentAt(IdentityPersistenceDbContext database, StepUpCaller caller, StepUpOperation operation, DateTimeOffset at) =>
        application.MutationCurrentAt(this, database, caller, operation, at);
    public override string ToString() => nameof(FederationLinkMutationValidity);
}
