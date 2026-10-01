using System.Diagnostics;
using OpenTelemetry;
using OpenTelemetry.Logs;

namespace Tunner.Observability;

/// <summary>Removes unapproved activity attributes before an exporter can observe them.</summary>
public sealed class TunnerActivityRedactionProcessor : BaseProcessor<Activity>
{
    public override void OnEnd(Activity data)
    {
        ArgumentNullException.ThrowIfNull(data);
        var unapprovedKeys = data.TagObjects
            .Where(tag => !TelemetryAttributePolicy.IsAllowed(tag.Key))
            .Select(tag => tag.Key)
            .ToArray();

        foreach (var key in unapprovedKeys)
        {
            data.SetTag(key, null);
        }

        if (unapprovedKeys.Length > 0)
        {
            data.SetTag("tunner.telemetry.redacted", "true");
        }
    }
}

/// <summary>Retains only approved structured attributes and prevents log bodies or exceptions from export.</summary>
public sealed class TunnerLogRedactionProcessor : BaseProcessor<LogRecord>
{
    public override void OnEnd(LogRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        record.Attributes = TelemetryAttributePolicy.Filter(record.Attributes ?? []);
        record.Body = "tunner.telemetry.log";
        record.FormattedMessage = "tunner.telemetry.log";
        record.Exception = null;
    }
}
