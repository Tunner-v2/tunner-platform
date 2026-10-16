using Tunner.Observability;
using Tunner.Platform;
using Tunner.Platform.Bff;

namespace Tunner.Admin.Bff;

/// <summary>Separate Admin browser composition root. Identity/session activation remains owner work.</summary>
public static class TunnerAdminBffHost
{
    public static WebApplication CreateApplication(string[] args, Action<WebApplicationBuilder>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(args);
        var builder = WebApplication.CreateBuilder(args);
        builder.Logging.ClearProviders();
        configure?.Invoke(builder);
        builder.Services.AddTunnerObservability();
        builder.Services.AddTunnerModules();
        builder.Services.AddTunnerBffFoundation(BffSurface.Admin, builder.Configuration["Bff:BrowserOrigin"]);
        var application = builder.Build();
        application.UseTunnerBffFoundation();
        application.MapTunnerBffHealth();
        return application;
    }
}

public static class Program
{
    public static void Main(string[] args) => TunnerAdminBffHost.CreateApplication(args).Run();
}
