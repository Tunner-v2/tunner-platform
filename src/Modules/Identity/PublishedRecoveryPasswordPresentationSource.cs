using System.Collections.Immutable;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Tunner.PresentationContent;
using Tunner.Platform.Policies;

namespace Tunner.Identity.Persistence;

/// <summary>Qualified Recovery owner facts and exact key mapping. Content wording never selects a goal, effect or follow-up.</summary>
public sealed record RecoveryPasswordContentBinding(RecoveryPasswordPolicyVersion Policy, string ConfigurationVersionReference,
    string QualificationReference, string ContentScope, string VariantReference, EffectivePolicyInterval EffectiveInterval,
    string GoalLabelKey, string GoalDescriptionKey, string EffectLabelKey, string EffectValueKey,
    ImmutableArray<RecoveryPasswordFollowUp> FollowUps)
{ public override string ToString() => nameof(RecoveryPasswordContentBinding); }
public interface IRecoveryPasswordContentBindingSource
{
    ValueTask<RecoveryPasswordContentBinding> RequireQualifiedAsync(RecoveryPasswordPolicy policy, DateTimeOffset effectiveAt, CancellationToken cancellationToken);
}
public interface IRecoveryPasswordAsyncPresentationSource : IRecoveryPasswordPresentationSource
{
    ValueTask<RecoveryPasswordPresentation> RequirePublishedAsync(RecoveryPasswordPolicy policy, DateTimeOffset at, CancellationToken cancellationToken);
}
public sealed class PublishedRecoveryPasswordPresentationSource(IPresentationContentResolver resolver, IRecoveryPasswordContentBindingSource bindings,
    TimeProvider clock) : IRecoveryPasswordAsyncPresentationSource
{
    public RecoveryPasswordPresentation RequirePublished(RecoveryPasswordPolicy policy, DateTimeOffset at) =>
        throw new InvalidOperationException("Published recovery content requires asynchronous browser preparation.");
    public async ValueTask<RecoveryPasswordPresentation> RequirePublishedAsync(RecoveryPasswordPolicy policy, DateTimeOffset at, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(policy); var expectedPolicy = JsonSerializer.Serialize(policy);
        try
        {
            var binding = await bindings.RequireQualifiedAsync(policy, at, cancellationToken).ConfigureAwait(false);
            Require(binding, policy, expectedPolicy, clock.GetUtcNow()); var snapshot = JsonSerializer.Serialize(binding);
            var projection = await PublishedPresentationProjection.RequireAsync(resolver, binding.ContentScope, binding.VariantReference, clock.GetUtcNow(),
                "Current published recovery content provenance is unavailable.", cancellationToken).ConfigureAwait(false);
            var current = await bindings.RequireQualifiedAsync(policy, clock.GetUtcNow(), cancellationToken).ConfigureAwait(false);
            var finalAt = clock.GetUtcNow(); Require(current, policy, expectedPolicy, finalAt);
            if (snapshot != JsonSerializer.Serialize(current) || projection.EffectiveInterval is null || !projection.EffectiveInterval.Contains(finalAt))
                throw new InvalidOperationException("Recovery publication qualification changed or expired.");
            var interval = PublishedPresentationAvailability.Intersect(binding.Policy.Metadata.EffectiveInterval, binding.EffectiveInterval, projection.EffectiveInterval);
            RecoveryPasswordPresentation publication;
            try { publication = new(projection.Locale, projection.Content, binding.ConfigurationVersionReference, interval,
                binding.GoalLabelKey, binding.GoalDescriptionKey, binding.EffectLabelKey, binding.EffectValueKey, binding.FollowUps); }
            catch (ArgumentException error) { throw new InvalidOperationException("Complete published recovery goal/effect provenance is required.", error); }
            publication.Require(policy, clock.GetUtcNow()); cancellationToken.ThrowIfCancellationRequested(); return publication;
        }
        catch (OperationCanceledException error) when (!cancellationToken.IsCancellationRequested)
        { throw new InvalidOperationException("Current published recovery dependency is unavailable.", error); }
        catch (Exception error) when (error is System.Data.Common.DbException or HttpRequestException or TimeoutException)
        { cancellationToken.ThrowIfCancellationRequested(); throw new InvalidOperationException("Current published recovery content is unavailable.", error); }
    }
    private static void Require(RecoveryPasswordContentBinding binding, RecoveryPasswordPolicy policy, string expectedPolicy, DateTimeOffset at)
    {
        if (binding?.Policy is null || binding.EffectiveInterval is null || !binding.EffectiveInterval.Contains(at) || binding.FollowUps.IsDefault ||
            new[] { binding.ConfigurationVersionReference, binding.QualificationReference, binding.ContentScope, binding.VariantReference,
                binding.GoalLabelKey, binding.GoalDescriptionKey, binding.EffectLabelKey, binding.EffectValueKey }.Any(string.IsNullOrWhiteSpace) ||
            JsonSerializer.Serialize(policy) != expectedPolicy || JsonSerializer.Serialize(binding.Policy.Values) != expectedPolicy)
            throw new InvalidOperationException("Exact qualified Recovery owner binding is unavailable.");
        binding.Policy.Require(policy.Origin, at);
    }
}
public static class PublishedRecoveryPasswordPresentationRegistration
{
    /// <summary>Presentation-only selection; no Recovery policy, proof capability, dispatcher, application or route activation.</summary>
    public static IServiceCollection AddTunnerPublishedRecoveryPasswordPresentation(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (new[] { typeof(IPresentationContentResolver), typeof(IRecoveryPasswordContentBindingSource), typeof(TimeProvider) }.Any(type => !services.Any(service => service.ServiceType == type)) ||
            services.Any(service => service.ServiceType == typeof(IRecoveryPasswordPresentationSource)))
            throw new InvalidOperationException("Published Recovery requires unique explicit Content, owner binding and clock providers.");
        services.AddScoped<IRecoveryPasswordPresentationSource, PublishedRecoveryPasswordPresentationSource>(); return services;
    }
}
