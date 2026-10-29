using Tunner.Identity.Persistence;

namespace Tunner.User.Bff;

/// <summary>Explicit owner activation on the shared User composition root; no Admin or default-runtime activation.</summary>
public static class TunnerAuthenticatedUserBffHost
{
    public static WebApplication CreateApplication(string[] args, Action<WebApplicationBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var application = TunnerUserBffHost.CreateApplication(args, builder =>
        {
            configure(builder);
            builder.Services.AddTunnerPasswordAuthentication(builder.Configuration["Bff:BrowserOrigin"] ?? throw new InvalidOperationException("User browser origin is required."));
        });
        application.MapTunnerPasswordAuthentication();
        return application;
    }
}
