using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Tunner.Database.Audit;

/// <summary>Exact-envelope checksum mechanics; this is not a signature or an independent trust anchor.</summary>
public static class AuditEventIntegrity
{
    public const string Format = "sha256-envelope-v1";

    internal static string Digest(string envelope) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(envelope)));

    public static bool VerifyEnvelope(string envelope, string integrityFormat, string digest)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        return integrityFormat == Format && StringComparer.Ordinal.Equals(Digest(envelope), digest);
    }
}

/// <summary>Audit-owned append record; the exact envelope preserves the full original event timestamp.</summary>
public sealed class AuditEventRecord
{
    private AuditEventRecord() { }

    public AuditEventRecord(AuditEvent auditEvent)
    {
        ArgumentNullException.ThrowIfNull(auditEvent);
        EventId = auditEvent.EventId;
        OccurredAtUtc = PostgresInstant(auditEvent.OccurredAtUtc);
        OperationId = auditEvent.OperationId;
        CorrelationId = auditEvent.CorrelationId;
        DataClassification = auditEvent.DataClassification;
        RetentionClassReference = auditEvent.RetentionClassReference;
        Envelope = auditEvent.ToEnvelope();
        IntegrityFormat = AuditEventIntegrity.Format;
        EnvelopeSha256 = AuditEventIntegrity.Digest(Envelope);
    }

    public Guid EventId { get; private set; }
    public DateTimeOffset OccurredAtUtc { get; private set; }
    public string OperationId { get; private set; } = string.Empty;
    public string CorrelationId { get; private set; } = string.Empty;
    public string DataClassification { get; private set; } = string.Empty;
    public string RetentionClassReference { get; private set; } = string.Empty;
    public string Envelope { get; private set; } = string.Empty;
    public string IntegrityFormat { get; private set; } = string.Empty;
    public string EnvelopeSha256 { get; private set; } = string.Empty;

    /// <summary>Checks bytes and persisted projections; callers must check before relying on the record.</summary>
    public bool VerifyIntegrity()
    {
        if (!AuditEventIntegrity.VerifyEnvelope(Envelope, IntegrityFormat, EnvelopeSha256)) return false;
        try
        {
            using var document = JsonDocument.Parse(Envelope);
            var root = document.RootElement;
            return root.ValueKind == JsonValueKind.Object &&
                root.TryGetProperty("schema_version", out var schema) && schema.ValueKind == JsonValueKind.Number && schema.TryGetInt32(out var version) && version == 1 &&
                root.TryGetProperty("event_id", out var id) && id.ValueKind == JsonValueKind.String && id.TryGetGuid(out var eventId) && eventId == EventId &&
                root.TryGetProperty("occurred_at_utc", out var time) && time.ValueKind == JsonValueKind.String && time.TryGetDateTimeOffset(out var instant) && PostgresInstant(instant) == OccurredAtUtc &&
                Matches(root, "operation_id", OperationId) && Matches(root, "correlation_id", CorrelationId) &&
                Matches(root, "data_classification", DataClassification) && Matches(root, "retention_class_ref", RetentionClassReference);
        }
        catch (JsonException) { return false; }
    }

    public void RequireIntegrity()
    {
        if (!VerifyIntegrity()) throw new InvalidOperationException("Audit event integrity verification failed.");
    }

    private static bool Matches(JsonElement root, string property, string expected) =>
        root.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String && StringComparer.Ordinal.Equals(value.GetString(), expected);

    private static DateTimeOffset PostgresInstant(DateTimeOffset instant) =>
        new(instant.UtcTicks - instant.UtcTicks % 10, TimeSpan.Zero);
}

