namespace Tunner.Accounts;

/// <summary>Approved jurisdiction-scoped policy. No worldwide consent-age default is supplied.</summary>
public sealed record AccountAgePolicy(string Version, string JurisdictionReference, string ConsentPolicyVersion,
    int ConsentBelowAgeYears, bool StrongerConsentProofRequired, bool SchoolAuthorizationAllowed)
{
    public void Validate()
    {
        if (new[] { Version, JurisdictionReference, ConsentPolicyVersion }.Any(string.IsNullOrWhiteSpace) ||
            ConsentBelowAgeYears is < 0 or > 150 || ConsentBelowAgeYears == 0 && StrongerConsentProofRequired)
            throw new InvalidOperationException("A complete approved age/consent policy is required.");
    }
}

/// <summary>Supplied only by the trusted consent owner after its actor/authorization checks.</summary>
public sealed record AccountConsentEvidence(string AccountId, string EvidenceReference, string ConsentPolicyVersion,
    string Source, string AuthorizedActorReference, string VerificationReference,
    DateTimeOffset EffectiveFrom, DateTimeOffset? ExpiresAt, bool Revoked, string? StrongerProofReference);

public sealed record AccountCapabilityEvaluation(string AccountId, string AgeClassification,
    bool IndependentCommercialEligible, bool AgeAllowsAccountLifecycle, bool ConsentRequired, bool ConsentSatisfied,
    string? ConsentEvidenceReference, IReadOnlyList<string> AvailableConsentSources,
    string PolicyVersion, string ConsentPolicyVersion, string JurisdictionReference,
    DateTimeOffset EvaluatedAt, string EvidenceReference, string CorrelationId)
{
    // Age cannot itself deny identity/session/ordinary Product lifecycle. Consent policy is a distinct scoped control.
    public bool OrdinaryUseConsentSatisfied => !ConsentRequired || ConsentSatisfied;
    public string? PayerResolutionCode => IndependentCommercialEligible ? null : "PAYER_CONTEXT_REQUIRED";
}

public interface IAccountCommercialEligibilityReader
{
    Task<AccountCapabilityEvaluation?> ReadCurrentAsync(string accountId, CancellationToken cancellationToken = default);
}

/// <summary>Current PDR-0011 calculation. Re-evaluate at action time; never cache a permanent minor flag.</summary>
public sealed class AccountAgeCapabilityEvaluator(TimeProvider clock)
{
    public AccountCapabilityEvaluation Evaluate(string accountId, DateOnly dateOfBirth, AccountAgePolicy policy,
        IEnumerable<AccountConsentEvidence> consentEvidence, string evidenceReference, string correlationId)
    {
        ArgumentNullException.ThrowIfNull(clock); ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(consentEvidence); policy.Validate();
        foreach (var value in new[] { accountId, evidenceReference, correlationId }) ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var now = clock.GetUtcNow().ToUniversalTime(); var date = DateOnly.FromDateTime(now.UtcDateTime);
        if (dateOfBirth > date) throw new ArgumentException("A valid past or current calendar date is required.", nameof(dateOfBirth));
        var adult = AgeEligibilityEvaluator.HasReachedAge(dateOfBirth, date, 18);
        var required = policy.ConsentBelowAgeYears > 0 && !AgeEligibilityEvaluator.HasReachedAge(dateOfBirth, date, policy.ConsentBelowAgeYears);
        string[] sources = policy.SchoolAuthorizationAllowed ? ["GUARDIAN", "AUTHORIZED", "SCHOOL"] : ["GUARDIAN", "AUTHORIZED"];
        var evidence = consentEvidence.Where(x => x.AccountId == accountId && x.ConsentPolicyVersion == policy.ConsentPolicyVersion &&
            !x.Revoked && x.EffectiveFrom <= now && (!x.ExpiresAt.HasValue || now < x.ExpiresAt) && sources.Contains(x.Source, StringComparer.Ordinal) &&
            !string.IsNullOrWhiteSpace(x.EvidenceReference) && !string.IsNullOrWhiteSpace(x.AuthorizedActorReference) &&
            !string.IsNullOrWhiteSpace(x.VerificationReference) && (!policy.StrongerConsentProofRequired || !string.IsNullOrWhiteSpace(x.StrongerProofReference)))
            .OrderBy(x => x.EvidenceReference, StringComparer.Ordinal).FirstOrDefault();
        return new(accountId, adult ? "ADULT" : "UNDER_18", adult, true, required, required && evidence is not null,
            required ? evidence?.EvidenceReference : null, required ? sources : [], policy.Version, policy.ConsentPolicyVersion,
            policy.JurisdictionReference, now, evidenceReference, correlationId);
    }
}
