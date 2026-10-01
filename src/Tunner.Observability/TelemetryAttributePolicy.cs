using System.Text.RegularExpressions;

namespace Tunner.Observability;

/// <summary>Allow-list policy for attributes emitted by the P0 observability facade.</summary>
public static partial class TelemetryAttributePolicy
{
    private static readonly HashSet<string> AllowedAttributeKeys = new(
    [
        "tunner.module",
        "tunner.operation_id",
        "tunner.causation_id",
        "tunner.product_id",
        "tunner.environment_id",
        "tunner.telemetry.redacted"
    ], StringComparer.Ordinal);

    public static bool IsAllowed(string key) => AllowedAttributeKeys.Contains(key);

    public static IReadOnlyList<KeyValuePair<string, object?>> Filter(IEnumerable<KeyValuePair<string, object?>> attributes)
    {
        ArgumentNullException.ThrowIfNull(attributes);
        return attributes.Where(attribute => IsAllowed(attribute.Key) && IsSafeValue(attribute.Value))
            .ToArray();
    }

    public static void RequireSafeIdentifier(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 96 || !SafeIdentifier().IsMatch(value))
        {
            throw new ArgumentException("Value must be a stable, non-sensitive identifier.", parameterName);
        }
    }

    private static bool IsSafeValue(object? value)
        => value is string text && text.Length <= 96 && SafeIdentifier().IsMatch(text);

    [GeneratedRegex("^[A-Za-z0-9._:-]+$", RegexOptions.CultureInvariant)]
    private static partial Regex SafeIdentifier();
}
