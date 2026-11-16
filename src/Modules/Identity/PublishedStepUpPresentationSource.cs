using Microsoft.Extensions.DependencyInjection;
using Tunner.Platform.Policies;
using Tunner.PresentationContent;

namespace Tunner.Identity.Persistence;

public sealed record StepUpContentBinding(AssurancePolicyScope AssuranceScope, string AssurancePolicyVersion,
    string ConfigurationVersionReference, string QualificationReference, string ContentScope, string VariantReference,
    EffectivePolicyInterval EffectiveInterval)
{
    internal void Require()
    {
        if (AssuranceScope is null || EffectiveInterval is null ||
            new[] { AssurancePolicyVersion, ConfigurationVersionReference, QualificationReference, ContentScope, VariantReference }.Any(string.IsNullOrWhiteSpace))
            throw new IdentityConfigurationUnavailableException();
    }
    internal void RequireCurrent(AssurancePolicyVersion policy, DateTimeOffset at)
    {
        Require(); ArgumentNullException.ThrowIfNull(policy);
        if (policy.Metadata.Scope != AssuranceScope || policy.Metadata.VersionId != AssurancePolicyVersion)
            throw new IdentityConfigurationUnavailableException();
        _ = new AssurancePolicyCatalog([policy]).RequireEffective(AssuranceScope, at, AssurancePolicyVersion);
        if (!EffectiveInterval.Contains(at)) throw new IdentityConfigurationUnavailableException();
    }
}

/// <summary>Qualified exact operation/context/version binding, independent of the current actor's available proof methods.</summary>
public interface IStepUpContentBindingSource
{
    StepUpContentBinding RequireQualified(AssurancePolicyVersion policy, DateTimeOffset effectiveAt);
}

public sealed class PublishedStepUpPresentationSource(IPresentationContentResolver resolver, IStepUpContentBindingSource bindings,
    TimeProvider clock) : IStepUpPresentationSource
{
    public StepUpPresentation RequirePublished(AssurancePolicyVersion policy)
    {
        ArgumentNullException.ThrowIfNull(policy); var at = clock.GetUtcNow();
        _ = new AssurancePolicyCatalog([policy]).RequireEffective(policy.Metadata.Scope, at, policy.Metadata.VersionId);
        var binding = bindings.RequireQualified(policy, at) ?? throw new IdentityConfigurationUnavailableException(); binding.RequireCurrent(policy, at);
        var projection = PublishedPresentationProjection.Require(resolver, binding.ContentScope, binding.VariantReference, at,
            "Published operation proof content provenance is unavailable.");
        // Optional TOTP/federation/provider-label checks remain with actual endpoint capabilities.
        try { return new(projection.Locale, projection.Content); }
        catch (ArgumentException error) { throw new InvalidOperationException("Published operation proof content is unavailable.", error); }
    }
}

public static class PublishedStepUpPresentationRegistration
{
    /// <summary>Presentation-only opt-in; no policy, operation owner, proof provider or workflow activation.</summary>
    public static IServiceCollection AddTunnerPublishedStepUpPresentation(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (!services.Any(x => x.ServiceType == typeof(IPresentationContentResolver)) ||
            !services.Any(x => x.ServiceType == typeof(IStepUpContentBindingSource)) || !services.Any(x => x.ServiceType == typeof(TimeProvider)))
            throw new InvalidOperationException("Published operation proof requires explicit content, binding and clock providers.");
        if (services.Any(x => x.ServiceType == typeof(IStepUpPresentationSource)))
            throw new InvalidOperationException("Operation proof presentation may be selected only once.");
        services.AddScoped<IStepUpPresentationSource, PublishedStepUpPresentationSource>(); return services;
    }
}
