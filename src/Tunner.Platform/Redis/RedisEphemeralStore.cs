using StackExchange.Redis;

namespace Tunner.Platform.Redis;

public sealed class RedisEphemeralStore : IEphemeralCache, IEphemeralCoordination
{
    private const string ReleaseOwnedTokenScript = "if redis.call('GET', KEYS[1]) == ARGV[1] then return redis.call('DEL', KEYS[1]) end return 0";
    private readonly IDatabase database;

    public RedisEphemeralStore(IDatabase database) => this.database = database ?? throw new ArgumentNullException(nameof(database));

    public async Task<byte[]?> GetAsync(string entryKey, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        RequireKey(entryKey);
        var value = await database.StringGetAsync(entryKey).ConfigureAwait(false);
        return value.HasValue ? (byte[]?)value : null;
    }

    public async Task SetAsync(string entryKey, ReadOnlyMemory<byte> value, TimeSpan timeToLive, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        RequireKey(entryKey);
        RequireFiniteTtl(timeToLive);
        await database.StringSetAsync(entryKey, value.ToArray(), timeToLive).ConfigureAwait(false);
    }

    public async Task<bool> RemoveAsync(string entryKey, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        RequireKey(entryKey);
        return await database.KeyDeleteAsync(entryKey).ConfigureAwait(false);
    }

    public async Task<bool> TryAcquireAsync(string tokenKey, Guid ownerToken, TimeSpan timeToLive, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        RequireKey(tokenKey);
        RequireOwnerToken(ownerToken);
        RequireFiniteTtl(timeToLive);
        return await database.StringSetAsync(tokenKey, ownerToken.ToString("N"), timeToLive, When.NotExists).ConfigureAwait(false);
    }

    public async Task<bool> TryReleaseAsync(string tokenKey, Guid ownerToken, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        RequireKey(tokenKey);
        RequireOwnerToken(ownerToken);
        var result = await database.ScriptEvaluateAsync(ReleaseOwnedTokenScript, [tokenKey], [ownerToken.ToString("N")]).ConfigureAwait(false);
        return (long)result == 1;
    }

    private static void RequireKey(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("A Redis entry key is required.", nameof(value));
    }

    private static void RequireOwnerToken(Guid ownerToken)
    {
        if (ownerToken == Guid.Empty) throw new ArgumentException("A non-empty owner token is required.", nameof(ownerToken));
    }

    private static void RequireFiniteTtl(TimeSpan timeToLive)
    {
        if (timeToLive <= TimeSpan.Zero || timeToLive == Timeout.InfiniteTimeSpan) throw new ArgumentOutOfRangeException(nameof(timeToLive), "A positive finite TTL is required.");
    }
}