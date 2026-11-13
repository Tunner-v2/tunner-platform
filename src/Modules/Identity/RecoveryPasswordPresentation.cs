using System.Collections.ObjectModel;
using Tunner.Platform.Policies;

namespace Tunner.Identity.Persistence;

public sealed record RecoveryPasswordFollowUp(string Reference, string LabelKey, string ValueKey);
public sealed class RecoveryPasswordPresentation
{
    public RecoveryPasswordPresentation(string locale, IEnumerable<RegistrationPublishedText> content, string version, EffectivePolicyInterval effectiveInterval,
        string goalLabelKey, string goalDescriptionKey, string effectLabelKey, string effectValueKey, IEnumerable<RecoveryPasswordFollowUp> followUps)
    {
        ArgumentNullException.ThrowIfNull(content); ArgumentNullException.ThrowIfNull(followUps); ArgumentNullException.ThrowIfNull(effectiveInterval);
        var published = content.ToDictionary(x => x.Key, StringComparer.Ordinal); var follow = followUps.ToArray();
        var keys = new[] { goalLabelKey, goalDescriptionKey, effectLabelKey, effectValueKey }.Concat(follow.SelectMany(x => new[] { x.LabelKey, x.ValueKey }));
        if (string.IsNullOrWhiteSpace(locale) || string.IsNullOrWhiteSpace(version) || keys.Any(x => string.IsNullOrWhiteSpace(x) || !published.ContainsKey(x)) ||
            published.Values.Any(x => x.State != "PUBLISHED" || x.Locale != locale || new[] { x.Key, x.Text, x.VariantRef, x.VersionRef, x.PublicationRef }.Any(string.IsNullOrWhiteSpace)) ||
            follow.Any(x => string.IsNullOrWhiteSpace(x.Reference)) || follow.Select(x => x.Reference).Distinct(StringComparer.Ordinal).Count() != follow.Length)
            throw new ArgumentException("Recovery requires complete published goal/effect/follow-up provenance.");
        Locale = locale; Content = new ReadOnlyDictionary<string, RegistrationPublishedText>(published); Version = version; EffectiveInterval = effectiveInterval;
        GoalLabelKey = goalLabelKey; GoalDescriptionKey = goalDescriptionKey; EffectLabelKey = effectLabelKey; EffectValueKey = effectValueKey;
        FollowUps = Array.AsReadOnly(follow);
    }
    public string Locale { get; }
    public IReadOnlyDictionary<string, RegistrationPublishedText> Content { get; }
    public string Version { get; }
    public EffectivePolicyInterval EffectiveInterval { get; }
    public string GoalLabelKey { get; }
    public string GoalDescriptionKey { get; }
    public string EffectLabelKey { get; }
    public string EffectValueKey { get; }
    public IReadOnlyList<RecoveryPasswordFollowUp> FollowUps { get; }
    internal void Require(RecoveryPasswordPolicy policy, DateTimeOffset at)
    {
        if (!EffectiveInterval.Contains(at) || !FollowUps.Select(x => x.Reference).Order(StringComparer.Ordinal)
            .SequenceEqual(policy.UnresolvedDownstreamEffects.Order(StringComparer.Ordinal), StringComparer.Ordinal))
            throw new InvalidOperationException("Current published recovery effects are unavailable.");
    }
    public override string ToString() => nameof(RecoveryPasswordPresentation);
}
public interface IRecoveryPasswordPresentationSource
{
    RecoveryPasswordPresentation RequirePublished(RecoveryPasswordPolicy policy, DateTimeOffset at);
}
