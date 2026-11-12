using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.AspNetCore.DataProtection;

namespace Tunner.Identity.Persistence;

/// <summary>Identity-local attempt/result owner. Provider network work is complete before RecordVerifiedAsync.</summary>
internal sealed partial class FederationAttemptApplication(IdentityPersistenceDbContext database, IFederationProfileSource profiles,
    IAuthenticationPolicySource authenticationPolicies, AuthenticationApplication authentication, TimeProvider clock, IDataProtectionProvider? protection = null)
{
    private readonly IDataProtector? linkEvidenceProtection = protection?.CreateProtector("Tunner.Identity.Federation.LinkSubject.v1");

    internal Task LockOriginalBrowserAsync(FederationCurrentBrowser browser, CancellationToken ct)
    {
        if (database.Database.CurrentTransaction is null) throw new InvalidOperationException("A current Identity transaction is required.");
        browser.Caller.Validate(); return LockBrowser(browser.Caller.BrowserDigest, ct);
    }

    internal async Task<FederationAttemptRow?> PrepareLinkInTransactionAsync(FederationCurrentBrowser browser, StepUpCaller caller,
        Guid attemptId, Guid requestId, FederationProviderKind provider, StepUpOperation operation, long accountVersion,
        AuthenticationSessionRow session, DateTimeOffset operationExpiresAt, string continuationPath, CancellationToken ct)
    {
        if (database.Database.CurrentTransaction is null || linkEvidenceProtection is null || attemptId == Guid.Empty || requestId == Guid.Empty ||
            !browser.OriginalFingerprint || caller.SessionId != browser.Caller.PreviousSessionId || session.SessionId != caller.SessionId ||
            session.AccountId != caller.AccountId || session.Origin != caller.Origin || session.State != "ACTIVE") return null;
        var now = clock.GetUtcNow(); var profile = Current(provider, caller.Origin, now);
        var auth = authenticationPolicies.RequireEffective(now); auth.Validate();
        if (browser.BrowserExpiresAt <= now || browser.AuthenticationPolicyVersion != auth.Version ||
            await database.Set<FederationAttemptRow>().AnyAsync(x => x.BrowserDigest == browser.Caller.BrowserDigest && x.RequestId == requestId, ct).ConfigureAwait(false)) return null;
        if (await database.Set<FederationAttemptRow>().CountAsync(x => x.BrowserDigest == browser.Caller.BrowserDigest &&
            (x.State == "PREPARED" || x.State == "CHALLENGE_ADMITTED" || x.State == "VERIFIED") && x.ExpiresAt > now, ct).ConfigureAwait(false) >= profile.Values.MaximumPendingAttempts ||
            !await authentication.AllowAsync("federation:N:" + browser.Caller.NetworkDigest, auth.NetworkAttemptLimit, auth, now, ct).ConfigureAwait(false)) return null;
        now = clock.GetUtcNow(); var current = Current(provider, caller.Origin, now);
        if (current.Snapshot != profile.Snapshot || authenticationPolicies.RequireEffective(now).Version != auth.Version ||
            !profile.Values.AllowedContinuations.Contains(continuationPath, StringComparer.Ordinal)) return null;
        var expires = profile.Clip(now + profile.Values.AttemptLifetime);
        foreach (var bound in new[] { operationExpiresAt, browser.BrowserExpiresAt, session.ExpiresAt }) if (bound < expires) expires = bound;
        if (expires <= now) return null;
        var row = new FederationAttemptRow { AttemptId = attemptId, RequestId = requestId, CorrelationId = operation.CorrelationId,
            Provider = provider.ToString(), Purpose = nameof(FederationAttemptPurpose.Link), BrowserKey = browser.BrowserKey,
            BrowserDigest = browser.Caller.BrowserDigest, Origin = caller.Origin, PreviousCookieDigest = browser.OriginalCookieDigest,
            PreviousSessionId = caller.SessionId, AuthenticationPolicyVersion = auth.Version, ProviderNamespace = profile.Values.StableNamespaceId,
            ClientId = profile.Values.ClientId, ProfileVersion = profile.Values.Version, MappingEvidenceReference = profile.Values.IssuerMappingEvidenceReference,
            ProfileSnapshot = profile.Snapshot, ContinuationPath = continuationPath, ProcessingReference = profile.Values.ProcessingReference,
            RetentionReference = profile.Values.RetentionReference, ResultRecoveryLifetimeTicks = profile.Values.ResultRecoveryLifetime.Ticks,
            CreatedAt = now, ExpiresAt = expires, OwnerAccountId = caller.AccountId, OwnerSessionId = caller.SessionId,
            OwnerAccountVersion = accountVersion, OwnerSessionVersion = session.Version, OwnerSecurityStampDigest = session.SecurityStampDigest,
            OwnerOperationId = operation.OperationId, OwnerContextDigest = operation.ContextDigest, OwnerExpectedVersion = operation.ExpectedVersion };
        _ = BoundOwner(row, caller); database.Add(row); return row;
    }

    internal FederationIdentityKey ReadLinkIdentity(FederationAttemptRow row, FederationProfileVersion profile)
    {
        if (row.Purpose != nameof(FederationAttemptPurpose.Link) || row.State is not ("VERIFIED" or "COMPLETED") ||
            row.ProtectedLinkSubject is null || row.ValidatedIssuer is null || linkEvidenceProtection is null)
            throw new InvalidOperationException("Exact verified link evidence is unavailable.");
        var subject = linkEvidenceProtection.CreateProtector(row.AttemptId.ToString("N")).Unprotect(row.ProtectedLinkSubject);
        return profile.Registration.BindExternalIdentity(row.ValidatedIssuer, row.ClientId, subject);
    }

    internal async Task<bool> OriginalPointerIsTerminalAsync(Guid id, Guid browserKey, string browserDigest, string origin, CancellationToken ct)
    {
        var row = await database.Set<FederationAttemptRow>().AsNoTracking().SingleOrDefaultAsync(x => x.AttemptId == id &&
            x.BrowserKey == browserKey && x.BrowserDigest == browserDigest && x.Origin == origin, ct).ConfigureAwait(false);
        return row is not null && (row.State is "COMPLETED" or "REJECTED" or "EXPIRED" || row.ExpiresAt <= clock.GetUtcNow());
    }
    internal async Task<FederationAttemptResult> ReadManagementOriginalAsync(FederationCurrentBrowser browser, StepUpCaller caller, Guid id, CancellationToken ct)
    {
        Clean(); await using var tx = await database.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        var row = await Owned(browser, id, ct).ConfigureAwait(false);
        if (row is null || row.Purpose is not (nameof(FederationAttemptPurpose.Link) or nameof(FederationAttemptPurpose.OperationProof)) ||
            row.OwnerAccountId != caller.AccountId || row.OwnerSessionId != caller.SessionId || !Original(row, browser) || !browser.OriginalFingerprint)
            return await Finish(tx, new(null, "NOT_FOUND_OR_NOT_DISCLOSABLE"), ct).ConfigureAwait(false);
        return await Finish(tx, new(row, row.ErrorCode), ct).ConfigureAwait(false);
    }
    internal async Task<FederationAttemptResult> StartAsync(FederationCurrentBrowser browser, Guid requestId,
        FederationProviderKind provider, FederationAttemptPurpose purpose, bool rememberMe, CancellationToken ct)
    {
        Clean(); var now = clock.GetUtcNow();
        if (requestId == Guid.Empty || purpose != FederationAttemptPurpose.SignIn || !browser.OriginalFingerprint)
            return new(null, "VERSION_CONFLICT");
        var profile = Current(provider, browser.Caller.Origin, now);
        var auth = authenticationPolicies.RequireEffective(now); auth.Validate();
        if (browser.BrowserExpiresAt <= now || browser.AuthenticationPolicyVersion != auth.Version) return new(null, "VERSION_CONFLICT");
        await using var transaction = await database.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        await LockBrowser(browser.Caller.BrowserDigest, ct).ConfigureAwait(false);
        var prior = await database.Set<FederationAttemptRow>().SingleOrDefaultAsync(x => x.BrowserDigest == browser.Caller.BrowserDigest && x.RequestId == requestId, ct).ConfigureAwait(false);
        if (prior is not null) return await Finish(transaction, new(prior, Original(prior, browser) && prior.Provider == provider.ToString() &&
            prior.Purpose == purpose.ToString() && prior.RememberMe == rememberMe ? null : "VERSION_CONFLICT"), ct).ConfigureAwait(false);
        if (!await authentication.AllowAsync("federation:N:" + browser.Caller.NetworkDigest, auth.NetworkAttemptLimit, auth, now, ct).ConfigureAwait(false) ||
            await database.Set<FederationAttemptRow>().CountAsync(x => x.BrowserDigest == browser.Caller.BrowserDigest &&
                (x.State == "PREPARED" || x.State == "CHALLENGE_ADMITTED" || x.State == "VERIFIED") && x.ExpiresAt > now, ct).ConfigureAwait(false) >= profile.Values.MaximumPendingAttempts)
            return await Finish(transaction, new(null, "RATE_OR_ABUSE_LIMITED"), ct).ConfigureAwait(false);
        // Abuse/count reads may await. Reject changed authority at the final preparation witness rather than
        // recording an earlier processing/retention/profile or authentication-policy snapshot.
        now = clock.GetUtcNow(); var finalProfile = Current(provider, browser.Caller.Origin, now);
        var finalAuthentication = authenticationPolicies.RequireEffective(now); finalAuthentication.Validate();
        if (profile.Snapshot != finalProfile.Snapshot || auth != finalAuthentication || browser.BrowserExpiresAt <= now)
            return await Finish(transaction, new(null, "VERSION_CONFLICT"), ct).ConfigureAwait(false);
        profile = finalProfile; auth = finalAuthentication;
        var row = new FederationAttemptRow
        {
            AttemptId = Guid.NewGuid(), RequestId = requestId, CorrelationId = Guid.NewGuid().ToString("N"), Provider = provider.ToString(), Purpose = purpose.ToString(),
            BrowserKey = browser.BrowserKey, BrowserDigest = browser.Caller.BrowserDigest, Origin = browser.Caller.Origin,
            PreviousCookieDigest = browser.OriginalCookieDigest, PreviousSessionId = browser.OriginalPreviousSessionId, RememberMe = rememberMe,
            AuthenticationPolicyVersion = auth.Version, ProviderNamespace = profile.Values.StableNamespaceId, ClientId = profile.Values.ClientId,
            ProfileVersion = profile.Values.Version, MappingEvidenceReference = profile.Values.IssuerMappingEvidenceReference,
            ProfileSnapshot = profile.Snapshot, ContinuationPath = profile.Values.ContinuationPath, ProcessingReference = profile.Values.ProcessingReference,
            RetentionReference = profile.Values.RetentionReference, ResultRecoveryLifetimeTicks = profile.Values.ResultRecoveryLifetime.Ticks,
            CreatedAt = now, ExpiresAt = profile.Clip(now + profile.Values.AttemptLifetime)
        };
        if (row.ExpiresAt > browser.BrowserExpiresAt) row.ExpiresAt = browser.BrowserExpiresAt;
        if (row.ExpiresAt <= now) return await Finish(transaction, new(null, "VERSION_CONFLICT"), ct).ConfigureAwait(false);
        database.Add(row); return await Finish(transaction, new(row), ct).ConfigureAwait(false);
    }

    internal async Task<FederationChallengeAdmission?> AdmitChallengeAsync(FederationCurrentBrowser browser, Guid attemptId, CancellationToken ct, StepUpCaller? owner = null)
    {
        Clean(); await using var transaction = await database.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        var row = await Owned(browser, attemptId, ct).ConfigureAwait(false); var now = clock.GetUtcNow();
        if (row is null || row.State != "PREPARED" || row.Purpose is not (nameof(FederationAttemptPurpose.SignIn) or nameof(FederationAttemptPurpose.Link) or nameof(FederationAttemptPurpose.OperationProof)) || !Original(row, browser) || !browser.OriginalFingerprint)
        { await Finish(transaction, new(row, "VERSION_CONFLICT"), ct).ConfigureAwait(false); return null; }
        var profile = Current(ParseProvider(row), row.Origin, now);
        if (row.Purpose == nameof(FederationAttemptPurpose.OperationProof) ? !OperationProofPendingCurrent(row, browser, profile, now, owner) : !PendingCurrent(row, browser, profile, now, owner))
        { await Reject(transaction, row, now, "VERSION_CONFLICT", ct).ConfigureAwait(false); return null; }
        row.State = "CHALLENGE_ADMITTED"; row.Version++;
        await Finish(transaction, new(row), ct).ConfigureAwait(false);
        return new(profile.Values.Provider, profile.Values.Version, profile.Values.TransportScheme, row.AttemptId,
            Enum.Parse<FederationAttemptPurpose>(row.Purpose, false));
    }

    internal async Task<FederationCallbackResult> RecordVerifiedAsync(FederationProtocolEvidence evidence, CancellationToken ct)
    {
        // No AuthenticationCaller, ordinary cookie, Account/link/session mutation or external I/O here.
        Clean(); ArgumentNullException.ThrowIfNull(evidence);
        var snapshot = await database.Set<FederationAttemptRow>().AsNoTracking().SingleOrDefaultAsync(x => x.AttemptId == evidence.AttemptId, ct).ConfigureAwait(false);
        if (snapshot is null || snapshot.Provider != evidence.Provider.ToString()) return new(null, false);
        if (snapshot.Purpose == nameof(FederationAttemptPurpose.OperationProof))
            return await RecordOperationProofEvidenceAsync(evidence, snapshot, ct).ConfigureAwait(false);
        await using var transaction = await database.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        await LockBrowser(snapshot.BrowserDigest, ct).ConfigureAwait(false); await LockAttempt(snapshot.AttemptId, ct).ConfigureAwait(false);
        var row = await database.Set<FederationAttemptRow>().SingleAsync(x => x.AttemptId == snapshot.AttemptId, ct).ConfigureAwait(false);
        var now = clock.GetUtcNow(); var profile = Current(evidence.Provider, row.Origin, now);
        if (row.State != "CHALLENGE_ADMITTED" || row.Purpose is not (nameof(FederationAttemptPurpose.SignIn) or nameof(FederationAttemptPurpose.Link)) ||
            now < row.CreatedAt || now >= row.ExpiresAt || evidence.ProfileVersion != row.ProfileVersion || !ExactProfile(row, profile))
        {
            // A repeated callback never extends VERIFIED or reopens a terminal attempt.
            await Finish(transaction, new(row), ct).ConfigureAwait(false); return new(row.ContinuationPath, false);
        }
        var key = profile.Registration.BindExternalIdentity(evidence.Issuer, evidence.ClientId, evidence.Subject);
        if (row.Purpose == nameof(FederationAttemptPurpose.Link))
        {
            if (linkEvidenceProtection is null || row.OwnerAccountId is null || row.OwnerSessionId is null) return new(row.ContinuationPath, false);
            now = clock.GetUtcNow(); profile = Current(evidence.Provider, row.Origin, now);
            if (now < row.CreatedAt || now >= row.ExpiresAt || !ExactProfile(row, profile))
            { await Reject(transaction, row, now, "VERSION_CONFLICT", ct).ConfigureAwait(false); return new(row.ContinuationPath, false); }
            row.ProtectedLinkSubject = linkEvidenceProtection.CreateProtector(row.AttemptId.ToString("N")).Protect(key.Subject);
            row.ValidatedIssuer = evidence.Issuer; row.VerifiedAt = now; row.VerificationExpiresAt = profile.Clip(now + profile.Values.VerifiedLifetime);
            if (row.VerificationExpiresAt > row.ExpiresAt) row.VerificationExpiresAt = row.ExpiresAt;
            row.State = "VERIFIED"; row.Version++; await Finish(transaction, new(row), ct).ConfigureAwait(false);
            return new(row.ContinuationPath, true);
        }
        var link = await database.ExternalIdentityLinks.SingleOrDefaultAsync(x => x.Provider == key.ProviderNamespace && x.Subject == key.Subject, ct).ConfigureAwait(false);
        if (link is null || !Qualified(link, profile))
        {
            await Reject(transaction, row, now, "AUTHENTICATION_FAILED", ct).ConfigureAwait(false);
            return new(row.ContinuationPath, false); // Same safe continuation for absent, legacy, retired or unqualified links; no email lookup.
        }
        now = clock.GetUtcNow(); profile = Current(evidence.Provider, row.Origin, now);
        if (now < row.CreatedAt || now >= row.ExpiresAt || !ExactProfile(row, profile))
        { await Reject(transaction, row, now, "VERSION_CONFLICT", ct).ConfigureAwait(false); return new(row.ContinuationPath, false); }
        row.State = "VERIFIED"; row.VerifiedAt = now; row.VerificationExpiresAt = profile.Clip(now + profile.Values.VerifiedLifetime);
        if (row.VerificationExpiresAt > row.ExpiresAt) row.VerificationExpiresAt = row.ExpiresAt;
        row.ValidatedIssuer = evidence.Issuer; row.LinkId = link.LinkId; row.LinkVersion = link.Version;
        row.LinkQualificationEvidenceReference = link.QualificationEvidenceReference; row.Version++;
        row.LinkQualificationProfileVersion = link.QualificationProfileVersion;
        await Finish(transaction, new(row), ct).ConfigureAwait(false);
        return new(row.ContinuationPath, true);
    }

    internal async Task<FederationAttemptResult> ReadOriginalAsync(FederationCurrentBrowser browser, Guid attemptId, CancellationToken ct)
    {
        Clean(); await using var transaction = await database.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        var row = await Owned(browser, attemptId, ct).ConfigureAwait(false); var now = clock.GetUtcNow();
        if (row is null || !SameBrowser(row, browser)) return await Finish(transaction, new(null, "NOT_FOUND_OR_NOT_DISCLOSABLE"), ct).ConfigureAwait(false);
        if (row.State != "COMPLETED") return await Finish(transaction, new(row,
            !Original(row, browser) || !browser.OriginalFingerprint || row.ExpiresAt <= now ? "VERSION_CONFLICT" : row.ErrorCode,
            projectedState: row.ExpiresAt <= now && row.State is ("PREPARED" or "CHALLENGE_ADMITTED" or "VERIFIED") ? "EXPIRED" : null), ct).ConfigureAwait(false);
        return await ReadCompleted(transaction, row, browser, now, ct).ConfigureAwait(false);
    }

    internal async Task<FederationAttemptResult> CompleteSignInAsync(FederationCurrentBrowser browser, Guid attemptId, CancellationToken ct)
    {
        Clean(); await using var transaction = await database.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        var row = await Owned(browser, attemptId, ct).ConfigureAwait(false); var now = clock.GetUtcNow();
        if (row is null || !SameBrowser(row, browser)) return await Finish(transaction, new(null, "NOT_FOUND_OR_NOT_DISCLOSABLE"), ct).ConfigureAwait(false);
        if (row.State == "COMPLETED") return await ReadCompleted(transaction, row, browser, now, ct).ConfigureAwait(false);
        if (row.State != "VERIFIED" || row.Purpose != nameof(FederationAttemptPurpose.SignIn) || !Original(row, browser) || !browser.OriginalFingerprint)
            return await Finish(transaction, new(row, "VERSION_CONFLICT"), ct).ConfigureAwait(false);
        var profile = Current(ParseProvider(row), row.Origin, now);
        if (!PendingCurrent(row, browser, profile, now) || row.VerificationExpiresAt is not { } verifiedUntil || verifiedUntil <= now)
            return await Reject(transaction, row, now, "VERSION_CONFLICT", ct).ConfigureAwait(false);
        var link = await database.ExternalIdentityLinks.AsNoTracking().SingleOrDefaultAsync(x => x.LinkId == row.LinkId, ct).ConfigureAwait(false);
        if (link is null || !VerifiedLink(row, link, profile)) return await Reject(transaction, row, now, "AUTHENTICATION_FAILED", ct).ConfigureAwait(false);
        var auth = authenticationPolicies.RequireEffective(now); auth.Validate();
        if (!await authentication.AllowAsync("federation:N:" + browser.Caller.NetworkDigest, auth.NetworkAttemptLimit, auth, now, ct).ConfigureAwait(false))
            return await Reject(transaction, row, now, "RATE_OR_ABUSE_LIMITED", ct).ConfigureAwait(false);
        await database.SaveChangesAsync(ct).ConfigureAwait(false);
        await transaction.CreateSavepointAsync("federation_owner", ct).ConfigureAwait(false);

        // Root-owned AuthenticationApplication hook owns the ordered previous-session/Account/user/link locks,
        // independent final admission/contact/stamp/source checks and actual current MFA/session policy.
        var ownerRequest = new FederationLinkedSignInRequest(row, browser, link, profile);
        var issued = await authentication.IssueFederatedSessionAsync(ownerRequest, ct).ConfigureAwait(false);
        var finalLink = await database.ExternalIdentityLinks.AsNoTracking().SingleOrDefaultAsync(x => x.LinkId == row.LinkId, ct).ConfigureAwait(false);
        // No awaited authority read follows this actual final decision witness.
        var decisionAt = clock.GetUtcNow(); var finalProfile = Current(ParseProvider(row), row.Origin, decisionAt);
        var resultSession = issued.Grant is null ? null : AuthenticationIdentity.SessionId(issued.Grant.Ticket.Principal);
        var validResult = authentication.FederatedSignInCurrentAt(ownerRequest, decisionAt) &&
            issued.ErrorCode is null && (issued.Grant is not null ^ issued.PendingMfa is not null) &&
            (issued.Grant is null || issued.Grant.Ticket.AuthenticationScheme == AuthenticationIdentity.Scheme &&
                resultSession is not null &&
                issued.Grant.Ticket.Principal.FindFirstValue(ClaimTypes.NameIdentifier) == link.AccountId &&
                issued.Grant.Ticket.Principal.FindFirstValue(AuthenticationIdentity.OriginClaim) == row.Origin) &&
            (issued.PendingMfa is null || issued.PendingMfa.PendingId != Guid.Empty && issued.PendingMfa.State == "PENDING" && issued.PendingMfa.RequestId == row.RequestId && issued.PendingMfa.AccountId == link.AccountId &&
                issued.PendingMfa.BrowserDigest == row.BrowserDigest && issued.PendingMfa.Origin == row.Origin);
        if (!validResult || !PendingCurrent(row, browser, finalProfile, decisionAt) || row.VerificationExpiresAt is not { } finalUntil || finalUntil <= decisionAt ||
            finalLink is null || !VerifiedLink(row, finalLink, finalProfile))
        {
            await transaction.RollbackToSavepointAsync("federation_owner", ct).ConfigureAwait(false);
            database.ChangeTracker.Clear(); row = await database.Set<FederationAttemptRow>().SingleAsync(x => x.AttemptId == attemptId, ct).ConfigureAwait(false);
            return await Reject(transaction, row, decisionAt, SafeError(issued.ErrorCode), ct).ConfigureAwait(false);
        }
        row.State = "COMPLETED"; row.Version++; row.EndedAt = decisionAt; row.ResultExpiresAt = decisionAt + TimeSpan.FromTicks(row.ResultRecoveryLifetimeTicks);
        row.ResultSessionId = resultSession;
        row.PendingMfaId = issued.PendingMfa?.PendingId;
        // await-using disposal completes before this task exposes the grant. Commit/disposal uncertainty is not retried here.
        return await Finish(transaction, new(row, authentication: issued), ct).ConfigureAwait(false);
    }

    internal async Task<FederationAttemptResult> CancelAsync(FederationCurrentBrowser browser, Guid attemptId, CancellationToken ct)
    {
        Clean(); await using var transaction = await database.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        var row = await Owned(browser, attemptId, ct).ConfigureAwait(false); var now = clock.GetUtcNow();
        if (row is null || !Original(row, browser) || !browser.OriginalFingerprint) return await Finish(transaction, new(null, "VERSION_CONFLICT"), ct).ConfigureAwait(false);
        if (row.State is "PREPARED" or "CHALLENGE_ADMITTED" or "VERIFIED")
            return await Reject(transaction, row, now, "AUTHENTICATION_FAILED", ct).ConfigureAwait(false);
        return await Finish(transaction, new(row), ct).ConfigureAwait(false);
    }

    private async Task<FederationAttemptResult> ReadCompleted(IDbContextTransaction transaction, FederationAttemptRow row,
        FederationCurrentBrowser browser, DateTimeOffset now, CancellationToken ct)
    {
        if (row.ResultExpiresAt is null || row.ResultExpiresAt <= now || row.LinkId is null || row.LinkVersion is null)
            return await Finish(transaction, new(row, "NOT_FOUND_OR_NOT_DISCLOSABLE"), ct).ConfigureAwait(false);
        var link = await database.ExternalIdentityLinks.AsNoTracking().SingleOrDefaultAsync(x => x.LinkId == row.LinkId, ct).ConfigureAwait(false);
        if (link is null) return await Finish(transaction, new(row, "NOT_FOUND_OR_NOT_DISCLOSABLE"), ct).ConfigureAwait(false);
        // Do NOT apply pending-profile equality or ACTIVE-link rules to historical session continuity.
        // The authentication owner resolves actual current session/source/unlink-effect/MFA policy independently.
        var current = Current(ParseProvider(row), row.Origin, now);
        var recovered = await authentication.ReadOriginalFederatedSignInAsync(new(row, browser, link, current), row.ResultSessionId, row.PendingMfaId, ct).ConfigureAwait(false);
        return await Finish(transaction, new(row, recovered.ErrorCode is null ? null : SafeError(recovered.ErrorCode), recovered), ct).ConfigureAwait(false);
    }

    private async Task<FederationAttemptRow?> Owned(FederationCurrentBrowser browser, Guid attemptId, CancellationToken ct)
    {
        browser.Caller.Validate(); await LockBrowser(browser.Caller.BrowserDigest, ct).ConfigureAwait(false); await LockAttempt(attemptId, ct).ConfigureAwait(false);
        return await database.Set<FederationAttemptRow>().SingleOrDefaultAsync(x => x.AttemptId == attemptId && x.BrowserDigest == browser.Caller.BrowserDigest, ct).ConfigureAwait(false);
    }
    private FederationProfileVersion Current(FederationProviderKind provider, string origin, DateTimeOffset at)
    { var value = profiles.RequireCurrent(provider, at); value.Require(origin, at); if (value.Values.Provider != provider) throw new InvalidOperationException("Federation profile scope changed."); return value; }
    private static FederationProviderKind ParseProvider(FederationAttemptRow row) => Enum.Parse<FederationProviderKind>(row.Provider, false);
    private bool PendingCurrent(FederationAttemptRow row, FederationCurrentBrowser browser, FederationProfileVersion profile, DateTimeOffset at, StepUpCaller? owner = null)
    {
        if (row.Purpose is not (nameof(FederationAttemptPurpose.SignIn) or nameof(FederationAttemptPurpose.Link)) || !ExactProfile(row, profile) || !SameBrowser(row, browser) ||
            browser.BrowserExpiresAt <= at || row.AuthenticationPolicyVersion != authenticationPolicies.RequireEffective(at).Version) return false;
        var purpose = Enum.Parse<FederationAttemptPurpose>(row.Purpose, false);
        var boundOwner = purpose == FederationAttemptPurpose.Link ? BoundOwner(row, owner) : null;
        if (purpose == FederationAttemptPurpose.Link && boundOwner is null) return false;
        // Exact persisted snapshot equality above establishes the original profile before reconstructing the shared binder.
        var binding = new FederationAttemptBinding(row.AttemptId, row.RequestId.ToString("N"), row.CorrelationId, purpose,
            profile.Registration, row.BrowserDigest, row.ContinuationPath, profile.Values.AllowedContinuations, row.CreatedAt, row.ExpiresAt, boundOwner);
        return binding.Matches(row.AttemptId, row.RequestId.ToString("N"), row.CorrelationId, purpose,
            profile.Registration, browser.Caller.BrowserDigest, row.ContinuationPath, at, boundOwner);
    }
    private static FederationAttemptOwner? BoundOwner(FederationAttemptRow row, StepUpCaller? caller)
    {
        if (caller is null || caller.AccountId != row.OwnerAccountId || caller.SessionId != row.OwnerSessionId || caller.Origin != row.Origin ||
            row.OwnerAccountVersion is null || row.OwnerSessionVersion is null || row.OwnerExpectedVersion is null ||
            row.OwnerOperationId is null || row.OwnerContextDigest is null) return null;
        // Original version snapshots are immutable binder inputs, not reconstructed current authority. The management
        // owner separately locks/revalidates actual Account/session/stamp and expected resource versions at execution.
        return new(caller, new(row.OwnerOperationId, row.CorrelationId, FederationLinkApplication.LinkScope, row.CorrelationId,
            row.OwnerExpectedVersion.Value, row.OwnerContextDigest), row.OwnerAccountVersion.Value, row.OwnerSessionVersion.Value);
    }
    private static bool ExactProfile(FederationAttemptRow row, FederationProfileVersion profile) => row.ProfileSnapshot == profile.Snapshot &&
        row.Provider == profile.Values.Provider.ToString() && row.ProfileVersion == profile.Values.Version && row.ProviderNamespace == profile.Values.StableNamespaceId &&
        row.ClientId == profile.Values.ClientId && row.MappingEvidenceReference == profile.Values.IssuerMappingEvidenceReference && row.ContinuationPath == profile.Values.ContinuationPath;
    private static bool SameBrowser(FederationAttemptRow row, FederationCurrentBrowser browser) => row.BrowserDigest == browser.Caller.BrowserDigest &&
        row.BrowserKey == browser.BrowserKey && row.Origin == browser.Caller.Origin;
    private static bool Original(FederationAttemptRow row, FederationCurrentBrowser browser) => SameBrowser(row, browser) &&
        row.PreviousCookieDigest == browser.OriginalCookieDigest && row.PreviousSessionId == browser.OriginalPreviousSessionId;
    private static bool Qualified(ExternalIdentityLinkRow link, FederationProfileVersion profile) => link.LinkState == "ACTIVE" &&
        link.Provider == profile.Values.StableNamespaceId && link.QualifiedClientId == profile.Values.ClientId &&
        link.IssuerMappingEvidenceReference == profile.Values.IssuerMappingEvidenceReference && !string.IsNullOrWhiteSpace(link.QualificationProfileVersion) &&
        !string.IsNullOrWhiteSpace(link.QualificationEvidenceReference);
    private static bool VerifiedLink(FederationAttemptRow row, ExternalIdentityLinkRow link, FederationProfileVersion profile) => Qualified(link, profile) &&
        link.LinkId == row.LinkId && link.Version == row.LinkVersion && link.QualificationEvidenceReference == row.LinkQualificationEvidenceReference &&
        link.QualificationProfileVersion == row.LinkQualificationProfileVersion;
    private async Task<FederationAttemptResult> Reject(IDbContextTransaction transaction, FederationAttemptRow row,
        DateTimeOffset now, string error, CancellationToken ct)
    {
        if (now < row.CreatedAt) return await Finish(transaction, new(row, "VERSION_CONFLICT"), ct).ConfigureAwait(false);
        row.State = now >= row.ExpiresAt ? "EXPIRED" : "REJECTED"; row.ErrorCode = error; row.EndedAt = now;
        row.ResultExpiresAt = now + TimeSpan.FromTicks(row.ResultRecoveryLifetimeTicks); row.Version++;
        return await Finish(transaction, new(row, error), ct).ConfigureAwait(false);
    }
    private async Task<FederationAttemptResult> Finish(IDbContextTransaction transaction, FederationAttemptResult result, CancellationToken ct)
    { await database.SaveChangesAsync(ct).ConfigureAwait(false); await transaction.CommitAsync(ct).ConfigureAwait(false); return result; }
    private void Clean()
    { if (database.Database.CurrentTransaction is not null || database.ChangeTracker.HasChanges()) throw new InvalidOperationException("Federation requires a clean Identity owner boundary."); }
    private Task<int> LockBrowser(string digest, CancellationToken ct) => database.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({"auth-browser:" + digest},0))", ct);
    private Task<int> LockAttempt(Guid id, CancellationToken ct) => database.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({"federation-attempt:" + id.ToString("N")},0))", ct);
    private static string SafeError(string? code) => code is "RATE_OR_ABUSE_LIMITED" or "STEP_UP_REQUIRED" or "VERSION_CONFLICT" ? code : "AUTHENTICATION_FAILED";
}
