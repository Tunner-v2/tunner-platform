using Microsoft.Extensions.Hosting;
using Tunner.Observability;
using Tunner.Platform;

namespace Tunner.Worker;

/// <summary>Composition root for later governed background processing.</summary>
public static class TunnerWorkerHost
{
    public static IHost CreateHost(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        var builder = Host.CreateApplicationBuilder(args);
        builder.Services.AddTunnerObservability();
        builder.Services.AddTunnerModules();
        return builder.Build();
    }
}

public static class Program
{
    public static void Main(string[] args) => TunnerWorkerHost.CreateHost(args).Run();
}