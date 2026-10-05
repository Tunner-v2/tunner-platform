namespace Tunner.Database.DurableWork;

/// <summary>Platform-owned leased inbox receipt used to prevent duplicate owner-domain mutation.</summary>
public sealed class ConsumerInboxEntry
{
    private ConsumerInboxEntry() { }

    public ConsumerInboxEntry(string consumerKey, Guid messageId, Guid leaseToken, DateTimeOffset reservedAtUtc, DateTimeOffset leaseExpiresAtUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(consumerKey, nameof(consumerKey));
        if (messageId == Guid.Empty) throw new ArgumentException("An inbox receipt requires a stable message identifier.", nameof(messageId));
        if (leaseToken == Guid.Empty) throw new ArgumentException("An inbox reservation requires a non-empty lease token.", nameof(leaseToken));
        var reservedAt = reservedAtUtc.ToUniversalTime();
        var leaseExpiresAt = leaseExpiresAtUtc.ToUniversalTime();
        if (leaseExpiresAt <= reservedAt) throw new ArgumentOutOfRangeException(nameof(leaseExpiresAtUtc), "An inbox lease must expire after it is reserved.");
        ConsumerKey = consumerKey;
        MessageId = messageId;
        LeaseToken = leaseToken;
        ReservedAtUtc = reservedAt;
        LeaseExpiresAtUtc = leaseExpiresAt;
    }

    public string ConsumerKey { get; private set; } = string.Empty;
    public Guid MessageId { get; private set; }
    public Guid? LeaseToken { get; private set; }
    public DateTimeOffset ReservedAtUtc { get; private set; }
    public DateTimeOffset? LeaseExpiresAtUtc { get; private set; }
    public DateTimeOffset? CompletedAtUtc { get; private set; }
}