using System.Globalization;
using System.Text;
using RabbitMQ.Client;
using Tunner.Database.Outbox;

namespace Tunner.Worker.Messaging;

/// <summary>Inverse of the existing confirmed publisher's properties/headers; the JSON string is never rewritten.</summary>
public static class RabbitMqOutboxEnvelope
{
    private static readonly UTF8Encoding Utf8 = new(false, true);

    public static bool TryDecode(IReadOnlyBasicProperties properties, ReadOnlyMemory<byte> body, int maximumPayloadBytes,
        out PendingOutboxMessage? message)
    {
        ArgumentNullException.ThrowIfNull(properties);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumPayloadBytes, 1);
        message = null;
        if (body.Length > maximumPayloadBytes || properties.ContentType != "application/json" ||
            properties.DeliveryMode != DeliveryModes.Persistent ||
            !Guid.TryParseExact(properties.MessageId, "D", out var eventId) || eventId == Guid.Empty) return false;
        try
        {
            var headers = properties.Headers ?? throw new FormatException();
            if (!DateTimeOffset.TryParseExact(Header(headers, "occurred_at_utc", true), "O", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var occurredAt) || occurredAt.Offset != TimeSpan.Zero) return false;
            // Reuse the authoritative generic outbox constructor's required-field/JSON validation.
            var original = new OutboxMessage(eventId, Header(headers, "envelope_version", true)!, properties.Type!,
                Header(headers, "event_schema_version", true)!, occurredAt, Header(headers, "source", true)!,
                Header(headers, "subject", true)!, Header(headers, "data_classification", true)!, Utf8.GetString(body.Span),
                Header(headers, "resource_version", false), Header(headers, "operation_id", false),
                Header(headers, "correlation_id", false), Header(headers, "causation_event_id", false));
            message = new PendingOutboxMessage(original.EventId, original.EnvelopeVersion, original.EventType,
                original.EventSchemaVersion, original.OccurredAtUtc, original.Source, original.Subject,
                original.DataClassification, original.Payload, original.ResourceVersion, original.OperationId,
                original.CorrelationId, original.CausationEventId);
            return true;
        }
        catch (Exception error) when (error is FormatException or ArgumentException)
        {
            // The invalid bytes and validation exception may contain sensitive payload/header material.
            return false;
        }
    }

    private static string? Header(IDictionary<string, object?> headers, string name, bool required)
    {
        if (!headers.TryGetValue(name, out var value))
        {
            if (required) throw new FormatException();
            return null;
        }
        return value switch
        {
            string text => text,
            byte[] bytes => Utf8.GetString(bytes),
            ReadOnlyMemory<byte> bytes => Utf8.GetString(bytes.Span),
            _ => throw new FormatException()
        };
    }
}
