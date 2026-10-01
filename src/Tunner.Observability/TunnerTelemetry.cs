using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Tunner.Observability;

/// <summary>Backend-neutral Activity and Meter entry points for later Tunner hosts.</summary>
public static class TunnerTelemetry
{
    public const string ActivitySourceName = "Tunner.Platform";
    public const string MeterName = "Tunner.Platform";

    private static readonly ActivitySource ActivitySource = new(ActivitySourceName);
    private static readonly Meter Meter = new(MeterName);

    public static Activity? StartOperation(string operationName, TunnerTelemetryScope scope)
    {
        TelemetryAttributePolicy.RequireSafeIdentifier(operationName, nameof(operationName));
        ArgumentNullException.ThrowIfNull(scope);

        var activity = ActivitySource.StartActivity(operationName, ActivityKind.Internal);
        if (activity is not null)
        {
            foreach (var attribute in scope.ToAttributes())
            {
                activity.SetTag(attribute.Key, attribute.Value);
            }
        }

        return activity;
    }

    public static Counter<long> CreateSafeCounter(string name, string? unit = null, string? description = null)
    {
        TelemetryAttributePolicy.RequireSafeIdentifier(name, nameof(name));
        return Meter.CreateCounter<long>(name, unit, description);
    }
}
