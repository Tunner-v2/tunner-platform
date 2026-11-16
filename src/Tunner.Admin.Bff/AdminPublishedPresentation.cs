using Tunner.Platform.Policies;
using Tunner.PresentationContent;

namespace Tunner.Admin.Bff;

/// <summary>Read-only published chrome composition. Resolving content grants no Content or Identity editing capability.</summary>
internal static class AdminPublishedPresentation
{
    internal static async Task<ContentResolution> ResolveAsync(HttpContext h, string scope, string variant, EffectivePolicyInterval interval)
    {
        var clock = h.RequestServices.GetRequiredService<TimeProvider>();
        if (string.IsNullOrWhiteSpace(scope) || string.IsNullOrWhiteSpace(variant) || interval is null || !interval.Contains(clock.GetUtcNow())) throw new ContentUnavailableException();
        var resolver = h.RequestServices.GetService<IPresentationContentResolver>() ?? throw new ContentUnavailableException();
        var result = await resolver.ResolveAsync(new(scope, variant, clock.GetUtcNow()), h.RequestAborted).ConfigureAwait(false);
        if (result is null || result.Scope != scope || result.RequestedVariantReference != variant ||
            string.IsNullOrWhiteSpace(result.EffectiveVariantReference) || string.IsNullOrWhiteSpace(result.Locale) || result.Content is null || result.Content.Count == 0 ||
            result.Content.Any(x => x.Value is null || x.Key != x.Value.Key || x.Value.VariantReference != result.EffectiveVariantReference ||
                x.Value.Locale != result.Locale || string.IsNullOrWhiteSpace(x.Value.VersionReference) || string.IsNullOrWhiteSpace(x.Value.PublicationReference)) || !interval.Contains(clock.GetUtcNow()))
            throw new ContentUnavailableException();
        return result;
    }
}
