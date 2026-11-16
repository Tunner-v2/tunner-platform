using Tunner.PresentationContent;

namespace Tunner.Identity.Persistence;

/// <summary>Shared publication-envelope mechanics; each consumer retains its own binding and required-content contract.</summary>
internal sealed record PublishedPresentationProjection(string Locale, IReadOnlyList<RegistrationPublishedText> Content)
{
    internal static PublishedPresentationProjection Require(IPresentationContentResolver resolver, string scope, string variant,
        DateTimeOffset at, string unavailableMessage)
    {
        var result = resolver.Resolve(new(scope, variant, at));
        if (result is null || result.Scope != scope || result.RequestedVariantReference != variant ||
            string.IsNullOrWhiteSpace(result.EffectiveVariantReference) || string.IsNullOrWhiteSpace(result.Locale) || result.Content is null ||
            result.Content.Any(item => item.Value is null || item.Key != item.Value.Key ||
                item.Value.VariantReference != result.EffectiveVariantReference || item.Value.Locale != result.Locale))
            throw new InvalidOperationException(unavailableMessage);
        return new(result.Locale, Array.AsReadOnly(result.Content.Values.Select(item => new RegistrationPublishedText(item.Key,
            item.VariantReference, item.VersionReference, item.PublicationReference, item.Locale, "PUBLISHED", item.Text)).ToArray()));
    }
}
