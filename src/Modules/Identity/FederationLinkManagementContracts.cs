using System.Text.Json;
using System.Text.Json.Serialization;
using Tunner.Platform.Policies;

namespace Tunner.Identity.Persistence;

public enum FederationUnlinkSessionEffect { PreserveExistingSessions = 1, RevokeLinkedSessions = 2 }

/// <summary>Explicit security-owner inputs. None of these values is selected by a browser or a default provider profile.</summary>
public sealed record FederationLinkManagementPolicy(string Version, string Origin, string ContinuationPath, string LinkAssurancePolicyVersion,
    string UnlinkAssurancePolicyVersion, TimeSpan OperationLifetime, TimeSpan ResultRecoveryLifetime, int MaximumPendingOperations,
    FederationUnlinkSessionEffect UnlinkSessionEffect, string SessionUsePolicyVersion, string SessionEffectEvidenceReference,
    string ProcessingReference, string RetentionReference);

public sealed class FederationLinkManagementPolicyVersion
{
    public FederationLinkManagementPolicyVersion(PolicyVersionCandidate<string> metadata, FederationLinkManagementPolicy values)
    {
        ArgumentNullException.ThrowIfNull(metadata); ArgumentNullException.ThrowIfNull(values);
        if (metadata.VersionId != values.Version || metadata.Scope != values.Origin ||
            new[] { values.Version, values.LinkAssurancePolicyVersion, values.UnlinkAssurancePolicyVersion, values.SessionUsePolicyVersion,
                values.SessionEffectEvidenceReference, values.ProcessingReference, values.RetentionReference }.Any(x => string.IsNullOrWhiteSpace(x) || x.Any(char.IsControl)) ||
            !Uri.TryCreate(values.Origin, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.UserInfo.Length != 0 ||
            values.Origin != uri.GetLeftPart(UriPartial.Authority) || !FederationProfileVersion.CleanPath(values.ContinuationPath) || values.OperationLifetime <= TimeSpan.Zero || values.ResultRecoveryLifetime <= TimeSpan.Zero ||
            values.MaximumPendingOperations <= 0 || !Enum.IsDefined(values.UnlinkSessionEffect))
            throw new ArgumentException("An explicit qualified federation management policy is required.");
        Metadata = metadata; Values = values;
        Snapshot = JsonSerializer.Serialize(new { metadata.VersionId, metadata.Status, metadata.Scope, metadata.EffectiveInterval,
            metadata.SupersedesVersionId, Approval = metadata.ApprovalEvidenceReferences.Order(StringComparer.Ordinal), Values });
    }
    public PolicyVersionCandidate<string> Metadata { get; }
    public FederationLinkManagementPolicy Values { get; }
    internal string Snapshot { get; }
    internal void Require(string origin, DateTimeOffset at)
    { if (Metadata.Status != "ACTIVE" || Metadata.Scope != origin || !Metadata.EffectiveInterval.Contains(at)) throw new InvalidOperationException("Current federation management authority is unavailable."); }
    public override string ToString() => nameof(FederationLinkManagementPolicyVersion);
}

/// <summary>Pure synchronous current owner policy; no network work inside the Identity transaction.</summary>
public interface IFederationLinkPolicySource
{ FederationLinkManagementPolicyVersion RequireCurrent(string origin, DateTimeOffset effectiveAt); }

public enum FederationAuthenticationPathKind { VerifiedContact, Passkey, Totp, Federation, RecoveryCodeSet }

/// <summary>Non-secret owner facts. Presence, ACTIVE state or a remaining count alone is never eligibility.</summary>
public sealed record FederationAuthenticationPath(FederationAuthenticationPathKind Kind, string Reference, string State,
    long Version, long UsageVersion, string PolicyVersion, string QualificationReference, int? RemainingUses);

public sealed class FederationUnlinkPostureRequest
{
    internal FederationUnlinkPostureRequest(string accountId, long accountVersion, Guid linkId, long linkVersion, bool passwordAvailable,
        string securityStampDigest, IEnumerable<FederationAuthenticationPath> paths)
    {
        AccountId = accountId; AccountVersion = accountVersion; LinkId = linkId; LinkVersion = linkVersion;
        PasswordAvailable = passwordAvailable; SecurityStampDigest = securityStampDigest;
        Paths = Array.AsReadOnly(paths.OrderBy(x => x.Kind).ThenBy(x => x.Reference, StringComparer.Ordinal).ToArray());
        FactsDigest = AuthenticationIdentity.Digest(JsonSerializer.Serialize(new { AccountId, AccountVersion, LinkId, LinkVersion,
            PasswordAvailable, SecurityStampDigest, Paths }));
    }
    public string AccountId { get; }
    public long AccountVersion { get; }
    public Guid LinkId { get; }
    public long LinkVersion { get; }
    public bool PasswordAvailable { get; }
    public string SecurityStampDigest { get; }
    public IReadOnlyList<FederationAuthenticationPath> Paths { get; }
    public string FactsDigest { get; }
    public override string ToString() => nameof(FederationUnlinkPostureRequest);
}

public sealed record FederationUnlinkPostureDecision(string AccountId, long AccountVersion, Guid LinkId, long LinkVersion,
    string FactsDigest, bool Allowed, string PolicyVersion, string EvidenceReference, EffectivePolicyInterval EffectiveInterval);

/// <summary>Trusted required-posture authority, re-resolved from the same immutable facts at the final clock. No I/O or count fallback.</summary>
public interface IFederationUnlinkPostureSource
{ FederationUnlinkPostureDecision RequireCurrent(FederationUnlinkPostureRequest request, DateTimeOffset effectiveAt); }

internal sealed record FederationUnlinkSessionRequest(StepUpCaller Caller, StepUpOperation Operation, Guid ManagementOperationId, FederationProviderKind Provider, Guid LinkId,
    long PriorLinkVersion, long UnlinkedVersion, FederationUnlinkSessionEffect Effect, string ManagementPolicyVersion,
    string SessionUsePolicyVersion, string SessionEffectEvidenceReference, string PosturePolicyVersion,
    string PostureEvidenceReference, EffectivePolicyInterval PostureInterval);

[JsonConverter(typeof(JsonStringEnumConverter<FederationManagedLinkState>))]
public enum FederationManagedLinkState { [JsonStringEnumMemberName("ACTIVE")] Active, [JsonStringEnumMemberName("UNLINKED")] Unlinked }
public sealed record FederationManagedLinkView(Guid LinkReference, long Version,
    [property: JsonConverter(typeof(JsonStringEnumConverter<FederationProviderKind>))] FederationProviderKind Provider, FederationManagedLinkState State,
    DateTimeOffset? LinkedAt, bool CanStartUnlink);
public sealed record FederationManagementView(long AccountVersion, bool GoogleAvailable, bool MicrosoftAvailable,
    bool CanStartLink, IReadOnlyList<FederationManagedLinkView> Links, FederationLinkOperationView? Operation = null);
public sealed record FederationLinkOperationView(Guid OperationId, Guid RequestId, string Kind, string Provider, string Status, long Version,
    Guid? TargetLinkReference, long ExpectedLinkVersion, long ExpectedAccountVersion, Guid? ResultLinkReference, long? ResultLinkVersion,
    string? ErrorCode, bool ProviderVerified, bool CanConfirm, bool CanRequestProof, bool CanCancel, string? SessionEffect, string? ImpactKey);

internal sealed class FederationLinkOperationResult(string? errorCode = null, FederationLinkOperationRow? row = null, FederationAttemptRow? attempt = null)
{
    internal string? ErrorCode { get; } = errorCode;
    internal FederationLinkOperationRow? Row { get; } = row;
    internal FederationAttemptRow? Attempt { get; } = attempt;
    public override string ToString() => nameof(FederationLinkOperationResult);
}
