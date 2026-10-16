using Microsoft.EntityFrameworkCore;

namespace Tunner.Database.DurableWork;

/// <summary>EF Core implementation of a leased consumer inbox with atomic duplicate protection.</summary>
public sealed class EfConsumerInboxStore(IDbContextFactory<TunnerMigrationsDbContext> contextFactory) : IConsumerInboxStore
{
    public async Task<bool> TryReserveAsync(string consumerKey, Guid messageId, Guid leaseToken, DateTimeOffset reservedAtUtc, DateTimeOffset leaseExpiresAtUtc, CancellationToken cancellationToken = default)
    {
        ValidateReservation(consumerKey, messageId, leaseToken, reservedAtUtc, leaseExpiresAtUtc);
        var reservedAt = reservedAtUtc.ToUniversalTime();
        var leaseExpiresAt = leaseExpiresAtUtc.ToUniversalTime();
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var affected = await context.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO platform.consumer_inbox (consumer_key, message_id, lease_token, reserved_at_utc, lease_expires_at_utc, completed_at_utc)
            VALUES ({consumerKey}, {messageId}, {leaseToken}, {reservedAt}, {leaseExpiresAt}, NULL)
            ON CONFLICT (consumer_key, message_id) DO UPDATE
            SET lease_token = EXCLUDED.lease_token,
                reserved_at_utc = EXCLUDED.reserved_at_utc,
                lease_expires_at_utc = EXCLUDED.lease_expires_at_utc
            WHERE platform.consumer_inbox.completed_at_utc IS NULL
              AND (platform.consumer_inbox.lease_expires_at_utc IS NULL OR platform.consumer_inbox.lease_expires_at_utc <= {reservedAt});
            """, cancellationToken).ConfigureAwait(false);
        return affected == 1;
    }

    public async Task<bool> TryCompleteAsync(string consumerKey, Guid messageId, Guid leaseToken, DateTimeOffset completedAtUtc, CancellationToken cancellationToken = default)
    {
        RequireIdentity(consumerKey, messageId, leaseToken);
        var completedAt = completedAtUtc.ToUniversalTime();
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var affected = await context.ConsumerInboxEntries
            .Where(entry => entry.ConsumerKey == consumerKey && entry.MessageId == messageId && entry.CompletedAtUtc == null && entry.LeaseToken == leaseToken && entry.LeaseExpiresAtUtc != null && entry.LeaseExpiresAtUtc > completedAt)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(entry => entry.CompletedAtUtc, completedAt)
                    .SetProperty(entry => entry.LeaseToken, (Guid?)null)
                    .SetProperty(entry => entry.LeaseExpiresAtUtc, (DateTimeOffset?)null),
                cancellationToken).ConfigureAwait(false);
        return affected == 1;
    }

    public async Task<bool> TryReleaseAsync(string consumerKey, Guid messageId, Guid leaseToken, DateTimeOffset releasedAtUtc, CancellationToken cancellationToken = default)
    {
        RequireIdentity(consumerKey, messageId, leaseToken);
        var releasedAt = releasedAtUtc.ToUniversalTime();
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var affected = await context.ConsumerInboxEntries
            .Where(entry => entry.ConsumerKey == consumerKey && entry.MessageId == messageId && entry.CompletedAtUtc == null && entry.LeaseToken == leaseToken && entry.LeaseExpiresAtUtc != null && entry.LeaseExpiresAtUtc > releasedAt)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(entry => entry.LeaseToken, (Guid?)null)
                    .SetProperty(entry => entry.LeaseExpiresAtUtc, (DateTimeOffset?)null),
                cancellationToken).ConfigureAwait(false);
        return affected == 1;
    }

    private static void ValidateReservation(string consumerKey, Guid messageId, Guid leaseToken, DateTimeOffset reservedAtUtc, DateTimeOffset leaseExpiresAtUtc)
    {
        RequireIdentity(consumerKey, messageId, leaseToken);
        if (leaseExpiresAtUtc.ToUniversalTime() <= reservedAtUtc.ToUniversalTime()) throw new ArgumentOutOfRangeException(nameof(leaseExpiresAtUtc), "An inbox lease must expire after it is reserved.");
    }

    private static void RequireIdentity(string consumerKey, Guid messageId, Guid leaseToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(consumerKey);
        if (messageId == Guid.Empty) throw new ArgumentException("An inbox receipt requires a stable message identifier.", nameof(messageId));
        if (leaseToken == Guid.Empty) throw new ArgumentException("An inbox reservation requires a non-empty lease token.", nameof(leaseToken));
    }
}