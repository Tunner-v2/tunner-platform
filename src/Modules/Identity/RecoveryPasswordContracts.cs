using System.Text.Json;
using System.Text.Json.Serialization;
using Tunner.Platform.Policies;

namespace Tunner.Identity.Persistence;

public enum RecoveryPasswordSessionEffect { InvalidateExistingTunnerSessions = 1 }
public sealed record RecoveryCaseCodeSetFacts(Guid SetReference, string State, long LifecycleVersion, long UsageVersion)
{
    public override string ToString() => nameof(RecoveryCaseCodeSetFacts);
}
public sealed record RecoveryCaseCodeSelection(Guid SetReference, long LifecycleVersion, long? UsageVersion)
{
    public override string ToString() => nameof(RecoveryCaseCodeSelection);
}
/// <summary>Explicit approved repair inputs. This is not a selected production policy or permission for factor enrollment.</summary>
public sealed record RecoveryPasswordPolicy(string Version, string Origin, string GoalReference, int PasswordMinimumLength,
    int PasswordMaximumLength, int PasswordHashIterations, RecoveryPasswordSessionEffect SessionEffect, bool RequireExactCodeUsageVersion,
    TimeSpan EntryLifetime, TimeSpan CaseLifetime, TimeSpan ResultRecoveryLifetime, int MaximumPendingEntries, int EntryNetworkLimit,
    int EntryIdentifierLimit, IReadOnlyCollection<string> UnresolvedDownstreamEffects, string ProcessingReference, string RetentionReference)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public RecoveryPasswordPasskeyProfile? PasskeyRecoveryProfile { get; init; }
    public override string ToString() => nameof(RecoveryPasswordPolicy);
}
public sealed class RecoveryPasswordPolicyVersion
{
    public RecoveryPasswordPolicyVersion(PolicyVersionCandidate<string> metadata, RecoveryPasswordPolicy values)
    {
        ArgumentNullException.ThrowIfNull(metadata); ArgumentNullException.ThrowIfNull(values); ArgumentNullException.ThrowIfNull(values.UnresolvedDownstreamEffects);
        Values = values with { UnresolvedDownstreamEffects = Array.AsReadOnly(values.UnresolvedDownstreamEffects.Order(StringComparer.Ordinal).ToArray()) };
        if (metadata.Scope != "ACCOUNT_RECOVERY_PASSWORD" || metadata.VersionId != values.Version ||
            new[] { values.Version, values.GoalReference, values.ProcessingReference, values.RetentionReference }.Any(string.IsNullOrWhiteSpace) ||
            !RecoveryCaseCaller.ValidOrigin(values.Origin) || values.PasswordMinimumLength <= 0 || values.PasswordMaximumLength < values.PasswordMinimumLength ||
            values.PasswordMaximumLength > 1_048_576 || values.PasswordHashIterations <= 0 || values.EntryLifetime <= TimeSpan.Zero ||
            values.CaseLifetime <= TimeSpan.Zero || values.ResultRecoveryLifetime <= TimeSpan.Zero || values.MaximumPendingEntries <= 0 ||
            values.EntryNetworkLimit <= 0 || values.EntryIdentifierLimit <= 0 || values.SessionEffect != RecoveryPasswordSessionEffect.InvalidateExistingTunnerSessions ||
            Values.UnresolvedDownstreamEffects.Any(string.IsNullOrWhiteSpace) ||
            Values.UnresolvedDownstreamEffects.Distinct(StringComparer.Ordinal).Count() != Values.UnresolvedDownstreamEffects.Count)
            throw new ArgumentException("Complete explicit recovery password authority is required.");
        values.PasskeyRecoveryProfile?.Validate();
        if (values.PasskeyRecoveryProfile is { } passkey)
            Values = Values with { PasskeyRecoveryProfile = passkey with
                { MethodAssurances = new System.Collections.ObjectModel.ReadOnlyDictionary<string, string>(
                    passkey.MethodAssurances.ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal)) } };
        Metadata = metadata;
        Snapshot = JsonSerializer.Serialize(new { metadata.VersionId, metadata.Status, metadata.Scope, metadata.EffectiveInterval,
            ApprovalEvidence = metadata.ApprovalEvidenceReferences.Order(StringComparer.Ordinal), Values });
    }
    public PolicyVersionCandidate<string> Metadata { get; }
    public RecoveryPasswordPolicy Values { get; }
    internal string Snapshot { get; }
    internal void Require(string origin, DateTimeOffset at)
    {
        if (Values.Origin != origin || Metadata.Status != "ACTIVE" || !Metadata.EffectiveInterval.Contains(at))
            throw new InvalidOperationException("Current recovery password authority is unavailable.");
    }
    public override string ToString() => nameof(RecoveryPasswordPolicyVersion);
}
public interface IRecoveryPasswordPolicySource { RecoveryPasswordPolicyVersion RequireEffective(DateTimeOffset at); }
public sealed class RecoveryPasswordPolicyCatalog(IEnumerable<RecoveryPasswordPolicyVersion> versions) : IRecoveryPasswordPolicySource
{
    private readonly RecoveryPasswordPolicyVersion[] versions = versions.ToArray();
    public RecoveryPasswordPolicyVersion RequireEffective(DateTimeOffset at)
    {
        var selected = EffectivePolicyVersionResolver.Resolve(at, "ACCOUNT_RECOVERY_PASSWORD", versions.Select(x => x.Metadata),
            static (a, b) => a == b, static x => x.Status == "ACTIVE").RequireResolvedVersion();
        return versions.Single(x => ReferenceEquals(x.Metadata, selected));
    }
}
internal sealed record RecoveryEntryCaller(string BrowserDigest, string PreviousCookieDigest, string Origin, string NetworkDigest)
{
    internal bool Valid => RecoveryCaseCaller.Digest(BrowserDigest) && RecoveryCaseCaller.Digest(PreviousCookieDigest) &&
        RecoveryCaseCaller.Digest(NetworkDigest) && RecoveryCaseCaller.ValidOrigin(Origin);
    public override string ToString() => nameof(RecoveryEntryCaller);
}
internal sealed record RecoveryEntryResult(string? ErrorCode, RecoveryEntryRow? Entry = null, RecoveryCaseRow? Case = null,
    RecoveryResultRow? Result = null, bool Reconciled = false)
{
    public override string ToString() => nameof(RecoveryEntryResult);
}
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed class RecoveryEntryStartCommand
{
    public required Guid RequestId { get; init; }
    public required string Email { get; init; }
    public required string GoalReference { get; init; }
    public override string ToString() => nameof(RecoveryEntryStartCommand);
}
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed class RecoveryEntryCommand
{
    public required Guid RequestId { get; init; }
    public override string ToString() => nameof(RecoveryEntryCommand);
}
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed class RecoveryPasswordRepairCommand
{
    public required Guid RequestId { get; init; }
    public required long ExpectedVersion { get; init; }
    [JsonIgnore] internal string? TransientCode => Code;
    public required string? Code { get; init; }
    public required string? Password { get; init; }
    public required string? PasswordConfirmation { get; init; }
    public override string ToString() => nameof(RecoveryPasswordRepairCommand);
}
