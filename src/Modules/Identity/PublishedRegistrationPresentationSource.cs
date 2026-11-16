using Microsoft.Extensions.DependencyInjection;
using Tunner.Platform.Policies;
using Tunner.PresentationContent;

namespace Tunner.Identity.Persistence;

/// <summary>Identity-qualified presentation and notice links for one Registration policy. Notice IDs are not ContentVersion IDs.</summary>
public sealed record RegistrationContentBinding(string RegistrationPolicyVersion, string TermsVersion, string PrivacyVersion,
    string ConfigurationVersionReference, string QualificationReference, string ContentScope, string VariantReference,
    EffectivePolicyInterval EffectiveInterval, Uri TermsUri, Uri PrivacyUri, Uri? SupportUri);

/// <summary>Supplied by qualified Identity configuration authority, never browser context or a synthetic production fallback.</summary>
public interface IRegistrationContentBindingSource
{
    RegistrationContentBinding RequireQualified(RegistrationPolicy policy, DateTimeOffset effectiveAt);
}

/// <summary>Read-only projection to the existing Registration/verification/personal/consent presentation contract.</summary>
public sealed class PublishedRegistrationPresentationSource(IPresentationContentResolver resolver,
    IRegistrationContentBindingSource bindings, TimeProvider clock) : IRegistrationPresentationSource
{
    public RegistrationPresentation RequirePublished(RegistrationPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy); policy.Validate(); var now = clock.GetUtcNow();
        var binding = bindings.RequireQualified(policy, now);
        if (binding is null || binding.RegistrationPolicyVersion != policy.Version || binding.TermsVersion != policy.TermsVersion ||
            binding.PrivacyVersion != policy.PrivacyVersion || new[] { binding.ConfigurationVersionReference, binding.QualificationReference,
                binding.ContentScope, binding.VariantReference }.Any(string.IsNullOrWhiteSpace) ||
            binding.EffectiveInterval is null || !binding.EffectiveInterval.Contains(now) || binding.TermsUri is null || binding.PrivacyUri is null ||
            new[] { binding.TermsUri, binding.PrivacyUri, binding.SupportUri }.OfType<Uri>().Any(uri =>
                !uri.IsAbsoluteUri || uri.Scheme != Uri.UriSchemeHttps || uri.UserInfo.Length != 0))
            throw new InvalidOperationException("Qualified registration presentation configuration is unavailable.");

        var result = resolver.Resolve(new(binding.ContentScope, binding.VariantReference, now));
        if (result is null || result.Scope != binding.ContentScope || result.RequestedVariantReference != binding.VariantReference ||
            string.IsNullOrWhiteSpace(result.EffectiveVariantReference) || string.IsNullOrWhiteSpace(result.Locale) || result.Content is null ||
            result.Content.Any(item => item.Value is null || item.Key != item.Value.Key ||
                item.Value.VariantReference != result.EffectiveVariantReference || item.Value.Locale != result.Locale))
            throw new InvalidOperationException("Published registration content provenance is unavailable.");
        try
        {
            // The existing constructor owns every mandatory Registration, verification, personal-information and consent key.
            // Text selection supplies no age capability or consent evidence; those remain existing Account-owned decisions.
            return new(result.Locale, result.Content.Values.Select(item => new RegistrationPublishedText(item.Key, item.VariantReference,
                item.VersionReference, item.PublicationReference, item.Locale, "PUBLISHED", item.Text)), binding.TermsUri, binding.PrivacyUri, binding.SupportUri);
        }
        catch (ArgumentException exception)
        { throw new InvalidOperationException("Published registration content is unavailable.", exception); }
    }
}

public static class PublishedRegistrationPresentationRegistration
{
    /// <summary>Presentation-only opt-in. No Registration policy, workflow, age/consent provider or production value is registered.</summary>
    public static IServiceCollection AddTunnerPublishedRegistrationPresentation(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (!services.Any(x => x.ServiceType == typeof(IPresentationContentResolver)) ||
            !services.Any(x => x.ServiceType == typeof(IRegistrationContentBindingSource)) || !services.Any(x => x.ServiceType == typeof(TimeProvider)))
            throw new InvalidOperationException("Published registration requires explicit content, binding and clock providers.");
        if (services.Any(x => x.ServiceType == typeof(IRegistrationPresentationSource)))
            throw new InvalidOperationException("Registration presentation may be selected only once.");
        services.AddScoped<IRegistrationPresentationSource, PublishedRegistrationPresentationSource>(); return services;
    }
}
