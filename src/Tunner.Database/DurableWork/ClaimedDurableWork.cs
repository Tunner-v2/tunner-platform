namespace Tunner.Database.DurableWork;

/// <summary>Opaque work projection returned only after a successful conditional lease claim.</summary>
public sealed record ClaimedDurableWork(Guid WorkId, string WorkKind, string Payload, DateTimeOffset RetryEligibleAtUtc, Guid LeaseToken, DateTimeOffset LeaseExpiresAtUtc);