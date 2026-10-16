namespace Tunner.Database.DurableWork;

/// <summary>Technical persistence boundary for durable-work claiming, completion, and caller-directed retry eligibility.</summary>
public interface IDurableWorkStore
{
    Task<IReadOnlyList<ClaimedDurableWork>> ClaimAvailableAsync(int maximumCount, Guid leaseToken, DateTimeOffset claimedAtUtc, DateTimeOffset leaseExpiresAtUtc, CancellationToken cancellationToken = default);
    Task<bool> TryCompleteAsync(Guid workId, Guid leaseToken, DateTimeOffset completedAtUtc, CancellationToken cancellationToken = default);
    Task<bool> TryReleaseAsync(Guid workId, Guid leaseToken, DateTimeOffset releasedAtUtc, DateTimeOffset retryEligibleAtUtc, CancellationToken cancellationToken = default);
}