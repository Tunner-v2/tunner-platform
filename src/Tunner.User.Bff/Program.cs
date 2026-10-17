using Tunner.Observability;
using Tunner.Platform;
using Tunner.Platform.Bff;
using Tunner.Identity.Persistence;

namespace Tunner.User.Bff;

/// <summary>Separate User browser composition root. Identity/session activation remains owner work.</summary>
public static class TunnerUserBffHost
{
    public static WebApplication CreateApplication(string[] args, Action<WebApplicationBuilder>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(args);
        var builder = WebApplication.CreateBuilder(args);
        builder.Logging.ClearProviders();
        configure?.Invoke(builder);
        builder.Services.AddTunnerObservability();
        builder.Services.AddTunnerModules();
        builder.Services.AddTunnerBffFoundation(BffSurface.User, builder.Configuration["Bff:BrowserOrigin"]);
        var registration = builder.Services.Any(x => x.ServiceType == typeof(IRegistrationPolicySource));
        if (registration) builder.Services.AddTunnerRegistrationApplication();
        var application = builder.Build();
        application.UseTunnerBffFoundation();
        application.MapTunnerBffHealth();
        if (registration) application.MapTunnerRegistration();
        return application;
    }
}

public static class Program
{
    public static void Main(string[] args) => TunnerUserBffHost.CreateApplication(args).Run();
}
