using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Tunner.Accounts;

namespace Tunner.Identity.Persistence;

/// <summary>Server-only fixed source provenance. Neither protocol receipt time nor this value is fresh step-up proof.</summary>
internal sealed class FederationAuthenticationSource(Guid attemptId, FederationProviderKind provider, Guid linkId, long linkVersion, string providerNamespace, string subject, string clientId, string issuer, string profileVersion, string issuerMappingEvidenceReference, string qualificationProfileVersion, string qualificationEvidenceReference, string signInAdmissionPolicyVersion, string assuranceMappingEvidenceReference, string sessionUsePolicyVersion, string primaryAssurance, DateTimeOffset? verificationExpiresAt)
{
    internal Guid AttemptId { get; } = attemptId;
    internal FederationProviderKind Provider { get; } = provider;
    internal Guid LinkId { get; } = linkId;
    internal long LinkVersion { get; } = linkVersion;
    internal string Namespace { get; } = providerNamespace;
    internal string Subject { get; } = subject;
    internal string ClientId { get; } = clientId;
    internal string Issuer { get; } = issuer;
    internal string ProfileVersion { get; } = profileVersion;
    internal string IssuerMappingEvidenceReference { get; } = issuerMappingEvidenceReference;
    internal string QualificationProfileVersion { get; } = qualificationProfileVersion;
    internal string QualificationEvidenceReference { get; } = qualificationEvidenceReference;
    internal string SignInAdmissionPolicyVersion { get; } = signInAdmissionPolicyVersion;
    internal string AssuranceMappingEvidenceReference { get; } = assuranceMappingEvidenceReference;
    internal string SessionUsePolicyVersion { get; } = sessionUsePolicyVersion;
    internal string PrimaryAssurance { get; } = primaryAssurance;
    internal DateTimeOffset? VerificationExpiresAt { get; } = verificationExpiresAt;

    internal const string SessionShapeConstraint = "((authentication_methods NOT IN ('federation','federation+totp') AND federation_attempt_id IS NULL AND federation_provider IS NULL AND federation_link_id IS NULL AND federation_link_version IS NULL AND federation_namespace IS NULL AND federation_client_id IS NULL AND federation_issuer IS NULL AND federation_profile_version IS NULL AND federation_issuer_mapping_evidence_reference IS NULL AND federation_qualification_profile_version IS NULL AND federation_qualification_evidence_reference IS NULL AND federation_sign_in_admission_policy_version IS NULL AND federation_assurance_mapping_evidence_reference IS NULL AND federation_session_use_policy_version IS NULL AND federation_primary_assurance IS NULL) OR (authentication_methods IN ('federation','federation+totp') AND federation_attempt_id IS NOT NULL AND federation_provider IS NOT NULL AND federation_link_id IS NOT NULL AND federation_link_version IS NOT NULL AND federation_namespace IS NOT NULL AND length(federation_namespace)>0 AND federation_client_id IS NOT NULL AND length(federation_client_id)>0 AND federation_issuer IS NOT NULL AND length(federation_issuer)>0 AND federation_profile_version IS NOT NULL AND length(federation_profile_version)>0 AND federation_issuer_mapping_evidence_reference IS NOT NULL AND length(federation_issuer_mapping_evidence_reference)>0 AND federation_qualification_profile_version IS NOT NULL AND length(federation_qualification_profile_version)>0 AND federation_qualification_evidence_reference IS NOT NULL AND length(federation_qualification_evidence_reference)>0 AND federation_sign_in_admission_policy_version IS NOT NULL AND length(federation_sign_in_admission_policy_version)>0 AND federation_assurance_mapping_evidence_reference IS NOT NULL AND length(federation_assurance_mapping_evidence_reference)>0 AND federation_session_use_policy_version IS NOT NULL AND length(federation_session_use_policy_version)>0 AND federation_link_version>=1 AND federation_provider IN ('Google','Microsoft') AND federation_primary_assurance IS NOT NULL AND length(federation_primary_assurance)>0))";
    internal const string PendingShapeConstraint = "((primary_method<>'federation' AND federation_attempt_id IS NULL AND federation_provider IS NULL AND federation_link_id IS NULL AND federation_link_version IS NULL AND federation_namespace IS NULL AND federation_client_id IS NULL AND federation_issuer IS NULL AND federation_profile_version IS NULL AND federation_issuer_mapping_evidence_reference IS NULL AND federation_qualification_profile_version IS NULL AND federation_qualification_evidence_reference IS NULL AND federation_sign_in_admission_policy_version IS NULL AND federation_assurance_mapping_evidence_reference IS NULL AND federation_session_use_policy_version IS NULL) OR (primary_method='federation' AND federation_attempt_id IS NOT NULL AND federation_provider IS NOT NULL AND federation_link_id IS NOT NULL AND federation_link_version IS NOT NULL AND federation_namespace IS NOT NULL AND length(federation_namespace)>0 AND federation_client_id IS NOT NULL AND length(federation_client_id)>0 AND federation_issuer IS NOT NULL AND length(federation_issuer)>0 AND federation_profile_version IS NOT NULL AND length(federation_profile_version)>0 AND federation_issuer_mapping_evidence_reference IS NOT NULL AND length(federation_issuer_mapping_evidence_reference)>0 AND federation_qualification_profile_version IS NOT NULL AND length(federation_qualification_profile_version)>0 AND federation_qualification_evidence_reference IS NOT NULL AND length(federation_qualification_evidence_reference)>0 AND federation_sign_in_admission_policy_version IS NOT NULL AND length(federation_sign_in_admission_policy_version)>0 AND federation_assurance_mapping_evidence_reference IS NOT NULL AND length(federation_assurance_mapping_evidence_reference)>0 AND federation_session_use_policy_version IS NOT NULL AND length(federation_session_use_policy_version)>0 AND federation_link_version>=1 AND federation_provider IN ('Google','Microsoft') AND passkey_credential_reference IS NULL AND passkey_credential_version IS NULL AND passkey_policy_version IS NULL AND passkey_assurance_key IS NULL))";

    internal static FederationAuthenticationSource From(FederationLinkedSignInRequest request) => new(request.AttemptId,
        request.CurrentProfile.Values.Provider, request.LinkId, request.LinkVersion, request.ProviderNamespace, request.Subject,
        request.ClientId, request.ValidatedIssuer, request.OriginalProfileVersion, request.IssuerMappingEvidenceReference,
        request.QualificationProfileVersion, request.QualificationEvidenceReference, request.CurrentProfile.Values.SignInAdmissionPolicyVersion,
        request.CurrentProfile.Values.AssuranceMappingEvidenceReference, request.CurrentProfile.Values.SessionUsePolicyVersion,
        request.CurrentProfile.Values.PrimaryAssurance, request.VerificationExpiresAt);

    internal string Digest => AuthenticationIdentity.Digest(JsonSerializer.Serialize(new[] { AttemptId.ToString("N"), Provider.ToString(), LinkId.ToString("N"), LinkVersion.ToString(System.Globalization.CultureInfo.InvariantCulture), Namespace, Subject, ClientId, Issuer, ProfileVersion, IssuerMappingEvidenceReference, QualificationProfileVersion, QualificationEvidenceReference, SignInAdmissionPolicyVersion, AssuranceMappingEvidenceReference, SessionUsePolicyVersion, PrimaryAssurance }));
    internal string Identifier => JsonSerializer.Serialize(new[] { Namespace, Subject, ClientId });

    internal void Store(AuthenticationSessionRow row)
    {
        row.FederationAttemptId = AttemptId;
        row.FederationProvider = Provider.ToString();
        row.FederationLinkId = LinkId;
        row.FederationLinkVersion = LinkVersion;
        row.FederationNamespace = Namespace;
        row.FederationClientId = ClientId;
        row.FederationIssuer = Issuer;
        row.FederationProfileVersion = ProfileVersion;
        row.FederationIssuerMappingEvidenceReference = IssuerMappingEvidenceReference;
        row.FederationQualificationProfileVersion = QualificationProfileVersion;
        row.FederationQualificationEvidenceReference = QualificationEvidenceReference;
        row.FederationSignInAdmissionPolicyVersion = SignInAdmissionPolicyVersion;
        row.FederationAssuranceMappingEvidenceReference = AssuranceMappingEvidenceReference;
        row.FederationSessionUsePolicyVersion = SessionUsePolicyVersion;
        row.FederationPrimaryAssurance = PrimaryAssurance;
    }

    internal void Store(TotpPendingAuthenticationRow row)
    {
        row.FederationAttemptId = AttemptId;
        row.FederationProvider = Provider.ToString();
        row.FederationLinkId = LinkId;
        row.FederationLinkVersion = LinkVersion;
        row.FederationNamespace = Namespace;
        row.FederationClientId = ClientId;
        row.FederationIssuer = Issuer;
        row.FederationProfileVersion = ProfileVersion;
        row.FederationIssuerMappingEvidenceReference = IssuerMappingEvidenceReference;
        row.FederationQualificationProfileVersion = QualificationProfileVersion;
        row.FederationQualificationEvidenceReference = QualificationEvidenceReference;
        row.FederationSignInAdmissionPolicyVersion = SignInAdmissionPolicyVersion;
        row.FederationAssuranceMappingEvidenceReference = AssuranceMappingEvidenceReference;
        row.FederationSessionUsePolicyVersion = SessionUsePolicyVersion;
    }

    internal static FederationAuthenticationSource? Read(AuthenticationSessionRow row, ExternalIdentityLinkRow link)
    {
        if (row.FederationAttemptId is not { } attemptId ||
            row.FederationProvider is not { } provider ||
            row.FederationLinkId is not { } linkId ||
            row.FederationLinkVersion is not { } linkVersion ||
            row.FederationNamespace is not { } providerNamespace ||
            row.FederationClientId is not { } clientId ||
            row.FederationIssuer is not { } issuer ||
            row.FederationProfileVersion is not { } profileVersion ||
            row.FederationIssuerMappingEvidenceReference is not { } issuerMappingEvidenceReference ||
            row.FederationQualificationProfileVersion is not { } qualificationProfileVersion ||
            row.FederationQualificationEvidenceReference is not { } qualificationEvidenceReference ||
            row.FederationSignInAdmissionPolicyVersion is not { } signInAdmissionPolicyVersion ||
            row.FederationAssuranceMappingEvidenceReference is not { } assuranceMappingEvidenceReference ||
            row.FederationSessionUsePolicyVersion is not { } sessionUsePolicyVersion ||
            !Enum.TryParse<FederationProviderKind>(provider, false, out var kind) || !Enum.IsDefined(kind) || provider != kind.ToString()) return null;
        return new(attemptId, kind, linkId, linkVersion, providerNamespace, link.Subject, clientId, issuer, profileVersion, issuerMappingEvidenceReference, qualificationProfileVersion, qualificationEvidenceReference, signInAdmissionPolicyVersion, assuranceMappingEvidenceReference, sessionUsePolicyVersion, row.FederationPrimaryAssurance ?? string.Empty, null);
    }

    internal static FederationAuthenticationSource? Read(TotpPendingAuthenticationRow row, ExternalIdentityLinkRow link)
    {
        if (row.FederationAttemptId is not { } attemptId ||
            row.FederationProvider is not { } provider ||
            row.FederationLinkId is not { } linkId ||
            row.FederationLinkVersion is not { } linkVersion ||
            row.FederationNamespace is not { } providerNamespace ||
            row.FederationClientId is not { } clientId ||
            row.FederationIssuer is not { } issuer ||
            row.FederationProfileVersion is not { } profileVersion ||
            row.FederationIssuerMappingEvidenceReference is not { } issuerMappingEvidenceReference ||
            row.FederationQualificationProfileVersion is not { } qualificationProfileVersion ||
            row.FederationQualificationEvidenceReference is not { } qualificationEvidenceReference ||
            row.FederationSignInAdmissionPolicyVersion is not { } signInAdmissionPolicyVersion ||
            row.FederationAssuranceMappingEvidenceReference is not { } assuranceMappingEvidenceReference ||
            row.FederationSessionUsePolicyVersion is not { } sessionUsePolicyVersion ||
            !Enum.TryParse<FederationProviderKind>(provider, false, out var kind) || !Enum.IsDefined(kind) || provider != kind.ToString()) return null;
        return new(attemptId, kind, linkId, linkVersion, providerNamespace, link.Subject, clientId, issuer, profileVersion, issuerMappingEvidenceReference, qualificationProfileVersion, qualificationEvidenceReference, signInAdmissionPolicyVersion, assuranceMappingEvidenceReference, sessionUsePolicyVersion, row.PrimaryAssurance, row.ExpiresAt);
    }

    public override string ToString() => nameof(FederationAuthenticationSource);
}

internal sealed partial class AuthenticationApplication
{
    private static bool IsFederationMethod(string method) => method is "federation" or "federation+totp";
    private sealed record FederationOwner(AccountRow Account, IdentityUserAdapter User, ExternalIdentityLinkRow Link, AuthenticationSessionRow? Previous)
    { public override string ToString() => nameof(FederationOwner); }
    private sealed record FederationIssuanceAuthority(Guid TransactionId, Guid AttemptId, long AccountVersion, string StampDigest,
        FederationOwner Owner, FederationAuthenticationSource Source, FederationProfileVersion Profile, string AuthenticationPolicyVersion,
        AccountAuthenticationAccessDecision Access, TotpMfaRequirementDecision Mfa, TotpPendingAuthenticationRow? Pending, FederationLinkedSignInRequest Request)
    { public override string ToString() => nameof(FederationIssuanceAuthority); }
    private FederationIssuanceAuthority? federationIssuanceAuthority;

    private FederationProfileVersion? CurrentFederation(FederationProviderKind provider, string origin, DateTimeOffset at)
    {
        var sources = federationProfiles?.Take(2).ToArray();
        if (sources is not { Length: 1 }) return null;
        try { var profile = sources[0].RequireCurrent(provider, at); profile.Require(origin, at); return profile; }
        catch (InvalidOperationException) { return null; }
    }
    private ITotpMfaRequirementSource? FederationMfaSource()
    {
        var sources = mfaRequirements?.Take(2).ToArray();
        return sources is { Length: 1 } ? sources[0] : null;
    }
    private static bool QualifiedFederationLink(ExternalIdentityLinkRow link, FederationAuthenticationSource source,
        FederationProfileVersion profile, string account, bool pending)
    {
        var values = profile.Values;
        if (link.LinkState != "ACTIVE" || link.AccountId != account || link.LinkId != source.LinkId || link.Version < source.LinkVersion ||
            link.Provider != source.Namespace || values.Provider != source.Provider || values.StableNamespaceId != source.Namespace ||
            values.ClientId != source.ClientId || link.QualifiedClientId != source.ClientId ||
            values.IssuerMappingEvidenceReference != source.IssuerMappingEvidenceReference || link.IssuerMappingEvidenceReference != source.IssuerMappingEvidenceReference ||
            string.IsNullOrWhiteSpace(link.QualificationProfileVersion) || string.IsNullOrWhiteSpace(link.QualificationEvidenceReference) ||
            values.PrimaryAssurance != source.PrimaryAssurance || values.AssuranceMappingEvidenceReference != source.AssuranceMappingEvidenceReference ||
            values.SessionUsePolicyVersion != source.SessionUsePolicyVersion) return false;
        if (pending && (link.Version != source.LinkVersion || link.Subject != source.Subject ||
            link.QualificationProfileVersion != source.QualificationProfileVersion || link.QualificationEvidenceReference != source.QualificationEvidenceReference ||
            values.Version != source.ProfileVersion || values.SignInAdmissionPolicyVersion != source.SignInAdmissionPolicyVersion)) return false;
        try
        {
            var key = profile.Registration.BindExternalIdentity(source.Issuer, source.ClientId, link.Subject);
            return key.ProviderNamespace == source.Namespace && key.Subject == source.Subject;
        }
        catch (ArgumentException) { return false; }
    }
    private bool FederationCurrentAt(FederationAuthenticationSource source, ExternalIdentityLinkRow link, string account,
        string origin, DateTimeOffset at, bool pending)
    {
        var profile = CurrentFederation(source.Provider, origin, at);
        return source.AttemptId != Guid.Empty && source.LinkId != Guid.Empty && source.LinkVersion >= 1 &&
            profile is not null && QualifiedFederationLink(link, source, profile, account, pending);
    }
    private async Task<ExternalIdentityLinkRow?> LockFederationLinkAsync(Guid id, string account, CancellationToken ct)
    {
        var link = await database.ExternalIdentityLinks.FromSqlInterpolated(
            $"SELECT * FROM identity.external_identity_links WHERE link_id={id} AND account_id={account} FOR NO KEY UPDATE").SingleOrDefaultAsync(ct).ConfigureAwait(false);
        if (link is not null) await database.Entry(link).ReloadAsync(ct).ConfigureAwait(false);
        return link;
    }
    private async Task<FederationOwner?> LockFederationOwnerAsync(FederationLinkedSignInRequest request, Guid? resultSessionId, CancellationToken ct)
    {
        AuthenticationSessionRow? previous = null;
        if (request.OriginalPreviousSessionId is { } previousId)
        {
            await LockSessionAsync(previousId, ct).ConfigureAwait(false);
            previous = await database.Set<AuthenticationSessionRow>().SingleOrDefaultAsync(x => x.SessionId == previousId &&
                x.Scheme == AuthenticationIdentity.Scheme && x.Origin == request.Caller.Origin, ct).ConfigureAwait(false);
            if (previous is not null) await database.Entry(previous).ReloadAsync(ct).ConfigureAwait(false);
        }
        if (resultSessionId is { } result && result != request.OriginalPreviousSessionId) await LockSessionAsync(result, ct).ConfigureAwait(false);
        var account = await database.Accounts.FromSqlInterpolated(
            $"SELECT * FROM accounts.accounts WHERE account_id={request.AccountId} FOR NO KEY UPDATE").SingleOrDefaultAsync(ct).ConfigureAwait(false);
        if (account is null) return null;
        await database.Entry(account).ReloadAsync(ct).ConfigureAwait(false);
        var user = await database.Users.FromSqlInterpolated(
            $"""SELECT * FROM identity."AspNetUsers" WHERE "AccountId"={request.AccountId} FOR NO KEY UPDATE""").SingleOrDefaultAsync(ct).ConfigureAwait(false);
        if (user is null) return null;
        await database.Entry(user).ReloadAsync(ct).ConfigureAwait(false);
        _ = await database.VerifiedContacts.FromSqlInterpolated(
            $"SELECT * FROM accounts.verified_contacts WHERE account_id={request.AccountId} ORDER BY contact_id FOR SHARE").ToArrayAsync(ct).ConfigureAwait(false);
        var link = await LockFederationLinkAsync(request.LinkId, request.AccountId, ct).ConfigureAwait(false);
        return link is null ? null : new(account, user, link, previous);
    }
    private async Task<bool> FederationContactCurrentAsync(IdentityUserAdapter user, CancellationToken ct) =>
        user.EmailConfirmed && !string.IsNullOrWhiteSpace(user.SecurityStamp) && !string.IsNullOrWhiteSpace(user.NormalizedEmail) &&
        await database.VerifiedContacts.AnyAsync(x => x.AccountId == user.AccountId && x.State == ContactVerificationState.Verified &&
            x.AnonymizedAtUtc == null && x.NormalizedEmail == user.NormalizedEmail, ct).ConfigureAwait(false);

    // The attempt owner owns this exact scoped DbContext transaction and commits session/MFA plus its safe outcome together.
    internal async Task<AuthenticationAttempt> IssueFederatedSessionAsync(FederationLinkedSignInRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request); request.Caller.Validate();
        federationIssuanceAuthority = null;
        if (database.Database.CurrentTransaction is null) throw new InvalidOperationException("Federation sign-in requires the current Identity owner transaction.");
        if (request.AttemptId == Guid.Empty || request.RequestId == Guid.Empty ||
            request.OriginalPreviousSessionId != request.Caller.PreviousSessionId || request.OriginalCookieDigest != request.ActualCookieDigest)
            return new("AUTHENTICATION_FAILED", null);
        var owner = await LockFederationOwnerAsync(request, null, ct).ConfigureAwait(false);
        if (owner is null || !await FederationContactCurrentAsync(owner.User, ct).ConfigureAwait(false)) return new("AUTHENTICATION_FAILED", null);
        var source = FederationAuthenticationSource.From(request);
        var now = clock.GetUtcNow(); var auth = policies.RequireEffective(now); auth.Validate();
        var profile = CurrentFederation(source.Provider, request.Caller.Origin, now);
        if (profile is null || profile.Snapshot != request.CurrentProfile.Snapshot || now < request.ProtocolVerifiedAt ||
            now >= request.VerificationExpiresAt || !FederationCurrentAt(source, owner.Link, request.AccountId, request.Caller.Origin, now, true))
            return new("AUTHENTICATION_FAILED", null);
        var prior = await database.Set<AuthenticationSessionRow>().AnyAsync(x => x.BrowserDigest == request.Caller.BrowserDigest &&
            x.IdempotencyKey == request.RequestId, ct).ConfigureAwait(false);
        if (prior) return new("VERSION_CONFLICT", null);
        var mfa = await PrepareTotpMfaAsync(request.Caller, request.RequestId, request.RememberMe, owner.User,
            partitions.Identifier(source.Identifier), "federation", source.PrimaryAssurance, request.ProtocolVerifiedAt, ct, federation: source).ConfigureAwait(false);
        if (mfa?.ErrorCode is not null) return mfa;
        var accessDecision = await AccessAsync(owner.Account, "SIGN_IN", clock.GetUtcNow(), ct).ConfigureAwait(false);
        var requirements = FederationMfaSource();
        if (requirements is null) return new("AUTHENTICATION_FAILED", null);
        var requirement = await requirements.RequireAsync(new(owner.Account.AccountId, owner.Account.Version, "federation",
            source.PrimaryAssurance, clock.GetUtcNow()), ct).ConfigureAwait(false);
        // Last async authority read is above. Re-resolve all time/profile/policy witnesses at this actual frontier.
        now = clock.GetUtcNow(); var finalAuth = policies.RequireEffective(now); finalAuth.Validate();
        ValidateRequirement(requirement, request.AccountId, owner.Account.Version, "federation", source.PrimaryAssurance, now);
        profile = CurrentFederation(source.Provider, request.Caller.Origin, now);
        if (accessDecision?.Allowed != true || !accessDecision.EffectiveInterval.Contains(now) || finalAuth.Version != auth.Version ||
            profile is null || profile.Snapshot != request.CurrentProfile.Snapshot || now < request.ProtocolVerifiedAt || now >= request.VerificationExpiresAt ||
            !FederationCurrentAt(source, owner.Link, request.AccountId, request.Caller.Origin, now, true) ||
            requirement.Required != (mfa?.PendingMfa is not null)) return new("AUTHENTICATION_FAILED", null);
        if (mfa?.PendingMfa is { } pending)
        {
            var authority = new PendingPrimaryAuthority(requirement, null, source, owner.Link);
            var totp = CurrentTotpPolicy(now);
            if (totp is null || !PendingCurrent(pending, request.Caller, owner.User, finalAuth, totp, now) ||
                !PendingPrimaryFactsCurrentAt(pending, owner.User, request.Caller, authority, finalAuth, now)) return new("AUTHENTICATION_FAILED", null);
            federationIssuanceAuthority = new(database.Database.CurrentTransaction!.TransactionId, request.AttemptId, owner.Account.Version,
                AuthenticationIdentity.Digest(owner.User.SecurityStamp!), owner, source, profile, finalAuth.Version, accessDecision, requirement, pending, request);
            return mfa;
        }
        federationIssuanceAuthority = new(database.Database.CurrentTransaction!.TransactionId, request.AttemptId, owner.Account.Version,
            AuthenticationIdentity.Digest(owner.User.SecurityStamp!), owner, source, profile, finalAuth.Version, accessDecision, requirement, null, request);
        if (owner.Previous is { State: "ACTIVE" } previous) Revoke(previous, now);
        return CreateSession(request.Caller, request.RequestId, request.RememberMe, owner.User, partitions.Identifier(source.Identifier),
            "federation", source.PrimaryAssurance, finalAuth, accessDecision, now, federation: source);
    }

    // Reuse the exact completed async decisions at the attempt owner's last frontier, after its final persisted-source read.
    internal bool FederatedSignInCurrentAt(FederationLinkedSignInRequest request, DateTimeOffset at)
    {
        var witness = federationIssuanceAuthority;
        if (witness is null || database.Database.CurrentTransaction?.TransactionId != witness.TransactionId || witness.AttemptId != request.AttemptId ||
            witness.Request.RequestId != request.RequestId || witness.Request.Caller != request.Caller || witness.Request.RememberMe != request.RememberMe ||
            witness.Request.OriginalCookieDigest != request.OriginalCookieDigest || witness.Request.ActualCookieDigest != request.ActualCookieDigest ||
            witness.Request.ProtocolVerifiedAt != request.ProtocolVerifiedAt || witness.Request.VerificationExpiresAt != request.VerificationExpiresAt ||
            witness.Owner.Account.Version != witness.AccountVersion || !witness.Owner.User.EmailConfirmed || string.IsNullOrWhiteSpace(witness.Owner.User.SecurityStamp) ||
            AuthenticationIdentity.Digest(witness.Owner.User.SecurityStamp) != witness.StampDigest || !witness.Access.Allowed || !witness.Access.EffectiveInterval.Contains(at) ||
            !witness.Mfa.EffectiveInterval.Contains(at) || at < request.ProtocolVerifiedAt || at >= request.VerificationExpiresAt) return false;
        var auth = policies.RequireEffective(at); auth.Validate(); var profile = CurrentFederation(witness.Source.Provider, request.Caller.Origin, at);
        if (auth.Version != witness.AuthenticationPolicyVersion || profile is null || profile.Snapshot != witness.Profile.Snapshot ||
            !FederationCurrentAt(witness.Source, witness.Owner.Link, request.AccountId, request.Caller.Origin, at, true)) return false;
        if (witness.Pending is not { } pending) return !witness.Mfa.Required;
        var totp = CurrentTotpPolicy(at);
        return witness.Mfa.Required && totp is not null && PendingCurrent(pending, request.Caller, witness.Owner.User, auth, totp, at) &&
            PendingPrimaryFactsCurrentAt(pending, witness.Owner.User, request.Caller,
                new(witness.Mfa, null, witness.Source, witness.Owner.Link), auth, at);
    }

    // Safe original result only; never start another transaction, mint a new session or accept primary evidence again.
    internal async Task<AuthenticationAttempt> ReadOriginalFederatedSignInAsync(FederationLinkedSignInRequest request,
        Guid? sessionId, Guid? mfaId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request); request.Caller.Validate();
        if (database.Database.CurrentTransaction is null) throw new InvalidOperationException("Federation result recovery requires the current Identity owner transaction.");
        if (sessionId.HasValue == mfaId.HasValue) return new("VERSION_CONFLICT", null);
        TotpPendingAuthenticationRow? pending = null;
        if (mfaId is { } pendingId)
        {
            pending = await database.Set<TotpPendingAuthenticationRow>().AsNoTracking().SingleOrDefaultAsync(x => x.PendingId == pendingId &&
                x.FederationAttemptId == request.AttemptId && x.AccountId == request.AccountId &&
                x.RequestId == request.RequestId && x.BrowserDigest == request.Caller.BrowserDigest && x.Origin == request.Caller.Origin, ct).ConfigureAwait(false);
            if (pending is null || pending.PrimaryMethod != "federation" || pending.RememberMe != request.RememberMe ||
                pending.PreviousSessionId != request.OriginalPreviousSessionId) return new("VERSION_CONFLICT", null);
            if (pending.State == "COMPLETED") sessionId = pending.ResultSessionId;
        }
        var owner = await LockFederationOwnerAsync(request, sessionId, ct).ConfigureAwait(false);
        if (owner is null || !await FederationContactCurrentAsync(owner.User, ct).ConfigureAwait(false)) return new("AUTHENTICATION_FAILED", null);
        if (request.Caller.PreviousSessionId != request.OriginalPreviousSessionId && request.Caller.PreviousSessionId != sessionId ||
            request.OriginalCookieDigest != request.ActualCookieDigest && request.Caller.PreviousSessionId != sessionId)
            return new("VERSION_CONFLICT", null);
        if (pending is not null && pending.State != "COMPLETED")
        {
            if (pending.FederationLinkId != request.LinkId || pending.FederationProfileVersion != request.OriginalProfileVersion)
                return new("VERSION_CONFLICT", null);
            var authority = await RequirePendingAuthority(pending, owner.User, owner.Account, request.Caller, ct).ConfigureAwait(false);
            var decision = await AccessAsync(owner.Account, "SIGN_IN", clock.GetUtcNow(), ct).ConfigureAwait(false);
            var now = clock.GetUtcNow(); var auth = policies.RequireEffective(now); auth.Validate(); var totp = CurrentTotpPolicy(now);
            return authority is not null && authority.Decision.EffectiveInterval.Contains(now) && decision?.Allowed == true &&
                decision.EffectiveInterval.Contains(now) && totp is not null && PendingCurrent(pending, request.Caller, owner.User, auth, totp, now) &&
                PendingPrimaryFactsCurrentAt(pending, owner.User, request.Caller, authority, auth, now)
                ? new(null, null, pending) : new("AUTHENTICATION_FAILED", null);
        }
        if (sessionId is not { } resultId || pending is not null && pending.RecoveryExpiresAt <= clock.GetUtcNow()) return new("VERSION_CONFLICT", null);
        var row = await database.Set<AuthenticationSessionRow>().SingleOrDefaultAsync(x => x.SessionId == resultId && x.AccountId == request.AccountId &&
            x.BrowserDigest == request.Caller.BrowserDigest && x.IdempotencyKey == request.RequestId && x.Origin == request.Caller.Origin, ct).ConfigureAwait(false);
        if (row is null) return new("VERSION_CONFLICT", null);
        await database.Entry(row).ReloadAsync(ct).ConfigureAwait(false);
        if (!IsFederationMethod(row.AuthenticationMethods) || row.FederationAttemptId != request.AttemptId || row.FederationLinkId != request.LinkId ||
            row.FederationNamespace != request.ProviderNamespace || row.FederationClientId != request.ClientId || row.RememberMe != request.RememberMe ||
            row.FederationProfileVersion != request.OriginalProfileVersion || pending is not null && (row.TotpAuthenticatorReference != pending.AcceptedAuthenticatorReference ||
                row.TotpGeneration != pending.AcceptedGeneration)) return new("VERSION_CONFLICT", null);
        var ticket = await ValidateAsync(row, request.Caller.Origin, clock.GetUtcNow(), ct).ConfigureAwait(false);
        return ticket is null ? new("AUTHENTICATION_FAILED", null) : new(null, new(keyProtection.Unprotect(row.ProtectedKey), ticket));
    }

    private async Task LockFederationContinuityAsync(AuthenticationSessionRow row, CancellationToken ct)
    {
        if (database.Database.CurrentTransaction is null) throw new InvalidOperationException("Federation continuity requires the current Identity transaction.");
        var account = await database.Accounts.FromSqlInterpolated(
            $"SELECT * FROM accounts.accounts WHERE account_id={row.AccountId} FOR NO KEY UPDATE").SingleOrDefaultAsync(ct).ConfigureAwait(false);
        if (account is not null) await database.Entry(account).ReloadAsync(ct).ConfigureAwait(false);
        var user = await database.Users.FromSqlInterpolated(
            $"""SELECT * FROM identity."AspNetUsers" WHERE "AccountId"={row.AccountId} FOR NO KEY UPDATE""").SingleOrDefaultAsync(ct).ConfigureAwait(false);
        if (user is not null) await database.Entry(user).ReloadAsync(ct).ConfigureAwait(false);
        _ = await database.VerifiedContacts.FromSqlInterpolated(
            $"SELECT * FROM accounts.verified_contacts WHERE account_id={row.AccountId} ORDER BY contact_id FOR SHARE").ToArrayAsync(ct).ConfigureAwait(false);
        if (row.FederationLinkId is { } id) _ = await LockFederationLinkAsync(id, row.AccountId, ct).ConfigureAwait(false);
    }
    private async Task<bool> FederationSessionCurrentAsync(AuthenticationSessionRow row, CancellationToken ct)
    {
        if (database.Database.CurrentTransaction is null || !IsFederationMethod(row.AuthenticationMethods) || row.FederationLinkId is not { } id) return false;
        if (!Enum.TryParse<FederationProviderKind>(row.FederationProvider, false, out var provider) || !Enum.IsDefined(provider) ||
            row.FederationProvider != provider.ToString() ||
            CurrentFederation(provider, row.Origin, clock.GetUtcNow()) is null) return false;
        var link = await LockFederationLinkAsync(id, row.AccountId, ct).ConfigureAwait(false);
        var source = link is null ? null : FederationAuthenticationSource.Read(row, link);
        return link is not null && source is not null && FederationCurrentAt(source, link, row.AccountId, row.Origin, clock.GetUtcNow(), false);
    }
    private bool FederationSessionFactsCurrentAt(AuthenticationSessionRow row, DateTimeOffset at)
    {
        if (database.Database.CurrentTransaction is null) return false;
        var link = row.FederationLinkId is { } id ? TrackedFederationLink(id, row.AccountId) : null;
        var source = link is null ? null : FederationAuthenticationSource.Read(row, link);
        return source is not null && link is not null && FederationCurrentAt(source, link, row.AccountId, row.Origin, at, false);
    }
    private ExternalIdentityLinkRow? TrackedFederationLink(Guid id, string account) => database.ChangeTracker.Entries<ExternalIdentityLinkRow>()
        .Where(x => x.State != EntityState.Deleted && x.State != EntityState.Detached).Select(x => x.Entity)
        .SingleOrDefault(x => x.LinkId == id && x.AccountId == account);
    private bool FederationPendingSourceCurrentAt(TotpPendingAuthenticationRow pending, AuthenticationCaller caller, DateTimeOffset at)
    {
        if (database.Database.CurrentTransaction is null) return false;
        var link = pending.FederationLinkId is { } id ? TrackedFederationLink(id, pending.AccountId) : null;
        var source = link is null ? null : FederationAuthenticationSource.Read(pending, link);
        return source is not null && link is not null && pending.PrimarySourceDigest == source.Digest &&
            FederationCurrentAt(source, link, pending.AccountId, caller.Origin, at, true);
    }
}
