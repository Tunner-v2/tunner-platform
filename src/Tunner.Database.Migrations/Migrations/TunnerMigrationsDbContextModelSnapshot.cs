using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Tunner.Database;

namespace Tunner.Database.Migrations.Migrations;

[DbContext(typeof(TunnerMigrationsDbContext))]
public sealed class TunnerMigrationsDbContextModelSnapshot : ModelSnapshot
{
    protected override void BuildModel(ModelBuilder modelBuilder)
    {
#pragma warning disable 612, 618
        modelBuilder
            .HasAnnotation("ProductVersion", "10.0.12")
            .HasAnnotation("Relational:MaxIdentifierLength", 63);

        modelBuilder.Entity("Tunner.Database.Outbox.OutboxMessage", entity =>
        {
            entity.Property<Guid>("EventId")
                .ValueGeneratedNever()
                .HasColumnType("uuid")
                .HasColumnName("event_id");

            entity.Property<string>("CausationEventId")
                .HasMaxLength(128)
                .HasColumnType("character varying(128)")
                .HasColumnName("causation_event_id");

            entity.Property<string>("CorrelationId")
                .HasMaxLength(128)
                .HasColumnType("character varying(128)")
                .HasColumnName("correlation_id");

            entity.Property<string>("DataClassification")
                .IsRequired()
                .HasMaxLength(64)
                .HasColumnType("character varying(64)")
                .HasColumnName("data_classification");

            entity.Property<string>("EnvelopeVersion")
                .IsRequired()
                .HasMaxLength(32)
                .HasColumnType("character varying(32)")
                .HasColumnName("envelope_version");

            entity.Property<string>("EventSchemaVersion")
                .IsRequired()
                .HasMaxLength(64)
                .HasColumnType("character varying(64)")
                .HasColumnName("event_schema_version");

            entity.Property<string>("EventType")
                .IsRequired()
                .HasMaxLength(256)
                .HasColumnType("character varying(256)")
                .HasColumnName("event_type");

            entity.Property<DateTimeOffset>("OccurredAtUtc")
                .HasColumnType("timestamp with time zone")
                .HasColumnName("occurred_at_utc");

            entity.Property<string>("OperationId")
                .HasMaxLength(128)
                .HasColumnType("character varying(128)")
                .HasColumnName("operation_id");

            entity.Property<string>("Payload")
                .IsRequired()
                .HasColumnType("jsonb")
                .HasColumnName("payload");

            entity.Property<DateTimeOffset?>("PublishedAtUtc")
                .HasColumnType("timestamp with time zone")
                .HasColumnName("published_at");

            entity.Property<string>("ResourceVersion")
                .HasMaxLength(256)
                .HasColumnType("character varying(256)")
                .HasColumnName("resource_version");

            entity.Property<string>("Source")
                .IsRequired()
                .HasMaxLength(256)
                .HasColumnType("character varying(256)")
                .HasColumnName("source");

            entity.Property<string>("Subject")
                .IsRequired()
                .HasMaxLength(512)
                .HasColumnType("character varying(512)")
                .HasColumnName("subject");

            entity.HasKey("EventId");
            entity.ToTable("outbox_messages", "platform");
        });

        modelBuilder.Entity("Tunner.Database.DurableWork.ConsumerInboxEntry", entity =>
        {
            entity.Property<string>("ConsumerKey")
                .HasMaxLength(256)
                .HasColumnType("character varying(256)")
                .HasColumnName("consumer_key");

            entity.Property<Guid>("MessageId")
                .ValueGeneratedNever()
                .HasColumnType("uuid")
                .HasColumnName("message_id");

            entity.Property<DateTimeOffset?>("CompletedAtUtc")
                .HasColumnType("timestamp with time zone")
                .HasColumnName("completed_at_utc");

            entity.Property<DateTimeOffset?>("LeaseExpiresAtUtc")
                .HasColumnType("timestamp with time zone")
                .HasColumnName("lease_expires_at_utc");

            entity.Property<Guid?>("LeaseToken")
                .HasColumnType("uuid")
                .HasColumnName("lease_token");

            entity.Property<DateTimeOffset>("ReservedAtUtc")
                .HasColumnType("timestamp with time zone")
                .HasColumnName("reserved_at_utc");

            entity.HasKey("ConsumerKey", "MessageId");
            entity.ToTable("consumer_inbox", "platform");
        });

        modelBuilder.Entity("Tunner.Database.DurableWork.DurableWorkItem", entity =>
        {
            entity.Property<Guid>("WorkId")
                .ValueGeneratedNever()
                .HasColumnType("uuid")
                .HasColumnName("work_id");

            entity.Property<DateTimeOffset?>("CompletedAtUtc")
                .HasColumnType("timestamp with time zone")
                .HasColumnName("completed_at_utc");

            entity.Property<DateTimeOffset>("CreatedAtUtc")
                .HasColumnType("timestamp with time zone")
                .HasColumnName("created_at_utc");

            entity.Property<DateTimeOffset?>("LeaseAcquiredAtUtc")
                .HasColumnType("timestamp with time zone")
                .HasColumnName("lease_acquired_at_utc");

            entity.Property<DateTimeOffset?>("LeaseExpiresAtUtc")
                .HasColumnType("timestamp with time zone")
                .HasColumnName("lease_expires_at_utc");

            entity.Property<Guid?>("LeaseToken")
                .HasColumnType("uuid")
                .HasColumnName("lease_token");

            entity.Property<string>("Payload")
                .IsRequired()
                .HasColumnType("jsonb")
                .HasColumnName("payload");

            entity.Property<DateTimeOffset>("RetryEligibleAtUtc")
                .HasColumnType("timestamp with time zone")
                .HasColumnName("retry_eligible_at_utc");

            entity.Property<string>("WorkKind")
                .IsRequired()
                .HasMaxLength(256)
                .HasColumnType("character varying(256)")
                .HasColumnName("work_kind");

            entity.HasKey("WorkId");
            entity.HasIndex("CompletedAtUtc", "RetryEligibleAtUtc", "LeaseExpiresAtUtc")
                .HasDatabaseName("IX_durable_work_items_claim");
            entity.ToTable("durable_work_items", "platform");
        });
        modelBuilder.Entity("Tunner.Database.Audit.AuditEventRecord", entity =>
        {
            entity.Property<Guid>("EventId").ValueGeneratedNever().HasColumnType("uuid").HasColumnName("event_id");
            entity.Property<DateTimeOffset>("OccurredAtUtc").HasColumnType("timestamp with time zone").HasColumnName("occurred_at_utc");
            entity.Property<string>("OperationId").IsRequired().HasColumnType("text").HasColumnName("operation_id");
            entity.Property<string>("CorrelationId").IsRequired().HasColumnType("text").HasColumnName("correlation_id");
            entity.Property<string>("DataClassification").IsRequired().HasColumnType("text").HasColumnName("data_classification");
            entity.Property<string>("RetentionClassReference").IsRequired().HasColumnType("text").HasColumnName("retention_class_ref");
            entity.Property<string>("Envelope").IsRequired().HasColumnType("text").HasColumnName("envelope");
            entity.Property<string>("IntegrityFormat").IsRequired().HasColumnType("text").HasColumnName("integrity_format");
            entity.Property<string>("EnvelopeSha256").IsRequired().HasMaxLength(64).HasColumnType("character varying(64)").HasColumnName("envelope_sha256");
            entity.HasKey("EventId");
            entity.HasIndex("OperationId").HasDatabaseName("IX_audit_events_operation");
            entity.HasIndex("CorrelationId").HasDatabaseName("IX_audit_events_correlation");
            entity.ToTable("events", "audit", table =>
            {
                table.HasCheckConstraint("ck_audit_events_integrity_format", "integrity_format = 'sha256-envelope-v1'");
                table.HasCheckConstraint("ck_audit_events_digest", "envelope_sha256 ~ '^[0-9a-f]{64}$'");
            });
        });

#pragma warning restore 612, 618
    }
}
