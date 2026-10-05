using Microsoft.EntityFrameworkCore;

namespace Tunner.Database.DurableWork;

/// <summary>EF Core implementation of opaque PostgreSQL-backed durable-work leasing.</summary>
public sealed class EfDurableWorkStore(IDbContextFactory<TunnerMigrationsDbContext> contextFactory) : IDurableWorkStore
{
    public async Task<IReadOnlyList<ClaimedDurableWork>> ClaimAvailableAsync(int maximumCount, Guid leaseToken, DateTimeOffset claimedAtUtc, DateTimeOffset leaseExpiresAtUtc, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumCount, 1);
        ValidateLease(leaseToken, claimedAtUtc, leaseExpiresAtUtc);
        var claimedAt = claimedAtUtc.ToUniversalTime();
        var leaseExpiresAt = leaseExpiresAtUtc.ToUniversalTime();
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var candidates = await context.DurableWorkItems.AsNoTracking()
            .Where(item => item.CompletedAtUtc == null && item.RetryEligibleAtUtc <= claimedAt && (item.LeaseExpiresAtUtc == null || item.LeaseExpiresAtUtc <= claimedAt))
            .OrderBy(item => item.RetryEligibleAtUtc).ThenBy(item => item.CreatedAtUtc).ThenBy(item => item.WorkId)
            .Take(maximumCount)
            .Select(item => new { item.WorkId, item.WorkKind, item.Payload, item.RetryEligibleAtUtc })
            .ToArrayAsync(cancellationToken).ConfigureAwait(false);

        var claimed = new List<ClaimedDurableWork>(candidates.Length);
        foreach (var candidate in candidates)
        {
            var affected = await context.DurableWorkItems
                .Where(item => item.WorkId == candidate.WorkId && item.CompletedAtUtc == null && item.RetryEligibleAtUtc <= claimedAt && (item.LeaseExpiresAtUtc == null || item.LeaseExpiresAtUtc <= claimedAt))
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(item => item.LeaseToken, leaseToken)
                        .SetProperty(item => item.LeaseAcquiredAtUtc, claimedAt)
                        .SetProperty(item => item.LeaseExpiresAtUtc, leaseExpiresAt),
                    cancellationToken).ConfigureAwait(false);
            if (affected == 1)
            {
                claimed.Add(new ClaimedDurableWork(candidate.WorkId, candidate.WorkKind, candidate.Payload, candidate.RetryEligibleAtUtc, leaseToken, leaseExpiresAt));
            }
        }
        return claimed;
    }

    public async Task<bool> TryCompleteAsync(Guid workId, Guid leaseToken, DateTimeOffset completedAtUtc, CancellationToken cancellationToken = default)
    {
        RequireIdentity(workId, leaseToken);
        var completedAt = completedAtUtc.ToUniversalTime();
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var affected = await context.DurableWorkItems
            .Where(item => item.WorkId == workId && item.CompletedAtUtc == null && item.LeaseToken == leaseToken && item.LeaseExpiresAtUtc != null && item.LeaseExpiresAtUtc > completedAt)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(item => item.CompletedAtUtc, completedAt)
                    .SetProperty(item => item.LeaseToken, (Guid?)null)
                    .SetProperty(item => item.LeaseAcquiredAtUtc, (DateTimeOffset?)null)
                    .SetProperty(item => item.LeaseExpiresAtUtc, (DateTimeOffset?)null),
                cancellationToken).ConfigureAwait(false);
        return affected == 1;
    }

    public async Task<bool> TryReleaseAsync(Guid workId, Guid leaseToken, DateTimeOffset releasedAtUtc, DateTimeOffset retryEligibleAtUtc, CancellationToken cancellationToken = default)
    {
        RequireIdentity(workId, leaseToken);
        var releasedAt = releasedAtUtc.ToUniversalTime();
        var retryEligibleAt = retryEligibleAtUtc.ToUniversalTime();
        if (retryEligibleAt < releasedAt) throw new ArgumentOutOfRangeException(nameof(retryEligibleAtUtc), "Retry eligibility cannot precede release.");
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var affected = await context.DurableWorkItems
            .Where(item => item.WorkId == workId && item.CompletedAtUtc == null && item.LeaseToken == leaseToken && item.LeaseExpiresAtUtc != null && item.LeaseExpiresAtUtc > releasedAt)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(item => item.RetryEligibleAtUtc, retryEligibleAt)
                    .SetProperty(item => item.LeaseToken, (Guid?)null)
                    .SetProperty(item => item.LeaseAcquiredAtUtc, (DateTimeOffset?)null)
                    .SetProperty(item => item.LeaseExpiresAtUtc, (DateTimeOffset?)null),
                cancellationToken).ConfigureAwait(false);
        return affected == 1;
    }

    private static void ValidateLease(Guid leaseToken, DateTimeOffset claimedAtUtc, DateTimeOffset leaseExpiresAtUtc)
    {
        if (leaseToken == Guid.Empty) throw new ArgumentException("A durable-work claim requires a non-empty lease token.", nameof(leaseToken));
        if (leaseExpiresAtUtc.ToUniversalTime() <= claimedAtUtc.ToUniversalTime()) throw new ArgumentOutOfRangeException(nameof(leaseExpiresAtUtc), "A durable-work lease must expire after it is claimed.");
    }

    private static void RequireIdentity(Guid workId, Guid leaseToken)
    {
        if (workId == Guid.Empty) throw new ArgumentException("Durable work requires a stable work identifier.", nameof(workId));
        if (leaseToken == Guid.Empty) throw new ArgumentException("A durable-work transition requires a non-empty lease token.", nameof(leaseToken));
    }
}