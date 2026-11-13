using Microsoft.EntityFrameworkCore;

namespace Tunner.Identity.Persistence;

/// <summary>Actual case-owned one-use contact challenge. Neutral entries never create this row.</summary>
internal sealed class RecoveryContactChallengeRow
{
    public Guid ChallengeId { get; set; }
    public Guid CaseId { get; set; }
    public Guid EntryId { get; set; }
    public Guid ContactReference { get; set; }
    public long ContactVersion { get; set; }
    public string ContextDigest { get; set; } = string.Empty;
    public string DestinationDigest { get; set; } = string.Empty;
    public string ProfileVersion { get; set; } = string.Empty;
    public string? ProtectedCode { get; set; }
    public string? ProtectedDigest { get; set; }
    public string? ProtectedAddress { get; set; }
    public int MaximumAttempts { get; set; }
    public int Attempts { get; set; }
    public string State { get; set; } = "PENDING";
    public long Version { get; set; } = 1;
    public Guid DeliveryId { get; set; }
    public Guid NotificationOutboxEventId { get; set; }
    public string NotificationSnapshot { get; set; } = string.Empty;
    public string ProcessingReference { get; set; } = string.Empty;
    public string RetentionReference { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset PurgeAfter { get; set; }
    public DateTimeOffset? EndedAt { get; set; }
    public DateTimeOffset? MaterialPurgedAt { get; set; }
    public Guid? ResultId { get; set; }
    public override string ToString() => nameof(RecoveryContactChallengeRow);
}
internal static class RecoveryContactModelConfiguration
{
    internal static void Configure(ModelBuilder model)
    {
        model.Entity<RecoveryContactChallengeRow>(b =>
        {
            b.ToTable("recovery_contact_challenges", "identity", t => t.HasCheckConstraint("ck_recovery_contact_challenge", RecoveryContactMigrationSql.ChallengeShape));
            b.HasKey(x => x.ChallengeId); b.Property(x => x.ChallengeId).ValueGeneratedNever(); b.Property(x => x.Version).IsConcurrencyToken();
            b.HasIndex(x => x.CaseId).IsUnique(); b.HasIndex(x => x.EntryId).IsUnique(); b.HasIndex(x => x.DeliveryId).IsUnique(); b.HasIndex(x => x.PurgeAfter);
            b.HasOne<RecoveryCaseRow>().WithMany().HasForeignKey(x => x.CaseId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<RecoveryEntryRow>().WithMany().HasForeignKey(x => x.EntryId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<VerifiedContactRow>().WithMany().HasForeignKey(x => x.ContactReference).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<RecoveryResultRow>().WithMany().HasForeignKey(x => x.ResultId).OnDelete(DeleteBehavior.Restrict);
        });
        IdentityColumnNames.UseSnakeCase(model, typeof(RecoveryContactChallengeRow));
    }
}
