using Tunner.Database.Outbox;

namespace Tunner.Worker.Messaging;

/// <summary>Coordinates post-commit delivery without exposing business or consumer semantics.</summary>
public sealed class OutboxDispatchCoordinator(IOutboxDispatchStore store, IConfirmedOutboxPublisher publisher, TimeProvider timeProvider)
{
    public async Task<int> DispatchPendingAsync(int maximumCount, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumCount, 1);
        var pending = await store.ReadPendingAsync(maximumCount, cancellationToken).ConfigureAwait(false);
        var published = 0;

        foreach (var message in pending)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await publisher.PublishConfirmedAsync(message, cancellationToken).ConfigureAwait(false);
            if (await store.TryMarkPublishedAsync(message.EventId, timeProvider.GetUtcNow(), cancellationToken).ConfigureAwait(false))
            {
                published++;
            }
        }

        return published;
    }
}