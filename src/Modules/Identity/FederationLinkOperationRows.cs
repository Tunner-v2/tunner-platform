using Microsoft.EntityFrameworkCore;

namespace Tunner.Identity.Persistence;

internal sealed class FederationLinkOperationRow
{
    public Guid OperationId { get; set; }
    public Guid RequestId { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string AccountId { get; set; } = string.Empty;
    public Guid SessionId { get; set; }
    public string Origin { get; set; } = string.Empty;
    public string Provider { get; set; } = string.Empty;
    public string ProfileVersion { get; set; } = string.Empty;
    public string ProfileSnapshot { get; set; } = string.Empty;
    public Guid? AttemptId { get; set; }
    public Guid? TargetLinkId { get; set; }
    public long ExpectedLinkVersion { get; set; }
    public long ExpectedAccountVersion { get; set; }
    public string PolicyVersion { get; set; } = string.Empty;
    public string PolicySnapshot { get; set; } = string.Empty;
    public string AssurancePolicyVersion { get; set; } = string.Empty;
    public string AuthenticationPolicyVersion { get; set; } = string.Empty;
    public string SecurityStampDigest { get; set; } = string.Empty;
    public string? OriginalCookieDigest { get; set; }
    public string ContextDigest { get; set; } = string.Empty;
    public string SessionEffect { get; set; } = string.Empty;
    public string SessionUsePolicyVersion { get; set; } = string.Empty;
    public string SessionEffectEvidenceReference { get; set; } = string.Empty;
    public string ProcessingReference { get; set; } = string.Empty;
    public string RetentionReference { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public long Version { get; set; } = 1;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset RecoveryExpiresAt { get; set; }
    public DateTimeOffset? ConfirmedAt { get; set; }
    public DateTimeOffset? EndedAt { get; set; }
    public Guid? ProofReference { get; set; }
    public Guid? ResultLinkId { get; set; }
    public long? ResultLinkVersion { get; set; }
    public string? PostureFactsDigest { get; set; }
    public string? PosturePolicyVersion { get; set; }
    public string? PostureEvidenceReference { get; set; }
    public DateTimeOffset? PostureFrom { get; set; }
    public bool? PostureIncludesFrom { get; set; }
    public DateTimeOffset? PostureUntil { get; set; }
    public bool? PostureIncludesUntil { get; set; }
    public string? ErrorCode { get; set; }
    public override string ToString() => nameof(FederationLinkOperationRow);
}

internal static class FederationLinkOperationModel
{
    internal static void Configure(ModelBuilder model)
    {
        model.Entity<FederationLinkOperationRow>(b =>
        {
            b.ToTable("federation_link_operations", "identity", t =>
            {
                t.HasCheckConstraint("ck_federation_management_kind", "kind IN ('LINK','UNLINK') AND provider IN ('Google','Microsoft') AND ((kind='LINK' AND attempt_id IS NOT NULL) OR (kind='UNLINK' AND attempt_id IS NULL AND target_link_id IS NOT NULL AND expected_link_version>=1))");
                t.HasCheckConstraint("ck_federation_management_state", "state IN ('PENDING_PROVIDER','PENDING_CONFIRMATION','PENDING_PROOF','COMPLETED','REJECTED','EXPIRED') AND version>=1 AND expected_link_version>=0 AND expected_account_version>=1");
                t.HasCheckConstraint("ck_federation_management_binding", "length(context_digest)=64 AND length(security_stamp_digest)=64 AND length(policy_version)>0 AND length(profile_version)>0 AND length(assurance_policy_version)>0 AND length(authentication_policy_version)>0 AND jsonb_typeof(policy_snapshot::jsonb)='object' AND jsonb_typeof(profile_snapshot::jsonb)='object' AND length(processing_reference)>0 AND length(retention_reference)>0 AND session_effect IN ('PreserveExistingSessions','RevokeLinkedSessions') AND length(session_use_policy_version)>0 AND length(session_effect_evidence_reference)>0");
                t.HasCheckConstraint("ck_federation_management_time", "expires_at>created_at AND recovery_expires_at>expires_at AND (confirmed_at IS NULL OR (confirmed_at>=created_at AND confirmed_at<expires_at)) AND ((state IN ('PENDING_PROVIDER','PENDING_CONFIRMATION','PENDING_PROOF') AND ended_at IS NULL AND proof_reference IS NULL AND result_link_id IS NULL AND result_link_version IS NULL AND error_code IS NULL) OR (state='COMPLETED' AND confirmed_at IS NOT NULL AND ended_at>=confirmed_at AND proof_reference IS NOT NULL AND result_link_id IS NOT NULL AND result_link_version>=1 AND error_code IS NULL) OR (state IN ('REJECTED','EXPIRED') AND ended_at>=created_at AND error_code IN ('AUTHENTICATION_FAILED','VERSION_CONFLICT','RATE_OR_ABUSE_LIMITED','STEP_UP_REQUIRED') AND proof_reference IS NULL AND result_link_id IS NULL AND result_link_version IS NULL))");
                t.HasCheckConstraint("ck_federation_management_confirmation", "state NOT IN ('PENDING_PROOF','COMPLETED') OR confirmed_at IS NOT NULL");
                t.HasCheckConstraint("ck_federation_management_cookie", "original_cookie_digest IS NULL OR length(original_cookie_digest)=64");
                t.HasCheckConstraint("ck_federation_management_posture", "(kind='UNLINK' AND state='COMPLETED' AND posture_facts_digest IS NOT NULL AND length(posture_facts_digest)=64 AND posture_policy_version IS NOT NULL AND length(posture_policy_version)>0 AND posture_evidence_reference IS NOT NULL AND length(posture_evidence_reference)>0 AND posture_from IS NOT NULL AND posture_includes_from IS NOT NULL AND posture_includes_until IS NOT NULL AND (posture_until IS NULL OR posture_until>=posture_from)) OR (NOT(kind='UNLINK' AND state='COMPLETED') AND posture_facts_digest IS NULL AND posture_policy_version IS NULL AND posture_evidence_reference IS NULL AND posture_from IS NULL AND posture_includes_from IS NULL AND posture_until IS NULL AND posture_includes_until IS NULL)");
            });
            b.HasKey(x => x.OperationId); b.Property(x => x.OperationId).ValueGeneratedNever();
            b.HasIndex(x => new { x.SessionId, x.RequestId }).IsUnique(); b.HasIndex(x => x.AttemptId).IsUnique(); b.HasIndex(x => x.ExpiresAt);
            b.Property(x => x.Version).IsConcurrencyToken(); b.Property(x => x.PolicySnapshot).HasColumnType("text"); b.Property(x => x.ProfileSnapshot).HasColumnType("text");
            b.HasOne<FederationAttemptRow>().WithMany().HasForeignKey(x => x.AttemptId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<AuthenticationSessionRow>().WithMany().HasForeignKey(x => x.SessionId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<ExternalIdentityLinkRow>().WithMany().HasForeignKey(x => x.TargetLinkId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<ExternalIdentityLinkRow>().WithMany().HasForeignKey(x => x.ResultLinkId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<StepUpProofRow>().WithMany().HasForeignKey(x => x.ProofReference).OnDelete(DeleteBehavior.Restrict);
        });
        IdentityColumnNames.UseSnakeCase(model, typeof(FederationLinkOperationRow));
    }
}
