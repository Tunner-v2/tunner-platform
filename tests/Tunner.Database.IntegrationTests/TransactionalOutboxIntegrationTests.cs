using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Tunner.Database;
using Tunner.Database.Migrations;
using Tunner.Database.Outbox;
using Tunner.Testing;
using Xunit;
using Xunit.Sdk;

namespace Tunner.Database.IntegrationTests;

public sealed class TransactionalOutboxIntegrationTests
{
    [Fact]
    public async Task PersistsOwnerWriteAndOutboxMessageWhenTheTransactionCommits()
    {
        SkipUnlessContainerTestsAreEnabled();

        await using var container = TestcontainersBaseline.CreatePostgresContainer();
        await container.StartAsync();
        var connectionString = BuildConnectionString(container);

        await using (var setupContext = CreateMigrationsContext(connectionString))
        {
            await PrepareDatabaseAsync(setupContext);
        }

        var ownerWriteId = Guid.NewGuid();
        var eventId = Guid.NewGuid();

        await using (var writeContext = CreateAtomicityContext(connectionString))
        {
            var transactionalOutbox = new EfTransactionalOutbox();
            await transactionalOutbox.PersistAsync(
                writeContext,
                (context, _) =>
                {
                    context.OwnerWriteProbes.Add(new OutboxOwnerWriteProbe(ownerWriteId));
                    return Task.CompletedTask;
                },
                CreateOutboxMessage(eventId));
        }

        await using var verificationContext = CreateAtomicityContext(connectionString);
        Assert.Equal(1, await verificationContext.OwnerWriteProbes.CountAsync(probe => probe.Id == ownerWriteId));
        var persistedMessage = await verificationContext.OutboxMessages.SingleAsync(message => message.EventId == eventId);
        Assert.Null(persistedMessage.PublishedAtUtc);
    }

    [Fact]
    public async Task RollsBackOwnerWriteWhenTheOutboxInsertFails()
    {
        SkipUnlessContainerTestsAreEnabled();

        await using var container = TestcontainersBaseline.CreatePostgresContainer();
        await container.StartAsync();
        var connectionString = BuildConnectionString(container);
        var duplicateEventId = Guid.NewGuid();

        await using (var setupContext = CreateMigrationsContext(connectionString))
        {
            await PrepareDatabaseAsync(setupContext);
        }

        await using (var initialContext = CreateAtomicityContext(connectionString))
        {
            initialContext.OutboxMessages.Add(CreateOutboxMessage(duplicateEventId));
            await initialContext.SaveChangesAsync();
        }

        var ownerWriteId = Guid.NewGuid();
        await using (var failingContext = CreateAtomicityContext(connectionString))
        {
            var transactionalOutbox = new EfTransactionalOutbox();
            await Assert.ThrowsAsync<DbUpdateException>(() => transactionalOutbox.PersistAsync(
                failingContext,
                async (context, cancellationToken) =>
                {
                    context.OwnerWriteProbes.Add(new OutboxOwnerWriteProbe(ownerWriteId));
                    await context.SaveChangesAsync(cancellationToken);
                },
                CreateOutboxMessage(duplicateEventId)));
        }

        await using var verificationContext = CreateAtomicityContext(connectionString);
        Assert.Equal(0, await verificationContext.OwnerWriteProbes.CountAsync(probe => probe.Id == ownerWriteId));
        Assert.Equal(1, await verificationContext.OutboxMessages.CountAsync(message => message.EventId == duplicateEventId));
    }

    private static TunnerMigrationsDbContext CreateMigrationsContext(string connectionString)
    {
        var options = new DbContextOptionsBuilder<TunnerMigrationsDbContext>()
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsAssembly(typeof(TunnerMigrationsDbContextFactory).Assembly.FullName))
            .Options;

        return new TunnerMigrationsDbContext(options);
    }

    private static OutboxAtomicityTestDbContext CreateAtomicityContext(string connectionString)
    {
        var options = new DbContextOptionsBuilder<OutboxAtomicityTestDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new OutboxAtomicityTestDbContext(options);
    }

    private static async Task PrepareDatabaseAsync(TunnerMigrationsDbContext context)
    {
        await context.Database.MigrateAsync();
        await context.Database.ExecuteSqlRawAsync("CREATE SCHEMA IF NOT EXISTS test_support;");
        await context.Database.ExecuteSqlRawAsync(
            "CREATE TABLE IF NOT EXISTS test_support.outbox_owner_write_probe (id uuid PRIMARY KEY);");
    }

    private static OutboxMessage CreateOutboxMessage(Guid eventId)
        => new(
            eventId,
            envelopeVersion: "1.0",
            eventType: "platform.persistence.atomicity-probe",
            eventSchemaVersion: "1.0",
            occurredAtUtc: DateTimeOffset.UtcNow,
            source: "platform.persistence",
            subject: "test-owner-write",
            dataClassification: "internal-test",
            payload: "{\"probe\":true}");

    private static string BuildConnectionString(DotNet.Testcontainers.Containers.IContainer container)
        => $"Host={container.Hostname};Port={container.GetMappedPublicPort(5432)};Database=tunner_outbox_test;Username=tunner";

    private static void SkipUnlessContainerTestsAreEnabled()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("TUNNER_RUN_CONTAINER_TESTS"), "1", StringComparison.Ordinal))
        {
            throw SkipException.ForSkip("Set TUNNER_RUN_CONTAINER_TESTS=1 to run real PostgreSQL transactional-outbox integration tests.");
        }
    }

    private sealed class OutboxAtomicityTestDbContext(DbContextOptions<OutboxAtomicityTestDbContext> options)
        : DbContext(options), ITransactionalOutboxContext
    {
        public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

        public DbSet<OutboxOwnerWriteProbe> OwnerWriteProbes => Set<OutboxOwnerWriteProbe>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            OutboxModelConfiguration.Configure(modelBuilder);
            modelBuilder.Entity<OutboxOwnerWriteProbe>(entity =>
            {
                entity.ToTable("outbox_owner_write_probe", "test_support");
                entity.HasKey(probe => probe.Id);
                entity.Property(probe => probe.Id).HasColumnName("id").ValueGeneratedNever();
            });
        }

        Task<IDbContextTransaction> ITransactionalOutboxContext.BeginOutboxTransactionAsync(CancellationToken cancellationToken)
            => Database.BeginTransactionAsync(cancellationToken);

        void ITransactionalOutboxContext.AddOutboxMessage(OutboxMessage message)
            => OutboxMessages.Add(message);

        Task<int> ITransactionalOutboxContext.SaveChangesAsync(CancellationToken cancellationToken)
            => base.SaveChangesAsync(cancellationToken);
    }

    private sealed class OutboxOwnerWriteProbe
    {
        private OutboxOwnerWriteProbe()
        {
        }

        public OutboxOwnerWriteProbe(Guid id)
        {
            Id = id;
        }

        public Guid Id { get; private set; }
    }
}