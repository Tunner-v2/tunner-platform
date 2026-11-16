using Microsoft.Extensions.DependencyInjection;
using Tunner.Platform.Policies;
using Tunner.PresentationContent;

namespace Tunner.Identity.Persistence;

/// <summary>Server-qualified content scope and legal links for one immutable Authentication policy.</summary>
public sealed record AuthenticationContentBinding(
    string AuthenticationPolicyVersion,
    string ConfigurationVersionReference,
    string QualificationReference,
    string ContentScope,
    string VariantReference,
    EffectivePolicyInterval EffectiveInterval,
    Uri TermsUri,
    Uri PrivacyUri,
    Uri? SupportUri);

/// <summary>Implemented by approved configuration authority, never by browser input or synthetic production fallback.</summary>
public interface IAuthenticationContentBindingSource
{
    AuthenticationContentBinding RequireQualified(AuthenticationPolicy policy, DateTimeOffset effectiveAt);
}

/// <summary>Adapts authoritative content publications to the existing Login presentation contract.</summary>
public sealed class PublishedAuthenticationPresentationSource(
    IPresentationContentResolver resolver,
    IAuthenticationContentBindingSource bindings,
    TimeProvider clock) : IAuthenticationPresentationSource
{
    public AuthenticationPresentation RequirePublished(AuthenticationPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        policy.Validate();
        var now = clock.GetUtcNow();
        var binding = bindings.RequireQualified(policy, now);
        if (binding is null || binding.AuthenticationPolicyVersion != policy.Version ||
            new[] { binding.ConfigurationVersionReference, binding.QualificationReference, binding.ContentScope,
                binding.VariantReference }.Any(string.IsNullOrWhiteSpace) ||
            binding.EffectiveInterval is null || !binding.EffectiveInterval.Contains(now))
            throw new InvalidOperationException("Qualified authentication content configuration is unavailable.");

        var result = resolver.Resolve(new ContentResolutionRequest(binding.ContentScope, binding.VariantReference, now));
        if (result is null || result.Scope != binding.ContentScope ||
            result.RequestedVariantReference != binding.VariantReference ||
            string.IsNullOrWhiteSpace(result.EffectiveVariantReference) || string.IsNullOrWhiteSpace(result.Locale) ||
            result.Content is null || result.Content.Any(item =>
                item.Value is null || item.Key != item.Value.Key ||
                item.Value.VariantReference != result.EffectiveVariantReference || item.Value.Locale != result.Locale))
            throw new InvalidOperationException("Published authentication content provenance is unavailable.");

        try
        {
            // The existing constructor owns the required Login keys and HTTPS-link contract.
            return new AuthenticationPresentation(result.Locale, result.Content.Values.Select(item =>
                new RegistrationPublishedText(item.Key, item.VariantReference, item.VersionReference,
                    item.PublicationReference, item.Locale, "PUBLISHED", item.Text)),
                binding.TermsUri, binding.PrivacyUri, binding.SupportUri);
        }
        catch (ArgumentException exception)
        {
            // Existing HTTP consumers map this dependency failure to their safe 503 response.
            throw new InvalidOperationException("Published authentication content is unavailable.", exception);
        }
    }
}

public static class PublishedAuthenticationPresentationRegistration
{
    /// <summary>Explicit opt-in; existing local fixture composition and host defaults remain unchanged.</summary>
    public static IServiceCollection AddTunnerPublishedAuthenticationPresentation(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (!services.Any(item => item.ServiceType == typeof(IPresentationContentResolver)) ||
            !services.Any(item => item.ServiceType == typeof(IAuthenticationContentBindingSource)) ||
            !services.Any(item => item.ServiceType == typeof(TimeProvider)))
            throw new InvalidOperationException("Published authentication requires explicit content, configuration and clock providers.");
        if (services.Any(item => item.ServiceType == typeof(IAuthenticationPresentationSource)))
            throw new InvalidOperationException("Authentication presentation may be activated only once.");
        services.AddScoped<IAuthenticationPresentationSource, PublishedAuthenticationPresentationSource>();
        return services;
    }
}
