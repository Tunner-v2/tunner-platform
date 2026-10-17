using Microsoft.EntityFrameworkCore;
namespace Tunner.Identity.Persistence;

/// <summary>Accounts-owned normal profile. Restricted values are protected together; never copied into registration.</summary>
internal sealed class AccountPersonalInformationRow
{
    public string AccountId { get; set; } = string.Empty;
    public string? ProtectedData { get; set; }
    public long Version { get; set; }
    public DateTimeOffset RecordedAt { get; set; }
    public DateTimeOffset PurgeAfter { get; set; }
    public string ProcessingReference { get; set; } = string.Empty;
    public string RetentionReference { get; set; } = string.Empty;
    public string EvaluationJson { get; set; } = string.Empty;
}
internal sealed class AccountPersonalInformationVersionRow
{
    public string AccountId { get; set; } = string.Empty;
    public long Version { get; set; }
    public Guid IdempotencyKey { get; set; }
    public Guid OnboardingOperationId { get; set; }
    public DateTimeOffset RecordedAt { get; set; }
    public string EvaluationJson { get; set; } = string.Empty;
}
internal static class AccountPersonalInformationModel
{
    internal static void Configure(ModelBuilder model)
    {
        model.Entity<AccountPersonalInformationRow>(b =>
        {
            b.ToTable("personal_information", "accounts", t =>
            {
                t.HasCheckConstraint("ck_personal_version", "\"Version\" >= 1");
                t.HasCheckConstraint("ck_personal_retention", "\"PurgeAfter\" > \"RecordedAt\"");
                t.HasCheckConstraint("ck_personal_processing", "length(\"ProcessingReference\") > 0 AND length(\"RetentionReference\") > 0");
            });
            b.HasKey(x => x.AccountId); b.Property(x => x.Version).IsConcurrencyToken();
            b.HasIndex(x => x.PurgeAfter); b.Property(x => x.EvaluationJson).HasColumnType("jsonb");
            b.HasOne<AccountRow>().WithOne().HasForeignKey<AccountPersonalInformationRow>(x => x.AccountId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<AccountPersonalInformationVersionRow>(b =>
        {
            b.ToTable("personal_information_versions", "accounts");b.HasKey(x => new { x.AccountId, x.Version });
            b.HasIndex(x => new { x.OnboardingOperationId, x.IdempotencyKey }).IsUnique();
            b.Property(x => x.EvaluationJson).HasColumnType("jsonb");
            b.HasOne<AccountRow>().WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
