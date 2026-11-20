using System.Globalization;
using System.Text;
using RabbitMQ.Client;
using Tunner.Database.Outbox;
using Tunner.Worker.Messaging;
using Xunit;

namespace Tunner.Worker.Tests;

public sealed class RabbitMqOutboxEnvelopeTests
{
    [Fact]
    public void PreservesTheExactPublishedEnvelopeIncludingWhitespaceAndOptionalIdentity()
    {
        var original = Message(); var properties = Properties(original);
        Assert.True(RabbitMqOutboxEnvelope.TryDecode(properties, Encoding.UTF8.GetBytes(original.Payload), 1024, out var restored));
        Assert.Equal(original, restored);
    }

    [Fact]
    public void BrokerByteHeadersAndAbsentOptionalHeadersDoNotCreateDefaultIdentifiers()
    {
        var original = Message() with { ResourceVersion = null, OperationId = null, CorrelationId = null, CausationEventId = null };
        var properties = Properties(original);
        properties.Headers = properties.Headers!.ToDictionary(pair => pair.Key, pair => (object?)Encoding.UTF8.GetBytes((string)pair.Value!), StringComparer.Ordinal);
        Assert.True(RabbitMqOutboxEnvelope.TryDecode(properties, Encoding.UTF8.GetBytes(original.Payload), 1024, out var restored));
        Assert.Equal(original, restored);
    }

    [Theory]
    [InlineData("id")]
    [InlineData("type")]
    [InlineData("content")]
    [InlineData("delivery")]
    [InlineData("missing")]
    [InlineData("timestamp")]
    [InlineData("header-type")]
    [InlineData("json")]
    [InlineData("utf8")]
    [InlineData("limit")]
    public void InvalidTransportMaterialIsNeverProjected(string mutation)
    {
        var original = Message(); var properties = Properties(original); var body = Encoding.UTF8.GetBytes(original.Payload); var maximum = 1024;
        switch (mutation)
        {
            case "id": properties.MessageId = Guid.Empty.ToString("D"); break;
            case "type": properties.Type = " "; break;
            case "content": properties.ContentType = "text/plain"; break;
            case "delivery": properties.DeliveryMode = DeliveryModes.Transient; break;
            case "missing": properties.Headers!.Remove("subject"); break;
            case "timestamp": properties.Headers!["occurred_at_utc"] = "yesterday"; break;
            case "header-type": properties.Headers!["operation_id"] = 12; break;
            case "json": body = "{"u8.ToArray(); break;
            case "utf8": body = [0xff, 0xfe]; break;
            case "limit": maximum = 1; break;
        }
        Assert.False(RabbitMqOutboxEnvelope.TryDecode(properties, body, maximum, out var restored));
        Assert.Null(restored);
    }

    internal static PendingOutboxMessage Message() => new(Guid.NewGuid(), "1.0", "fixture.receive", "1.0",
        new DateTimeOffset(2026, 11, 16, 1, 2, 3, TimeSpan.Zero), "fixture", "original-subject", "INTERNAL",
        "{ \"value\": \"original ☃\" }\n", "7", "original-operation", "original-correlation", "original-cause");

    internal static BasicProperties Properties(PendingOutboxMessage value)
    {
        var headers = new Dictionary<string, object?>
        {
            ["envelope_version"] = value.EnvelopeVersion, ["event_schema_version"] = value.EventSchemaVersion,
            ["occurred_at_utc"] = value.OccurredAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            ["source"] = value.Source, ["subject"] = value.Subject, ["data_classification"] = value.DataClassification
        };
        if (value.ResourceVersion is not null) headers["resource_version"] = value.ResourceVersion;
        if (value.OperationId is not null) headers["operation_id"] = value.OperationId;
        if (value.CorrelationId is not null) headers["correlation_id"] = value.CorrelationId;
        if (value.CausationEventId is not null) headers["causation_event_id"] = value.CausationEventId;
        return new BasicProperties { MessageId = value.EventId.ToString("D"), Type = value.EventType, ContentType = "application/json", DeliveryMode = DeliveryModes.Persistent, Headers = headers };
    }
}
