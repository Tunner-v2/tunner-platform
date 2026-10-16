using Microsoft.EntityFrameworkCore;

namespace Tunner.Database.Outbox;

/// <summary>Maps the single Platform Persistence/Outbox table for atomic owner-write contexts.</summary>
public static class OutboxModelConfiguration
{
    public static void Configure(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        modelBuilder.Entity<OutboxMessage>(entity =>
        {
            entity.ToTable("outbox_messages", "platform");
            entity.HasKey(message => message.EventId);
            entity.Property(message => message.EventId).HasColumnName("event_id").ValueGeneratedNever();
            entity.Property(message => message.EnvelopeVersion).HasColumnName("envelope_version").HasMaxLength(32).IsRequired();
            entity.Property(message => message.EventType).HasColumnName("event_type").HasMaxLength(256).IsRequired();
            entity.Property(message => message.EventSchemaVersion).HasColumnName("event_schema_version").HasMaxLength(64).IsRequired();
            entity.Property(message => message.OccurredAtUtc).HasColumnName("occurred_at_utc").IsRequired();
            entity.Property(message => message.PublishedAtUtc).HasColumnName("published_at");
            entity.Property(message => message.Source).HasColumnName("source").HasMaxLength(256).IsRequired();
            entity.Property(message => message.Subject).HasColumnName("subject").HasMaxLength(512).IsRequired();
            entity.Property(message => message.ResourceVersion).HasColumnName("resource_version").HasMaxLength(256);
            entity.Property(message => message.OperationId).HasColumnName("operation_id").HasMaxLength(128);
            entity.Property(message => message.CorrelationId).HasColumnName("correlation_id").HasMaxLength(128);
            entity.Property(message => message.CausationEventId).HasColumnName("causation_event_id").HasMaxLength(128);
            entity.Property(message => message.DataClassification).HasColumnName("data_classification").HasMaxLength(64).IsRequired();
            entity.Property(message => message.Payload).HasColumnName("payload").HasColumnType("jsonb").IsRequired();
        });
    }
}