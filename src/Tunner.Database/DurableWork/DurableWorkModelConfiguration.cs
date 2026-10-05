using Microsoft.EntityFrameworkCore;

namespace Tunner.Database.DurableWork;

/// <summary>Maps only the shared technical durable-work and inbox persistence records.</summary>
public static class DurableWorkModelConfiguration
{
    public static void Configure(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        modelBuilder.Entity<DurableWorkItem>(entity =>
        {
            entity.ToTable("durable_work_items", "platform");
            entity.HasKey(item => item.WorkId);
            entity.Property(item => item.WorkId).HasColumnName("work_id").ValueGeneratedNever();
            entity.Property(item => item.WorkKind).HasColumnName("work_kind").HasMaxLength(256).IsRequired();
            entity.Property(item => item.Payload).HasColumnName("payload").HasColumnType("jsonb").IsRequired();
            entity.Property(item => item.RetryEligibleAtUtc).HasColumnName("retry_eligible_at_utc").IsRequired();
            entity.Property(item => item.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
            entity.Property(item => item.LeaseToken).HasColumnName("lease_token");
            entity.Property(item => item.LeaseAcquiredAtUtc).HasColumnName("lease_acquired_at_utc");
            entity.Property(item => item.LeaseExpiresAtUtc).HasColumnName("lease_expires_at_utc");
            entity.Property(item => item.CompletedAtUtc).HasColumnName("completed_at_utc");
            entity.HasIndex(item => new { item.CompletedAtUtc, item.RetryEligibleAtUtc, item.LeaseExpiresAtUtc }).HasDatabaseName("IX_durable_work_items_claim");
        });
        modelBuilder.Entity<ConsumerInboxEntry>(entity =>
        {
            entity.ToTable("consumer_inbox", "platform");
            entity.HasKey(entry => new { entry.ConsumerKey, entry.MessageId });
            entity.Property(entry => entry.ConsumerKey).HasColumnName("consumer_key").HasMaxLength(256).IsRequired();
            entity.Property(entry => entry.MessageId).HasColumnName("message_id").ValueGeneratedNever();
            entity.Property(entry => entry.LeaseToken).HasColumnName("lease_token");
            entity.Property(entry => entry.ReservedAtUtc).HasColumnName("reserved_at_utc").IsRequired();
            entity.Property(entry => entry.LeaseExpiresAtUtc).HasColumnName("lease_expires_at_utc");
            entity.Property(entry => entry.CompletedAtUtc).HasColumnName("completed_at_utc");
        });
    }
}