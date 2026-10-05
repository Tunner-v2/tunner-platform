using DotNet.Testcontainers.Containers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using Tunner.Database;
using Tunner.Database.Audit;
using Tunner.Database.Migrations;
using Tunner.Database.Outbox;
using Tunner.Testing;
using Xunit;
using Xunit.Sdk;

namespace Tunner.Database.IntegrationTests;

public sealed class AuditPostgresFixture : IAsyncLifetime
{
    private IContainer? container;
    public string ConnectionString { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        if (Environment.GetEnvironmentVariable("TUNNER_RUN_CONTAINER_TESTS") != "1") return;
        container = TestcontainersBaseline.CreatePostgresContainer();
        await container.StartAsync();
        ConnectionString = $"Host={container.Hostname};Port={container.GetMappedPublicPort(5432)};Database=tunner_outbox_test;Username=tunner";
        await using var context = Context();
        await context.Database.MigrateAsync();
        await context.Database.ExecuteSqlRawAsync("CREATE SCHEMA IF NOT EXISTS test_support; CREATE TABLE test_support.audit_owner_probe (id uuid PRIMARY KEY);");
    }

    public TunnerMigrationsDbContext Context() => new(new DbContextOptionsBuilder<TunnerMigrationsDbContext>()
        .UseNpgsql(ConnectionString, x => x.MigrationsAssembly(typeof(TunnerMigrationsDbContextFactory).Assembly.FullName)).Options);

    public async Task DisposeAsync()
    {
        if (container is not null) await container.DisposeAsync();
    }
}

public sealed class AuditAppendIntegrationTests(AuditPostgresFixture fixture) : IClassFixture<AuditPostgresFixture>
{
    [Fact]
    public async Task MigrationAndRoundTripPreserveCorrelationEnvelopeAndIntegrity()
    {
        Enabled();
        var auditEvent = AuditTestData.Event();
        await Append(auditEvent);
        await using var read = fixture.Context();
        Assert.False(read.Database.HasPendingModelChanges());
        var record = await read.AuditEvents.AsNoTracking().SingleAsync(x => x.EventId == auditEvent.EventId);
        Assert.Equal(auditEvent.ToEnvelope(), record.Envelope);
        Assert.Equal(auditEvent.OperationId, record.OperationId);
        Assert.Equal(auditEvent.CorrelationId, record.CorrelationId);
        Assert.True(record.VerifyIntegrity());
        record.RequireIntegrity();
    }

    [Theory]
    [InlineData("UPDATE audit.events SET operation_id = 'changed' WHERE event_id = {0}")]
    [InlineData("DELETE FROM audit.events WHERE event_id = {0}")]
    [InlineData("TRUNCATE audit.events")]
    public async Task OrdinarySqlCannotMutateAuditHistory(string sql)
    {
        Enabled();
        var auditEvent = AuditTestData.Event();
        await Append(auditEvent);
        await using var context = fixture.Context();
        var exception = await Assert.ThrowsAsync<PostgresException>(() => context.Database.ExecuteSqlRawAsync(sql, auditEvent.EventId));
        Assert.Equal("55000", exception.SqlState);
        Assert.True((await context.AuditEvents.AsNoTracking().SingleAsync(x => x.EventId == auditEvent.EventId)).VerifyIntegrity());
    }

    [Fact]
    public async Task ARepeatedStableIdIsRejectedAndItsOriginalRecordSurvives()
    {
        Enabled();
        var auditEvent = AuditTestData.Event();
        await Append(auditEvent);
        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => Append(auditEvent));
        Assert.Equal("23505", Assert.IsType<PostgresException>(exception.InnerException).SqlState);
        await using var context = fixture.Context();
        Assert.Equal(1, await context.AuditEvents.CountAsync(x => x.EventId == auditEvent.EventId));
    }

    [Fact]
    public async Task ConcurrentSameIdAppendsHaveOneWinner()
    {
        Enabled();
        var auditEvent = AuditTestData.Event();
        async Task<bool> TryAppend()
        {
            try { await Append(auditEvent); return true; }
            catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: "23505" }) { return false; }
        }
        var results = await Task.WhenAll(TryAppend(), TryAppend());
        Assert.Equal(1, results.Count(x => x));
        await using var context = fixture.Context();
        Assert.Equal(1, await context.AuditEvents.CountAsync(x => x.EventId == auditEvent.EventId));
    }

    [Fact]
    public async Task OwnerAuditAndOutboxCommitThroughTheExistingTransactionBoundary()
    {
        Enabled();
        var ownerId = Guid.NewGuid();
        var auditEvent = AuditTestData.Event();
        var outbox = Outbox(Guid.NewGuid());
        await using (var context = AtomicContext())
            await new EfTransactionalOutbox().PersistAsync(context, (owner, _) =>
            {
                owner.OwnerProbes.Add(new AuditOwnerProbe(ownerId));
                EfAuditAppender.StageAppend(owner, auditEvent);
                return Task.CompletedTask;
            }, outbox);
        await using var verify = AtomicContext();
        Assert.Equal(1, await verify.OwnerProbes.CountAsync(x => x.Id == ownerId));
        Assert.True((await verify.AuditEvents.SingleAsync(x => x.EventId == auditEvent.EventId)).VerifyIntegrity());
        Assert.Equal(1, await verify.OutboxMessages.CountAsync(x => x.EventId == outbox.EventId));
    }

    [Fact]
    public async Task LaterOutboxFailureRollsBackAlreadyFlushedOwnerAndAuditWrites()
    {
        Enabled();
        var duplicateId = Guid.NewGuid();
        await using (var initial = fixture.Context())
        {
            initial.OutboxMessages.Add(Outbox(duplicateId));
            await initial.SaveChangesAsync();
        }
        var ownerId = Guid.NewGuid();
        var auditEvent = AuditTestData.Event();
        await using (var context = AtomicContext())
            await Assert.ThrowsAsync<DbUpdateException>(() => new EfTransactionalOutbox().PersistAsync(context, async (owner, token) =>
            {
                owner.OwnerProbes.Add(new AuditOwnerProbe(ownerId));
                EfAuditAppender.StageAppend(owner, auditEvent);
                await owner.SaveChangesAsync(token);
            }, Outbox(duplicateId)));
        await using var verify = AtomicContext();
        Assert.Equal(0, await verify.OwnerProbes.CountAsync(x => x.Id == ownerId));
        Assert.Equal(0, await verify.AuditEvents.CountAsync(x => x.EventId == auditEvent.EventId));
        Assert.Equal(1, await verify.OutboxMessages.CountAsync(x => x.EventId == duplicateId));
    }

    [Fact]
    public async Task CancellationAfterFlushCannotLeaveAnAuditOfAnUncommittedOwnerWrite()
    {
        Enabled();
        var ownerId = Guid.NewGuid();
        var auditEvent = AuditTestData.Event();
        var outboxId = Guid.NewGuid();
        using var cancellation = new CancellationTokenSource();
        await using (var context = AtomicContext())
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new EfTransactionalOutbox().PersistAsync(context, async (owner, _) =>
            {
                owner.OwnerProbes.Add(new AuditOwnerProbe(ownerId));
                EfAuditAppender.StageAppend(owner, auditEvent);
                await owner.SaveChangesAsync(CancellationToken.None);
                cancellation.Cancel();
            }, Outbox(outboxId), cancellation.Token));
        await using var verify = AtomicContext();
        Assert.Equal(0, await verify.OwnerProbes.CountAsync(x => x.Id == ownerId));
        Assert.Equal(0, await verify.AuditEvents.CountAsync(x => x.EventId == auditEvent.EventId));
        Assert.Equal(0, await verify.OutboxMessages.CountAsync(x => x.EventId == outboxId));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PrivilegedTestCorruptionIsDetectedInEnvelopeOrProjection(bool projection)
    {
        Enabled();
        var auditEvent = AuditTestData.Event();
        await Append(auditEvent);
        await using var context = fixture.Context();
        // Deliberate corruption is confined to this throwaway test database; normal DML remains guarded.
        await context.Database.ExecuteSqlRawAsync("ALTER TABLE audit.events DISABLE TRIGGER audit_events_immutable_rows");
        try
        {
            if (projection)
                await context.Database.ExecuteSqlRawAsync("UPDATE audit.events SET operation_id = 'corrupted' WHERE event_id = {0}", auditEvent.EventId);
            else
                await context.Database.ExecuteSqlRawAsync("UPDATE audit.events SET envelope = {0} WHERE event_id = {1}", auditEvent.ToEnvelope().Replace("test-only.success", "test-only.succesx", StringComparison.Ordinal), auditEvent.EventId);
        }
        finally
        {
            await context.Database.ExecuteSqlRawAsync("ALTER TABLE audit.events ENABLE TRIGGER audit_events_immutable_rows");
        }
        var record = await context.AuditEvents.AsNoTracking().SingleAsync(x => x.EventId == auditEvent.EventId);
        Assert.False(record.VerifyIntegrity());
        Assert.Throws<InvalidOperationException>(record.RequireIntegrity);
    }

    private async Task Append(AuditEvent auditEvent)
    {
        await using var context = fixture.Context();
        EfAuditAppender.StageAppend(context, auditEvent);
        await context.SaveChangesAsync();
    }

    private AuditAtomicityContext AtomicContext() => new(new DbContextOptionsBuilder<AuditAtomicityContext>().UseNpgsql(fixture.ConnectionString).Options);

    private static OutboxMessage Outbox(Guid id) => new(id, "1.0", "test.audit.append", "1.0", DateTimeOffset.UtcNow, "test-owner", "test-resource", "test-classification", "{\"test\":true}", operationId: "test-operation", correlationId: "test-correlation");

    private static void Enabled()
    {
        if (Environment.GetEnvironmentVariable("TUNNER_RUN_CONTAINER_TESTS") != "1")
            throw SkipException.ForSkip("Set TUNNER_RUN_CONTAINER_TESTS=1 to run disposable real PostgreSQL audit tests.");
    }

    private sealed class AuditAtomicityContext(DbContextOptions<AuditAtomicityContext> options) : DbContext(options), ITransactionalOutboxContext
    {
        public DbSet<AuditEventRecord> AuditEvents => Set<AuditEventRecord>();
        public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
        public DbSet<AuditOwnerProbe> OwnerProbes => Set<AuditOwnerProbe>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            AuditModelConfiguration.Configure(modelBuilder);
            OutboxModelConfiguration.Configure(modelBuilder);
            modelBuilder.Entity<AuditOwnerProbe>(entity =>
            {
                entity.ToTable("audit_owner_probe", "test_support");
                entity.HasKey(x => x.Id);
                entity.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            });
        }

        Task<IDbContextTransaction> ITransactionalOutboxContext.BeginOutboxTransactionAsync(CancellationToken cancellationToken) => Database.BeginTransactionAsync(cancellationToken);
        void ITransactionalOutboxContext.AddOutboxMessage(OutboxMessage message) => OutboxMessages.Add(message);
        Task<int> ITransactionalOutboxContext.SaveChangesAsync(CancellationToken cancellationToken) => base.SaveChangesAsync(cancellationToken);
    }

    private sealed class AuditOwnerProbe
    {
        private AuditOwnerProbe() { }
        public AuditOwnerProbe(Guid id) => Id = id;
        public Guid Id { get; private set; }
    }
}

