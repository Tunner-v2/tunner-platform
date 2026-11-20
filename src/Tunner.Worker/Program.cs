using Microsoft.Extensions.Hosting;
using Tunner.Observability;
using Tunner.Platform;

namespace Tunner.Worker;

/// <summary>Composition root for later governed background processing.</summary>
public static class TunnerWorkerHost
{
    public static IHost CreateHost(string[] args)
        => CreateHost(args, null);

    /// <summary>Explicit environment-owned module composition; the ordinary entry point selects no background work.</summary>
    public static IHost CreateHost(string[] args, Action<HostApplicationBuilder>? configure)
    {
        ArgumentNullException.ThrowIfNull(args);

        var builder = Host.CreateApplicationBuilder(args);
        builder.Services.AddTunnerObservability();
        builder.Services.AddTunnerModules();
        configure?.Invoke(builder);
        return builder.Build();
    }
}

public static class Program
{
    public static void Main(string[] args) => TunnerWorkerHost.CreateHost(args).Run();
}
