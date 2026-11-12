using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;

namespace Tunner.Identity.Persistence;

/// <summary>Explicit User owner composition; a native client and approved runtime profiles are separate inputs.</summary>
public static class FederationRegistration
{
    public static IServiceCollection AddTunnerFederation(this IServiceCollection services, IEnumerable<FederationProviderKind> enabledTransports)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(enabledTransports);
        var enabled = enabledTransports.ToArray();
        if (enabled.Length == 0 || enabled.Any(kind => !Enum.IsDefined(kind)) || enabled.Distinct().Count() != enabled.Length)
            throw new ArgumentException("Explicit distinct admitted transports are required.", nameof(enabledTransports));
        var capabilities = Array.AsReadOnly(enabled);
        if (services.Count(x => x.ServiceType == typeof(IFederationProfileSource)) != 1 ||
            services.Count(x => x.ServiceType == typeof(ITotpMfaRequirementSource)) != 1)
            throw new InvalidOperationException("Federation requires one explicit profile source and one current MFA requirement source.");
        Type[] required = [typeof(AuthenticationEndpoints.Surface), typeof(AuthenticationApplication),
            typeof(IdentityPersistenceDbContext), typeof(IAuthenticationPolicySource), typeof(IAccountAuthenticationAccessSource),
            typeof(AuthenticationPartitionKey), typeof(IAuthenticationPresentationSource)];
        if (required.Any(type => !services.Any(x => x.ServiceType == type)) || services.Any(x => x.ServiceType == typeof(FederationBrowserService)))
            throw new InvalidOperationException("Federation requires one configured User authentication owner.");
        services.AddScoped<FederationAttemptApplication>();
        services.AddScoped(provider => new FederationBrowserService(provider.GetRequiredService<FederationAttemptApplication>(),
            provider.GetRequiredService<IFederationProfileSource>(), provider.GetRequiredService<IAuthenticationPolicySource>(),
            provider.GetRequiredService<AuthenticationPartitionKey>(), provider.GetRequiredService<IDataProtectionProvider>(),
            provider.GetRequiredService<IAntiforgery>(), provider.GetRequiredService<TimeProvider>(),
            provider.GetRequiredService<AuthenticationEndpoints.Surface>().Origin, capabilities));
        return services;
    }
}
