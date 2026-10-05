using Microsoft.EntityFrameworkCore;
using Tunner.Database;
using Tunner.Database.DurableWork;
using Tunner.Database.Migrations;
using Tunner.Testing;
using Xunit;
using Xunit.Sdk;

namespace Tunner.Database.IntegrationTests;

public sealed class DurableWorkInboxIntegrationTests
{
    private static readonly DateTimeOffset BaseTime = new(2026, 10, 2, 20, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ConcurrentClaimsDoNotLeaseSameWorkItem()
    {
        SkipUnlessContainerTestsAreEnabled();
        await using var container = TestcontainersBaseline.CreatePostgresContainer();
        await container.StartAsync();
        var connectionString = BuildConnectionString(container);
        await PrepareDatabaseAsync(connectionString);
        var workId = Guid.NewGuid();
        await SeedWorkAsync(connectionString, workId, BaseTime, BaseTime);

        var left = new EfDurableWorkStore(CreateFactory(connectionString));
        var right = new EfDurableWorkStore(CreateFactory(connectionString));
        var results = await Task.WhenAll(
            left.ClaimAvailableAsync(1, Guid.NewGuid(), BaseTime, BaseTime.AddMinutes(5)),
            right.ClaimAvailableAsync(1, Guid.NewGuid(), BaseTime, BaseTime.AddMinutes(5)));

        Assert.Single(results.SelectMany(result => result));
        Assert.Equal(workId, results.SelectMany(result => result).Single().WorkId);
    }

    [Fact]
    public async Task ReclaimsAnExpiredLeaseAndRejectsAStaleCompletion()
    {
        SkipUnlessContainerTestsAreEnabled();
        await using var container = TestcontainersBaseline.CreatePostgresContainer();
        await container.StartAsync();
        var connectionString = BuildConnectionString(container);
        await PrepareDatabaseAsync(connectionString);
        var workId = Guid.NewGuid();
        await SeedWorkAsync(connectionString, workId, BaseTime, BaseTime);
        var store = new EfDurableWorkStore(CreateFactory(connectionString));
        var firstToken = Guid.NewGuid();
        var secondToken = Guid.NewGuid();

        Assert.Single(await store.ClaimAvailableAsync(1, firstToken, BaseTime, BaseTime.AddMinutes(1)));
        var reclaimed = await store.ClaimAvailableAsync(1, secondToken, BaseTime.AddMinutes(2), BaseTime.AddMinutes(3));
        Assert.Single(reclaimed);
        Assert.Equal(secondToken, reclaimed[0].LeaseToken);
        Assert.False(await store.TryCompleteAsync(workId, firstToken, BaseTime.AddMinutes(2)));
        Assert.True(await store.TryCompleteAsync(workId, secondToken, BaseTime.AddMinutes(2)));
        Assert.Empty(await store.ClaimAvailableAsync(1, Guid.NewGuid(), BaseTime.AddMinutes(4), BaseTime.AddMinutes(5)));
    }

    [Fact]
    public async Task ReleasesWorkAtCallerSuppliedEligibility()
    {
        SkipUnlessContainerTestsAreEnabled();
        await using var container = TestcontainersBaseline.CreatePostgresContainer();
        await container.StartAsync();
        var connectionString = BuildConnectionString(container);
        await PrepareDatabaseAsync(connectionString);
        var workId = Guid.NewGuid();
        await SeedWorkAsync(connectionString, workId, BaseTime, BaseTime);
        var store = new EfDurableWorkStore(CreateFactory(connectionString));
        var firstToken = Guid.NewGuid();

        Assert.Single(await store.ClaimAvailableAsync(1, firstToken, BaseTime, BaseTime.AddMinutes(10)));
        Assert.True(await store.TryReleaseAsync(workId, firstToken, BaseTime.AddMinutes(1), BaseTime.AddMinutes(5)));
        Assert.Empty(await store.ClaimAvailableAsync(1, Guid.NewGuid(), BaseTime.AddMinutes(4), BaseTime.AddMinutes(10)));
        Assert.Single(await store.ClaimAvailableAsync(1, Guid.NewGuid(), BaseTime.AddMinutes(5), BaseTime.AddMinutes(10)));
    }

    [Fact]
    public async Task ConsumerInboxReservesReclaimsReleasesAndCompletesWithoutDuplicateSuccess()
    {
        SkipUnlessContainerTestsAreEnabled();
        await using var container = TestcontainersBaseline.CreatePostgresContainer();
        await container.StartAsync();
        var connectionString = BuildConnectionString(container);
        await PrepareDatabaseAsync(connectionString);
        var store = new EfConsumerInboxStore(CreateFactory(connectionString));
        var messageId = Guid.NewGuid();
        var firstToken = Guid.NewGuid();
        var secondToken = Guid.NewGuid();
        var thirdToken = Guid.NewGuid();
        var fourthToken = Guid.NewGuid();

        Assert.True(await store.TryReserveAsync("technical-test-consumer", messageId, firstToken, BaseTime, BaseTime.AddMinutes(1)));
        Assert.False(await store.TryReserveAsync("technical-test-consumer", messageId, secondToken, BaseTime, BaseTime.AddMinutes(1)));
        Assert.True(await store.TryReserveAsync("technical-test-consumer", messageId, secondToken, BaseTime.AddMinutes(2), BaseTime.AddMinutes(3)));
        Assert.True(await store.TryReleaseAsync("technical-test-consumer", messageId, secondToken, BaseTime.AddMinutes(2)));
        Assert.True(await store.TryReserveAsync("technical-test-consumer", messageId, thirdToken, BaseTime.AddMinutes(2), BaseTime.AddMinutes(3)));
        Assert.True(await store.TryCompleteAsync("technical-test-consumer", messageId, thirdToken, BaseTime.AddMinutes(2)));
        Assert.False(await store.TryReserveAsync("technical-test-consumer", messageId, fourthToken, BaseTime.AddMinutes(4), BaseTime.AddMinutes(5)));
    }

    [Fact]
    public async Task RejectsInvalidLeaseArgumentsWithoutOpeningAContext()
    {
        var durableWork = new EfDurableWorkStore(new ThrowingContextFactory());
        var inbox = new EfConsumerInboxStore(new ThrowingContextFactory());
        var workId = Guid.NewGuid();
        var leaseToken = Guid.NewGuid();
        var messageId = Guid.NewGuid();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => durableWork.ClaimAvailableAsync(0, leaseToken, BaseTime, BaseTime.AddMinutes(1)));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => durableWork.TryReleaseAsync(workId, leaseToken, BaseTime, BaseTime.AddMinutes(-1)));
        await Assert.ThrowsAsync<ArgumentException>(() => inbox.TryReserveAsync("technical-test-consumer", messageId, Guid.Empty, BaseTime, BaseTime.AddMinutes(1)));
    }
    private static async Task SeedWorkAsync(string connectionString, Guid workId, DateTimeOffset retryEligibleAtUtc, DateTimeOffset createdAtUtc)
    {
        await using var context = CreateContext(connectionString);
        context.DurableWorkItems.Add(new DurableWorkItem(workId, "technical-test-work", "{\"probe\":true}", retryEligibleAtUtc, createdAtUtc));
        await context.SaveChangesAsync();
    }

    private static async Task PrepareDatabaseAsync(string connectionString)
    {
        await using var context = CreateContext(connectionString);
        await context.Database.MigrateAsync();
    }

    private static TunnerMigrationsDbContext CreateContext(string connectionString)
        => new(CreateOptions(connectionString));

    private static TestContextFactory CreateFactory(string connectionString)
        => new(CreateOptions(connectionString));

    private static DbContextOptions<TunnerMigrationsDbContext> CreateOptions(string connectionString)
        => new DbContextOptionsBuilder<TunnerMigrationsDbContext>()
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsAssembly(typeof(TunnerMigrationsDbContextFactory).Assembly.FullName))
            .Options;

    private static string BuildConnectionString(DotNet.Testcontainers.Containers.IContainer container)
        => $"Host={container.Hostname};Port={container.GetMappedPublicPort(5432)};Database=tunner_durable_work_test;Username=tunner";

    private static void SkipUnlessContainerTestsAreEnabled()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("TUNNER_RUN_CONTAINER_TESTS"), "1", StringComparison.Ordinal))
        {
            throw SkipException.ForSkip("Set TUNNER_RUN_CONTAINER_TESTS=1 to run real PostgreSQL durable-work/inbox integration tests.");
        }
    }

    private sealed class ThrowingContextFactory : IDbContextFactory<TunnerMigrationsDbContext>
    {
        public TunnerMigrationsDbContext CreateDbContext() => throw new InvalidOperationException("The invalid-input test must not create a database context.");
        public Task<TunnerMigrationsDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) => throw new InvalidOperationException("The invalid-input test must not create a database context.");
    }
    private sealed class TestContextFactory(DbContextOptions<TunnerMigrationsDbContext> options) : IDbContextFactory<TunnerMigrationsDbContext>
    {
        public TunnerMigrationsDbContext CreateDbContext() => new(options);
        public Task<TunnerMigrationsDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) => Task.FromResult(CreateDbContext());
    }
}