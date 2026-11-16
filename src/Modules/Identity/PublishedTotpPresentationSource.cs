using Microsoft.Extensions.DependencyInjection;
using Tunner.Platform.Policies;
using Tunner.PresentationContent;

namespace Tunner.Identity.Persistence;

public sealed record TotpContentBinding(string TotpPolicyVersion, string ConfigurationVersionReference, string QualificationReference,
    string ContentScope, string VariantReference, EffectivePolicyInterval EffectiveInterval);

/// <summary>Qualified Identity configuration, never browser input or inline authenticator material.</summary>
public interface ITotpContentBindingSource
{
    TotpContentBinding RequireQualified(TotpPolicy policy, DateTimeOffset effectiveAt);
}

/// <summary>Presentation-only projection; enrollment, proof, seed handling and session effects stay in their existing owners.</summary>
public sealed class PublishedTotpPresentationSource(IPresentationContentResolver resolver, ITotpContentBindingSource bindings,
    TimeProvider clock) : ITotpPresentationSource
{
    public TotpPresentation RequirePublished(TotpPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy); policy.Validate(); var at = clock.GetUtcNow();
        var binding = bindings.RequireQualified(policy, at);
        if (binding is null || binding.TotpPolicyVersion != policy.Version ||
            new[] { binding.ConfigurationVersionReference, binding.QualificationReference, binding.ContentScope, binding.VariantReference }.Any(string.IsNullOrWhiteSpace) ||
            binding.EffectiveInterval is null || !binding.EffectiveInterval.Contains(at))
            throw new InvalidOperationException("Qualified TOTP presentation configuration is unavailable.");
        var result = PublishedPresentationProjection.Require(resolver, binding.ContentScope, binding.VariantReference, at,
            "Published TOTP content provenance is unavailable.");
        try { return new(result.Locale, result.Content); }
        catch (ArgumentException error) { throw new InvalidOperationException("Published TOTP content is unavailable.", error); }
    }
}

public static class PublishedTotpPresentationRegistration
{
    /// <summary>Explicit presentation-only selection. No TOTP policy, seed, workflow or production value is installed.</summary>
    public static IServiceCollection AddTunnerPublishedTotpPresentation(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (!services.Any(x => x.ServiceType == typeof(IPresentationContentResolver)) ||
            !services.Any(x => x.ServiceType == typeof(ITotpContentBindingSource)) || !services.Any(x => x.ServiceType == typeof(TimeProvider)))
            throw new InvalidOperationException("Published TOTP requires explicit content, binding and clock providers.");
        if (services.Any(x => x.ServiceType == typeof(ITotpPresentationSource)))
            throw new InvalidOperationException("TOTP presentation may be selected only once.");
        services.AddScoped<ITotpPresentationSource, PublishedTotpPresentationSource>(); return services;
    }
}
