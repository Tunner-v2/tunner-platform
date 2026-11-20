using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Tunner.Platform.Policies;
using Tunner.PresentationContent;

namespace Tunner.Identity.Persistence;

public sealed record SessionListingContentBinding(string AuthenticationPolicyVersion, string ConfigurationVersionReference, string QualificationReference,
    string ContentScope, string VariantReference, EffectivePolicyInterval EffectiveInterval);

/// <summary>Qualified presentation binding; listing capacity and session permissions remain separate owner inputs.</summary>
public interface ISessionListingContentBindingSource
{
    SessionListingContentBinding RequireQualified(AuthenticationPolicy policy, DateTimeOffset effectiveAt);
}

/// <summary>Presentation-only projection; authentication and all session lifecycle effects remain with their existing owners.</summary>
public sealed class PublishedSessionListingPresentationSource(IPresentationContentResolver resolver, ISessionListingContentBindingSource bindings,
    TimeProvider clock) : ISessionListingPresentationSource
{
    public SessionListingPresentation RequirePublished(AuthenticationPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy); policy.Validate(); var at = clock.GetUtcNow();
        var binding = bindings.RequireQualified(policy, at);
        if (binding is null || binding.AuthenticationPolicyVersion != policy.Version ||
            new[] { binding.ConfigurationVersionReference, binding.QualificationReference, binding.ContentScope, binding.VariantReference }.Any(string.IsNullOrWhiteSpace) ||
            binding.EffectiveInterval is null || !binding.EffectiveInterval.Contains(at))
            throw new InvalidOperationException("Qualified session listing presentation configuration is unavailable.");
        var result = PublishedPresentationProjection.Require(resolver, binding.ContentScope, binding.VariantReference, at,
            "Published session listing content provenance is unavailable.");
        SessionListingPresentation presentation;
        try { presentation = new(result.Locale, result.Content); }
        catch (ArgumentException error) { throw new InvalidOperationException("Published session listing content is unavailable.", error); }
        // Storage and the final binding lookup can both outlive the original effective frontier.
        // Compare values, including interval bounds, rather than the interval object's reference identity.
        var current = bindings.RequireQualified(policy, clock.GetUtcNow());
        var finalAt = clock.GetUtcNow();
        if (current is null || current.EffectiveInterval is null || JsonSerializer.Serialize(current) != JsonSerializer.Serialize(binding) ||
            !binding.EffectiveInterval.Contains(finalAt) || !current.EffectiveInterval.Contains(finalAt))
            throw new InvalidOperationException("Qualified session listing presentation configuration changed or expired.");
        return presentation;
    }
}

public static class PublishedSessionListingPresentationRegistration
{
    /// <summary>Explicit presentation-only selection. Listing routes still require their independently qualified budget source.</summary>
    public static IServiceCollection AddTunnerPublishedSessionListingPresentation(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (!services.Any(x => x.ServiceType == typeof(IPresentationContentResolver)) ||
            !services.Any(x => x.ServiceType == typeof(ISessionListingContentBindingSource)) || !services.Any(x => x.ServiceType == typeof(TimeProvider)))
            throw new InvalidOperationException("Published session listing requires explicit content, binding and clock providers.");
        if (services.Any(x => x.ServiceType == typeof(ISessionListingPresentationSource)))
            throw new InvalidOperationException("session listing presentation may be selected only once.");
        services.AddScoped<ISessionListingPresentationSource, PublishedSessionListingPresentationSource>(); return services;
    }
}
