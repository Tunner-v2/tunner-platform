using System.Text.Json;
using Tunner.Platform.Policies;

namespace Tunner.Identity.Persistence;

internal enum RecoveryCaseGoal { ContainSelectedTunnerSession = 1 }
internal enum RecoveryCaseSessionEffect { RevokeExactSelectedSession = 1 }

/// <summary>Trusted resolved owner/browser facts, never an HTTP Account selection or authenticated StepUp caller.</summary>
internal sealed record RecoveryCaseCaller(string AccountId, string OriginalBrowserDigest, string Origin, string NetworkDigest)
{
    internal bool Valid => !string.IsNullOrWhiteSpace(AccountId) && Digest(OriginalBrowserDigest) && Digest(NetworkDigest) && ValidOrigin(Origin);
    internal static bool ValidOrigin(string value) => Uri.TryCreate(value, UriKind.Absolute, out var origin) && origin.Scheme == "https" &&
        origin.UserInfo.Length == 0 && value == origin.GetLeftPart(UriPartial.Authority);
    internal static bool Digest(string value) => value is { Length: 64 } && value.All(Uri.IsHexDigit);
    public override string ToString() => nameof(RecoveryCaseCaller);
}

internal sealed record RecoveryCaseStartCommand(Guid RequestId, Guid TargetSessionId, long ExpectedSessionVersion,
    long ExpectedAccountVersion, Guid SetReference, long ExpectedSetLifecycleVersion, long? ExpectedUsageVersion)
{
    public override string ToString() => nameof(RecoveryCaseStartCommand);
}

/// <summary>Only supported containment shape. Every value is an explicit qualified owner input; no restoration claim.</summary>
internal sealed record RecoveryCasePolicy(string Version, string Origin, RecoveryCaseGoal Goal, RecoveryCaseSessionEffect SessionEffect,
    RecoveryCodeUsageAdmission UsageAdmission, TimeSpan CaseLifetime, TimeSpan ResultRecoveryLifetime, int MaximumPendingCases,
    IReadOnlyCollection<string> UnresolvedDownstreamEffects, string ProcessingReference, string RetentionReference)
{
    public override string ToString() => nameof(RecoveryCasePolicy);
}

internal sealed class RecoveryCasePolicyVersion
{
    internal RecoveryCasePolicyVersion(PolicyVersionCandidate<string> metadata, RecoveryCasePolicy values)
    {
        ArgumentNullException.ThrowIfNull(metadata); ArgumentNullException.ThrowIfNull(values);
        ArgumentNullException.ThrowIfNull(values.UnresolvedDownstreamEffects);
        Values = values with { UnresolvedDownstreamEffects = Array.AsReadOnly(values.UnresolvedDownstreamEffects.Order(StringComparer.Ordinal).ToArray()) };
        if (metadata.Scope != "ACCOUNT_RECOVERY" || metadata.VersionId != values.Version ||
            new[] { values.Version, values.Origin, values.ProcessingReference, values.RetentionReference }.Any(string.IsNullOrWhiteSpace) ||
            !RecoveryCaseCaller.ValidOrigin(values.Origin) ||
            values.Goal != RecoveryCaseGoal.ContainSelectedTunnerSession || values.SessionEffect != RecoveryCaseSessionEffect.RevokeExactSelectedSession ||
            values.UsageAdmission is not (RecoveryCodeUsageAdmission.ExactUsageSnapshot or RecoveryCodeUsageAdmission.IndependentUnusedEntry) ||
            values.CaseLifetime <= TimeSpan.Zero || values.ResultRecoveryLifetime <= TimeSpan.Zero || values.MaximumPendingCases <= 0 ||
            Values.UnresolvedDownstreamEffects.Count == 0 || Values.UnresolvedDownstreamEffects.Any(string.IsNullOrWhiteSpace) ||
            Values.UnresolvedDownstreamEffects.Distinct(StringComparer.Ordinal).Count() != Values.UnresolvedDownstreamEffects.Count)
            throw new ArgumentException("Complete qualified recovery containment authority is required.");
        Metadata = metadata;
        Snapshot = JsonSerializer.Serialize(new { metadata.VersionId, metadata.Status, metadata.Scope, metadata.EffectiveInterval,
            ApprovalEvidence = metadata.ApprovalEvidenceReferences.Order(StringComparer.Ordinal), Values });
    }
    internal PolicyVersionCandidate<string> Metadata { get; }
    internal RecoveryCasePolicy Values { get; }
    internal string Snapshot { get; }
    internal void Require(string origin, DateTimeOffset at)
    {
        if (Metadata.Status != "ACTIVE" || Values.Origin != origin || !Metadata.EffectiveInterval.Contains(at))
            throw new InvalidOperationException("Current recovery authority is unavailable.");
    }
    public override string ToString() => nameof(RecoveryCasePolicyVersion);
}
internal interface IRecoveryCasePolicySource { RecoveryCasePolicyVersion RequireEffective(DateTimeOffset at); }

internal sealed class RecoveryCasePolicyCatalog(IEnumerable<RecoveryCasePolicyVersion> versions) : IRecoveryCasePolicySource
{
    private readonly RecoveryCasePolicyVersion[] versions = versions.ToArray();
    public RecoveryCasePolicyVersion RequireEffective(DateTimeOffset at)
    {
        var selected = EffectivePolicyVersionResolver.Resolve(at, "ACCOUNT_RECOVERY", versions.Select(x => x.Metadata),
            static (a, b) => a == b, static x => x.Status == "ACTIVE").RequireResolvedVersion();
        return versions.Single(x => ReferenceEquals(x.Metadata, selected));
    }
}

internal sealed record RecoveryCaseAuthenticationPath(string Kind, Guid Reference, string State, long Version, string PolicyVersion,
    string? QualificationEvidenceReference)
{
    public override string ToString() => nameof(RecoveryCaseAuthenticationPath);
}
internal sealed record RecoveryCaseHierarchyFacts(string AccountId, string AccountState, long AccountVersion, bool PasswordAvailable, bool EmailConfirmed,
    string SecurityStampDigest, IReadOnlyList<RecoveryCaseAuthenticationPath> Paths)
{
    internal string Digest() => AuthenticationIdentity.Digest(JsonSerializer.Serialize(this));
    public override string ToString() => nameof(RecoveryCaseHierarchyFacts);
}
internal sealed record RecoveryCaseHierarchyRequest(Guid CaseId, Guid RequestId, string ContextDigest, string PolicyVersion,
    RecoveryCaseGoal Goal, RecoveryCaseHierarchyFacts Facts)
{
    public override string ToString() => nameof(RecoveryCaseHierarchyRequest);
}
/// <summary>The trusted hierarchy owner must qualify stronger-proof unavailability from the complete current facts.
/// A browser assertion, password reset, row count or Support override cannot supply this decision.</summary>
internal sealed record RecoveryCaseHierarchyDecision(bool RecoveryCodeAllowed, bool StrongAuthenticatorUnavailable, string PolicyVersion,
    string EvidenceReference, string FactsDigest, EffectivePolicyInterval EffectiveInterval)
{
    internal string Snapshot() => JsonSerializer.Serialize(this);
    public override string ToString() => nameof(RecoveryCaseHierarchyDecision);
}
internal interface IRecoveryCaseHierarchySource
{
    // Pure local current authority. No network/provider I/O, captured decision fallback or mutation.
    RecoveryCaseHierarchyDecision RequireCurrent(RecoveryCaseHierarchyRequest request, DateTimeOffset at);
}

internal sealed record RecoveryCaseResult(string? ErrorCode, RecoveryCaseRow? Case = null, RecoveryResultRow? Result = null,
    bool Reconciled = false)
{
    public override string ToString() => nameof(RecoveryCaseResult);
}
