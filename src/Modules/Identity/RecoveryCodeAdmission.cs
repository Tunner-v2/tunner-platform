using System.Collections.ObjectModel;
using Tunner.Platform.Policies;

namespace Tunner.Identity.Persistence;

public sealed record RecoveryCodeSetFacts(Guid SetReference, long LifecycleVersion, long UsageVersion, int RemainingCodeCount)
{
    public override string ToString() => nameof(RecoveryCodeSetFacts);
}
public sealed record RecoveryCodeRetirementTarget(Guid SetReference, long LifecycleVersion)
{
    public override string ToString() => nameof(RecoveryCodeRetirementTarget);
}
public sealed class RecoveryCodeMutationAdmissionRequest
{
    public RecoveryCodeMutationAdmissionRequest(string accountId, long accountVersion, string kind, string policyVersion,
        string factsDigest, IEnumerable<RecoveryCodeSetFacts> activeSets, DateTimeOffset effectiveAt)
    {
        AccountId = accountId; AccountVersion = accountVersion; Kind = kind; PolicyVersion = policyVersion; FactsDigest = factsDigest;
        ActiveSets = Array.AsReadOnly(activeSets.ToArray()); EffectiveAt = effectiveAt;
    }
    public string AccountId { get; }
    public long AccountVersion { get; }
    public string Kind { get; }
    public string PolicyVersion { get; }
    public string FactsDigest { get; }
    public IReadOnlyList<RecoveryCodeSetFacts> ActiveSets { get; }
    public DateTimeOffset EffectiveAt { get; }
    public override string ToString() => nameof(RecoveryCodeMutationAdmissionRequest);
}
/// <summary>Trusted current owner chooses exact targets/admission; the browser never selects a retirement population.</summary>
public sealed class RecoveryCodeMutationAdmission
{
    public RecoveryCodeMutationAdmission(string accountId, long accountVersion, string kind, string factsDigest, bool allowed,
        IEnumerable<RecoveryCodeRetirementTarget> retirementTargets, string policyVersion, string evidenceReference, EffectivePolicyInterval effectiveInterval)
    {
        AccountId = accountId; AccountVersion = accountVersion; Kind = kind; FactsDigest = factsDigest; Allowed = allowed;
        RetirementTargets = Array.AsReadOnly(retirementTargets.ToArray()); PolicyVersion = policyVersion; EvidenceReference = evidenceReference; EffectiveInterval = effectiveInterval;
    }
    public string AccountId { get; }
    public long AccountVersion { get; }
    public string Kind { get; }
    public string FactsDigest { get; }
    public bool Allowed { get; }
    public ReadOnlyCollection<RecoveryCodeRetirementTarget> RetirementTargets { get; }
    public string PolicyVersion { get; }
    public string EvidenceReference { get; }
    public EffectivePolicyInterval EffectiveInterval { get; }
    public override string ToString() => nameof(RecoveryCodeMutationAdmission);
}
public interface IRecoveryCodeMutationAdmissionSource
{
    /// <summary>Local trusted authority only. No provider/network I/O or mutation; caller already holds common owner locks.</summary>
    ValueTask<RecoveryCodeMutationAdmission> RequireAsync(RecoveryCodeMutationAdmissionRequest request, CancellationToken cancellationToken);
    /// <summary>Pure synchronous current resolution at the final decision instant, using the original pre-mutation facts.
    /// No network/provider I/O, transaction mutation, cached-decision fallback or irrevocability assumption.</summary>
    RecoveryCodeMutationAdmission RequireCurrent(RecoveryCodeMutationAdmissionRequest request);
}
