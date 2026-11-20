using Tunner.Platform.Policies;

namespace Tunner.Identity.Persistence;

/// <summary>Published key mappings only; the Recovery owner supplies the policy, goal and effects.</summary>
public sealed record RecoveryPasswordContentBindingValues(string RecoveryPasswordPolicyVersion, string ConfigurationVersionReference,
    string QualificationReference, string ContentScope, string VariantReference, EffectivePolicyInterval EffectiveInterval,
    string GoalLabelKey, string GoalDescriptionKey, string EffectLabelKey, string EffectValueKey,
    IReadOnlyList<RecoveryPasswordFollowUp> FollowUps)
{
    internal RecoveryPasswordContentBindingValues Snapshot()
    {
        Require();
        return this with { FollowUps = Array.AsReadOnly(FollowUps.Select(item => new RecoveryPasswordFollowUp(item.Reference, item.LabelKey, item.ValueKey)).ToArray()) };
    }

    internal void Require()
    {
        if (new[] { RecoveryPasswordPolicyVersion, ConfigurationVersionReference, QualificationReference, ContentScope, VariantReference,
            GoalLabelKey, GoalDescriptionKey, EffectLabelKey, EffectValueKey }.Any(string.IsNullOrWhiteSpace) || EffectiveInterval is null ||
            FollowUps is null || FollowUps.Any(item => item is null || new[] { item.Reference, item.LabelKey, item.ValueKey }.Any(string.IsNullOrWhiteSpace)) ||
            FollowUps.Select(item => item.Reference).Distinct(StringComparer.Ordinal).Count() != FollowUps.Count)
            throw new IdentityConfigurationUnavailableException();
    }

    internal void RequireCurrent(RecoveryPasswordPolicyVersion owner, DateTimeOffset at)
    {
        Require();
        owner.Require(owner.Values.Origin, at);
        if (owner.Values.Version != RecoveryPasswordPolicyVersion || !FollowUps.Select(item => item.Reference).Order(StringComparer.Ordinal)
            .SequenceEqual(owner.Values.UnresolvedDownstreamEffects.Order(StringComparer.Ordinal), StringComparer.Ordinal))
            throw new IdentityConfigurationUnavailableException();
    }

    internal static RecoveryPasswordPolicyVersion RequireOwner(IRecoveryPasswordPolicySource? source, string version, DateTimeOffset at)
    {
        try
        {
            var owner = source?.RequireEffective(at) ?? throw new IdentityConfigurationUnavailableException();
            owner.Require(owner.Values.Origin, at);
            if (owner.Values.Version != version) throw new IdentityConfigurationUnavailableException();
            return owner;
        }
        catch (Exception error) when (error is InvalidOperationException or ArgumentException)
        { throw new IdentityConfigurationUnavailableException(); }
    }

    public override string ToString() => nameof(RecoveryPasswordContentBindingValues);
}
