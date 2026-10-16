using Microsoft.EntityFrameworkCore;

namespace Tunner.Identity.Persistence;

internal sealed class RegistrationOperationRow
{
    public Guid OperationId { get; set; }
    public string BrowserDigest { get; set; } = string.Empty;
    public Guid IdempotencyKey { get; set; }
    public string CorrelationId { get; set; } = string.Empty;
    public string? AccountId { get; set; }
    public Guid? ContactId { get; set; }
    public string NormalizedEmail { get; set; } = string.Empty;
    public string? ProtectedAddress { get; set; }
    public string? ProtectedCredential { get; set; }
    public string? ProtectedCode { get; set; }
    public string? ProtectedDigest { get; set; }
    public string PolicyVersion { get; set; } = string.Empty;
    public string AgePolicyVersion { get; set; } = string.Empty;
    public string TermsVersion { get; set; } = string.Empty;
    public string PrivacyVersion { get; set; } = string.Empty;
    public string ProcessingReference { get; set; } = string.Empty;
    public string RetentionReference { get; set; } = string.Empty;
    public DateTimeOffset AgeEvaluatedAt { get; set; }
    public string AgeEvidenceReference { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset ResendAfter { get; set; }
    public DateTimeOffset PurgeAfter { get; set; }
    public long Version { get; set; }
    public int Attempts { get; set; }
    public int Resends { get; set; }
    public string Status { get; set; } = "REQUIRES_ACTION";
    public string? ErrorCode { get; set; }
    public Guid? DeliveryId { get; set; }
    public string TemplateVersion { get; set; } = string.Empty;
}
internal sealed class RegistrationMutationRow
{
    public Guid OperationId { get; set; }
    public string Kind { get; set; } = string.Empty;
    public Guid IdempotencyKey { get; set; }
    public string ResultJson { get; set; } = string.Empty;
}
internal sealed class RegistrationAbuseBucketRow
{
    public string Partition { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public long Window { get; set; }
    public int Count { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
}
internal static class RegistrationModelConfiguration
{
    internal static void Configure(ModelBuilder model)
    {
        model.Entity<RegistrationOperationRow>(b =>
        {
            b.ToTable("registration_operations", "identity", t =>
            {
                t.HasCheckConstraint("ck_registration_version", "\"Version\" >= 1 AND \"Attempts\" >= 0 AND \"Resends\" >= 0");
                t.HasCheckConstraint("ck_registration_status", "\"Status\" IN ('REQUIRES_ACTION','SUCCEEDED','FAILED')");
            });
            b.HasKey(x => x.OperationId);
            b.HasIndex(x => new { x.BrowserDigest, x.IdempotencyKey }).IsUnique();
            b.HasIndex(x => x.PurgeAfter);
            b.Property(x => x.Version).IsConcurrencyToken();
            b.HasOne<AccountRow>().WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<RegistrationMutationRow>(b =>
        {
            b.ToTable("registration_mutations", "identity");
            b.HasKey(x => new { x.OperationId, x.Kind, x.IdempotencyKey });
            b.Property(x => x.ResultJson).HasColumnType("jsonb");
            b.HasOne<RegistrationOperationRow>().WithMany().HasForeignKey(x => x.OperationId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<RegistrationAbuseBucketRow>(b =>
        {
            b.ToTable("registration_abuse_buckets", "identity");
            b.HasKey(x => new { x.Partition, x.Kind, x.Window });
        });
    }
}
