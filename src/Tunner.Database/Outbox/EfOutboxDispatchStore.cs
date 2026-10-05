using Microsoft.EntityFrameworkCore;

namespace Tunner.Database.Outbox;

/// <summary>EF Core implementation over the Platform-owned outbox table only.</summary>
public sealed class EfOutboxDispatchStore(IDbContextFactory<TunnerMigrationsDbContext> contextFactory) : IOutboxDispatchStore
{
    public async Task<IReadOnlyList<PendingOutboxMessage>> ReadPendingAsync(int maximumCount, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumCount, 1);

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return await context.OutboxMessages
            .AsNoTracking()
            .Where(message => message.PublishedAtUtc == null)
            .OrderBy(message => message.OccurredAtUtc)
            .ThenBy(message => message.EventId)
            .Take(maximumCount)
            .Select(message => new PendingOutboxMessage(
                message.EventId,
                message.EnvelopeVersion,
                message.EventType,
                message.EventSchemaVersion,
                message.OccurredAtUtc,
                message.Source,
                message.Subject,
                message.DataClassification,
                message.Payload,
                message.ResourceVersion,
                message.OperationId,
                message.CorrelationId,
                message.CausationEventId))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<bool> TryMarkPublishedAsync(Guid eventId, DateTimeOffset publishedAtUtc, CancellationToken cancellationToken = default)
    {
        if (eventId == Guid.Empty)
        {
            throw new ArgumentException("An outbox dispatch requires a stable event identifier.", nameof(eventId));
        }

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var affected = await context.OutboxMessages
            .Where(message => message.EventId == eventId && message.PublishedAtUtc == null)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(message => message.PublishedAtUtc, publishedAtUtc.ToUniversalTime()),
                cancellationToken)
            .ConfigureAwait(false);
        return affected == 1;
    }
}