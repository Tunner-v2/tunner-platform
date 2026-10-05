namespace Tunner.Database.Outbox;

/// <summary>Technical persistence boundary for post-commit outbox dispatch.</summary>
public interface IOutboxDispatchStore
{
    Task<IReadOnlyList<PendingOutboxMessage>> ReadPendingAsync(int maximumCount, CancellationToken cancellationToken = default);

    /// <summary>Records confirmed broker acceptance without changing owner-domain state.</summary>
    Task<bool> TryMarkPublishedAsync(Guid eventId, DateTimeOffset publishedAtUtc, CancellationToken cancellationToken = default);
}