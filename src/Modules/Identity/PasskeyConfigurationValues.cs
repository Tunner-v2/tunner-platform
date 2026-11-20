using Tunner.Platform.Policies;

namespace Tunner.Identity.Persistence;

/// <summary>Serializable owner inputs, including all three fixed constructor invariants absent from PasskeyPolicy's getters.
/// No production value or native/provider behavior is selected here.</summary>
public sealed record PasskeyConfigurationValues(string Version, string RelyingPartyId, string Origin, TimeSpan CeremonyLifetime,
    TimeSpan AuthenticatorTimeout, TimeSpan ResultRecoveryLifetime, PasskeyRevocationSessionEffect RevocationSessionEffect,
    string ResidentKeyRequirement, IReadOnlyCollection<int> AllowedAlgorithms, int ChallengeBytes, int MaximumCredentialJsonBytes,
    int MaximumActiveCredentials, int MaximumPendingCeremonies, string EnrollmentAssurancePolicyVersion, string RevocationAssurancePolicyVersion,
    string ProcessingReference, string RetentionReference, string UserVerificationRequirement, string AttestationConveyancePreference,
    IReadOnlyDictionary<string, string> AssuranceMappings)
{
    public PasskeyPolicy ToPolicy() => new(Version, RelyingPartyId, Origin, CeremonyLifetime, AuthenticatorTimeout, ResultRecoveryLifetime,
        RevocationSessionEffect, ResidentKeyRequirement, AllowedAlgorithms, ChallengeBytes, MaximumCredentialJsonBytes, MaximumActiveCredentials,
        MaximumPendingCeremonies, EnrollmentAssurancePolicyVersion, RevocationAssurancePolicyVersion, ProcessingReference, RetentionReference,
        UserVerificationRequirement, AttestationConveyancePreference, AssuranceMappings);

    internal PasskeyConfigurationValues Snapshot()
    {
        // The unchanged domain constructor validates and detaches both collections, preserving algorithm order.
        var policy = ToPolicy(); return this with { AllowedAlgorithms = policy.AllowedAlgorithms, AssuranceMappings = policy.AssuranceMappings };
    }
}
public sealed record PasskeyConfigurationVersion(PolicyVersionCandidate<string> Metadata, PasskeyConfigurationValues Values);
