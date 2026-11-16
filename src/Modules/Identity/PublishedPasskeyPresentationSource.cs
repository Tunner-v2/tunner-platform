using Microsoft.Extensions.DependencyInjection;
using Tunner.Platform.Policies;
using Tunner.PresentationContent;

namespace Tunner.Identity.Persistence;

public sealed record PasskeyContentBinding(string PasskeyPolicyVersion, string ConfigurationVersionReference, string QualificationReference,
    string ContentScope, string VariantReference, EffectivePolicyInterval EffectiveInterval);

/// <summary>Qualified Identity presentation binding; never browser input, a verifier choice or credential material.</summary>
public interface IPasskeyContentBindingSource
{
    PasskeyContentBinding RequireQualified(PasskeyPolicy policy, DateTimeOffset effectiveAt);
}

public sealed class PublishedPasskeyPresentationSource(IPresentationContentResolver resolver, IPasskeyContentBindingSource bindings,
    TimeProvider clock) : IPasskeyPresentationSource
{
    public PasskeyPresentation RequirePublished(PasskeyPolicy policy)
    {
        // PasskeyPolicy is immutable and validated by its existing constructor; presentation selects no native/provider behavior.
        ArgumentNullException.ThrowIfNull(policy); var at = clock.GetUtcNow(); var binding = bindings.RequireQualified(policy, at);
        if (binding is null || binding.PasskeyPolicyVersion != policy.Version ||
            new[] { binding.PasskeyPolicyVersion, binding.ConfigurationVersionReference, binding.QualificationReference, binding.ContentScope, binding.VariantReference }.Any(string.IsNullOrWhiteSpace) ||
            binding.EffectiveInterval is null || !binding.EffectiveInterval.Contains(at))
            throw new InvalidOperationException("Qualified Passkey presentation configuration is unavailable.");
        var projection = PublishedPresentationProjection.Require(resolver, binding.ContentScope, binding.VariantReference, at,
            "Published Passkey content provenance is unavailable.");
        try { return new(projection.Locale, projection.Content); }
        catch (ArgumentException error) { throw new InvalidOperationException("Published Passkey content is unavailable.", error); }
    }
}

public static class PublishedPasskeyPresentationRegistration
{
    /// <summary>Presentation-only selection: no Passkey policy, native provider, workflow or production values are registered.</summary>
    public static IServiceCollection AddTunnerPublishedPasskeyPresentation(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (!services.Any(x => x.ServiceType == typeof(IPresentationContentResolver)) ||
            !services.Any(x => x.ServiceType == typeof(IPasskeyContentBindingSource)) || !services.Any(x => x.ServiceType == typeof(TimeProvider)))
            throw new InvalidOperationException("Published Passkeys require explicit content, binding and clock providers.");
        if (services.Any(x => x.ServiceType == typeof(IPasskeyPresentationSource)))
            throw new InvalidOperationException("Passkey presentation may be selected only once.");
        services.AddScoped<IPasskeyPresentationSource, PublishedPasskeyPresentationSource>(); return services;
    }
}
