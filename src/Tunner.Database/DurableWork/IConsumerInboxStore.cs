namespace Tunner.Database.DurableWork;

/// <summary>Technical leased-inbox boundary; callers retain consumer behavior and business-effect ownership.</summary>
public interface IConsumerInboxStore
{
    Task<bool> TryReserveAsync(string consumerKey, Guid messageId, Guid leaseToken, DateTimeOffset reservedAtUtc, DateTimeOffset leaseExpiresAtUtc, CancellationToken cancellationToken = default);
    Task<bool> TryCompleteAsync(string consumerKey, Guid messageId, Guid leaseToken, DateTimeOffset completedAtUtc, CancellationToken cancellationToken = default);
    Task<bool> TryReleaseAsync(string consumerKey, Guid messageId, Guid leaseToken, DateTimeOffset releasedAtUtc, CancellationToken cancellationToken = default);
}