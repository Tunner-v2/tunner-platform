using System.Text.Json;

namespace Tunner.Database.Outbox;

/// <summary>
/// A platform-owned record of a committed domain fact awaiting separately governed post-commit transport.
/// </summary>
public sealed class OutboxMessage
{
    private OutboxMessage()
    {
    }

    public OutboxMessage(
        Guid eventId,
        string envelopeVersion,
        string eventType,
        string eventSchemaVersion,
        DateTimeOffset occurredAtUtc,
        string source,
        string subject,
        string dataClassification,
        string payload,
        string? resourceVersion = null,
        string? operationId = null,
        string? correlationId = null,
        string? causationEventId = null)
    {
        if (eventId == Guid.Empty)
        {
            throw new ArgumentException("An outbox message requires a stable event identifier.", nameof(eventId));
        }

        EventId = eventId;
        EnvelopeVersion = RequireValue(envelopeVersion, nameof(envelopeVersion));
        EventType = RequireValue(eventType, nameof(eventType));
        EventSchemaVersion = RequireValue(eventSchemaVersion, nameof(eventSchemaVersion));
        OccurredAtUtc = occurredAtUtc.ToUniversalTime();
        Source = RequireValue(source, nameof(source));
        Subject = RequireValue(subject, nameof(subject));
        DataClassification = RequireValue(dataClassification, nameof(dataClassification));
        Payload = RequireJson(payload);
        ResourceVersion = resourceVersion;
        OperationId = operationId;
        CorrelationId = correlationId;
        CausationEventId = causationEventId;
    }

    public Guid EventId { get; private set; }
    public string EnvelopeVersion { get; private set; } = string.Empty;
    public string EventType { get; private set; } = string.Empty;
    public string EventSchemaVersion { get; private set; } = string.Empty;
    public DateTimeOffset OccurredAtUtc { get; private set; }

    /// <summary>Reserved canonical envelope metadata. P1 intentionally leaves it null because dispatch is out of scope.</summary>
    public DateTimeOffset? PublishedAtUtc { get; private set; }

    public string Source { get; private set; } = string.Empty;
    public string Subject { get; private set; } = string.Empty;
    public string? ResourceVersion { get; private set; }
    public string? OperationId { get; private set; }
    public string? CorrelationId { get; private set; }
    public string? CausationEventId { get; private set; }
    public string DataClassification { get; private set; } = string.Empty;
    public string Payload { get; private set; } = string.Empty;

    private static string RequireValue(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        return value;
    }

    private static string RequireJson(string payload)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(payload, nameof(payload));
        try
        {
            using var document = JsonDocument.Parse(payload);
        }
        catch (JsonException exception)
        {
            throw new ArgumentException("Outbox payload must be valid JSON.", nameof(payload), exception);
        }

        return payload;
    }
}