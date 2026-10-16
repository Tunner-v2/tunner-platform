using Microsoft.Extensions.DependencyInjection;

namespace Tunner.Platform;

/// <summary>Registers a bounded module at an API or Worker composition root.</summary>
public interface ITunnerModule
{
    void Register(IServiceCollection services);
}

/// <summary>Provides the single explicit module-registration path used by Tunner hosts.</summary>
public static class TunnerModuleRegistration
{
    public static IServiceCollection AddTunnerModules(this IServiceCollection services, params ITunnerModule[] modules)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(modules);

        foreach (var module in modules)
        {
            ArgumentNullException.ThrowIfNull(module);
            module.Register(services);
        }

        return services;
    }
}