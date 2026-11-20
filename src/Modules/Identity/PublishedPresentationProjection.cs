using Tunner.PresentationContent;
using Tunner.Platform.Policies;

namespace Tunner.Identity.Persistence;

/// <summary>Shared publication-envelope mechanics; each consumer retains its own binding and required-content contract.</summary>
internal sealed record PublishedPresentationProjection(string Locale, IReadOnlyList<RegistrationPublishedText> Content)
{
    internal EffectivePolicyInterval? EffectiveInterval { get; init; }
    internal static PublishedPresentationProjection Require(IPresentationContentResolver resolver, string scope, string variant,
        DateTimeOffset at, string unavailableMessage) => Project(resolver.Resolve(new(scope, variant, at)), scope, variant, unavailableMessage);
    internal static async Task<PublishedPresentationProjection> RequireAsync(IPresentationContentResolver resolver, string scope, string variant,
        DateTimeOffset at, string unavailableMessage, CancellationToken cancellationToken) =>
        Project(await resolver.ResolveAsync(new(scope, variant, at), cancellationToken).ConfigureAwait(false), scope, variant, unavailableMessage);
    private static PublishedPresentationProjection Project(ContentResolution result, string scope, string variant, string unavailableMessage)
    {
        if (result is null || result.Scope != scope || result.RequestedVariantReference != variant ||
            string.IsNullOrWhiteSpace(result.EffectiveVariantReference) || string.IsNullOrWhiteSpace(result.Locale) || result.Content is null ||
            result.Content.Any(item => item.Value is null || item.Key != item.Value.Key ||
                item.Value.VariantReference != result.EffectiveVariantReference || item.Value.Locale != result.Locale))
            throw new InvalidOperationException(unavailableMessage);
        return new(result.Locale, Array.AsReadOnly(result.Content.Values.Select(item => new RegistrationPublishedText(item.Key,
            item.VariantReference, item.VersionReference, item.PublicationReference, item.Locale, "PUBLISHED", item.Text)).ToArray())) { EffectiveInterval = result.EffectiveInterval };
    }
}
