using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Tunner.Accounts;

namespace Tunner.Identity.Persistence;

/// <summary>Optional trusted admission, covered by the existing provider profile's approval/currentness snapshot.</summary>
public sealed record FederationRegistrationProfile(string Version, string RegistrationPolicyVersion,
    string ProviderVerifiedEmailMappingEvidenceReference)
{
    internal bool Complete => new[] { Version, RegistrationPolicyVersion, ProviderVerifiedEmailMappingEvidenceReference }
        .All(x => !string.IsNullOrWhiteSpace(x) && !x.Any(char.IsControl));
    public override string ToString() => nameof(FederationRegistrationProfile);
}

/// <summary>INTERNAL producer result only, never HTTP input. The native owner must persist its matching protected
/// contact/subject resolution on the SAME SignIn attempt before it can construct this value.</summary>
internal sealed class FederationRegistrationIntake(FederationAttemptRow originalAttempt, FederationIdentityKey identity,
    string verifiedEmail, string contactMappingEvidenceReference, string originalRegistrationBrowserDigest)
{
    [JsonIgnore] internal FederationAttemptRow Attempt { get; } = originalAttempt;
    [JsonIgnore] internal FederationIdentityKey Identity { get; } = identity;
    [JsonIgnore] internal string VerifiedEmail { get; } = verifiedEmail;
    internal string ContactMappingEvidenceReference { get; } = contactMappingEvidenceReference;
    internal string RegistrationBrowserDigest { get; } = originalRegistrationBrowserDigest;
    public override string ToString() => nameof(FederationRegistrationIntake);
}

public sealed partial class RegistrationApplication
{
    // No transaction, endpoint, native callback or DI activation is added here. The existing attempt completion owner
    // calls this under its original browser+attempt locks and transaction, after reading its durable closed intake result.
    internal async Task<RegistrationResult> IntakeFederationInTransactionAsync(RegistrationCaller caller,
        FederationCurrentBrowser browser, FederationRegistrationIntake intake, Guid idempotencyKey,
        string termsVersion, string privacyVersion, bool acknowledgedNotices, CancellationToken ct)
    {
        caller.Validate(); browser.Caller.Validate();
        if (database.Database.CurrentTransaction is null || !browser.OriginalFingerprint ||
            caller.BrowserDigest != intake.RegistrationBrowserDigest || idempotencyKey == Guid.Empty ||
            database.Entry(intake.Attempt).State is EntityState.Detached or EntityState.Deleted)
            throw new InvalidOperationException("An original Identity-owned federation intake transaction is required.");
        var now = timeProvider.GetUtcNow(); var policy = policies.RequireEffective(now); policy.Validate();
        if (!await AllowAsync(caller,"REGISTER",policy.RegisterLimit,policy,now,ct).ConfigureAwait(false))
            return Failure(Guid.Empty,"RATE_OR_ABUSE_LIMITED");
        await LockAsync("registration:" + caller.BrowserDigest + idempotencyKey,ct).ConfigureAwait(false);
        var prior = await database.Set<RegistrationOperationRow>().SingleOrDefaultAsync(x =>
            x.BrowserDigest == caller.BrowserDigest && x.IdempotencyKey == idempotencyKey,ct).ConfigureAwait(false);
        if (prior is not null)
            return prior.Mode == "FEDERATED" && prior.FederationAttemptId == intake.Attempt.AttemptId && now < prior.PurgeAfter
                ? await DescribeAsync(prior,now,ct).ConfigureAwait(false) : Failure(Guid.Empty,"NOT_FOUND_OR_NOT_DISCLOSABLE");
        var source = intake.Attempt;
        if (!acknowledgedNotices || termsVersion != policy.TermsVersion || privacyVersion != policy.PrivacyVersion ||
            !ValidEmail(intake.VerifiedEmail) || source.Purpose != nameof(FederationAttemptPurpose.SignIn) || source.State != "VERIFIED" ||
            source.LinkId is not null || source.BrowserKey != browser.BrowserKey || source.BrowserDigest != browser.Caller.BrowserDigest ||
            source.Origin != browser.Caller.Origin || source.PreviousCookieDigest != browser.OriginalCookieDigest ||
            source.PreviousSessionId != browser.OriginalPreviousSessionId || source.VerifiedAt is null ||
            source.VerificationExpiresAt is not { } verifiedExpiry || verifiedExpiry <= now)
            return Failure(Guid.Empty,"NOT_FOUND_OR_NOT_DISCLOSABLE");
        var email = intake.VerifiedEmail.Trim(); var normalized = Normalizer.NormalizeEmail(email)!;
        await LockAsync("email:" + normalized,ct).ConfigureAwait(false);
        // These are the exact existing Identity email and external-key reservation locks. Never adopt a pending Account by email.
        await database.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({"federation-key:" + AuthenticationIdentity.Digest(JsonSerializer.Serialize(new[] { intake.Identity.ProviderNamespace, intake.Identity.Subject }))},0))",ct).ConfigureAwait(false);
        var usedContact = await database.VerifiedContacts.AnyAsync(x => x.NormalizedEmail == normalized && x.AnonymizedAtUtc == null,ct).ConfigureAwait(false);
        var usedIdentity = await database.ExternalIdentityLinks.AnyAsync(x => x.Provider == intake.Identity.ProviderNamespace && x.Subject == intake.Identity.Subject,ct).ConfigureAwait(false);
        var duplicateSource = await database.Set<RegistrationOperationRow>().AnyAsync(x => x.FederationAttemptId == source.AttemptId,ct).ConfigureAwait(false);
        now = timeProvider.GetUtcNow(); var profile = CurrentRegistrationProfile(source.Provider,source.Origin,now);
        if (usedContact || usedIdentity || duplicateSource || profile is null || profile.Snapshot != source.ProfileSnapshot ||
            profile.Values.Registration is not { Complete: true } registration || registration.RegistrationPolicyVersion != policy.Version ||
            registration.ProviderVerifiedEmailMappingEvidenceReference != intake.ContactMappingEvidenceReference ||
            profile.Registration.ProviderNamespace != intake.Identity.ProviderNamespace || profile.Values.Version != intake.Identity.RegistrationProfileVersion ||
            profile.Values.IssuerMappingEvidenceReference != intake.Identity.IssuerMappingEvidenceReference ||
            policies.RequireEffective(now).Version != policy.Version || browser.BrowserExpiresAt <= now ||
            now < source.CreatedAt || source.ExpiresAt <= now || source.VerificationExpiresAt <= now)
            return Failure(Guid.Empty,"NOT_FOUND_OR_NOT_DISCLOSABLE");
        var operation = new RegistrationOperationRow { OperationId = Guid.NewGuid(), Mode = "FEDERATED",
            BrowserDigest = caller.BrowserDigest, IdempotencyKey = idempotencyKey, CorrelationId = source.CorrelationId,
            PolicyVersion = policy.Version, TermsVersion = policy.TermsVersion, PrivacyVersion = policy.PrivacyVersion,
            ProcessingReference = policy.ProcessingReference, RetentionReference = policy.RetentionReference,
            CreatedAt = now, ExpiresAt = now + policy.ProofLifetime, ResendAfter = now + policy.ResendCooldown,
            PurgeAfter = now + policy.PendingMaterialRetention, Version = 1, Stage = "VERIFY_CONTACT", NormalizedEmail = normalized,
            FederationAttemptId = source.AttemptId, FederationProvider = source.Provider, FederationOrigin = source.Origin,
            FederationProfileSnapshot = profile.Snapshot, FederationContactMappingEvidenceReference = intake.ContactMappingEvidenceReference,
            FederationVerifiedContactDigest = AuthenticationIdentity.Digest(normalized), FederationContactVerifiedAt = source.VerifiedAt };
        var account = new AccountRow { AccountId = "ACC-" + Guid.NewGuid().ToString("N"), State = AccountState.VerificationPending,
            Version = 1, RecordedAtUtc = now, OperationId = OperationReference(operation.OperationId), CorrelationId = operation.CorrelationId,
            EvidenceReference = "registration:" + operation.OperationId };
        var contact = new VerifiedContactRow { ContactId = Guid.NewGuid(), AccountId = account.AccountId, NormalizedEmail = normalized,
            State = ContactVerificationState.Unverified, Version = 1, EvidenceReference = "registration-contact:" + operation.OperationId };
        var user = new IdentityUserAdapter { Id = Guid.NewGuid().ToString("N"), AccountId = account.AccountId,
            UserName = account.AccountId, NormalizedUserName = Normalizer.NormalizeName(account.AccountId), Email = email,
            NormalizedEmail = normalized, EmailConfirmed = false, SecurityStamp = Guid.NewGuid().ToString("N"), ConcurrencyStamp = Guid.NewGuid().ToString("N") };
        operation.AccountId = account.AccountId; operation.ContactId = contact.ContactId;
        database.Add(operation); database.Add(account); database.Add(contact); database.Users.Add(user);
        AddEvent(operation,"AccountRegistered",account.AccountId,account.Version,now);
        // The mode selects an actual independent evidence installer; a missing credential never selects federation.
        return CompleteRegistrationContact(operation,account,contact,user,profile,intake,now);
    }

    private async Task<RegistrationResult> CompleteRegistrationContactAsync(RegistrationOperationRow operation,
        DateTimeOffset now, CancellationToken ct)
    {
        await LockAsync("email:" + operation.NormalizedEmail,ct).ConfigureAwait(false);
        var account = await database.Accounts.SingleAsync(x => x.AccountId == operation.AccountId,ct).ConfigureAwait(false);
        var contact = await database.VerifiedContacts.SingleAsync(x => x.ContactId == operation.ContactId,ct).ConfigureAwait(false);
        var collision = await database.VerifiedContacts.AnyAsync(x => x.AccountId != account.AccountId && x.NormalizedEmail == operation.NormalizedEmail &&
            x.State == ContactVerificationState.Verified && x.AnonymizedAtUtc == null,ct).ConfigureAwait(false);
        if (account.State != AccountState.VerificationPending || contact.State != ContactVerificationState.Unverified || collision ||
            operation.Mode != "PASSWORD" || operation.ProtectedCredential is null)
            return Failure(operation.OperationId,"NOT_FOUND_OR_NOT_DISCLOSABLE",operation);
        var user = await database.Users.SingleAsync(x => x.AccountId == account.AccountId,ct).ConfigureAwait(false);
        return CompleteRegistrationContact(operation,account,contact,user,null,null,now);
    }

    private RegistrationResult CompleteRegistrationContact(RegistrationOperationRow operation, AccountRow account,
        VerifiedContactRow contact, IdentityUserAdapter user, FederationProfileVersion? profile,
        FederationRegistrationIntake? intake, DateTimeOffset now)
    {
        if (operation.Mode == "PASSWORD" && intake is null && profile is null && operation.ProtectedCredential is { } credential)
            user.PasswordHash = credentialProtection.Unprotect(credential);
        else if (operation.Mode == "FEDERATED" && operation.ProtectedCredential is null && user.PasswordHash is null &&
            intake is not null && profile is not null && operation.FederationAttemptId == intake.Attempt.AttemptId)
        {
            var link = intake.Identity.PrepareOwnedLink(Guid.NewGuid(),account.AccountId,OperationReference(operation.OperationId));
            link.LinkState = "ACTIVE"; link.QualifiedClientId = profile.Values.ClientId;
            link.QualificationProfileVersion = profile.Values.Version; link.IssuerMappingEvidenceReference = profile.Values.IssuerMappingEvidenceReference;
            link.QualificationEvidenceReference = OperationReference(operation.OperationId);
            operation.FederationLinkId = link.LinkId; database.Add(link);
            AddEvent(operation,"ExternalIdentityLinked",link.LinkId.ToString("N"),link.Version,now);
        }
        else throw new InvalidOperationException("A mode-bound registration contact source is required.");
        user.EmailConfirmed = true; user.SecurityStamp = Guid.NewGuid().ToString("N"); user.ConcurrencyStamp = Guid.NewGuid().ToString("N");
        contact.State = ContactVerificationState.Verified; contact.Version++;
        contact.EvidenceReference = (operation.Mode == "PASSWORD" ? "contact-proof:" : "federated-contact:") + operation.OperationId;
        if (operation.Mode == "FEDERATED") operation.FederationContactVersion = contact.Version;
        operation.Stage = "PERSONAL_INFORMATION"; operation.Version++; ClearMaterial(operation);
        AddEvent(operation,"VerifiedContactChanged",contact.ContactId.ToString("N"),contact.Version,now);
        return Result(operation);
    }

    private FederationProfileVersion? CurrentRegistrationProfile(string provider, string origin, DateTimeOffset at)
    {
        if (!Enum.TryParse<FederationProviderKind>(provider,false,out var kind) || kind != FederationProviderKind.Google) return null;
        var sources = federationProfiles?.Take(2).ToArray(); if (sources is not { Length: 1 }) return null;
        try { var profile = sources[0].RequireCurrent(kind,at); profile.Require(origin,at);
            return profile.Values.Provider == kind && profile.Values.Registration is { Complete: true } ? profile : null; }
        catch (InvalidOperationException) { return null; }
    }

    private async Task<bool> FederatedActivationCurrentAsync(RegistrationOperationRow operation, CancellationToken ct)
    {
        if (operation.Mode == "PASSWORD") return operation.FederationAttemptId is null && operation.FederationLinkId is null;
        if (operation.Mode != "FEDERATED" || operation.AccountId is null || operation.ContactId is null ||
            operation.FederationAttemptId is null || operation.FederationLinkId is null || operation.FederationProvider is null ||
            operation.FederationOrigin is null || operation.FederationContactVerifiedAt is null) return false;
        var contact = await database.VerifiedContacts.SingleOrDefaultAsync(x => x.ContactId == operation.ContactId && x.AccountId == operation.AccountId,ct).ConfigureAwait(false);
        var user = await database.Users.SingleOrDefaultAsync(x => x.AccountId == operation.AccountId,ct).ConfigureAwait(false);
        var link = await database.ExternalIdentityLinks.SingleOrDefaultAsync(x => x.LinkId == operation.FederationLinkId && x.AccountId == operation.AccountId,ct).ConfigureAwait(false);
        var at = timeProvider.GetUtcNow(); var profile = CurrentRegistrationProfile(operation.FederationProvider,operation.FederationOrigin,at);
        return profile is not null && profile.Snapshot == operation.FederationProfileSnapshot &&
            profile.Values.Registration is { Complete: true } registration && registration.RegistrationPolicyVersion == operation.PolicyVersion &&
            registration.ProviderVerifiedEmailMappingEvidenceReference == operation.FederationContactMappingEvidenceReference &&
            policies.RequireEffective(at).Version == operation.PolicyVersion && at < operation.PurgeAfter && operation.FederationContactVerifiedAt <= at &&
            contact is { State: ContactVerificationState.Verified, AnonymizedAtUtc: null, NormalizedEmail: not null } &&
            contact.Version == operation.FederationContactVersion && contact.EvidenceReference == "federated-contact:" + operation.OperationId &&
            AuthenticationIdentity.Digest(contact.NormalizedEmail) == operation.FederationVerifiedContactDigest &&
            user is { EmailConfirmed: true, PasswordHash: null } && user.NormalizedEmail == contact.NormalizedEmail &&
            link is { LinkState: "ACTIVE", Version: 1 } && link.Provider == profile.Values.StableNamespaceId &&
            link.QualifiedClientId == profile.Values.ClientId && link.QualificationProfileVersion == profile.Values.Version &&
            link.IssuerMappingEvidenceReference == profile.Values.IssuerMappingEvidenceReference &&
            link.QualificationEvidenceReference == OperationReference(operation.OperationId);
    }

    internal bool FederationIntakeCurrentAt(Guid operationId, FederationRegistrationIntake intake,
        FederationRegistrationBrowser browser, FederationRegistrationAcknowledgement notice, DateTimeOffset at)
    {
        if (database.Database.CurrentTransaction is null || browser.ExpiresAt <= at || !notice.AcknowledgedNotices) return false;
        var operation = database.ChangeTracker.Entries<RegistrationOperationRow>().Select(x => x.Entity).SingleOrDefault(x => x.OperationId == operationId);
        if (operation is null || operation.Mode != "FEDERATED" || operation.FederationAttemptId != intake.Attempt.AttemptId ||
            operation.BrowserDigest != browser.Caller.BrowserDigest || operation.IdempotencyKey != browser.IdempotencyKey ||
            operation.Stage != "PERSONAL_INFORMATION" || operation.Status != "REQUIRES_ACTION" || at >= operation.PurgeAfter) return false;
        var profile = CurrentRegistrationProfile(intake.Attempt.Provider,intake.Attempt.Origin,at);
        var policy = policies.RequireEffective(at); policy.Validate();
        var account = database.ChangeTracker.Entries<AccountRow>().Select(x => x.Entity).SingleOrDefault(x => x.AccountId == operation.AccountId);
        var user = database.ChangeTracker.Entries<IdentityUserAdapter>().Select(x => x.Entity).SingleOrDefault(x => x.AccountId == operation.AccountId);
        var contact = database.ChangeTracker.Entries<VerifiedContactRow>().Select(x => x.Entity).SingleOrDefault(x => x.ContactId == operation.ContactId);
        var link = database.ChangeTracker.Entries<ExternalIdentityLinkRow>().Select(x => x.Entity).SingleOrDefault(x => x.LinkId == operation.FederationLinkId);
        return profile is not null && profile.Snapshot == operation.FederationProfileSnapshot && policy.Version == operation.PolicyVersion &&
            notice.TermsVersion == policy.TermsVersion && notice.PrivacyVersion == policy.PrivacyVersion &&
            profile.Values.Registration is { Complete: true } qualification && qualification.RegistrationPolicyVersion == policy.Version &&
            qualification.ProviderVerifiedEmailMappingEvidenceReference == operation.FederationContactMappingEvidenceReference &&
            account is { State: AccountState.VerificationPending, Version: 1 } && user is { EmailConfirmed: true, PasswordHash: null } &&
            contact is { State: ContactVerificationState.Verified, AnonymizedAtUtc: null, NormalizedEmail: not null } &&
            contact.AccountId == operation.AccountId && contact.Version == operation.FederationContactVersion &&
            AuthenticationIdentity.Digest(contact.NormalizedEmail) == operation.FederationVerifiedContactDigest && user.NormalizedEmail == contact.NormalizedEmail &&
            link is { LinkState: "ACTIVE", Version: 1 } && link.AccountId == operation.AccountId && link.Provider == intake.Identity.ProviderNamespace &&
            link.Subject == intake.Identity.Subject && link.QualifiedClientId == profile.Values.ClientId &&
            link.QualificationEvidenceReference == OperationReference(operation.OperationId) &&
            link.QualificationProfileVersion == profile.Values.Version && link.IssuerMappingEvidenceReference == profile.Values.IssuerMappingEvidenceReference;
    }

    internal async Task<RegistrationResult?> ReadFederationOriginalInTransactionAsync(FederationRegistrationBrowser browser,
        Guid operationId, Guid attemptId, CancellationToken ct)
    {
        if (database.Database.CurrentTransaction is null) throw new InvalidOperationException("The original Identity owner transaction is required.");
        var now = timeProvider.GetUtcNow(); var policy = policies.RequireEffective(now); policy.Validate();
        if (!await AllowAsync(browser.Caller,"STATUS",policy.LookupLimit,policy,now,ct).ConfigureAwait(false)) return null;
        var operation = await OwnedAsync(browser.Caller,operationId,ct).ConfigureAwait(false);
        return operation is not null && operation.Mode == "FEDERATED" && operation.FederationAttemptId == attemptId &&
            operation.IdempotencyKey == browser.IdempotencyKey && now < operation.PurgeAfter
            ? await DescribeAsync(operation,now,ct).ConfigureAwait(false) : null;
    }

    internal bool FederationRegistrationPolicyCurrentAt(FederationProfileVersion profile, DateTimeOffset at)
    {
        var policy = policies.RequireEffective(at); policy.Validate();
        return profile.Values.Provider == FederationProviderKind.Google && profile.Values.Registration is { Complete: true } qualification &&
            qualification.RegistrationPolicyVersion == policy.Version;
    }

    internal bool FederationRegistrationNoticeCurrentAt(FederationProfileVersion profile,FederationRegistrationAcknowledgement notice,DateTimeOffset at)
    {
        var policy = policies.RequireEffective(at); policy.Validate();
        return FederationRegistrationPolicyCurrentAt(profile,at) && notice.TermsVersion == policy.TermsVersion && notice.PrivacyVersion == policy.PrivacyVersion;
    }
    internal Task<RegistrationResult> DescribeFederationIntakeInTransactionAsync(Guid operationId,CancellationToken ct)
    {
        if (database.Database.CurrentTransaction is null) throw new InvalidOperationException("The original Identity owner transaction is required.");
        var row = database.ChangeTracker.Entries<RegistrationOperationRow>().Select(x => x.Entity).Single(x => x.OperationId == operationId && x.Mode == "FEDERATED");
        return DescribeAsync(row,timeProvider.GetUtcNow(),ct);
    }
}
