using StackExchange.Redis;
using Tunner.Platform.Redis;
using Tunner.Testing;
using Xunit;
using Xunit.Sdk;

namespace Tunner.Database.IntegrationTests;

public sealed class RedisEphemeralStoreIntegrationTests
{
    [Fact]
    public async Task CacheEntriesAreFiniteAndCoordinationReleaseRequiresTheOwningToken()
    {
        SkipUnlessContainerTestsAreEnabled();
        await using var container = TestcontainersBaseline.CreateRedisContainer();
        await container.StartAsync();
        using var multiplexer = await ConnectionMultiplexer.ConnectAsync($"{container.Hostname}:{container.GetMappedPublicPort(6379)},abortConnect=false");
        var store = new RedisEphemeralStore(multiplexer.GetDatabase());
        var cacheKey = $"test-cache-{Guid.NewGuid():N}";
        var tokenKey = $"test-token-{Guid.NewGuid():N}";
        var firstOwner = Guid.NewGuid();
        var secondOwner = Guid.NewGuid();

        await store.SetAsync(cacheKey, new byte[] { 1, 2, 3 }, TimeSpan.FromMinutes(1));
        Assert.Equal(new byte[] { 1, 2, 3 }, await store.GetAsync(cacheKey));
        Assert.True(await store.RemoveAsync(cacheKey));
        Assert.Null(await store.GetAsync(cacheKey));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => store.SetAsync(cacheKey, Array.Empty<byte>(), TimeSpan.Zero));

        Assert.True(await store.TryAcquireAsync(tokenKey, firstOwner, TimeSpan.FromMinutes(1)));
        Assert.False(await store.TryAcquireAsync(tokenKey, secondOwner, TimeSpan.FromMinutes(1)));
        Assert.False(await store.TryReleaseAsync(tokenKey, secondOwner));
        Assert.True(await store.TryReleaseAsync(tokenKey, firstOwner));
        Assert.True(await store.TryAcquireAsync(tokenKey, secondOwner, TimeSpan.FromMinutes(1)));
    }

    private static void SkipUnlessContainerTestsAreEnabled()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("TUNNER_RUN_CONTAINER_TESTS"), "1", StringComparison.Ordinal)) throw SkipException.ForSkip("Set TUNNER_RUN_CONTAINER_TESTS=1 to run Redis container integration tests.");
    }
}