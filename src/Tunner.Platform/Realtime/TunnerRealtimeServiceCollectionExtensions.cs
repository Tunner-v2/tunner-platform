using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Tunner.Platform.Realtime;

/// <summary>Technical policy names required for a Tunner browser-realtime endpoint.</summary>
public static class TunnerRealtimePolicies
{
    public const string AuthenticatedConnect = "tunner.realtime.authenticated-connect";
}

/// <summary>
/// Explicit BFF-owned mapping requirements. Named policies ensure a host cannot
/// expose this browser endpoint without declaring its origin and rate protection.
/// </summary>
public sealed record TunnerRealtimeEndpointOptions(string Path, string BrowserOriginPolicy, string ConnectionRateLimitPolicy)
{
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Path) || !Path.StartsWith('/'))
        {
            throw new ArgumentException("Realtime endpoint path must be absolute.", nameof(Path));
        }

        ValidatePolicyName(BrowserOriginPolicy, nameof(BrowserOriginPolicy));
        ValidatePolicyName(ConnectionRateLimitPolicy, nameof(ConnectionRateLimitPolicy));
    }

    private static void ValidatePolicyName(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Any(char.IsWhiteSpace))
        {
            throw new ArgumentException("Realtime policy names must be explicit non-empty identifiers.", parameterName);
        }
    }
}

/// <summary>Registers the reusable non-authoritative SignalR foundation.</summary>
public static class TunnerRealtimeServiceCollectionExtensions
{
    public static IServiceCollection AddTunnerRealtimeFoundation(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSignalR();
        services.AddAuthorization(options =>
        {
            options.AddPolicy(TunnerRealtimePolicies.AuthenticatedConnect, policy => policy.RequireAuthenticatedUser());
        });
        services.TryAddSingleton<IRealtimeAudienceResolver, DenyAllRealtimeAudienceResolver>();
        services.TryAddSingleton<IRealtimeHintPublisher, SignalRRealtimeHintPublisher>();
        return services;
    }
}

/// <summary>Maps the SignalR foundation only at an authenticated, protected BFF boundary.</summary>
public static class TunnerRealtimeEndpointRouteBuilderExtensions
{
    public static HubEndpointConventionBuilder MapTunnerRealtimeFoundation(
        this IEndpointRouteBuilder endpoints,
        TunnerRealtimeEndpointOptions options)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();

        return endpoints.MapHub<TunnerRealtimeHub>(options.Path)
            .RequireAuthorization(TunnerRealtimePolicies.AuthenticatedConnect)
            .RequireCors(options.BrowserOriginPolicy)
            .RequireRateLimiting(options.ConnectionRateLimitPolicy);
    }
}