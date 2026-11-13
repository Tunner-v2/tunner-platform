using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Tunner.Platform.Operations;

namespace Tunner.Identity.Persistence;

public static class RecoveryEndpoints
{
    private static readonly string[] Commands = ["start", "result", "cancel", "repair"];
    public static IServiceCollection AddTunnerRecoveryPassword(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        Type[] required = [typeof(RecoveryCodeApplication), typeof(AuthenticationApplication), typeof(AuthenticationEndpoints.Surface),
            typeof(IRecoveryPasswordPolicySource), typeof(IRecoveryCaseHierarchySource), typeof(IRecoveryPasswordPresentationSource),
            typeof(IRegistrationPasswordBlocklist), typeof(IAuthenticationPolicySource), typeof(AuthenticationPartitionKey)];
        if (required.Any(type => services.Count(x => x.ServiceType == type) != 1) || services.Any(x => x.ServiceType == typeof(RecoveryBrowserService)))
            throw new InvalidOperationException("Recovery browser composition requires exact explicit approved owner/profile/publication capabilities.");
        services.AddScoped(provider => new RecoveryCaseApplication(provider.GetRequiredService<IdentityPersistenceDbContext>(),
            provider.GetRequiredService<RecoveryCodeApplication>(), provider.GetRequiredService<AuthenticationApplication>(), provider.GetRequiredService<TimeProvider>(),
            provider.GetService<IRecoveryCasePolicySource>(), provider.GetRequiredService<IRecoveryCaseHierarchySource>(),
            provider.GetRequiredService<IRecoveryPasswordPolicySource>(), provider.GetRequiredService<IRegistrationPasswordBlocklist>(),
            provider.GetRequiredService<IAuthenticationPolicySource>(), provider.GetRequiredService<AuthenticationPartitionKey>()));
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
        assets.MapScreens(endpoints, "recovery.html", ["/recovery", "/recovery/verify", "/recovery/result"],
            context => context.RequestServices.GetRequiredService<RecoveryBrowserService>().ScreenAllowed(context));
        return endpoints;
    }
}
