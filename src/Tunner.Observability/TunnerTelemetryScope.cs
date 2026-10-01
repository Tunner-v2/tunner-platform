namespace Tunner.Observability;

/// <summary>Explicit, opaque correlation values permitted for telemetry.</summary>
public sealed record TunnerTelemetryScope(
    string Module,
    string OperationId,
    string? CausationId = null,
    string? SafeProductId = null,
    string? SafeEnvironmentId = null)
{
    public IReadOnlyList<KeyValuePair<string, object?>> ToAttributes()
    {
        TelemetryAttributePolicy.RequireSafeIdentifier(Module, nameof(Module));
        TelemetryAttributePolicy.RequireSafeIdentifier(OperationId, nameof(OperationId));

        var attributes = new List<KeyValuePair<string, object?>>
        {
            new("tunner.module", Module),
            new("tunner.operation_id", OperationId)
        };

        AddIfSafe(attributes, "tunner.causation_id", CausationId, nameof(CausationId));
        AddIfSafe(attributes, "tunner.product_id", SafeProductId, nameof(SafeProductId));
        AddIfSafe(attributes, "tunner.environment_id", SafeEnvironmentId, nameof(SafeEnvironmentId));
        return attributes;
    }

    private static void AddIfSafe(List<KeyValuePair<string, object?>> attributes, string key, string? value, string parameterName)
    {
        if (value is null)
        {
            return;
        }

        TelemetryAttributePolicy.RequireSafeIdentifier(value, parameterName);
        attributes.Add(new KeyValuePair<string, object?>(key, value));
    }
}
