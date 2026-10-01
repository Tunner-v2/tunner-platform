namespace Tunner.Observability;

/// <summary>Non-secret, development/test observability configuration.</summary>
public sealed class TunnerObservabilityOptions
{
    public const string DefaultServiceName = "tunner.platform";
    public static readonly Uri DefaultOtlpEndpoint = new("http://127.0.0.1:24318");

    public string ServiceName { get; init; } = DefaultServiceName;

    public string ServiceVersion { get; init; } = "0.0.0-p0";

    public string EnvironmentName { get; init; } = "development";

    public Uri OtlpEndpoint { get; init; } = DefaultOtlpEndpoint;

    public static TunnerObservabilityOptions FromEnvironment()
    {
        var configuredEndpoint = Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT");
        if (string.IsNullOrWhiteSpace(configuredEndpoint))
        {
            return new TunnerObservabilityOptions();
        }

        if (!Uri.TryCreate(configuredEndpoint, UriKind.Absolute, out var endpoint))
        {
            throw new InvalidOperationException("OTEL_EXPORTER_OTLP_ENDPOINT must be an absolute URI.");
        }

        return new TunnerObservabilityOptions { OtlpEndpoint = endpoint };
    }

    public void Validate()
    {
        TelemetryAttributePolicy.RequireSafeIdentifier(ServiceName, nameof(ServiceName));
        TelemetryAttributePolicy.RequireSafeIdentifier(ServiceVersion, nameof(ServiceVersion));
        TelemetryAttributePolicy.RequireSafeIdentifier(EnvironmentName, nameof(EnvironmentName));

        if (!OtlpEndpoint.IsAbsoluteUri || !OtlpEndpoint.IsLoopback || OtlpEndpoint.Scheme != Uri.UriSchemeHttp)
        {
            throw new InvalidOperationException("P0 observability accepts only a loopback HTTP OTLP endpoint.");
        }
    }
}
