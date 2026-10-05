namespace Tunner.Database.Outbox;

/// <summary>Immutable transport projection of one committed, not-yet-published outbox record.</summary>
public sealed record PendingOutboxMessage(
    Guid EventId,
    string EnvelopeVersion,
    string EventType,
    string EventSchemaVersion,
    DateTimeOffset OccurredAtUtc,
    string Source,
    string Subject,
    string DataClassification,
    string Payload,
    string? ResourceVersion,
    string? OperationId,
    string? CorrelationId,
    string? CausationEventId);