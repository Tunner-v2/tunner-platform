using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;
using Tunner.Platform.Policies;

namespace Tunner.Identity.Persistence;

/// <summary>Explicit trusted discovery capacity; never a page size, browser claim or production default.</summary>
public sealed class SessionBulkRevocationDiscoveryBudget(PolicyVersionCandidate<string> metadata, int maximumCandidates)
{
    public const string Scope = "USER_BFF_SESSION_BULK_DISCOVERY";
    public PolicyVersionCandidate<string> Metadata { get; } = metadata ?? throw new ArgumentNullException(nameof(metadata));
    public int MaximumCandidates { get; } = maximumCandidates > 0 && maximumCandidates < int.MaxValue
        ? maximumCandidates : throw new ArgumentOutOfRangeException(nameof(maximumCandidates));
    internal static SessionBulkRevocationDiscoveryBudget Current(ISessionBulkRevocationDiscoverySource source, DateTimeOffset at)
    {
        var budget = source.RequireEffective(at) ?? throw new InvalidOperationException("Bulk discovery authority is unavailable.");
        _ = EffectivePolicyVersionResolver.Resolve(at, Scope, new[] { budget.Metadata }, static (a, b) => a == b,
            static x => x.Status == "ACTIVE").RequireResolvedVersion();
        if (budget.Metadata.ApprovalEvidenceReferences.Count == 0) throw new InvalidOperationException("Bulk discovery approval evidence is unavailable.");
        return budget;
    }
    public override string ToString() => nameof(SessionBulkRevocationDiscoveryBudget);
}
public interface ISessionBulkRevocationDiscoverySource
{
    SessionBulkRevocationDiscoveryBudget RequireEffective(DateTimeOffset at);
}

internal sealed record SessionBulkRevocationTarget(Guid SessionReference, long ExpectedVersion);

/// <summary>Identity-created original set. Copies and canonicalizes its membership defensively.</summary>
internal sealed class SessionBulkRevocationManifest
{
    private SessionBulkRevocationManifest(StepUpCaller caller, Guid requestId, string correlationId, IEnumerable<SessionBulkRevocationTarget> targets,
        DateTimeOffset discoveredAt, string authenticationVersion, string assuranceVersion, string discoveryVersion, int maximumCandidates)
    {
        if (requestId == Guid.Empty) throw new ArgumentException("An exact original request is required.", nameof(requestId));
        ArgumentException.ThrowIfNullOrWhiteSpace(correlationId);
        var copy = targets.OrderBy(x => x.SessionReference.ToString("N"), StringComparer.Ordinal).ToArray();
        ArgumentException.ThrowIfNullOrWhiteSpace(authenticationVersion); ArgumentException.ThrowIfNullOrWhiteSpace(assuranceVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(discoveryVersion);
        if (maximumCandidates < 1 || maximumCandidates == int.MaxValue || copy.Length == 0 || copy.Length > maximumCandidates || copy.Any(x => x.SessionReference == Guid.Empty ||
            x.SessionReference == caller.SessionId || x.ExpectedVersion < 1 || x.ExpectedVersion == long.MaxValue) ||
            copy.Select(x => x.SessionReference).Distinct().Count() != copy.Length)
            throw new ArgumentException("A complete nonempty original target set is required.", nameof(targets));
        AccountId = caller.AccountId; ActorSessionId = caller.SessionId; Origin = caller.Origin;
        RequestId = requestId; CorrelationId = correlationId; Targets = Array.AsReadOnly(copy); DiscoveredAt = discoveredAt;
        AuthenticationVersion = authenticationVersion; AssuranceVersion = assuranceVersion;
        DiscoveryVersion = discoveryVersion; MaximumCandidates = maximumCandidates;
    }
    internal static SessionBulkRevocationManifest FromDiscovery(StepUpCaller caller, Guid requestId, string correlationId,
        IEnumerable<SessionBulkRevocationTarget> targets, DateTimeOffset at, string authenticationVersion, string assuranceVersion,
        SessionBulkRevocationDiscoveryBudget budget) => new(caller, requestId, correlationId, targets, at, authenticationVersion, assuranceVersion,
            budget.Metadata.VersionId, budget.MaximumCandidates);
    // Called only after authenticating the purpose-protected complete original envelope.
    internal static SessionBulkRevocationManifest RestoreTrustedOriginal(StepUpCaller caller, Guid requestId, string correlationId,
        IEnumerable<SessionBulkRevocationTarget> targets, DateTimeOffset at, string authenticationVersion, string assuranceVersion,
        string discoveryVersion, int maximumCandidates) => new(caller, requestId, correlationId, targets, at, authenticationVersion,
            assuranceVersion, discoveryVersion, maximumCandidates);
    internal string AccountId { get; }
    internal Guid ActorSessionId { get; }
    internal string Origin { get; }
    internal Guid RequestId { get; }
    internal string CorrelationId { get; }
    internal ReadOnlyCollection<SessionBulkRevocationTarget> Targets { get; }
    internal DateTimeOffset DiscoveredAt { get; }
    internal string AuthenticationVersion { get; }
    internal string AssuranceVersion { get; }
    internal string DiscoveryVersion { get; }
    internal int MaximumCandidates { get; }
    internal void RequireActor(StepUpCaller caller)
    {
        if (caller.AccountId != AccountId || caller.SessionId != ActorSessionId || caller.Origin != Origin)
            throw new ArgumentException("The original server-authenticated actor is required.", nameof(caller));
    }
    internal StepUpOperation Operation(StepUpCaller caller)
    {
        RequireActor(caller);
        // The immutable manifest has no mutable aggregate row/version. Each target version is
        // bound in the context; zero is the existing supported non-versioned-resource marker.
        return new("OP-SESSION-REVOKE-OTHERS-" + RequestId.ToString("N"), CorrelationId, SessionBulkRevocationApplication.Scope,
            RequestId.ToString("N"), 0, AuthenticationIdentity.Digest(JsonSerializer.Serialize(new
            {
                AccountId, ActorSessionId, Origin, RequestId, CorrelationId, DiscoveredAt,
                AuthenticationVersion, AssuranceVersion, DiscoveryVersion, MaximumCandidates,
                targets = Targets.Select(x => new { reference = x.SessionReference.ToString("N"), version = x.ExpectedVersion.ToString(CultureInfo.InvariantCulture) })
            })));
    }
    public override string ToString() => nameof(SessionBulkRevocationManifest);
}
internal sealed record SessionBulkRevocationPreparation(string? ErrorCode, SessionBulkRevocationManifest? Manifest = null)
{ public override string ToString() => nameof(SessionBulkRevocationPreparation); }
internal sealed record SessionBulkRevocationResult(string? ErrorCode, IReadOnlyList<SessionRevocationReceipt>? Receipts = null)
{
    internal bool Applied => Receipts is { Count: > 0 };
    public override string ToString() => nameof(SessionBulkRevocationResult);
}
