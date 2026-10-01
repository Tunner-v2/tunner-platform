using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Exporter;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Tunner.Observability;

/// <summary>Registers traces, metrics, and logs for the local, loopback-only P0 OTLP endpoint.</summary>
public static class TunnerObservabilityServiceCollectionExtensions
{
    public static IServiceCollection AddTunnerObservability(this IServiceCollection services, Action<TunnerObservabilityOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        var options = TunnerObservabilityOptions.FromEnvironment();
        configure?.Invoke(options);
        options.Validate();

        services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(options.ServiceName, serviceVersion: options.ServiceVersion)
                .AddAttributes(
                [
                    new KeyValuePair<string, object>("deployment.environment.name", options.EnvironmentName)
                ]))
            .WithTracing(tracing => tracing
                .AddSource(TunnerTelemetry.ActivitySourceName)
                .AddProcessor(new TunnerActivityRedactionProcessor())
                .AddOtlpExporter(exporter => ConfigureExporter(exporter, options)))
            .WithMetrics(metrics => metrics
                .AddMeter(TunnerTelemetry.MeterName)
                .AddOtlpExporter(exporter => ConfigureExporter(exporter, options)));

        services.AddLogging(logging => logging.AddOpenTelemetry(logger =>
        {
            logger.IncludeFormattedMessage = false;
            logger.IncludeScopes = false;
            logger.ParseStateValues = true;
            logger.AddProcessor(new TunnerLogRedactionProcessor());
            logger.AddOtlpExporter(exporter => ConfigureExporter(exporter, options));
        }));

        return services;
    }

    private static void ConfigureExporter(OtlpExporterOptions exporter, TunnerObservabilityOptions options)
    {
        exporter.Endpoint = options.OtlpEndpoint;
        exporter.Protocol = OtlpExportProtocol.HttpProtobuf;
    }
}
