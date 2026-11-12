using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace Tunner.Identity.Persistence;

internal sealed partial class FederationAttemptApplication
{
    private static bool ProofOwnerMatches(UserStepUpOperationContext context, StepUpCaller caller, bool allowCompleted = false) =>
        (allowCompleted || !context.Completed) && context.Actor.AccountId == caller.AccountId && context.Actor.SessionId == caller.SessionId && context.Actor.Origin == caller.Origin;

    private static bool ProofBindingMatches(FederationAttemptRow row, UserStepUpOperationContext context, StepUpCaller caller, bool allowCompleted = false) =>
        ProofOwnerMatches(context, caller, allowCompleted) && row.OwnerAccountId == caller.AccountId && row.OwnerSessionId == caller.SessionId &&
        row.OwnerOperationId == context.Binding.OperationId && row.CorrelationId == context.Binding.CorrelationId &&
        row.OwnerContextDigest == context.Binding.ContextDigest && row.OwnerExpectedVersion == context.Binding.ExpectedVersion &&
        row.OwnerAssurancePolicyVersion == context.ExpectedPolicyVersion;

    private bool OperationProofPendingCurrent(FederationAttemptRow row, FederationCurrentBrowser browser,
        FederationProfileVersion profile, DateTimeOffset at, StepUpCaller? caller)
    {
        return caller is not null && row.Purpose == nameof(FederationAttemptPurpose.OperationProof) &&
            row.OwnerAccountId == caller.AccountId && row.OwnerSessionId == caller.SessionId && row.Origin == caller.Origin &&
            row.PreviousSessionId == caller.SessionId && browser.Caller.PreviousSessionId == caller.SessionId &&
            SameBrowser(row, browser) && Original(row, browser) && browser.OriginalFingerprint && browser.BrowserExpiresAt > at &&
            row.CreatedAt <= at && row.ExpiresAt > at && row.ProfileSnapshot == profile.Snapshot &&
            row.AuthenticationPolicyVersion == authenticationPolicies.RequireEffective(at).Version &&
            profile.Values.FreshAuthentication is { } freshness && row.ContinuationPath == freshness.ContinuationPath &&
            profile.Values.AllowedContinuations.Contains(row.ContinuationPath, StringComparer.Ordinal);
    }

    internal async Task<FederationAttemptResult> StartOperationProofAsync(FederationCurrentBrowser browser, StepUpCaller caller,
        UserStepUpOperationContext context, Guid requestId, FederationProviderKind provider, Guid selectedLinkId,
        IUserStepUpOperationSource owner, IAssurancePolicySource assurances, CancellationToken ct)
    {
        Clean(); browser.Caller.Validate();
        if (requestId == Guid.Empty || selectedLinkId == Guid.Empty || !ProofOwnerMatches(context, caller) ||
            caller.SessionId != browser.Caller.PreviousSessionId || !browser.OriginalFingerprint)
            return new(null, "VERSION_CONFLICT");
        await using var transaction = await database.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        await LockBrowser(browser.Caller.BrowserDigest, ct).ConfigureAwait(false);
        var prior = await database.Set<FederationAttemptRow>().SingleOrDefaultAsync(value =>
            value.BrowserDigest == browser.Caller.BrowserDigest && value.RequestId == requestId, ct).ConfigureAwait(false);
        if (prior is not null)
            return await Finish(transaction, new(prior, prior.Purpose == nameof(FederationAttemptPurpose.OperationProof) &&
                prior.Provider == provider.ToString() && prior.SelectedProofLinkId == selectedLinkId &&
                ProofBindingMatches(prior, context, caller) && Original(prior, browser) ? null : "VERSION_CONFLICT"), ct).ConfigureAwait(false);
        var session = await authentication.LockForOperationAsync(caller, context.Binding.Scope.Operation, ct).ConfigureAwait(false);
        var currentOwner = await owner.ResolveAsync(new(caller), ct).ConfigureAwait(false);
        if (session is null || currentOwner is null || !ProofOwnerMatches(currentOwner, caller) ||
            currentOwner.Binding.OperationId != context.Binding.OperationId || currentOwner.Binding.ContextDigest != context.Binding.ContextDigest ||
            currentOwner.ExpectedPolicyVersion != context.ExpectedPolicyVersion)
            return await Finish(transaction, new(null, "VERSION_CONFLICT"), ct).ConfigureAwait(false);
        var link = await database.ExternalIdentityLinks.FromSqlInterpolated(
            $"SELECT * FROM identity.external_identity_links WHERE link_id={selectedLinkId} AND account_id={caller.AccountId} FOR NO KEY UPDATE")
            .SingleOrDefaultAsync(ct).ConfigureAwait(false);
        if (link is not null) await database.Entry(link).ReloadAsync(ct).ConfigureAwait(false);
        var account = await database.Accounts.SingleAsync(value => value.AccountId == caller.AccountId, ct).ConfigureAwait(false);
        var now = clock.GetUtcNow(); var profile = Current(provider, caller.Origin, now);
        var auth = authenticationPolicies.RequireEffective(now); auth.Validate();
        if (link is null || !Qualified(link, profile) || profile.Values.FreshAuthentication is not { } freshness ||
            browser.AuthenticationPolicyVersion != auth.Version || browser.BrowserExpiresAt <= now)
            return await Finish(transaction, new(null, "STEP_UP_REQUIRED"), ct).ConfigureAwait(false);
        var assurance = assurances.RequireEffective(context.Binding.Scope, now, context.ExpectedPolicyVersion);
        if (!assurance.MethodAssurances.TryGetValue("federation:" + provider, out var mapped) || mapped != assurance.RequiredAssurance ||
            !freshness.QualifiedMethods.Any(value => value.Assurance == mapped))
            return await Finish(transaction, new(null, "STEP_UP_REQUIRED"), ct).ConfigureAwait(false);
        if (!await authentication.AllowAsync("federation:N:" + browser.Caller.NetworkDigest, auth.NetworkAttemptLimit, auth, now, ct).ConfigureAwait(false) ||
            await database.Set<FederationAttemptRow>().CountAsync(value => value.BrowserDigest == browser.Caller.BrowserDigest &&
                (value.State == "PREPARED" || value.State == "CHALLENGE_ADMITTED" || value.State == "VERIFIED") && value.ExpiresAt > now, ct).ConfigureAwait(false) >= profile.Values.MaximumPendingAttempts)
            return await Finish(transaction, new(null, "RATE_OR_ABUSE_LIMITED"), ct).ConfigureAwait(false);
        now = clock.GetUtcNow();
        if (Current(provider, caller.Origin, now).Snapshot != profile.Snapshot || authenticationPolicies.RequireEffective(now).Version != auth.Version ||
            !assurance.Metadata.EffectiveInterval.Contains(now)) return await Finish(transaction, new(null, "VERSION_CONFLICT"), ct).ConfigureAwait(false);
        var expires = profile.Clip(now + profile.Values.AttemptLifetime);
        foreach (var bound in new[] { browser.BrowserExpiresAt, session.ExpiresAt }) if (bound < expires) expires = bound;
        if (assurance.Metadata.EffectiveInterval.Until is { } until && until < expires) expires = until;
        if (expires <= now) return await Finish(transaction, new(null, "STEP_UP_REQUIRED"), ct).ConfigureAwait(false);
        var row = new FederationAttemptRow
        {
            AttemptId = Guid.NewGuid(), RequestId = requestId, CorrelationId = context.Binding.CorrelationId,
            Provider = provider.ToString(), Purpose = nameof(FederationAttemptPurpose.OperationProof), BrowserKey = browser.BrowserKey,
            BrowserDigest = browser.Caller.BrowserDigest, Origin = caller.Origin, PreviousCookieDigest = browser.OriginalCookieDigest,
            PreviousSessionId = caller.SessionId, AuthenticationPolicyVersion = auth.Version, ProviderNamespace = profile.Values.StableNamespaceId,
            ClientId = profile.Values.ClientId, ProfileVersion = profile.Values.Version, MappingEvidenceReference = profile.Values.IssuerMappingEvidenceReference,
            ProfileSnapshot = profile.Snapshot, ContinuationPath = freshness.ContinuationPath, ProcessingReference = profile.Values.ProcessingReference,
            RetentionReference = profile.Values.RetentionReference, ResultRecoveryLifetimeTicks = profile.Values.ResultRecoveryLifetime.Ticks,
            CreatedAt = now, ExpiresAt = expires, OwnerAccountId = caller.AccountId, OwnerSessionId = caller.SessionId,
            OwnerAccountVersion = account.Version, OwnerSessionVersion = session.Version, OwnerSecurityStampDigest = session.SecurityStampDigest,
            OwnerOperationId = context.Binding.OperationId, OwnerContextDigest = context.Binding.ContextDigest,
            OwnerExpectedVersion = context.Binding.ExpectedVersion, OwnerAssurancePolicyVersion = context.ExpectedPolicyVersion,
            SelectedProofLinkId = link.LinkId, SelectedProofLinkVersion = link.Version
        };
        database.Add(row); return await Finish(transaction, new(row), ct).ConfigureAwait(false);
    }

    internal async Task<FederationAttemptResult> ReadOperationProofOriginalAsync(FederationCurrentBrowser browser, StepUpCaller caller,
        UserStepUpOperationContext context, Guid requestId, Guid? attemptId, CancellationToken ct)
    {
        Clean(); browser.Caller.Validate();
        if (requestId == Guid.Empty || !ProofOwnerMatches(context, caller, allowCompleted: true) ||
            browser.Caller.PreviousSessionId != caller.SessionId || browser.Caller.Origin != caller.Origin)
            return new(null, "NOT_FOUND_OR_NOT_DISCLOSABLE");
        await using var transaction = await database.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        await LockBrowser(browser.Caller.BrowserDigest, ct).ConfigureAwait(false);
        // A lost pointer is recovered only by this exact original scoped request, never a latest-attempt query.
        var row = await database.Set<FederationAttemptRow>().SingleOrDefaultAsync(value => value.RequestId == requestId &&
            value.BrowserDigest == browser.Caller.BrowserDigest && value.OwnerSessionId == caller.SessionId && value.OwnerAccountId == caller.AccountId &&
            value.Purpose == nameof(FederationAttemptPurpose.OperationProof) && (!attemptId.HasValue || value.AttemptId == attemptId.Value), ct).ConfigureAwait(false);
        var now = clock.GetUtcNow();
        if (row is null || !ProofBindingMatches(row, context, caller, allowCompleted: true) || row.Origin != caller.Origin ||
            row.PreviousSessionId != caller.SessionId || row.PreviousCookieDigest != browser.ActualCookieDigest ||
            row.AuthenticationPolicyVersion != authenticationPolicies.RequireEffective(now).Version || row.CreatedAt > now)
            return await Finish(transaction, new(null, "NOT_FOUND_OR_NOT_DISCLOSABLE"), ct).ConfigureAwait(false);
        var recoveryUntil = row.ResultExpiresAt ?? row.ExpiresAt + TimeSpan.FromTicks(row.ResultRecoveryLifetimeTicks);
        if (now >= recoveryUntil) return await Finish(transaction, new(null, "NOT_FOUND_OR_NOT_DISCLOSABLE"), ct).ConfigureAwait(false);
        if (now >= row.ExpiresAt && row.State is "PREPARED" or "CHALLENGE_ADMITTED" or "VERIFIED")
            return await Finish(transaction, new(row, "VERSION_CONFLICT", projectedState: "EXPIRED"), ct).ConfigureAwait(false);
        try
        {
            if (Current(ParseProvider(row), row.Origin, now).Snapshot != row.ProfileSnapshot)
                return await Finish(transaction, new(row, "VERSION_CONFLICT", projectedState: "UNKNOWN"), ct).ConfigureAwait(false);
        }
        catch (InvalidOperationException)
        { return await Finish(transaction, new(row, "VERSION_CONFLICT", projectedState: "UNKNOWN"), ct).ConfigureAwait(false); }
        return await Finish(transaction, new(row), ct).ConfigureAwait(false);
    }

    private async Task<FederationCallbackResult> RecordOperationProofEvidenceAsync(FederationProtocolEvidence evidence,
        FederationAttemptRow snapshot, CancellationToken ct)
    {
        await using var transaction = await database.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        await LockBrowser(snapshot.BrowserDigest, ct).ConfigureAwait(false); await LockAttempt(snapshot.AttemptId, ct).ConfigureAwait(false);
        var row = await database.Set<FederationAttemptRow>().SingleAsync(value => value.AttemptId == snapshot.AttemptId, ct).ConfigureAwait(false);
        var now = clock.GetUtcNow(); var profile = Current(evidence.Provider, row.Origin, now);
        if (row.Purpose != nameof(FederationAttemptPurpose.OperationProof) || row.State != "CHALLENGE_ADMITTED")
        { await Finish(transaction, new(row), ct).ConfigureAwait(false); return new(row.ContinuationPath, false); }
        if (row.CreatedAt > now || row.ExpiresAt <= now || row.ProfileSnapshot != profile.Snapshot || evidence.ProfileVersion != row.ProfileVersion ||
            row.Provider != evidence.Provider.ToString() || row.SelectedProofLinkId is null || row.SelectedProofLinkVersion is null ||
            evidence.AuthenticatedAt is null || evidence.AuthenticationMethods is null)
        { await Reject(transaction, row, now, "STEP_UP_REQUIRED", ct).ConfigureAwait(false); return new(row.ContinuationPath, false); }
        var key = profile.Registration.BindExternalIdentity(evidence.Issuer, evidence.ClientId, evidence.Subject);
        var link = await database.ExternalIdentityLinks.AsNoTracking().SingleOrDefaultAsync(value => value.LinkId == row.SelectedProofLinkId &&
            value.AccountId == row.OwnerAccountId && value.Provider == key.ProviderNamespace && value.Subject == key.Subject, ct).ConfigureAwait(false);
        now = clock.GetUtcNow(); profile = Current(evidence.Provider, row.Origin, now);
        var verified = profile.Values.FreshAuthentication?.Bind(evidence.AuthenticatedAt.Value, evidence.AuthenticationMethods, now);
        if (link is null || link.Version != row.SelectedProofLinkVersion || !Qualified(link, profile) || verified is null ||
            profile.Snapshot != row.ProfileSnapshot || row.ExpiresAt <= now)
        { await Reject(transaction, row, now, "STEP_UP_REQUIRED", ct).ConfigureAwait(false); return new(row.ContinuationPath, false); }
        var expiry = profile.Clip(now + profile.Values.VerifiedLifetime);
        if (row.ExpiresAt < expiry) expiry = row.ExpiresAt;
        if (verified.ExpiresAt < expiry) expiry = verified.ExpiresAt;
        if (expiry <= now) { await Reject(transaction, row, now, "STEP_UP_REQUIRED", ct).ConfigureAwait(false); return new(row.ContinuationPath, false); }
        row.State = "VERIFIED"; row.Version++; row.VerifiedAt = now; row.VerificationExpiresAt = expiry;
        row.ValidatedIssuer = evidence.Issuer; row.LinkId = link.LinkId; row.LinkVersion = link.Version;
        row.LinkQualificationEvidenceReference = link.QualificationEvidenceReference; row.LinkQualificationProfileVersion = link.QualificationProfileVersion;
        row.ProviderAuthenticatedAt = verified.AuthenticatedAt; row.ProviderMethodsJson = JsonSerializer.Serialize(verified.Methods);
        await Finish(transaction, new(row), ct).ConfigureAwait(false); return new(row.ContinuationPath, true);
    }

    internal async Task<StepUpIssueResult> CompleteOperationProofAsync(FederationCurrentBrowser browser, StepUpCaller caller,
        UserStepUpOperationContext context, Guid attemptId, IUserStepUpOperationSource owner, StepUpApplication stepUp, CancellationToken ct)
    {
        Clean(); await using var transaction = await database.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        var row = await Owned(browser, attemptId, ct).ConfigureAwait(false);
        if (row is null || !ProofBindingMatches(row, context, caller) || !Original(row, browser) || !browser.OriginalFingerprint)
            return new("VERSION_CONFLICT", null);
        var session = await authentication.LockForOperationAsync(caller, context.Binding.Scope.Operation, ct).ConfigureAwait(false);
        var currentOwner = await owner.ResolveAsync(new(caller), ct).ConfigureAwait(false);
        if (session is null || currentOwner is null || !ProofBindingMatches(row, currentOwner, caller)) return new("STEP_UP_REQUIRED", null);
        var now = clock.GetUtcNow();
        if (row.State == "COMPLETED")
        {
            if (row.ResultExpiresAt <= now || row.ResultProofId is not { } proofId) return new("STEP_UP_REQUIRED", null);
            var original = await stepUp.RecoverProofAsync(caller, context.Binding, proofId, context.ExpectedPolicyVersion, ct).ConfigureAwait(false);
            await Finish(transaction, new(row), ct).ConfigureAwait(false); return original;
        }
        var profile = Current(ParseProvider(row), row.Origin, now);
        if (row.State != "VERIFIED" || row.VerificationExpiresAt <= now || !OperationProofPendingCurrent(row, browser, profile, now, caller) ||
            row.OwnerSecurityStampDigest != session.SecurityStampDigest || row.LinkId is not { } linkId || row.ProviderAuthenticatedAt is not { } authenticatedAt)
            return new("STEP_UP_REQUIRED", null);
        var auth = authenticationPolicies.RequireEffective(now); auth.Validate();
        if (!await authentication.AllowAsync("federation:N:" + browser.Caller.NetworkDigest, auth.NetworkAttemptLimit, auth, now, ct).ConfigureAwait(false))
        {
            await Reject(transaction, row, clock.GetUtcNow(), "RATE_OR_ABUSE_LIMITED", ct).ConfigureAwait(false);
            return new("RATE_OR_ABUSE_LIMITED", null);
        }
        var link = await database.ExternalIdentityLinks.FromSqlInterpolated(
            $"SELECT * FROM identity.external_identity_links WHERE link_id={linkId} AND account_id={caller.AccountId} FOR NO KEY UPDATE")
            .SingleOrDefaultAsync(ct).ConfigureAwait(false);
        if (link is null) return new("STEP_UP_REQUIRED", null);
        await database.Entry(link).ReloadAsync(ct).ConfigureAwait(false);
        await transaction.CreateSavepointAsync("federation_operation_proof", ct).ConfigureAwait(false);
        var source = new FederationOperationProofSource(row, link, true);
        var issued = await stepUp.IssueVerifiedProofAsync(caller, session, context.Binding, context.ExpectedPolicyVersion,
            "federation:" + row.Provider, null, null, null, authenticatedAt, ct, federation: source).ConfigureAwait(false);
        var proof = database.ChangeTracker.Entries<StepUpProofRow>().Select(value => value.Entity).SingleOrDefault(value =>
            value.SessionId == caller.SessionId && value.OperationId == context.Binding.OperationId && value.FederationAttemptId == row.AttemptId);
        if (issued.Grant is null || proof is null) return await RejectProof(issued.ErrorCode).ConfigureAwait(false);
        row.State = "COMPLETED"; row.Version++; row.ResultProofId = proof.ProofId;
        var continuation = await authentication.VerifyContinuedOperationAsync(caller, context.Binding, session, ct).ConfigureAwait(false);
        currentOwner = await owner.ResolveAsync(new(caller), ct).ConfigureAwait(false);
        now = clock.GetUtcNow();
        if (continuation is null || !continuation.SessionUseInterval.Contains(now) || !continuation.OperationInterval.Contains(now) ||
            currentOwner is null || !ProofBindingMatches(row, currentOwner, caller) ||
            !authentication.FederatedOperationProofCurrentAt(proof, new(row, link), context.Binding, now) ||
            authenticationPolicies.RequireEffective(now).Version != row.AuthenticationPolicyVersion)
            return await RejectProof("STEP_UP_REQUIRED").ConfigureAwait(false);
        row.EndedAt = now; row.ResultExpiresAt = now + TimeSpan.FromTicks(row.ResultRecoveryLifetimeTicks);
        await Finish(transaction, new(row), ct).ConfigureAwait(false); return issued;

        async Task<StepUpIssueResult> RejectProof(string? error)
        {
            await transaction.RollbackToSavepointAsync("federation_operation_proof", ct).ConfigureAwait(false);
            database.ChangeTracker.Clear(); row = await database.Set<FederationAttemptRow>().SingleAsync(value => value.AttemptId == attemptId, ct).ConfigureAwait(false);
            await Reject(transaction, row, clock.GetUtcNow(), SafeError(error), ct).ConfigureAwait(false);
            return new(SafeError(error), null);
        }
    }
}
