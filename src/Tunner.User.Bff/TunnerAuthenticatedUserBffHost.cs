using Tunner.Identity.Persistence;

namespace Tunner.User.Bff;

/// <summary>Explicit owner activation on the shared User composition root; no Admin or default-runtime activation.</summary>
public static class TunnerAuthenticatedUserBffHost
{
    public static WebApplication CreateApplication(string[] args, Action<WebApplicationBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var stepUp = false;
        var passkeys = false;
        var totp = false;
        var application = TunnerUserBffHost.CreateApplication(args, builder =>
        {
            configure(builder);
            builder.Services.AddTunnerPasswordAuthentication(builder.Configuration["Bff:BrowserOrigin"] ?? throw new InvalidOperationException("User browser origin is required."));
            passkeys = builder.Services.Any(x => x.ServiceType == typeof(IPasskeyPolicySource));
            if (passkeys) builder.Services.AddTunnerPasskeys();
            // Capability composition only. No provider/profile is registered here; default production Main does not use this opt-in host.
            totp = builder.Services.Any(x => x.ServiceType == typeof(ITotpPolicySource));
            if (totp) builder.Services.AddTunnerTotp();
            stepUp = builder.Services.Any(x => x.ServiceType == typeof(IUserStepUpOperationSource));
            if (stepUp) builder.Services.AddTunnerOperationStepUp();
        });
        application.MapTunnerPasswordAuthentication();
        if (stepUp) application.MapTunnerOperationStepUp();
        if (passkeys) application.MapTunnerPasskeys();
        if (totp) application.MapTunnerTotp();
        return application;
    }
}
