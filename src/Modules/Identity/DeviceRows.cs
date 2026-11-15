using Microsoft.EntityFrameworkCore;
using Tunner.Database.Outbox;

namespace Tunner.Identity.Persistence;

internal sealed class DeviceRecordRow
{
    public Guid DeviceId { get; set; }
    public string AccountId { get; set; } = string.Empty;
    public string Origin { get; set; } = string.Empty;
    public string CorrelationDigest { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset LastObservedAt { get; set; }
    public string CharacteristicsJson { get; set; } = "{}";
    public string PolicyVersion { get; set; } = string.Empty;
    public string PolicySnapshot { get; set; } = string.Empty;
    public string CollectionReference { get; set; } = string.Empty;
    public string InventoryReference { get; set; } = string.Empty;
    public string ProcessingReference { get; set; } = string.Empty;
    public string RetentionReference { get; set; } = string.Empty;
    public string Classification { get; set; } = string.Empty;
    public long Version { get; set; }
}
internal sealed class DeviceTrustRow
{
    public Guid DeviceId { get; set; }
    public long Version { get; set; }
    public string AccountId { get; set; } = string.Empty;
    public string Origin { get; set; } = string.Empty;
    public long DeviceVersion { get; set; }
    public string State { get; set; } = string.Empty;
    public Guid SourceSessionId { get; set; }
    public Guid? RemovalOperationId { get; set; }
    public string EligibilityFactsJson { get; set; } = string.Empty;
    public string EligibilityDecisionJson { get; set; } = string.Empty;
    public string PolicyVersion { get; set; } = string.Empty;
    public string PolicySnapshot { get; set; } = string.Empty;
    public DateTimeOffset RecordedAt { get; set; }
    public string OperationId { get; set; } = string.Empty;
    public string CorrelationId { get; set; } = string.Empty;
    public Guid OutboxEventId { get; set; }
}
internal sealed class DeviceSessionEvidenceRow
{
    public Guid SessionId { get; set; }
    public string AccountId { get; set; } = string.Empty;
    public string Origin { get; set; } = string.Empty;
    public Guid DeviceId { get; set; }
    public long DeviceVersion { get; set; }
    public long TrustVersion { get; set; }
    public DateTimeOffset ObservedAt { get; set; }
    public string PolicySnapshot { get; set; } = string.Empty;
    public string CharacteristicsJson { get; set; } = "{}";
    public Guid OutboxEventId { get; set; }
}
internal sealed class DeviceOperationRow
{
    public Guid Id { get; set; }
    public Guid RequestId { get; set; }
    public string AccountId { get; set; } = string.Empty;
    public Guid SessionId { get; set; }
    public string Origin { get; set; } = string.Empty;
    public Guid DeviceId { get; set; }
    public long ExpectedDeviceVersion { get; set; }
    public long ExpectedTrustVersion { get; set; }
    public long ExpectedAccountVersion { get; set; }
    public string SecurityStampDigest { get; set; } = string.Empty;
    public string PolicyVersion { get; set; } = string.Empty;
    public string PolicySnapshot { get; set; } = string.Empty;
    public string AssurancePolicyVersion { get; set; } = string.Empty;
    public string ContextDigest { get; set; } = string.Empty;
    public string CorrelationId { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset RecoveryExpiresAt { get; set; }
    public string State { get; set; } = "PENDING";
    public long Version { get; set; } = 1;
    public Guid? ProofId { get; set; }
    public long? ResultTrustVersion { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public Guid? OutboxEventId { get; set; }
}
internal static class DeviceModelConfiguration
{
    internal static void Configure(ModelBuilder model)
    {
        model.Entity<DeviceRecordRow>(b =>
        {
            b.ToTable("device_records", "identity", t =>
            {
                t.HasCheckConstraint("ck_device_record", "version>=1 AND last_observed_at>=created_at AND length(correlation_digest)=64 AND length(origin)>0 AND length(policy_version)>0 AND length(collection_reference)>0 AND length(inventory_reference)>0 AND length(processing_reference)>0 AND length(retention_reference)>0 AND length(classification)>0 AND jsonb_typeof(characteristics_json::jsonb)='object' AND jsonb_typeof(policy_snapshot::jsonb)='object'");
            });
            b.HasKey(x => x.DeviceId); b.Property(x => x.DeviceId).ValueGeneratedNever();
            b.HasAlternateKey(x => new { x.AccountId, x.Origin, x.DeviceId });
            b.HasIndex(x => new { x.AccountId, x.Origin, x.CorrelationDigest }).IsUnique();
            b.Property(x => x.Version).IsConcurrencyToken();
            b.HasOne<AccountRow>().WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<DeviceTrustRow>(b =>
        {
            b.ToTable("device_trust_versions", "identity", t =>
            {
                t.HasCheckConstraint("ck_device_trust", "version>=1 AND device_version>=1 AND length(policy_version)>0 AND length(operation_id)>0 AND length(correlation_id)>0 AND jsonb_typeof(policy_snapshot::jsonb)='object' AND ((state IN ('TRUSTED','UNTRUSTED') AND removal_operation_id IS NULL AND jsonb_typeof(eligibility_facts_json::jsonb)='object' AND jsonb_typeof(eligibility_decision_json::jsonb)='object') OR (state='REVOKED' AND removal_operation_id IS NOT NULL AND version>=2 AND eligibility_facts_json='' AND eligibility_decision_json=''))");
            });
            b.HasKey(x => new { x.DeviceId, x.Version });
            b.HasAlternateKey(x => new { x.AccountId, x.Origin, x.DeviceId, x.Version });
            b.HasOne<DeviceRecordRow>().WithMany().HasForeignKey(x => new { x.AccountId, x.Origin, x.DeviceId })
                .HasPrincipalKey(x => new { x.AccountId, x.Origin, x.DeviceId }).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<AuthenticationSessionRow>().WithMany().HasForeignKey(x => x.SourceSessionId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<DeviceOperationRow>().WithMany().HasForeignKey(x => x.RemovalOperationId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<OutboxMessage>().WithMany().HasForeignKey(x => x.OutboxEventId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<DeviceSessionEvidenceRow>(b =>
        {
            b.ToTable("device_session_evidence", "identity", t =>
            {
                t.HasCheckConstraint("ck_device_session_evidence", "device_version>=1 AND trust_version>=1 AND jsonb_typeof(policy_snapshot::jsonb)='object' AND jsonb_typeof(characteristics_json::jsonb)='object'");
            });
            b.HasKey(x => x.SessionId); b.Property(x => x.SessionId).ValueGeneratedNever();
            b.HasOne<AuthenticationSessionRow>().WithMany().HasForeignKey(x => x.SessionId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<DeviceTrustRow>().WithMany().HasForeignKey(x => new { x.AccountId, x.Origin, x.DeviceId, x.TrustVersion })
                .HasPrincipalKey(x => new { x.AccountId, x.Origin, x.DeviceId, x.Version }).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<OutboxMessage>().WithMany().HasForeignKey(x => x.OutboxEventId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<DeviceOperationRow>(b =>
        {
            b.ToTable("device_operations", "identity", t =>
            {
                t.HasCheckConstraint("ck_device_operation", "expected_device_version>=1 AND expected_trust_version>=1 AND expected_account_version>=1 AND expires_at>created_at AND recovery_expires_at>expires_at AND length(context_digest)=64 AND length(security_stamp_digest)=64 AND length(policy_version)>0 AND length(assurance_policy_version)>0 AND jsonb_typeof(policy_snapshot::jsonb)='object' AND ((state='PENDING' AND version=1 AND proof_id IS NULL AND result_trust_version IS NULL AND completed_at IS NULL AND outbox_event_id IS NULL) OR (state='COMPLETED' AND version=2 AND proof_id IS NOT NULL AND result_trust_version=expected_trust_version+1 AND completed_at>=created_at AND completed_at<expires_at AND outbox_event_id IS NOT NULL))");
            });
            b.HasKey(x => x.Id); b.Property(x => x.Id).ValueGeneratedNever(); b.Property(x => x.Version).IsConcurrencyToken();
            b.HasIndex(x => new { x.SessionId, x.RequestId }).IsUnique();
            b.HasOne<AuthenticationSessionRow>().WithMany().HasForeignKey(x => x.SessionId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<DeviceRecordRow>().WithMany().HasForeignKey(x => new { x.AccountId, x.Origin, x.DeviceId })
                .HasPrincipalKey(x => new { x.AccountId, x.Origin, x.DeviceId }).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<StepUpProofRow>().WithMany().HasForeignKey(x => x.ProofId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<OutboxMessage>().WithMany().HasForeignKey(x => x.OutboxEventId).OnDelete(DeleteBehavior.Restrict);
        });
        IdentityColumnNames.UseSnakeCase(model, typeof(DeviceRecordRow), typeof(DeviceTrustRow), typeof(DeviceSessionEvidenceRow), typeof(DeviceOperationRow));
    }
}
