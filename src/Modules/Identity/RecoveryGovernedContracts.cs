using System.Text.Json;
using System.Text.Json.Serialization;
using Tunner.Platform.Policies;

namespace Tunner.Identity.Persistence;

/// <summary>Optional approved consumer qualification; it does not create a review workflow, reviewer role or approval.</summary>
public sealed record RecoveryGovernedProfile(string Version, string DispositionContractReference)
{
    internal void Validate()
    {
        if (new[] { Version, DispositionContractReference }.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("Explicit authoritative governed-recovery disposition contract qualification is required.");
    }
    public override string ToString() => nameof(RecoveryGovernedProfile);
}
public sealed record RecoveryGovernedContactFacts(Guid Reference, long Version, string State, bool Anonymized, string? DestinationDigest)
{
    public override string ToString() => nameof(RecoveryGovernedContactFacts);
}
public sealed record RecoveryGovernedSelection(string ProfileVersion, bool RecoveryCodesUnavailable, bool SafeContactUnavailable,
    string UnavailabilityEvidenceReference);
public sealed record RecoveryGovernedDispositionRequest(Guid CaseId, Guid RequestId, string AccountId, long OriginalCaseVersion,
    long AccountVersion, string ContextDigest, string FactsDigest, string UserSourceDigest, string ContactsDigest,
    string Goal, string SessionEffect, string RecoveryPolicyVersion, string ProfileVersion, string DispositionContractReference,
    string UnresolvedEffectsJson, string EffectsEncoding, string EffectsDigest,
    string ProcessingReference, string RetentionReference)
{
    /// <summary>SHA256 uppercase hex over UTF-8 fields Goal, SessionEffect, UnresolvedEffectsJson in that order.
    /// Each field is its invariant decimal UTF-8 byte count, a colon, then its exact text; no JSON reserialization or normalization.
    /// The authoritative producer must independently bind these exact effects using this encoding.</summary>
    public const string RequiredEffectsEncoding = "UTF8_BYTE_LENGTH_PREFIXED_FIELDS_V1";
}
/// <summary>Projection of an actual immutable Identity/Security owner disposition, after that owner validates its required controls.
/// AuthorizesExactEffect is not an ApprovalRequest/IdentityVerificationRecord status. No customer or Support payload can supply this projection.</summary>
public sealed record RecoveryGovernedDisposition(Guid CaseId, Guid RequestId, string AccountId, long OriginalCaseVersion,
    long AccountVersion, string ContextDigest, string FactsDigest, string UserSourceDigest, string ContactsDigest,
    string Goal, string SessionEffect, string RecoveryPolicyVersion, string ProfileVersion, string DispositionContractReference,
    string UnresolvedEffectsJson, string EffectsEncoding, string EffectsDigest,
    string DispositionReference, long DispositionVersion, string AuthorityPolicyVersion, string ControlEvidenceReference,
    bool AuthorizesExactEffect, EffectivePolicyInterval EffectiveInterval)
{
    internal string Snapshot() => JsonSerializer.Serialize(this);
    public override string ToString() => nameof(RecoveryGovernedDisposition);
}
/// <summary>Pure local current authoritative owner registry; no network, mutation, captured fallback, Support override or inferred approval.
/// The producing owner must validate policy-required authorization, verification, approval/step-up and dependency controls.
/// A Support request, generic approval or verification result alone cannot authorize the downstream credential effect.</summary>
public interface IRecoveryGovernedDispositionSource
{
    RecoveryGovernedDisposition RequireCurrent(RecoveryGovernedDispositionRequest request, DateTimeOffset at);
}
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed class RecoveryPasswordGovernedCommand
{
    public required Guid RequestId { get; init; }
    public required long ExpectedVersion { get; init; }
    public required string? Password { get; init; }
    public required string? PasswordConfirmation { get; init; }
    public override string ToString() => nameof(RecoveryPasswordGovernedCommand);
}
