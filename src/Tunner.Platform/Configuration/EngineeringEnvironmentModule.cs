using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Tunner.Platform.Configuration;

/// <summary>Explicit composition module; validates before registering one immutable environment snapshot.</summary>
public sealed class EngineeringEnvironmentModule : ITunnerModule
{
    private readonly IConfiguration configuration;

    public EngineeringEnvironmentModule(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        this.configuration = configuration;
    }

    public void Register(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (services.Any(descriptor => descriptor.ServiceType == typeof(EngineeringEnvironmentConfiguration)))
            throw new InvalidOperationException("An engineering environment snapshot is already registered.");
        var snapshot = EngineeringEnvironmentConfiguration.Load(configuration);
        services.AddSingleton(snapshot);
    }
}
