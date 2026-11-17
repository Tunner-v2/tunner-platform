using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Tunner.Platform.Policies;
using Tunner.PresentationContent;

namespace Tunner.Identity.Persistence;

/// <summary>Explicit owner-selected presentation binding. Each separate source port qualifies its own optional action.</summary>
public sealed record SessionActionContentBinding(string AuthenticationPolicyVersion, string ConfigurationVersionReference,
    string QualificationReference, string ContentScope, string VariantReference, EffectivePolicyInterval EffectiveInterval);
public interface ISessionRevocationContentBindingSource
{
    ValueTask<SessionActionContentBinding> RequireQualifiedAsync(AuthenticationPolicy policy, DateTimeOffset effectiveAt, CancellationToken cancellationToken);
}
public interface ISessionBulkRevocationContentBindingSource
{
    ValueTask<SessionActionContentBinding> RequireQualifiedAsync(AuthenticationPolicy policy, DateTimeOffset effectiveAt, CancellationToken cancellationToken);
}
public interface ISessionRevocationAsyncPresentationSource : ISessionRevocationPresentationSource
{
    ValueTask<SessionRevocationPresentation> RequirePublishedAsync(AuthenticationPolicy policy, CancellationToken cancellationToken);
}
public interface ISessionBulkRevocationAsyncPresentationSource : ISessionBulkRevocationPresentationSource
{
    ValueTask<SessionBulkRevocationPresentation> RequirePublishedAsync(AuthenticationPolicy policy, CancellationToken cancellationToken);
}

public sealed class PublishedSessionRevocationPresentationSource(IPresentationContentResolver resolver, ISessionRevocationContentBindingSource bindings,
    TimeProvider clock) : ISessionRevocationAsyncPresentationSource
{
    public SessionRevocationPresentation RequirePublished(AuthenticationPolicy policy) => throw new InvalidOperationException("Published session action content requires asynchronous browser preparation.");
    public ValueTask<SessionRevocationPresentation> RequirePublishedAsync(AuthenticationPolicy policy, CancellationToken cancellationToken) =>
        PublishedSessionActionProjection.RequireAsync(policy, bindings.RequireQualifiedAsync, resolver, clock,
            static projection => new SessionRevocationPresentation(projection.Locale, projection.Content), cancellationToken);
}
public sealed class PublishedSessionBulkRevocationPresentationSource(IPresentationContentResolver resolver, ISessionBulkRevocationContentBindingSource bindings,
    TimeProvider clock) : ISessionBulkRevocationAsyncPresentationSource
{
    public SessionBulkRevocationPresentation RequirePublished(AuthenticationPolicy policy) => throw new InvalidOperationException("Published session action content requires asynchronous browser preparation.");
    public ValueTask<SessionBulkRevocationPresentation> RequirePublishedAsync(AuthenticationPolicy policy, CancellationToken cancellationToken) =>
        PublishedSessionActionProjection.RequireAsync(policy, bindings.RequireQualifiedAsync, resolver, clock,
            static projection => new SessionBulkRevocationPresentation(projection.Locale, projection.Content), cancellationToken);
}

/// <summary>Only the shared publication-envelope/final-binding mechanics; each existing domain constructor validates its own required copy.</summary>
internal static class PublishedSessionActionProjection
{
    internal static async ValueTask<T> RequireAsync<T>(AuthenticationPolicy policy,
        Func<AuthenticationPolicy, DateTimeOffset, CancellationToken, ValueTask<SessionActionContentBinding>> requireBinding,
        IPresentationContentResolver resolver, TimeProvider clock, Func<PublishedPresentationProjection, T> create, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(policy); policy.Validate();
        var binding = await requireBinding(policy, clock.GetUtcNow(), cancellationToken).ConfigureAwait(false); Require(binding, policy, clock.GetUtcNow());
        var snapshot = JsonSerializer.Serialize(binding);
        var projection = await PublishedPresentationProjection.RequireAsync(resolver, binding.ContentScope, binding.VariantReference, clock.GetUtcNow(),
            "Current published session action content provenance is unavailable.", cancellationToken).ConfigureAwait(false);
        T result;
        try { result = create(projection); }
        catch (ArgumentException error) { throw new InvalidOperationException("Complete published session action content is required.", error); }
        var current = await requireBinding(policy, clock.GetUtcNow(), cancellationToken).ConfigureAwait(false);
        var finalAt = clock.GetUtcNow(); Require(current, policy, finalAt);
        if (snapshot != JsonSerializer.Serialize(current) || !binding.EffectiveInterval.Contains(finalAt) ||
            projection.EffectiveInterval is null || !projection.EffectiveInterval.Contains(finalAt))
            throw new InvalidOperationException("Session action publication changed or expired during resolution.");
        cancellationToken.ThrowIfCancellationRequested(); return result;
    }
    private static void Require(SessionActionContentBinding binding, AuthenticationPolicy policy, DateTimeOffset at)
    {
        if (binding is null || binding.AuthenticationPolicyVersion != policy.Version || binding.EffectiveInterval is null || !binding.EffectiveInterval.Contains(at) ||
            new[] { binding.ConfigurationVersionReference, binding.QualificationReference, binding.ContentScope, binding.VariantReference }.Any(string.IsNullOrWhiteSpace))
            throw new InvalidOperationException("Exact qualified session action Content binding is unavailable.");
    }
}
public static class PublishedSessionRevocationPresentationRegistration
{
    public static IServiceCollection AddTunnerPublishedSessionRevocationPresentation(this IServiceCollection services)
    {
        Require(services, typeof(ISessionRevocationContentBindingSource), typeof(ISessionRevocationPresentationSource));
        services.AddScoped<ISessionRevocationPresentationSource, PublishedSessionRevocationPresentationSource>(); return services;
    }
    public static IServiceCollection AddTunnerPublishedSessionBulkRevocationPresentation(this IServiceCollection services)
    {
        Require(services, typeof(ISessionBulkRevocationContentBindingSource), typeof(ISessionBulkRevocationPresentationSource));
        services.AddScoped<ISessionBulkRevocationPresentationSource, PublishedSessionBulkRevocationPresentationSource>(); return services;
    }
    private static void Require(IServiceCollection services, Type binding, Type presentation)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (new[] { typeof(IPresentationContentResolver), binding, typeof(TimeProvider) }.Any(type => !services.Any(service => service.ServiceType == type)) ||
            services.Any(service => service.ServiceType == presentation))
            throw new InvalidOperationException("Explicit unique Content, action binding and clock providers are required.");
    }
}
