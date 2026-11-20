using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Tunner.Accounts;
using Tunner.Database.Outbox;

namespace Tunner.Identity.Persistence;

internal sealed partial class RecoveryCaseApplication
{
    private IDataProtector ContactProofProtection => recoveryProtection!.CreateProtector("Tunner.Identity.Recovery.ContactChallenge.v1");
    private IDataProtector ContactAddressProtection => recoveryProtection!.CreateProtector("Tunner.Identity.Recovery.ContactAddress.v1");
    internal bool ContactAvailable(string origin, DateTimeOffset at) => PasswordAvailable && contactRisk is not null && contactNotifications is not null &&
        contactDispatcher is not null && recoveryProtection is not null && PasswordCurrent(origin, at).Values.ContactRecoveryProfile is not null;
    private static RecoveryVerifiedContactFacts ContactFacts(VerifiedContactRow contact) => new(contact.ContactId, contact.Version, contact.AccountId,
        contact.State.ToString().ToUpperInvariant(), AuthenticationIdentity.Digest(contact.NormalizedEmail!), contact.EvidenceReference);
    private RecoveryCaseRow? PrepareContactPasswordCase(RecoveryCaseRow row, RecoveryEntryCaller caller, RecoveryEntryRow entry,
        RecoveryCaseLockedFacts facts, IReadOnlyList<RecoveryCaseCodeSetFacts> sets, RecoveryPasswordPolicyVersion policy, RecoveryCaseHierarchyFacts posture,
        VerifiedContactRow? contact, RecoveryCaseHierarchyDecision decision, DateTimeOffset at)
    {
        var profile = policy.Values.ContactRecoveryProfile;
        if (!ContactAvailable(caller.Origin, at) || profile is null || contact is null || !ContactSelected(decision, ContactFacts(contact), profile, posture, at)) return null;
        row.ProofKind = "CONTACT_RISK"; row.State = "PENDING_CONTACT_RISK"; row.SetReference = null; row.ExpectedSetLifecycleVersion = null;
        row.ExpectedUsageVersion = null; row.UsageAdmission = null; row.ContactReference = contact.ContactId; row.ExpectedContactVersion = contact.Version;
        row.ContactDestinationDigest = AuthenticationIdentity.Digest(contact.NormalizedEmail!); row.ContactChallengeId = Guid.NewGuid(); row.ContextDigest = Context(row);
        RecoveryContactRiskDecision risk;
        try { risk = contactRisk!.RequireCurrent(ContactRiskRequest(row, facts, contact, profile), at); }
        catch (Exception error) when (error is InvalidOperationException or ArgumentException) { return null; }
        if (risk is null || !RiskAdmitted(row, facts, contact, profile, risk, at)) return null;
        row.InitialContactRiskSnapshot = risk.Snapshot(); row.HierarchySnapshot = decision.Snapshot();
        row.ExpiresAt = IdentityPersistenceTime.Normalize(Clip(row.CreatedAt + policy.Values.CaseLifetime, entry.ExpiresAt, policy.Metadata.EffectiveInterval.Until,
            decision.EffectiveInterval.Until, risk.EffectiveInterval.Until)); row.RecoveryExpiresAt = entry.RecoveryExpiresAt;
        return at < row.ExpiresAt && ContactPasswordAuthorityCurrent(caller, entry, row, facts, sets, at, false, out _) ? row : null;
    }
    private static bool ContactSelected(RecoveryCaseHierarchyDecision decision, RecoveryVerifiedContactFacts contact, RecoveryContactProfile profile,
        RecoveryCaseHierarchyFacts posture, DateTimeOffset at) => decision.StrongAuthenticatorUnavailable && !decision.RecoveryCodeAllowed &&
        decision.CodeSelection is null && decision.PasskeySelection is null && decision.ContactSelection == new RecoveryCaseContactSelection(contact.ContactReference, contact.Version, profile.Version, true) &&
        decision.FactsDigest == posture.Digest() && !string.IsNullOrWhiteSpace(decision.PolicyVersion) && !string.IsNullOrWhiteSpace(decision.EvidenceReference) && decision.EffectiveInterval.Contains(at);
    private static RecoveryContactRiskRequest ContactRiskRequest(RecoveryCaseRow row, RecoveryCaseLockedFacts facts, VerifiedContactRow contact, RecoveryContactProfile profile) =>
        new(row.CaseId, row.RequestId, row.AccountId, row.ContextDigest, profile.Version, profile.RiskPolicyVersion, RecoveryCaseGoal.RepairPassword,
            row.OriginalBrowserDigest, row.Origin, row.NetworkDigest, facts.Value() with { SecurityStampDigest = row.PriorSecurityStampDigest!, PasswordAvailable = true }, ContactFacts(contact),
            row.ProcessingReference, row.RetentionReference);
    private static bool RiskAdmitted(RecoveryCaseRow row, RecoveryCaseLockedFacts facts, VerifiedContactRow contact, RecoveryContactProfile profile,
        RecoveryContactRiskDecision decision, DateTimeOffset at) => decision.CaseId == row.CaseId && decision.RequestId == row.RequestId && decision.AccountId == row.AccountId &&
        decision.ContextDigest == row.ContextDigest && decision.ContactReference == contact.ContactId && decision.ContactVersion == contact.Version &&
        decision.DestinationDigest == row.ContactDestinationDigest && decision.FactsDigest == (facts.Value() with { SecurityStampDigest = row.PriorSecurityStampDigest!, PasswordAvailable = true }).Digest() &&
        decision.PolicyVersion == profile.RiskPolicyVersion && decision.Outcome == "ALLOW" && !string.IsNullOrWhiteSpace(decision.DecisionReference) &&
        !string.IsNullOrWhiteSpace(decision.EvidenceReference) && decision.EffectiveInterval.Contains(at);
    private bool ContactPasswordAuthorityCurrent(RecoveryEntryCaller caller, RecoveryEntryRow entry, RecoveryCaseRow row, RecoveryCaseLockedFacts facts,
        IReadOnlyList<RecoveryCaseCodeSetFacts> sets, DateTimeOffset at, bool repaired, out RecoveryContactRiskDecision? currentRisk)
    {
        currentRisk = null; var policy = PasswordCurrent(caller.Origin, at); var profile = policy.Values.ContactRecoveryProfile;
        var contact = database.VerifiedContacts.Local.SingleOrDefault(x => x.ContactId == row.ContactReference);
        if (profile is null || contact is null || contact.NormalizedEmail is null || contact.State != ContactVerificationState.Verified ||
            contact.AnonymizedAtUtc is not null || contact.Version != row.ExpectedContactVersion || !ContactAvailable(caller.Origin, at)) return false;
        var posture = facts.Value() with { SecurityStampDigest = row.PriorSecurityStampDigest!, PasswordAvailable = true, CodeSets = sets };
        RecoveryCaseHierarchyDecision hierarchyNow;
        try
        {
            hierarchyNow = hierarchy!.RequireCurrent(new(row.CaseId, row.RequestId, PasswordSelectionContext(entry), row.PolicyVersion, RecoveryCaseGoal.RepairPassword, posture)
                { VerifiedContact = ContactFacts(contact) }, at);
            currentRisk = contactRisk!.RequireCurrent(ContactRiskRequest(row, facts, contact, profile), at);
        }
        catch (Exception error) when (error is InvalidOperationException or ArgumentException) { return false; }
        if (hierarchyNow is null || currentRisk is null) return false;
        return row.ProofKind == "CONTACT_RISK" && row.Goal == nameof(RecoveryCaseGoal.RepairPassword) && row.State == (repaired ? "REPAIRED" : "PENDING_CONTACT_RISK") &&
            row.SetReference is null && row.ExpectedSetLifecycleVersion is null && row.ExpectedUsageVersion is null && row.UsageAdmission is null &&
            row.PasskeyCredentialReference is null && row.PasskeyCeremonyId is null && row.ContactChallengeId is not null && row.ContactReference == contact.ContactId &&
            row.ExpectedContactVersion == contact.Version && contact.AccountId == row.AccountId && contact.State == ContactVerificationState.Verified && contact.AnonymizedAtUtc is null &&
            contact.NormalizedEmail is not null && row.ContactDestinationDigest == AuthenticationIdentity.Digest(contact.NormalizedEmail) &&
            contact.NormalizedEmail == facts.User.NormalizedEmail && facts.User.EmailConfirmed && facts.User.Email is not null &&
            new Microsoft.AspNetCore.Identity.UpperInvariantLookupNormalizer().NormalizeEmail(facts.User.Email) == contact.NormalizedEmail &&
            database.Entry(contact).State == EntityState.Unchanged && facts.OtherSourcesUnmutated(database) && UserSourceDigest(facts.User) == row.UserSourceDigest &&
            (repaired || facts.User.PasswordHash is not null && AuthenticationIdentity.Digest(facts.User.PasswordHash) == row.PriorPasswordDigest &&
                AuthenticationIdentity.Digest(facts.User.SecurityStamp!) == row.PriorSecurityStampDigest) &&
            facts.Account.AccountId == row.AccountId && facts.Account.Version == row.ExpectedAccountVersion && facts.Account.State.ToString() == row.AccountState &&
            posture.Digest() == row.FactsDigest && ContactSelected(hierarchyNow, ContactFacts(contact), profile, posture, at) && hierarchyNow.Snapshot() == row.HierarchySnapshot &&
            RiskAdmitted(row, facts, contact, profile, currentRisk, at) && !string.IsNullOrWhiteSpace(row.InitialContactRiskSnapshot) &&
            row.PolicySnapshot == policy.Snapshot && row.SessionEffect == nameof(RecoveryPasswordSessionEffect.InvalidateExistingTunnerSessions) &&
            row.EntryId == entry.EntryId && row.RequestId == entry.RequestId && row.RecoveryExpiresAt == entry.RecoveryExpiresAt && Context(row) == row.ContextDigest &&
            row.TargetSessionId is null && row.ExpectedSessionVersion is null && EntryCurrent(caller, entry, at) && row.OriginalBrowserDigest == caller.BrowserDigest &&
            row.Origin == caller.Origin && row.NetworkDigest == caller.NetworkDigest && at >= row.CreatedAt;
    }
    private async Task<VerifiedContactRow?> LockRecoveryContact(RecoveryCaseRow row, CancellationToken ct)
    {
        var contact = await database.VerifiedContacts.FromSqlInterpolated($"SELECT * FROM accounts.verified_contacts WHERE contact_id={row.ContactReference} AND account_id={row.AccountId} FOR SHARE")
            .SingleOrDefaultAsync(ct).ConfigureAwait(false);
        if (contact is not null) await database.Entry(contact).ReloadAsync(ct).ConfigureAwait(false); return contact;
    }
    private RecoveryContactNotificationDecision NotificationCurrent(RecoveryCaseRow row, RecoveryContactProfile profile, DateTimeOffset at)
    {
        var request = new RecoveryContactNotificationRequest(row.CaseId, row.ContactChallengeId!.Value, row.AccountId, row.ContactReference!.Value,
            row.ExpectedContactVersion!.Value, row.ContactDestinationDigest!, row.ContextDigest, profile.NotificationContractReference, row.ProcessingReference, row.RetentionReference);
        var decision = contactNotifications!.RequireCurrent(request, at);
        if (decision is null || decision.CaseId != row.CaseId || decision.ChallengeId != row.ContactChallengeId || decision.AccountId != row.AccountId ||
            decision.ContactReference != row.ContactReference || decision.ContactVersion != row.ExpectedContactVersion || decision.DestinationDigest != row.ContactDestinationDigest ||
            decision.ContextDigest != row.ContextDigest || decision.ContractReference != profile.NotificationContractReference || decision.Channel != "EMAIL" ||
            decision.NotificationClass is not ("MANDATORY_SECURITY" or "MANDATORY_TRANSACTIONAL" or "SERVICE_OPERATIONAL" or "PRODUCT_OPTIONAL" or "MARKETING") ||
            new[] { decision.TemplateVersion, decision.CodeVariableReference, decision.Locale, decision.RecipientEvidenceReference, decision.RouteResolutionReference, decision.EvidenceReference }.Any(string.IsNullOrWhiteSpace) ||
            decision.ProcessingReference != row.ProcessingReference || decision.RetentionReference != row.RetentionReference || !decision.EffectiveInterval.Contains(at))
            throw new InvalidOperationException("Current approved recovery notification contract/recipient/route authority is unavailable.");
        return decision;
    }
    private static string ContactProofBinding(RecoveryCaseRow row) => row.ContactChallengeId!.Value.ToString("N") + ":" + row.ContextDigest + ":" + row.PolicyVersion;
    private bool ContactNotificationMatches(RecoveryCaseRow row, RecoveryContactProfile profile, RecoveryContactChallengeRow challenge, DateTimeOffset at)
    {
        try { return NotificationCurrent(row, profile, at).Snapshot() == challenge.NotificationSnapshot; }
        catch (Exception error) when (error is InvalidOperationException or ArgumentException) { return false; }
    }
}
