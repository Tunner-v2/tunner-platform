using Microsoft.EntityFrameworkCore;
using Tunner.Accounts;

namespace Tunner.Identity.Persistence;

internal sealed class RecoveryCodeSetRow
{
    public Guid SetReference { get; set; }
    public string AccountId { get; set; } = string.Empty;
    public Guid IssuanceOperationId { get; set; }
    public string State { get; set; } = string.Empty;
    public long LifecycleVersion { get; set; } = 1;
    public long UsageVersion { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? RetiredAt { get; set; }
    public Guid? RetiredByOperationId { get; set; }
    public string PolicyVersion { get; set; } = string.Empty;
    public string CryptoProfileVersion { get; set; } = string.Empty;
    public string ProcessingReference { get; set; } = string.Empty;
    public string RetentionReference { get; set; } = string.Empty;
    public override string ToString() => nameof(RecoveryCodeSetRow);
}

internal sealed class RecoveryCodeEntryRow
{
    public Guid EntryReference { get; set; }
    public Guid SetReference { get; set; }
    public string AccountId { get; set; } = string.Empty;
    public string Verifier { get; set; } = string.Empty;
    public string CryptoProfileVersion { get; set; } = string.Empty;
    public DateTimeOffset? ConsumedAt { get; set; }
    public string? ConsumedByOperationId { get; set; }
    public string? ConsumptionCorrelationId { get; set; }
    public long Version { get; set; } = 1;
    public override string ToString() => nameof(RecoveryCodeEntryRow);
}

/// <summary>Generate/Replace operation only; no P2-011 case table or plaintext outcome.</summary>
internal sealed class RecoveryCodeOperationRow
{
    public Guid OperationId { get; set; }
    public Guid RequestId { get; set; }
    public string AccountId { get; set; } = string.Empty;
    public Guid SessionId { get; set; }
    public string Origin { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public Guid SetReference { get; set; }
    public long ExpectedVersion { get; set; }
    public string ContextDigest { get; set; } = string.Empty;
    public string PriorSetPlanDigest { get; set; } = string.Empty;
    public string ImpactFactsDigest { get; set; } = string.Empty;
    public string PolicyVersion { get; set; } = string.Empty;
    public string AssurancePolicyVersion { get; set; } = string.Empty;
    public Guid? ProofReference { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public DateTimeOffset? FirstResponseCommittedAt { get; set; }
    public string ProcessingReference { get; set; } = string.Empty;
    public string RetentionReference { get; set; } = string.Empty;
    public long Version { get; set; } = 1;
    public override string ToString() => nameof(RecoveryCodeOperationRow);
}

internal static class RecoveryCodeModelConfiguration
{
    internal static void Configure(ModelBuilder model)
    {
        model.Entity<RecoveryCodeOperationRow>(b =>
        {
            b.ToTable("recovery_code_operations", "identity", t =>
            {
                t.HasCheckConstraint("ck_recovery_operation", "kind IN ('GENERATE','REPLACE') AND state IN ('PENDING_PROOF','COMPLETED','REJECTED','EXPIRED') AND version>=1 AND expected_version>=0 AND expires_at>created_at");
                t.HasCheckConstraint("ck_recovery_first_response", "(state='COMPLETED' AND completed_at IS NOT NULL AND first_response_committed_at IS NOT NULL AND first_response_committed_at=completed_at AND proof_reference IS NOT NULL) OR (state<>'COMPLETED' AND completed_at IS NULL AND first_response_committed_at IS NULL AND proof_reference IS NULL)");
                t.HasCheckConstraint("ck_recovery_operation_binding", "length(account_id)>0 AND length(origin)>0 AND length(context_digest)=64 AND length(prior_set_plan_digest)=64 AND length(impact_facts_digest)=64 AND length(policy_version)>0 AND length(assurance_policy_version)>0 AND length(processing_reference)>0 AND length(retention_reference)>0");
            });
            b.HasKey(x => x.OperationId); b.Property(x => x.OperationId).ValueGeneratedNever();
            b.HasAlternateKey(x => new { x.AccountId, x.OperationId });
            b.HasAlternateKey(x => new { x.AccountId, x.OperationId, x.SetReference });
            b.HasIndex(x => new { x.SessionId, x.RequestId }).IsUnique(); b.Property(x => x.Version).IsConcurrencyToken();
            b.HasOne<AuthenticationSessionRow>().WithMany().HasForeignKey(x => new { x.SessionId, x.AccountId })
                .HasPrincipalKey(x => new { x.SessionId, x.AccountId }).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<RecoveryCodeSetRow>(b =>
        {
            b.ToTable("recovery_code_sets", "identity", t =>
            {
                t.HasCheckConstraint("ck_recovery_set", "state IN ('ACTIVE','REPLACED') AND lifecycle_version>=1 AND usage_version>=0");
                t.HasCheckConstraint("ck_recovery_set_retirement", "(state='ACTIVE' AND retired_at IS NULL AND retired_by_operation_id IS NULL) OR (state='REPLACED' AND retired_at IS NOT NULL AND retired_at>=created_at AND retired_by_operation_id IS NOT NULL)");
                t.HasCheckConstraint("ck_recovery_set_binding", "length(account_id)>0 AND length(policy_version)>0 AND length(crypto_profile_version)>0 AND length(processing_reference)>0 AND length(retention_reference)>0");
            });
            b.HasKey(x => x.SetReference); b.Property(x => x.SetReference).ValueGeneratedNever();
            b.HasAlternateKey(x => new { x.AccountId, x.SetReference }); b.HasIndex(x => x.IssuanceOperationId).IsUnique();
            b.Property(x => x.LifecycleVersion).IsConcurrencyToken(); b.Property(x => x.UsageVersion).IsConcurrencyToken();
            b.HasOne<AccountRow>().WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<RecoveryCodeOperationRow>().WithMany().HasForeignKey(x => new { x.AccountId, x.IssuanceOperationId, x.SetReference })
                .HasPrincipalKey(x => new { x.AccountId, x.OperationId, x.SetReference }).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<RecoveryCodeOperationRow>().WithMany().HasForeignKey(x => new { x.AccountId, x.RetiredByOperationId })
                .HasPrincipalKey(x => new { x.AccountId, x.OperationId }).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<RecoveryCodeEntryRow>(b =>
        {
            b.ToTable("recovery_code_entries", "identity", t =>
            {
                t.HasCheckConstraint("ck_recovery_verifier", "length(verifier)=84 AND length(crypto_profile_version)>0");
                t.HasCheckConstraint("ck_recovery_consumption", "(consumed_at IS NULL AND consumed_by_operation_id IS NULL AND consumption_correlation_id IS NULL AND version=1) OR (consumed_at IS NOT NULL AND consumed_by_operation_id IS NOT NULL AND consumption_correlation_id IS NOT NULL AND length(consumed_by_operation_id)>0 AND length(consumption_correlation_id)>0 AND version=2)");
            });
            b.HasKey(x => x.EntryReference); b.Property(x => x.EntryReference).ValueGeneratedNever(); b.Property(x => x.Version).IsConcurrencyToken();
            b.HasIndex(x => new { x.AccountId, x.SetReference });
            b.HasOne<RecoveryCodeSetRow>().WithMany().HasForeignKey(x => new { x.AccountId, x.SetReference })
                .HasPrincipalKey(x => new { x.AccountId, x.SetReference }).OnDelete(DeleteBehavior.Restrict);
        });
        IdentityColumnNames.UseSnakeCase(model, typeof(RecoveryCodeOperationRow), typeof(RecoveryCodeSetRow), typeof(RecoveryCodeEntryRow));
    }
}
