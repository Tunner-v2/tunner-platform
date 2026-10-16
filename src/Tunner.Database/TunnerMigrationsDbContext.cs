using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Tunner.Database.Audit;
using Tunner.Database.DurableWork;
using Tunner.Database.Outbox;

namespace Tunner.Database;

/// <summary>Migration model for governed shared platform persistence; Product/domain entities remain separately owned.</summary>
public sealed class TunnerMigrationsDbContext(DbContextOptions<TunnerMigrationsDbContext> options) : DbContext(options), ITransactionalOutboxContext
{
    public DbSet<AuditEventRecord> AuditEvents => Set<AuditEventRecord>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<DurableWorkItem> DurableWorkItems => Set<DurableWorkItem>();
    public DbSet<ConsumerInboxEntry> ConsumerInboxEntries => Set<ConsumerInboxEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        AuditModelConfiguration.Configure(modelBuilder);
        OutboxModelConfiguration.Configure(modelBuilder);
        DurableWorkModelConfiguration.Configure(modelBuilder);
    }

    Task<IDbContextTransaction> ITransactionalOutboxContext.BeginOutboxTransactionAsync(CancellationToken cancellationToken)
        => Database.BeginTransactionAsync(cancellationToken);

    void ITransactionalOutboxContext.AddOutboxMessage(OutboxMessage message)
        => OutboxMessages.Add(message);

    Task<int> ITransactionalOutboxContext.SaveChangesAsync(CancellationToken cancellationToken)
        => base.SaveChangesAsync(cancellationToken);
}
