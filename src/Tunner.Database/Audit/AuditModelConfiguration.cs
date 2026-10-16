using Microsoft.EntityFrameworkCore;

namespace Tunner.Database.Audit;

public static class AuditModelConfiguration
{
    public static void Configure(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        modelBuilder.Entity<AuditEventRecord>(entity =>
        {
            entity.ToTable("events", "audit", table =>
            {
                table.HasCheckConstraint("ck_audit_events_integrity_format", "integrity_format = 'sha256-envelope-v1'");
                table.HasCheckConstraint("ck_audit_events_digest", "envelope_sha256 ~ '^[0-9a-f]{64}$'");
            });
            entity.HasKey(x => x.EventId);
            entity.Property(x => x.EventId).HasColumnName("event_id").ValueGeneratedNever();
            entity.Property(x => x.OccurredAtUtc).HasColumnName("occurred_at_utc");
            entity.Property(x => x.OperationId).HasColumnName("operation_id").HasColumnType("text").IsRequired();
            entity.Property(x => x.CorrelationId).HasColumnName("correlation_id").HasColumnType("text").IsRequired();
            entity.Property(x => x.DataClassification).HasColumnName("data_classification").HasColumnType("text").IsRequired();
            entity.Property(x => x.RetentionClassReference).HasColumnName("retention_class_ref").HasColumnType("text").IsRequired();
            entity.Property(x => x.Envelope).HasColumnName("envelope").HasColumnType("text").IsRequired();
            entity.Property(x => x.IntegrityFormat).HasColumnName("integrity_format").HasColumnType("text").IsRequired();
            entity.Property(x => x.EnvelopeSha256).HasColumnName("envelope_sha256").HasMaxLength(64).IsRequired();
            entity.HasIndex(x => x.OperationId).HasDatabaseName("IX_audit_events_operation");
            entity.HasIndex(x => x.CorrelationId).HasDatabaseName("IX_audit_events_correlation");
        });
    }
}

/// <summary>Stages a new audit entry; the authorized owner commits through its existing transaction.</summary>
public static class EfAuditAppender
{
    public static void StageAppend(DbContext ownerContext, AuditEvent auditEvent)
    {
        ArgumentNullException.ThrowIfNull(ownerContext);
        ArgumentNullException.ThrowIfNull(auditEvent);
        ownerContext.Set<AuditEventRecord>().Add(new AuditEventRecord(auditEvent));
    }
}

