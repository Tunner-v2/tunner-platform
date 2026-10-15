using Tunner.Database.Outbox;
using Tunner.Worker.Messaging;
using Xunit;

namespace Tunner.Worker.Tests;

public sealed partial class OutboxDispatchCoordinatorTests
{
    private static readonly DateTimeOffset PublishedAtUtc = new(2026, 10, 2, 19, 15, 0, TimeSpan.Zero);

    [Fact]
    public async Task DispatchesPendingMessagesInStoreOrderAndMarksThemAfterConfirmation()
    {
        var first = CreateMessage("platform.foundation.first");
        var second = CreateMessage("platform.foundation.second");
        var store = new RecordingStore([first, second]);
        var publisher = new RecordingPublisher();
        var coordinator = new OutboxDispatchCoordinator(store, publisher, new FixedTimeProvider(PublishedAtUtc));

        var dispatched = await coordinator.DispatchPendingAsync(2);

        Assert.Equal(2, dispatched);
        Assert.Equal([2], store.RequestedMaximumCounts);
        Assert.Equal([first.EventId, second.EventId], publisher.PublishedEventIds);
        Assert.Equal([first.EventId, second.EventId], store.MarkAttempts.Select(attempt => attempt.EventId));
        Assert.All(store.MarkAttempts, attempt => Assert.Equal(PublishedAtUtc, attempt.PublishedAtUtc));
    }

    [Fact]
    public async Task LeavesTheFailedAndRemainingMessagesPendingWhenConfirmationFails()
    {
        var first = CreateMessage("platform.foundation.first");
        var failing = CreateMessage("platform.foundation.failing");
        var remaining = CreateMessage("platform.foundation.remaining");
        var store = new RecordingStore([first, failing, remaining]);
        var publisher = new RecordingPublisher(failing.EventId);
        var coordinator = new OutboxDispatchCoordinator(store, publisher, new FixedTimeProvider(PublishedAtUtc));

        await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.DispatchPendingAsync(3));

        Assert.Equal([first.EventId, failing.EventId], publisher.PublishedEventIds);
        Assert.Equal([first.EventId], store.MarkAttempts.Select(attempt => attempt.EventId));
    }

    [Fact]
    public async Task DoesNotCountAConditionalMarkThatAnotherDispatcherAlreadyCompleted()
    {
        var message = CreateMessage("platform.foundation.race");
        var store = new RecordingStore([message], eventId => eventId != message.EventId);
        var publisher = new RecordingPublisher();
        var coordinator = new OutboxDispatchCoordinator(store, publisher, new FixedTimeProvider(PublishedAtUtc));

        var dispatched = await coordinator.DispatchPendingAsync(1);

        Assert.Equal(0, dispatched);
        Assert.Equal([message.EventId], publisher.PublishedEventIds);
        Assert.Equal([message.EventId], store.MarkAttempts.Select(attempt => attempt.EventId));
    }

    [Fact]
    public async Task RejectsANonPositiveBatchWithoutReadingTheStore()
    {
        var store = new RecordingStore([]);
        var coordinator = new OutboxDispatchCoordinator(store, new RecordingPublisher(), new FixedTimeProvider(PublishedAtUtc));

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => coordinator.DispatchPendingAsync(0));

        Assert.Empty(store.RequestedMaximumCounts);
    }

    private static PendingOutboxMessage CreateMessage(string eventType)
        => new(
            Guid.NewGuid(),
            "1.0",
            eventType,
            "1.0",
            PublishedAtUtc,
            "platform.foundation",
            "test-subject",
            "internal-test",
            "{\"probe\":true}",
            ResourceVersion: null,
            OperationId: null,
            CorrelationId: null,
            CausationEventId: null);

    private sealed class RecordingStore(IReadOnlyList<PendingOutboxMessage> pending, Func<Guid, bool>? markResult = null) : IOutboxDispatchStore
    {
        private readonly Func<Guid, bool> markResult = markResult ?? (_ => true);

        public List<int> RequestedMaximumCounts { get; } = [];
        public List<(Guid EventId, DateTimeOffset PublishedAtUtc)> MarkAttempts { get; } = [];

        public Task<IReadOnlyList<PendingOutboxMessage>> ReadPendingAsync(int maximumCount, CancellationToken cancellationToken = default)
        {
            RequestedMaximumCounts.Add(maximumCount);
            return Task.FromResult<IReadOnlyList<PendingOutboxMessage>>(pending.Take(maximumCount).ToArray());
        }

        public Task<bool> TryMarkPublishedAsync(Guid eventId, DateTimeOffset publishedAtUtc, CancellationToken cancellationToken = default)
        {
            MarkAttempts.Add((eventId, publishedAtUtc));
            return Task.FromResult(markResult(eventId));
        }
    }

    private sealed class RecordingPublisher(Guid? failingEventId = null) : IConfirmedOutboxPublisher
    {
        public List<Guid> PublishedEventIds { get; } = [];

        public Task PublishConfirmedAsync(PendingOutboxMessage message, CancellationToken cancellationToken = default)
        {
            PublishedEventIds.Add(message.EventId);
            if (message.EventId == failingEventId)
            {
                throw new InvalidOperationException("Simulated broker confirmation failure.");
            }

            return Task.CompletedTask;
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
