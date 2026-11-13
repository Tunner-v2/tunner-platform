using Microsoft.EntityFrameworkCore;

namespace Tunner.Identity.Persistence;

internal sealed class RecoveryCaseRow
{
    public Guid CaseId { get; set; }
    public Guid RequestId { get; set; }
    public string AccountId { get; set; } = string.Empty;
    public string OriginalBrowserDigest { get; set; } = string.Empty;
    public string Origin { get; set; } = string.Empty;
    public string NetworkDigest { get; set; } = string.Empty;
    public string ContextDigest { get; set; } = string.Empty;
    public string Goal { get; set; } = string.Empty;
    public string SessionEffect { get; set; } = string.Empty;
    public Guid? TargetSessionId { get; set; }
    public long? ExpectedSessionVersion { get; set; }
    public long ExpectedAccountVersion { get; set; }
    public string AccountState { get; set; } = string.Empty;
    public Guid SetReference { get; set; }
    public long ExpectedSetLifecycleVersion { get; set; }
    public long? ExpectedUsageVersion { get; set; }
    public string UsageAdmission { get; set; } = string.Empty;
    public string PolicyVersion { get; set; } = string.Empty;
    public string PolicySnapshot { get; set; } = string.Empty;
    public string HierarchySnapshot { get; set; } = string.Empty;
    public string FactsDigest { get; set; } = string.Empty;
    public string ProcessingReference { get; set; } = string.Empty;
    public string RetentionReference { get; set; } = string.Empty;
    public string UnresolvedEffectsJson { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset RecoveryExpiresAt { get; set; }
    public string State { get; set; } = "PENDING_CODE";
    public long Version { get; set; } = 1;
    public Guid? ResultId { get; set; }
    public DateTimeOffset? EndedAt { get; set; }
    public Guid? EntryId { get; set; }
    public string? PriorPasswordDigest { get; set; }
    public string? PriorSecurityStampDigest { get; set; }
    public string? UserSourceDigest { get; set; }
    public override string ToString() => nameof(RecoveryCaseRow);
}

/// <summary>Immutable containment outcome. It does not assert restored access, credential repair or notification delivery.</summary>
internal sealed class RecoveryResultRow
{
    public Guid ResultId { get; set; }
    public Guid CaseId { get; set; }
    public string AccountId { get; set; } = string.Empty;
    public string Outcome { get; set; } = string.Empty;
    public Guid? TargetSessionId { get; set; }
    public long? PriorSessionVersion { get; set; }
    public long? EndedSessionVersion { get; set; }
    public string ContextDigest { get; set; } = string.Empty;
    public string PolicyVersion { get; set; } = string.Empty;
    public string PolicySnapshot { get; set; } = string.Empty;
    public string HierarchySnapshot { get; set; } = string.Empty;
    public Guid SetReference { get; set; }
    public long SetLifecycleVersion { get; set; }
    public DateTimeOffset RecordedAt { get; set; }
    public string UnresolvedEffectsJson { get; set; } = string.Empty;
    public string ProcessingReference { get; set; } = string.Empty;
    public string RetentionReference { get; set; } = string.Empty;
    public Guid? SessionOutboxEventId { get; set; }
    public Guid? RepairOutboxEventId { get; set; }
    public string? PriorPasswordDigest { get; set; }
    public string? RepairedPasswordDigest { get; set; }
    public string? PriorSecurityStampDigest { get; set; }
    public string? RepairedSecurityStampDigest { get; set; }
    public string? SessionEffect { get; set; }
    public override string ToString() => nameof(RecoveryResultRow);
}

internal static class RecoveryCaseModelConfiguration
{
    internal static void Configure(ModelBuilder model)
    {
        model.Entity<RecoveryCaseRow>(b =>
        {
            b.ToTable("recovery_cases", "identity", t =>
            {
                t.HasCheckConstraint("ck_recovery_case_binding", RecoveryPasswordMigrationSql.CaseBinding);
                t.HasCheckConstraint("ck_recovery_case_versions", RecoveryPasswordMigrationSql.CaseVersions);
                t.HasCheckConstraint("ck_recovery_case_state", RecoveryPasswordMigrationSql.CaseState);
            });
            b.HasKey(x => x.CaseId); b.Property(x => x.CaseId).ValueGeneratedNever();
            b.HasIndex(x => new { x.AccountId, x.OriginalBrowserDigest, x.RequestId }).IsUnique();
            b.HasIndex(x => x.ExpiresAt); b.HasIndex(x => x.ResultId).IsUnique(); b.Property(x => x.Version).IsConcurrencyToken();
            b.HasOne<AccountRow>().WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<AuthenticationSessionRow>().WithMany().HasForeignKey(x => x.TargetSessionId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<RecoveryCodeSetRow>().WithMany().HasForeignKey(x => new { x.AccountId, x.SetReference })
                .HasPrincipalKey(x => new { x.AccountId, x.SetReference }).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<RecoveryEntryRow>().WithMany().HasForeignKey(x => x.EntryId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<RecoveryResultRow>(b =>
        {
            b.ToTable("recovery_results", "identity", t =>
            {
                t.HasCheckConstraint("ck_recovery_result_shape", RecoveryPasswordMigrationSql.ResultShape);
            });
            b.HasKey(x => x.ResultId); b.Property(x => x.ResultId).ValueGeneratedNever(); b.HasIndex(x => x.CaseId).IsUnique();
            b.HasOne<RecoveryCaseRow>().WithMany().HasForeignKey(x => x.CaseId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<AuthenticationSessionRow>().WithMany().HasForeignKey(x => x.TargetSessionId).OnDelete(DeleteBehavior.Restrict);
        });
        IdentityColumnNames.UseSnakeCase(model, typeof(RecoveryCaseRow), typeof(RecoveryResultRow));
    }
}
