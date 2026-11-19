using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Tunner.Platform.Operations;

namespace Tunner.Identity.Persistence;

public static class RecoveryEndpoints
{
    private static readonly string[] Commands = ["start", "result", "cancel", "repair", "passkey/options", "passkey/repair", "contact/options", "contact/repair", "review/repair"];
    public static IServiceCollection AddTunnerRecoveryPassword(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        Type[] required = [typeof(AuthenticationApplication), typeof(AuthenticationEndpoints.Surface),
            typeof(IRecoveryPasswordPolicySource), typeof(IRecoveryCaseHierarchySource), typeof(IRecoveryPasswordPresentationSource),
            typeof(IRegistrationPasswordBlocklist), typeof(IAuthenticationPolicySource), typeof(AuthenticationPartitionKey)];
        if (required.Any(type => services.Count(x => x.ServiceType == type) != 1) || services.Any(x => x.ServiceType == typeof(RecoveryBrowserService)))
            throw new InvalidOperationException("Recovery browser composition requires exact explicit approved owner/profile/publication capabilities.");
        var codeOwners = services.Count(x => x.ServiceType == typeof(RecoveryCodeApplication));
        var nativeOwners = services.Count(x => x.ServiceType == typeof(PasskeyApplication));
        var contactOwners = services.Count(x => x.ServiceType == typeof(IRecoveryContactRiskSource));
        if (contactOwners > 1 || contactOwners == 1 && (services.Count(x => x.ServiceType == typeof(IRecoveryContactNotificationSource)) != 1 ||
            services.Count(x => x.ServiceType == typeof(IRecoveryContactNotificationDispatcher)) != 1))
            throw new InvalidOperationException("Contact recovery requires exact explicit risk and approved notification capabilities.");
        var governedOwners = services.Count(x => x.ServiceType == typeof(IRecoveryGovernedDispositionSource));
        if (governedOwners > 1) throw new InvalidOperationException("Governed recovery requires one exact authoritative disposition source.");
        if (codeOwners > 1 || nativeOwners > 1 || codeOwners + nativeOwners + contactOwners + governedOwners == 0)
            throw new InvalidOperationException("Recovery requires at least one exact explicitly composed native proof capability.");
        services.AddScoped(provider => new RecoveryCaseApplication(provider.GetRequiredService<IdentityPersistenceDbContext>(),
            provider.GetService<RecoveryCodeApplication>(), provider.GetRequiredService<AuthenticationApplication>(), provider.GetRequiredService<TimeProvider>(),
            provider.GetService<IRecoveryCasePolicySource>(), provider.GetRequiredService<IRecoveryCaseHierarchySource>(),
            provider.GetRequiredService<IRecoveryPasswordPolicySource>(), provider.GetRequiredService<IRegistrationPasswordBlocklist>(),
            provider.GetRequiredService<IAuthenticationPolicySource>(), provider.GetRequiredService<AuthenticationPartitionKey>(), provider.GetService<PasskeyApplication>(),
            provider.GetService<IRecoveryContactRiskSource>(), provider.GetService<IRecoveryContactNotificationSource>(),
            provider.GetService<IDataProtectionProvider>(), provider.GetService<IRecoveryContactNotificationDispatcher>(), provider.GetService<IRecoveryGovernedDispositionSource>()));
        services.AddScoped(provider => new RecoveryBrowserService(provider.GetRequiredService<RecoveryCaseApplication>(),
            provider.GetRequiredService<IRecoveryPasswordPolicySource>(), provider.GetRequiredService<IRecoveryPasswordPresentationSource>(),
            provider.GetRequiredService<IAuthenticationPolicySource>(), provider.GetRequiredService<IDataProtectionProvider>(),
            provider.GetRequiredService<AuthenticationPartitionKey>(), provider.GetRequiredService<AuthenticationEndpoints.Surface>().Origin,
            provider.GetRequiredService<TimeProvider>()));
        return services;
    }
    public static IEndpointRouteBuilder MapTunnerRecoveryPassword(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        endpoints.MapGet("/recovery/options", (HttpContext context, RecoveryBrowserService service) => service.OptionsAsync(context)).AllowAnonymous();
        foreach (var kind in Commands)
        {
            var selected = kind;
            endpoints.MapPost("/recovery/" + selected, (HttpContext context, RecoveryBrowserService service) => service.CommandAsync(context, selected))
                .AllowAnonymous().WithMetadata(new AuthenticationEndpoints.CredentialAuthenticationMetadata());
        }
        var assets = endpoints.ServiceProvider.GetRequiredService<IdentityWebAssets>(); assets.MapAssets(endpoints);
        assets.MapScreensAsync(endpoints, "recovery.html", ["/recovery", "/recovery/verify", "/recovery/repair", "/recovery/result"],
            context => context.RequestServices.GetRequiredService<RecoveryBrowserService>().ScreenAllowedAsync(context));
        return endpoints;
    }
}
