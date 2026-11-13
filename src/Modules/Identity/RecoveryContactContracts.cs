using System.Text.Json;
using System.Text.Json.Serialization;
using Tunner.Platform.Policies;

namespace Tunner.Identity.Persistence;

public sealed record RecoveryContactProfile(string Version, int ProofDigits, TimeSpan ProofLifetime, int MaximumProofAttempts,
    TimeSpan PendingMaterialRetention, string RiskPolicyVersion, string NotificationContractReference)
{
    internal void Validate()
    {
        if (new[] { Version, RiskPolicyVersion, NotificationContractReference }.Any(string.IsNullOrWhiteSpace) || ProofDigits is < 6 or > 9 ||
            ProofLifetime <= TimeSpan.Zero || MaximumProofAttempts <= 0 || PendingMaterialRetention < ProofLifetime)
            throw new ArgumentException("Explicit supported contact/risk recovery and notification authority is required.");
    }
    public override string ToString() => nameof(RecoveryContactProfile);
}
public sealed record RecoveryVerifiedContactFacts(Guid ContactReference, long Version, string AccountId, string State, string DestinationDigest, string EvidenceReference);
public sealed record RecoveryCaseContactSelection(Guid ContactReference, long Version, string ProfileVersion, bool RecoveryCodesUnavailable);
public sealed record RecoveryContactRiskRequest(Guid CaseId, Guid RequestId, string AccountId, string ContextDigest, string ProfileVersion,
    string ExpectedRiskPolicyVersion, RecoveryCaseGoal Goal, string BrowserDigest, string Origin, string NetworkDigest,
    RecoveryCaseHierarchyFacts Facts, RecoveryVerifiedContactFacts Contact, string ProcessingReference, string RetentionReference);
public sealed record RecoveryContactRiskDecision(Guid CaseId, Guid RequestId, string AccountId, string ContextDigest, Guid ContactReference,
    long ContactVersion, string DestinationDigest, string FactsDigest, string PolicyVersion, string DecisionReference,
    string Outcome, string EvidenceReference, EffectivePolicyInterval EffectiveInterval)
{
    internal string Snapshot() => JsonSerializer.Serialize(this);
    public override string ToString() => nameof(RecoveryContactRiskDecision);
}
/// <summary>Trusted current Risk owner decision, pure local execution gate; no browser score, threshold or Support override.</summary>
public interface IRecoveryContactRiskSource { RecoveryContactRiskDecision RequireCurrent(RecoveryContactRiskRequest request, DateTimeOffset at); }
public sealed record RecoveryContactNotificationRequest(Guid CaseId, Guid ChallengeId, string AccountId, Guid ContactReference,
    long ContactVersion, string DestinationDigest, string ContextDigest, string ContractReference, string ProcessingReference, string RetentionReference);
public sealed record RecoveryContactNotificationDecision(Guid CaseId, Guid ChallengeId, string AccountId, Guid ContactReference,
    long ContactVersion, string DestinationDigest, string ContextDigest, string ContractReference, string NotificationClass,
    string Channel, string TemplateVersion, string CodeVariableReference, string Locale, string RecipientEvidenceReference, string RouteResolutionReference, string EvidenceReference,
    string ProcessingReference, string RetentionReference, EffectivePolicyInterval EffectiveInterval)
{
    internal string Snapshot() => JsonSerializer.Serialize(this);
    public override string ToString() => nameof(RecoveryContactNotificationDecision);
}
/// <summary>Pure local approved class/template/schema/recipient/route authority: no network/provider I/O, captured fallback or mutation.
/// Missing production capability remains unavailable.</summary>
public interface IRecoveryContactNotificationSource
{
    RecoveryContactNotificationDecision RequireCurrent(RecoveryContactNotificationRequest request, DateTimeOffset at);
}
/// <summary>Post-commit NotificationIntent handoff only. Adapter deduplicates DeliveryId and independently enforces recipient safety;
/// queue acceptance never means sent/delivered and cannot change RecoveryCase authority.</summary>
public interface IRecoveryContactNotificationDispatcher
{
    Task QueueAsync(RecoveryContactNotification notification, CancellationToken cancellationToken);
}
public sealed class RecoveryContactNotification(Guid deliveryId, Guid caseId, Guid challengeId, string address, string code,
    RecoveryContactNotificationDecision authority, DateTimeOffset expiresAt)
{
    public Guid DeliveryId { get; } = deliveryId;
    public Guid CaseId { get; } = caseId;
    public Guid ChallengeId { get; } = challengeId;
    [JsonIgnore] public string Address { get; } = address;
    [JsonIgnore] public string Code { get; } = code;
    public RecoveryContactNotificationDecision Authority { get; } = authority;
    public DateTimeOffset ExpiresAt { get; } = expiresAt;
    public override string ToString() => nameof(RecoveryContactNotification);
}
internal sealed record RecoveryContactPresentation(Guid ChallengeId, DateTimeOffset ExpiresAt);
internal sealed record RecoveryContactOptionsResult(string? ErrorCode, RecoveryEntryRow? Entry = null, RecoveryContactPresentation? Presentation = null);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed class RecoveryPasswordContactRepairCommand
{
    public required Guid RequestId { get; init; }
    public required long ExpectedVersion { get; init; }
    public required Guid ChallengeId { get; init; }
    public required string? Code { get; init; }
    public required string? Password { get; init; }
    public required string? PasswordConfirmation { get; init; }
    public override string ToString() => nameof(RecoveryPasswordContactRepairCommand);
}
