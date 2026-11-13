using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Tunner.Governance;
using Tunner.Identity.Persistence;
using Tunner.Platform.Policies;

/// <summary>Explicit finite V01–V03 inputs for real neutral entry/original/cancel HTTP journeys.
/// No production publication, admitted proof, disposition producer or repair outcome is supplied.</summary>
internal sealed class BrowserTestRecoveryCase(string origin, TimeProvider clock) :
    IRecoveryPasswordPolicySource, IRecoveryCaseHierarchySource, IRecoveryPasswordPresentationSource
{
    internal const string JourneyId = "RECOVERY_CASE_V01_V03_V1";
    internal const string GoalReference = "SYNTHETIC_FINITE_RECOVERY_011_GOAL_V1";
    internal const string GoalLabel = "Recover account access";
    private const string Provenance = "SYNTHETIC-FINITE-TEST-NOT-A-PRODUCTION-PUBLICATION";
    private const string GoalLabelKey = "finiteRecoveryCase.goal";
    private const string GoalDescriptionKey = "finiteRecoveryCase.goalDescription";
    private const string EffectLabelKey = "finiteRecoveryCase.effect";
    private const string EffectValueKey = "finiteRecoveryCase.effectValue";
    private const string FollowUpLabelKey = "finiteRecoveryCase.followUp";
    private const string FollowUpValueKey = "finiteRecoveryCase.followUpValue";
    private const string UnresolvedReference = "ACCESS_RESTORATION";
    private readonly DateTimeOffset began = clock.GetUtcNow();

    private EffectivePolicyInterval Interval => new(began.AddMinutes(-1), true, began.AddMinutes(30), false);

    internal void Configure(IServiceCollection services)
    {
        services.AddSingleton(this);
        services.AddSingleton<IRecoveryPasswordPolicySource>(this);
        services.AddSingleton<IRecoveryCaseHierarchySource>(this);
        services.AddSingleton<IRecoveryPasswordPresentationSource>(this);
        // BrowserTestHost already composes the actual native passkey owner. Do not fabricate a proof owner here.
    }

    public RecoveryPasswordPolicyVersion RequireEffective(DateTimeOffset at)
    {
        if (!Interval.Contains(at)) throw new InvalidOperationException("FINITE_RECOVERY_CASE_POLICY_EXPIRED");
        // Same bounded native password-policy shape as the existing private recovery fixture, isolated to this finite journey.
        var policy = new RecoveryPasswordPolicy("SYNTHETIC_FINITE_RECOVERY_011_POLICY_V1", origin, GoalReference,
            15, 128, 10_000, RecoveryPasswordSessionEffect.InvalidateExistingTunnerSessions, false,
            TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5), 32, 100, 100,
            [UnresolvedReference], "SYNTHETIC-DISPOSABLE-RUN-ONLY-PROCESSING",
            "SYNTHETIC-OWNED-RUN-CLEANUP-NOT-A-PRODUCTION-RETENTION-POLICY");
        return new(new(policy.Version, "ACTIVE", "ACCOUNT_RECOVERY_PASSWORD", Interval, null, [Provenance]), policy);
    }

    public RecoveryCaseHierarchyDecision RequireCurrent(RecoveryCaseHierarchyRequest request, DateTimeOffset at)
    {
        _ = RequireEffective(at);
        // No tier is admitted. An original neutral entry is not a proof, a governed review or a completed recovery.
        return new(false, false, "SYNTHETIC_FINITE_RECOVERY_011_HIERARCHY_V1", Provenance, request.Facts.Digest(), Interval);
    }

    public RecoveryPasswordPresentation RequirePublished(RecoveryPasswordPolicy policy, DateTimeOffset at)
    {
        var current = RequireEffective(at).Values;
        if (policy.Version != current.Version || policy.Origin != origin || policy.GoalReference != GoalReference)
            throw new InvalidOperationException("FINITE_RECOVERY_CASE_PUBLICATION_UNAVAILABLE");
        var root = RepositoryRootResolver.Resolve();
        using var source = JsonDocument.Parse(File.ReadAllText(Path.Combine(root.FullName, "web", "apps", "user", "recovery.content.proposal.json")));
        var content = source.RootElement.GetProperty("content").EnumerateObject().Select(property => Published(property.Name, property.Value.GetString()!)).ToList();
        content.AddRange([
            Published(GoalLabelKey, GoalLabel),
            Published(GoalDescriptionKey, "Start a request to regain access to your Tunner account."),
            Published(EffectLabelKey, "Existing Tunner sessions"),
            Published(EffectValueKey, "Invalidate existing Tunner sessions after an authorized repair."),
            Published(FollowUpLabelKey, "Account access"),
            Published(FollowUpValueKey, "No restoration outcome has been confirmed.")
        ]);
        return new("en", content, "SYNTHETIC_FINITE_RECOVERY_011_PUBLICATION_V1", Interval,
            GoalLabelKey, GoalDescriptionKey, EffectLabelKey, EffectValueKey,
            [new(UnresolvedReference, FollowUpLabelKey, FollowUpValueKey)]);
    }

    private static RegistrationPublishedText Published(string key, string text) =>
        new(key, "synthetic-finite-test", "synthetic-finite-recovery-011-v1", Provenance, "en", "PUBLISHED", text);
}
