using System.Text.Json;

namespace Tunner.Database.DurableWork;

/// <summary>Platform-owned opaque work record. Its kind and payload meaning remain with the caller/owner domain.</summary>
public sealed class DurableWorkItem
{
    private DurableWorkItem() { }

    public DurableWorkItem(Guid workId, string workKind, string payload, DateTimeOffset retryEligibleAtUtc, DateTimeOffset createdAtUtc)
    {
        if (workId == Guid.Empty) throw new ArgumentException("Durable work requires a stable work identifier.", nameof(workId));
        WorkId = workId;
        WorkKind = RequireValue(workKind, nameof(workKind));
        Payload = RequireJson(payload);
        RetryEligibleAtUtc = retryEligibleAtUtc.ToUniversalTime();
        CreatedAtUtc = createdAtUtc.ToUniversalTime();
    }

    public Guid WorkId { get; private set; }
    public string WorkKind { get; private set; } = string.Empty;
    public string Payload { get; private set; } = string.Empty;
    public DateTimeOffset RetryEligibleAtUtc { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public Guid? LeaseToken { get; private set; }
    public DateTimeOffset? LeaseAcquiredAtUtc { get; private set; }
    public DateTimeOffset? LeaseExpiresAtUtc { get; private set; }
    public DateTimeOffset? CompletedAtUtc { get; private set; }

    private static string RequireValue(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        return value;
    }

    private static string RequireJson(string payload)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(payload, nameof(payload));
        try { using var document = JsonDocument.Parse(payload); }
        catch (JsonException exception) { throw new ArgumentException("Durable work payload must be valid JSON.", nameof(payload), exception); }
        return payload;
    }
}