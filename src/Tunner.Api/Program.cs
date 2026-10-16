using Tunner.Observability;
using Tunner.Platform;

namespace Tunner.Api;

/// <summary>Composition root for the synchronous Tunner HTTP/application entry point.</summary>
public static class TunnerApiHost
{
    public static WebApplication CreateApplication(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        var builder = WebApplication.CreateBuilder(args);
        builder.Services.AddTunnerObservability();
        builder.Services.AddTunnerModules();
        builder.Services.AddHealthChecks();
        builder.Services.AddOpenApi(options =>
        {
            options.OpenApiVersion = Microsoft.OpenApi.OpenApiSpecVersion.OpenApi3_1;
        });

        var application = builder.Build();
        application.MapHealthChecks("/health");
        return application;
    }
}

public static class Program
{
    public static void Main(string[] args) => TunnerApiHost.CreateApplication(args).Run();
}