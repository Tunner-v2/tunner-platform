using System.Collections.Immutable;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Tunner.Platform.Policies;
using Tunner.PresentationContent;

namespace Tunner.Identity.Persistence;

/// <summary>Explicit semantic owner qualification. Semantics supplies truthful Product/current Account/assurance/legal and transport facts.
/// Its Content and ScopeDescriptions arrays are ignored; all wording and provenance come from Content.</summary>
public sealed record ProductAuthorizationPresentationContentBinding(ProductAuthorizationPresentation Semantics,
    string ContentScope, string VariantReference, string QualificationReference);
public interface IProductAuthorizationPresentationContentBindingSource
{
    ValueTask<ProductAuthorizationPresentationContentBinding> RequireQualifiedAsync(ProductAuthorizationPresentationFacts facts,
        DateTimeOffset effectiveAt, CancellationToken cancellationToken);
}

public sealed class PublishedProductAuthorizationPresentationSource(IPresentationContentResolver resolver,
    IProductAuthorizationPresentationContentBindingSource bindings, TimeProvider clock) : IProductAuthorizationPresentationSource
{
    public async ValueTask<ProductAuthorizationPresentation> RequirePublishedAsync(ProductAuthorizationPresentationFacts facts,
        DateTimeOffset at, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(facts);
        var binding = await bindings.RequireQualifiedAsync(facts, at, cancellationToken).ConfigureAwait(false);
        RequireBinding(binding, facts, clock.GetUtcNow()); var original = Snapshot(binding);
        var resolved = await resolver.ResolveAsync(new(binding.ContentScope, binding.VariantReference, clock.GetUtcNow()), cancellationToken).ConfigureAwait(false);
        if (resolved is null || resolved.Scope != binding.ContentScope || resolved.RequestedVariantReference != binding.VariantReference ||
            resolved.EffectiveInterval is null || string.IsNullOrWhiteSpace(resolved.Locale) || string.IsNullOrWhiteSpace(resolved.EffectiveVariantReference) ||
            resolved.Content is null || resolved.Content.Any(pair => pair.Value is null || pair.Key != pair.Value.Key || pair.Value.Locale != resolved.Locale ||
                pair.Value.VariantReference != resolved.EffectiveVariantReference || new[] { pair.Value.Key, pair.Value.VersionReference, pair.Value.PublicationReference, pair.Value.Text }.Any(string.IsNullOrWhiteSpace)))
            throw new InvalidOperationException("Current published Product presentation provenance and effective bounds are required.");
        var current = await bindings.RequireQualifiedAsync(facts, clock.GetUtcNow(), cancellationToken).ConfigureAwait(false);
        var finalAt = clock.GetUtcNow(); RequireBinding(current, facts, finalAt);
        if (Snapshot(current) != original || !resolved.EffectiveInterval.Contains(finalAt)) throw new InvalidOperationException("Product presentation qualification changed or expired.");
        var texts = resolved.Content.Values.ToDictionary(text => text.Key, text => new RegistrationPublishedText(text.Key, text.VariantReference,
            text.VersionReference, text.PublicationReference, text.Locale, "PUBLISHED", text.Text), StringComparer.Ordinal);
        if (facts.Scopes.IsDefault || facts.Scopes.Any(scope => !texts.ContainsKey(scope))) throw new InvalidOperationException("Exact published Product scope descriptions are required.");
        var semantic = binding.Semantics; var metadata = semantic.Metadata;
        var interval = Intersection(metadata.EffectiveInterval, resolved.EffectiveInterval);
        var result = semantic with
        {
            Metadata = new(metadata.VersionId, metadata.Status, metadata.Scope, interval, metadata.SupersedesVersionId, metadata.ApprovalEvidenceReferences),
            Locale = resolved.Locale, Content = texts.Values.OrderBy(text => text.Key, StringComparer.Ordinal).ToImmutableArray(),
            ScopeDescriptions = facts.Scopes.Select(scope => texts[scope]).ToImmutableArray()
        };
        if (facts.LoginOnly) result.RequireLogin(facts, clock.GetUtcNow()); else result.Require(facts, clock.GetUtcNow());
        cancellationToken.ThrowIfCancellationRequested(); return result;
    }
    private static string Snapshot(ProductAuthorizationPresentationContentBinding binding) => JsonSerializer.Serialize(binding with
    { Semantics = binding.Semantics with { Content = [], ScopeDescriptions = [] } });
    private static void RequireBinding(ProductAuthorizationPresentationContentBinding binding, ProductAuthorizationPresentationFacts facts, DateTimeOffset at)
    {
        if (binding?.Semantics?.Metadata is not { } metadata || binding.Semantics.FactsDigest != facts.Digest || metadata.Scope != facts.Origin || metadata.Status != "ACTIVE" ||
            !metadata.EffectiveInterval.Contains(at) || new[] { binding.ContentScope, binding.VariantReference, binding.QualificationReference }.Any(string.IsNullOrWhiteSpace))
            throw new InvalidOperationException("Exact qualified Product presentation semantics and Content binding are unavailable.");
    }
    private static EffectivePolicyInterval Intersection(EffectivePolicyInterval a, EffectivePolicyInterval b)
    {
        var from = a.From > b.From ? a.From : b.From; var includesFrom = (from != a.From || a.IncludesFrom) && (from != b.From || b.IncludesFrom);
        var until = a.Until is null || b.Until is { } end && end < a.Until ? b.Until : a.Until;
        return new(from, includesFrom, until, (until != a.Until || a.IncludesUntil) && (until != b.Until || b.IncludesUntil));
    }
}
public static class PublishedProductAuthorizationPresentationRegistration
{
    /// <summary>Presentation-only opt-in. No Product registry, Account authority, policy, continuation, proof or native route activation.</summary>
    public static IServiceCollection AddTunnerPublishedProductAuthorizationPresentation(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (new[] { typeof(IPresentationContentResolver), typeof(IProductAuthorizationPresentationContentBindingSource), typeof(TimeProvider) }.Any(type => !services.Any(service => service.ServiceType == type)) ||
            services.Any(service => service.ServiceType == typeof(IProductAuthorizationPresentationSource)))
            throw new InvalidOperationException("Product presentation requires unique explicit Content, semantic binding and clock providers.");
        services.AddScoped<IProductAuthorizationPresentationSource, PublishedProductAuthorizationPresentationSource>(); return services;
    }
}
