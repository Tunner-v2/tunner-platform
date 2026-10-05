using Tunner.Platform.Policies;
using Xunit;

namespace Tunner.Platform.ArchitectureTests;

public sealed class EffectivePolicyVersionResolverTests
{
    private static readonly DateTimeOffset EventTime = new(2026, 1, 15, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void HistoricalEventTimeSelectsExactVersionWithoutCurrentTimeOrLatestVersionBias()
    {
        var oldVersion = Candidate("old", from: EventTime.AddDays(-10), until: EventTime.AddDays(1));
        var newVersion = Candidate("new", from: EventTime.AddDays(1), supersedes: "old");
        var historical = Resolve([newVersion, oldVersion], EventTime);
        var later = Resolve([oldVersion, newVersion], EventTime.AddDays(2));

        Assert.Same(oldVersion, historical.RequireResolvedVersion());
        Assert.Same(newVersion, later.RequireResolvedVersion());
        Assert.Same(oldVersion, historical.RequireResolvedVersion());
        Assert.Equal(EventTime, historical.BusinessEffectiveTime);
    }

    [Fact]
    public void ExactVersionRetainsStatusScopeLineageAndApprovalEvidence()
    {
        var candidate = Candidate("version-2", supersedes: "version-1");
        var result = Resolve([candidate]);

        Assert.Equal(PolicyVersionResolutionOutcome.Resolved, result.Outcome);
        Assert.Equal("owner-a", result.Scope);
        Assert.Equal("version-2", result.RequireResolvedVersion().VersionId);
        Assert.Equal("OWNER_APPROVED", result.SelectedVersion!.Status);
        Assert.Equal("version-1", result.SelectedVersion.SupersedesVersionId);
        Assert.Equal(["approval/version-2"], result.SelectedVersion.ApprovalEvidenceReferences);
        Assert.Equal(["version-2"], result.MatchingVersionIds);
    }

    [Fact]
    public void RequestedOwnerScopeDoesNotSelectAnotherProductOrEnvironment()
    {
        var other = Candidate("other", scope: "owner-b");
        var own = Candidate("own");
        Assert.Same(own, Resolve([other, own]).RequireResolvedVersion());
        Assert.Equal(PolicyVersionResolutionOutcome.Missing, Resolve([other]).Outcome);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public void IntervalBoundariesFollowExplicitOwnerInclusion(bool includesFrom, bool includesUntil)
    {
        var interval = new EffectivePolicyInterval(EventTime, includesFrom, EventTime.AddHours(1), includesUntil);
        Assert.Equal(includesFrom, interval.Contains(EventTime));
        Assert.Equal(includesUntil, interval.Contains(EventTime.AddHours(1)));
        Assert.True(interval.Contains(EventTime.AddMinutes(30)));
        Assert.False(interval.Contains(EventTime.AddTicks(-1)));
        Assert.False(interval.Contains(EventTime.AddHours(1).AddTicks(1)));
    }

    [Fact]
    public void IntervalsCompareAbsoluteInstantsAcrossTimezoneOffsets()
    {
        var interval = new EffectivePolicyInterval(EventTime.ToOffset(TimeSpan.FromHours(-5)), true, EventTime.AddHours(1), false);
        var candidate = new PolicyVersionCandidate<string>("zone", "OWNER_APPROVED", "owner-a", interval, null, ["approval/zone"]);
        Assert.Same(candidate, Resolve([candidate], EventTime.ToOffset(TimeSpan.FromHours(9))).RequireResolvedVersion());
    }

    [Fact]
    public void OwnerMaySupplyOpenEndedOrInclusiveSingleInstantInterval()
    {
        var open = new EffectivePolicyInterval(EventTime, true, null, false);
        var point = new EffectivePolicyInterval(EventTime, true, EventTime, true);
        Assert.True(open.Contains(EventTime.AddYears(100)));
        Assert.True(point.Contains(EventTime));
        Assert.False(point.Contains(EventTime.AddTicks(1)));
        Assert.False(point.Contains(EventTime.AddTicks(-1)));
    }

    [Fact]
    public void InvertedAndEmptyIntervalsAreRejected()
    {
        Assert.Throws<ArgumentException>(() => new EffectivePolicyInterval(EventTime, true, EventTime.AddTicks(-1), true));
        Assert.Throws<ArgumentException>(() => new EffectivePolicyInterval(EventTime, true, EventTime, false));
        Assert.Throws<ArgumentException>(() => new EffectivePolicyInterval(EventTime, false, EventTime, true));
    }

    [Fact]
    public void MissingResolutionNeverSuppliesAPolicyOrPermissiveDefault()
    {
        var result = Resolve([Candidate("future", from: EventTime.AddDays(1))]);
        Assert.Equal(PolicyVersionResolutionOutcome.Missing, result.Outcome);
        Assert.Null(result.SelectedVersion);
        Assert.Empty(result.MatchingVersionIds);
        Assert.Throws<InvalidOperationException>(() => result.RequireResolvedVersion());
    }

    [Fact]
    public void AmbiguityCannotBeBrokenByInputOrderIdOrSupersession()
    {
        var earlier = Candidate("a-version");
        var successor = Candidate("z-version", supersedes: "a-version");
        var first = Resolve([successor, earlier]);
        var reversed = Resolve([earlier, successor]);

        Assert.Equal(PolicyVersionResolutionOutcome.Ambiguous, first.Outcome);
        Assert.Equal(first.Outcome, reversed.Outcome);
        Assert.Null(first.SelectedVersion);
        Assert.Null(reversed.SelectedVersion);
        Assert.Equal(["a-version", "z-version"], first.MatchingVersionIds);
        Assert.Equal(first.MatchingVersionIds, reversed.MatchingVersionIds);
        Assert.Throws<InvalidOperationException>(() => first.RequireResolvedVersion());
    }

    [Fact]
    public void OwnerEligibilityIsRequiredRatherThanAnAssumedStatusVocabulary()
    {
        var ownerApproved = Candidate("approved");
        var ownerRejected = Candidate("rejected", status: "OWNER_DRAFT");
        Assert.Same(ownerApproved, Resolve([ownerRejected, ownerApproved]).RequireResolvedVersion());
        var result = EffectivePolicyVersionResolver.Resolve(EventTime, "owner-a", [ownerApproved], StringComparer.Ordinal.Equals, _ => false);
        Assert.Equal(PolicyVersionResolutionOutcome.Missing, result.Outcome);
    }

    [Fact]
    public void OwnerControlsPrecedenceBySupplyingItsApprovedCandidateCohort()
    {
        var lower = Candidate("platform-policy");
        var higher = Candidate("agreement-policy");
        var cohort = new[] { lower, higher }.Where(version => version.VersionId == higher.VersionId);
        Assert.Same(higher, Resolve(cohort).RequireResolvedVersion());
        Assert.Equal(PolicyVersionResolutionOutcome.Ambiguous, Resolve([lower, higher]).Outcome);
    }

    [Fact]
    public void DuplicateIdentitiesAndNullCandidatesAreInvalidRegardlessOfScope()
    {
        Assert.Throws<ArgumentException>(() => Resolve([Candidate("duplicate"), Candidate("duplicate", scope: "other")]));
        Assert.Throws<ArgumentException>(() => Resolve([null!]));
    }

    [Fact]
    public void MissingRequiredMetadataAndSelfSupersessionAreRejected()
    {
        var interval = new EffectivePolicyInterval(EventTime, true, null, false);
        Assert.Throws<ArgumentException>(() => Candidate(" "));
        Assert.Throws<ArgumentException>(() => Candidate("id", status: " "));
        Assert.Throws<ArgumentException>(() => Candidate("id", supersedes: "id"));
        Assert.Throws<ArgumentException>(() => new PolicyVersionCandidate<string>("id", "approved", "scope", interval, null, []));
        Assert.Throws<ArgumentException>(() => new PolicyVersionCandidate<string>("id", "approved", "scope", interval, null, [" "]));
        Assert.Throws<ArgumentNullException>(() => new PolicyVersionCandidate<string>("id", "approved", null!, interval, null, ["ref"]));
    }

    [Fact]
    public void InputCollectionsCannotRewriteSelectedVersionEvidence()
    {
        var approval = new List<string> { "original-approval" };
        var candidate = new PolicyVersionCandidate<string>("id", "approved", "scope", new EffectivePolicyInterval(EventTime, true, null, false), null, approval);
        approval[0] = "rewritten-approval";
        var candidates = new List<PolicyVersionCandidate<string>> { candidate };
        var result = EffectivePolicyVersionResolver.Resolve(EventTime, "scope", candidates, StringComparer.Ordinal.Equals, _ =>
        {
            candidates.Clear();
            return true;
        });
        Assert.Same(candidate, result.RequireResolvedVersion());
        Assert.Equal(["original-approval"], result.RequireResolvedVersion().ApprovalEvidenceReferences);
        Assert.Throws<NotSupportedException>(() => ((IList<string>)candidate.ApprovalEvidenceReferences)[0] = "mutated");
    }

    [Fact]
    public void MissingOrFailingOwnerEvaluatorsCannotBecomeFallbackResolution()
    {
        var candidates = new[] { Candidate("id") };
        Assert.Throws<ArgumentNullException>(() => EffectivePolicyVersionResolver.Resolve(EventTime, "owner-a", candidates, null!, _ => true));
        Assert.Throws<ArgumentNullException>(() => EffectivePolicyVersionResolver.Resolve(EventTime, "owner-a", candidates, StringComparer.Ordinal.Equals, null!));
        Assert.Throws<InvalidOperationException>(() => EffectivePolicyVersionResolver.Resolve(EventTime, "owner-a", candidates, StringComparer.Ordinal.Equals, _ => throw new InvalidOperationException("owner failed")));
        Assert.Throws<InvalidOperationException>(() => EffectivePolicyVersionResolver.Resolve(EventTime, "owner-a", candidates, (_, _) => throw new InvalidOperationException("scope failed"), _ => true));
    }

    private static PolicyVersionResolution<string> Resolve(IEnumerable<PolicyVersionCandidate<string>> candidates, DateTimeOffset? at = null) =>
        EffectivePolicyVersionResolver.Resolve(at ?? EventTime, "owner-a", candidates, StringComparer.Ordinal.Equals, version => version.Status == "OWNER_APPROVED");

    private static PolicyVersionCandidate<string> Candidate(string id, string scope = "owner-a", string status = "OWNER_APPROVED", DateTimeOffset? from = null, DateTimeOffset? until = null, string? supersedes = null) =>
        new(id, status, scope, new EffectivePolicyInterval(from ?? EventTime.AddDays(-1), true, until, false), supersedes, ["approval/" + id]);
}
