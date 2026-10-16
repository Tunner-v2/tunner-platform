namespace Tunner.Platform.Policies;

/// <summary>Effective-time bounds and inclusion semantics supplied by the owning policy contract.</summary>
public sealed class EffectivePolicyInterval
{
    public EffectivePolicyInterval(DateTimeOffset from, bool includesFrom, DateTimeOffset? until, bool includesUntil)
    {
        if (until is { } end && (end < from || end == from && !(includesFrom && includesUntil)))
            throw new ArgumentException("An effective interval cannot be inverted or empty.", nameof(until));

        From = from;
        IncludesFrom = includesFrom;
        Until = until;
        IncludesUntil = includesUntil;
    }

    public DateTimeOffset From { get; }
    public bool IncludesFrom { get; }
    public DateTimeOffset? Until { get; }
    public bool IncludesUntil { get; }

    public bool Contains(DateTimeOffset instant) =>
        (instant > From || IncludesFrom && instant == From) &&
        (Until is null || instant < Until || IncludesUntil && instant == Until);
}

/// <summary>Immutable version metadata. Scope must be an immutable value defined by the owner.</summary>
public sealed class PolicyVersionCandidate<TScope> where TScope : notnull
{
    public PolicyVersionCandidate(
        string versionId,
        string status,
        TScope scope,
        EffectivePolicyInterval effectiveInterval,
        string? supersedesVersionId,
        IEnumerable<string> approvalEvidenceReferences)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(versionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(status);
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(effectiveInterval);
        ArgumentNullException.ThrowIfNull(approvalEvidenceReferences);
        if (supersedesVersionId is not null &&
            (string.IsNullOrWhiteSpace(supersedesVersionId) || StringComparer.Ordinal.Equals(versionId, supersedesVersionId)))
            throw new ArgumentException("Supersession must reference a distinct nonempty version ID.", nameof(supersedesVersionId));

        var evidence = approvalEvidenceReferences.ToArray();
        if (evidence.Length == 0 || evidence.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("Explicit approval evidence references are required.", nameof(approvalEvidenceReferences));

        VersionId = versionId;
        Status = status;
        Scope = scope;
        EffectiveInterval = effectiveInterval;
        SupersedesVersionId = supersedesVersionId;
        ApprovalEvidenceReferences = Array.AsReadOnly(evidence);
    }

    public string VersionId { get; }
    public string Status { get; }
    public TScope Scope { get; }
    public EffectivePolicyInterval EffectiveInterval { get; }
    public string? SupersedesVersionId { get; }
    public IReadOnlyList<string> ApprovalEvidenceReferences { get; }
}

public enum PolicyVersionResolutionOutcome
{
    Resolved,
    Missing,
    Ambiguous
}

public sealed class PolicyVersionResolution<TScope> where TScope : notnull
{
    internal PolicyVersionResolution(TScope scope, DateTimeOffset businessEffectiveTime, PolicyVersionCandidate<TScope>[] matches)
    {
        Scope = scope;
        BusinessEffectiveTime = businessEffectiveTime;
        Outcome = matches.Length switch
        {
            0 => PolicyVersionResolutionOutcome.Missing,
            1 => PolicyVersionResolutionOutcome.Resolved,
            _ => PolicyVersionResolutionOutcome.Ambiguous
        };
        SelectedVersion = matches.Length == 1 ? matches[0] : null;
        MatchingVersionIds = Array.AsReadOnly(matches.Select(version => version.VersionId).Order(StringComparer.Ordinal).ToArray());
    }

    public TScope Scope { get; }
    public DateTimeOffset BusinessEffectiveTime { get; }
    public PolicyVersionResolutionOutcome Outcome { get; }
    public PolicyVersionCandidate<TScope>? SelectedVersion { get; }
    public IReadOnlyList<string> MatchingVersionIds { get; }

    /// <summary>Required controls cannot obtain a version from missing or ambiguous resolution.</summary>
    public PolicyVersionCandidate<TScope> RequireResolvedVersion() =>
        SelectedVersion ?? throw new InvalidOperationException($"Policy version resolution is {Outcome}.");
}

/// <summary>Deterministic selection only; the owner supplies scope, eligibility and precedence.</summary>
public static class EffectivePolicyVersionResolver
{
    public static PolicyVersionResolution<TScope> Resolve<TScope>(
        DateTimeOffset businessEffectiveTime,
        TScope requestedScope,
        IEnumerable<PolicyVersionCandidate<TScope>> candidates,
        Func<TScope, TScope, bool> scopeMatches,
        Func<PolicyVersionCandidate<TScope>, bool> isEligible) where TScope : notnull
    {
        ArgumentNullException.ThrowIfNull(requestedScope);
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(scopeMatches);
        ArgumentNullException.ThrowIfNull(isEligible);

        var snapshot = candidates.ToArray();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var version in snapshot)
        {
            if (version is null)
                throw new ArgumentException("Candidate records cannot be null.", nameof(candidates));
            if (!ids.Add(version.VersionId))
                throw new ArgumentException("Candidate version IDs must be unique.", nameof(candidates));
        }

        var matches = snapshot.Where(version =>
            scopeMatches(version.Scope, requestedScope) &&
            version.EffectiveInterval.Contains(businessEffectiveTime) &&
            isEligible(version)).ToArray();
        return new PolicyVersionResolution<TScope>(requestedScope, businessEffectiveTime, matches);
    }
}
