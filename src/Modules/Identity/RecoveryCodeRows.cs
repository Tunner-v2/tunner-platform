using Microsoft.EntityFrameworkCore;
using System.Text.Json.Serialization;
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
    [JsonIgnore] public string Verifier { get; set; } = string.Empty;
    public string CryptoProfileVersion { get; set; } = string.Empty;
    public DateTimeOffset? ConsumedAt { get; set; }
    public string? ConsumedByOperationId { get; set; }
    public string? ConsumptionCorrelationId { get; set; }
    public string? ConsumptionContextDigest { get; set; }
    public string? ConsumptionPolicyVersion { get; set; }
    public string? ConsumptionOwnerPolicyVersion { get; set; }
    public string? ConsumptionOutcomeReference { get; set; }
    public long? ConsumptionSetLifecycleVersion { get; set; }
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
    public string PolicyDigest { get; set; } = string.Empty;
    public string AuthenticationPolicyVersion { get; set; } = string.Empty;
    public string SecurityStampDigest { get; set; } = string.Empty;
    public string CryptoProfileVersion { get; set; } = string.Empty;
    public string FormatProfileVersion { get; set; } = string.Empty;
    public string DeliveryProfileVersion { get; set; } = string.Empty;
    public int CodesPerSet { get; set; }
    public int MaximumCodesPerSet { get; set; }
    public string RetirementPlanJson { get; set; } = string.Empty;
    public int PriorSetCount { get; set; }
    public int PriorRemainingCodeCount { get; set; }
    public string PublishedImpactKey { get; set; } = string.Empty;
    public string PublicationDigest { get; set; } = string.Empty;
    public string PublicationVersion { get; set; } = string.Empty;
    public DateTimeOffset PublicationFrom { get; set; }
    public bool PublicationIncludesFrom { get; set; }
    public DateTimeOffset? PublicationUntil { get; set; }
    public bool PublicationIncludesUntil { get; set; }
    public string AdmissionPolicyVersion { get; set; } = string.Empty;
    public string AdmissionEvidenceReference { get; set; } = string.Empty;
    public DateTimeOffset AdmissionFrom { get; set; }
    public bool AdmissionIncludesFrom { get; set; }
    public DateTimeOffset? AdmissionUntil { get; set; }
    public bool AdmissionIncludesUntil { get; set; }
    public string AssurancePolicyVersion { get; set; } = string.Empty;
    public Guid? ProofReference { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset ResultRecoveryExpiresAt { get; set; }
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
                t.HasCheckConstraint("ck_recovery_operation", "kind IN ('GENERATE','REPLACE') AND state IN ('PENDING_PROOF','COMPLETED','REJECTED','EXPIRED') AND version>=1 AND expected_version>0 AND expected_version<=9007199254740991 AND expires_at>created_at AND result_recovery_expires_at>expires_at AND codes_per_set>0 AND maximum_codes_per_set>=codes_per_set AND prior_set_count>=0 AND prior_remaining_code_count>=0");
                t.HasCheckConstraint("ck_recovery_first_response", "(state='COMPLETED' AND completed_at IS NOT NULL AND first_response_committed_at IS NOT NULL AND first_response_committed_at=completed_at AND proof_reference IS NOT NULL) OR (state<>'COMPLETED' AND completed_at IS NULL AND first_response_committed_at IS NULL AND proof_reference IS NULL)");
                t.HasCheckConstraint("ck_recovery_operation_binding", "length(account_id)>0 AND length(origin)>0 AND length(context_digest)=64 AND length(prior_set_plan_digest)=64 AND length(impact_facts_digest)=64 AND length(policy_version)>0 AND length(assurance_policy_version)>0 AND length(processing_reference)>0 AND length(retention_reference)>0");
                t.HasCheckConstraint("ck_recovery_operation_profile", "length(policy_digest)=64 AND length(security_stamp_digest)=64 AND length(authentication_policy_version)>0 AND length(crypto_profile_version)>0 AND format_profile_version='NATIVE_HEX32_V1' AND length(delivery_profile_version)>0 AND length(retirement_plan_json)>0 AND length(published_impact_key)>0 AND length(publication_digest)=64 AND length(publication_version)>0 AND length(admission_policy_version)>0 AND length(admission_evidence_reference)>0");
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
                t.HasCheckConstraint("ck_recovery_consumption", "(consumed_at IS NULL AND consumed_by_operation_id IS NULL AND consumption_correlation_id IS NULL AND consumption_context_digest IS NULL AND consumption_policy_version IS NULL AND consumption_owner_policy_version IS NULL AND consumption_outcome_reference IS NULL AND consumption_set_lifecycle_version IS NULL AND version=1) OR (consumed_at IS NOT NULL AND consumed_by_operation_id IS NOT NULL AND consumption_correlation_id IS NOT NULL AND consumption_context_digest IS NOT NULL AND consumption_policy_version IS NOT NULL AND consumption_owner_policy_version IS NOT NULL AND consumption_outcome_reference IS NOT NULL AND consumption_set_lifecycle_version IS NOT NULL AND consumption_set_lifecycle_version>=1 AND length(consumed_by_operation_id)>0 AND length(consumption_correlation_id)>0 AND length(consumption_context_digest)=64 AND length(consumption_policy_version)>0 AND length(consumption_owner_policy_version)>0 AND length(consumption_outcome_reference)>0 AND version=2)");
            });
            b.HasKey(x => x.EntryReference); b.Property(x => x.EntryReference).ValueGeneratedNever(); b.Property(x => x.Version).IsConcurrencyToken();
            b.HasIndex(x => new { x.AccountId, x.SetReference });
            b.HasIndex(x => new { x.AccountId, x.ConsumedByOperationId }).IsUnique();
            b.HasOne<RecoveryCodeSetRow>().WithMany().HasForeignKey(x => new { x.AccountId, x.SetReference })
                .HasPrincipalKey(x => new { x.AccountId, x.SetReference }).OnDelete(DeleteBehavior.Restrict);
        });
        IdentityColumnNames.UseSnakeCase(model, typeof(RecoveryCodeOperationRow), typeof(RecoveryCodeSetRow), typeof(RecoveryCodeEntryRow));
    }
}
