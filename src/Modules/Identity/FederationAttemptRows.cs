using Microsoft.EntityFrameworkCore;

namespace Tunner.Identity.Persistence;

internal sealed class FederationAttemptRow
{
    public Guid AttemptId { get; set; }
    public Guid RequestId { get; set; }
    public string CorrelationId { get; set; } = string.Empty;
    public string Provider { get; set; } = string.Empty;
    public string Purpose { get; set; } = string.Empty;
    public Guid BrowserKey { get; set; }
    public string BrowserDigest { get; set; } = string.Empty;
    public string Origin { get; set; } = string.Empty;
    public string PreviousCookieDigest { get; set; } = string.Empty;
    public Guid? PreviousSessionId { get; set; }
    public bool RememberMe { get; set; }
    public string AuthenticationPolicyVersion { get; set; } = string.Empty;
    public string ProviderNamespace { get; set; } = string.Empty;
    public string ClientId { get; set; } = string.Empty;
    public string ProfileVersion { get; set; } = string.Empty;
    public string MappingEvidenceReference { get; set; } = string.Empty;
    public string ProfileSnapshot { get; set; } = string.Empty;
    public string ContinuationPath { get; set; } = string.Empty;
    public string ProcessingReference { get; set; } = string.Empty;
    public string RetentionReference { get; set; } = string.Empty;
    public long ResultRecoveryLifetimeTicks { get; set; }
    public string State { get; set; } = "PREPARED";
    public long Version { get; set; } = 1;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? VerifiedAt { get; set; }
    public DateTimeOffset? VerificationExpiresAt { get; set; }
    public string? ValidatedIssuer { get; set; }
    public Guid? LinkId { get; set; }
    public long? LinkVersion { get; set; }
    public string? LinkQualificationEvidenceReference { get; set; }
    public string? LinkQualificationProfileVersion { get; set; }
    public Guid? ResultSessionId { get; set; }
    public Guid? PendingMfaId { get; set; }
    public string? ErrorCode { get; set; }
    public DateTimeOffset? EndedAt { get; set; }
    public DateTimeOffset? ResultExpiresAt { get; set; }
}

internal static class FederationAttemptModel
{
    internal static void Configure(ModelBuilder model)
    {
        model.Entity<FederationAttemptRow>(b =>
        {
            b.ToTable("federation_attempts", "identity", t =>
            {
                t.HasCheckConstraint("ck_federation_purpose", "provider IN ('Google','Microsoft') AND purpose IN ('SignIn','Registration','Link','OperationProof')");
                t.HasCheckConstraint("ck_federation_state", "state IN ('PREPARED','CHALLENGE_ADMITTED','VERIFIED','COMPLETED','REJECTED','EXPIRED') AND version>=1");
                t.HasCheckConstraint("ck_federation_bounds", "expires_at>created_at AND result_recovery_lifetime_ticks>0 AND length(browser_digest)=64 AND length(previous_cookie_digest)=64");
                t.HasCheckConstraint("ck_federation_profile", "length(provider_namespace)>0 AND length(client_id)>0 AND length(profile_version)>0 AND length(mapping_evidence_reference)>0 AND length(correlation_id)>0 AND length(processing_reference)>0 AND length(retention_reference)>0 AND jsonb_typeof(profile_snapshot::jsonb)='object'");
                t.HasCheckConstraint("ck_federation_verified", "(state NOT IN ('VERIFIED','COMPLETED') AND verified_at IS NULL AND verification_expires_at IS NULL AND validated_issuer IS NULL AND link_id IS NULL AND link_version IS NULL AND link_qualification_evidence_reference IS NULL AND link_qualification_profile_version IS NULL) OR (verified_at IS NOT NULL AND verification_expires_at IS NOT NULL AND validated_issuer IS NOT NULL AND link_id IS NOT NULL AND link_version IS NOT NULL AND link_qualification_evidence_reference IS NOT NULL AND link_qualification_profile_version IS NOT NULL AND verified_at>=created_at AND verified_at<expires_at AND verification_expires_at>verified_at AND verification_expires_at<=expires_at AND length(validated_issuer)>0 AND link_version>=1 AND length(link_qualification_evidence_reference)>0 AND length(link_qualification_profile_version)>0)");
                t.HasCheckConstraint("ck_federation_error", "error_code IS NULL OR error_code IN ('AUTHENTICATION_FAILED','VERSION_CONFLICT','RATE_OR_ABUSE_LIMITED','STEP_UP_REQUIRED')");
                t.HasCheckConstraint("ck_federation_result", "(state IN ('PREPARED','CHALLENGE_ADMITTED','VERIFIED') AND ended_at IS NULL AND result_expires_at IS NULL AND result_session_id IS NULL AND pending_mfa_id IS NULL AND error_code IS NULL) OR (state IN ('COMPLETED','REJECTED','EXPIRED') AND ended_at IS NOT NULL AND result_expires_at IS NOT NULL AND ended_at>=created_at AND result_expires_at>ended_at AND ((state='COMPLETED' AND purpose='SignIn' AND verified_at IS NOT NULL AND error_code IS NULL AND ((result_session_id IS NOT NULL AND pending_mfa_id IS NULL) OR (result_session_id IS NULL AND pending_mfa_id IS NOT NULL))) OR (state IN ('REJECTED','EXPIRED') AND error_code IS NOT NULL AND result_session_id IS NULL AND pending_mfa_id IS NULL)))");
            });
            b.HasKey(x => x.AttemptId); b.Property(x => x.AttemptId).ValueGeneratedNever();
            b.HasIndex(x => new { x.BrowserDigest, x.RequestId }).IsUnique(); b.HasIndex(x => x.ExpiresAt);
            b.Property(x => x.Version).IsConcurrencyToken(); b.Property(x => x.ProfileSnapshot).HasColumnType("text");
            b.HasOne<ExternalIdentityLinkRow>().WithMany().HasForeignKey(x => x.LinkId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<AuthenticationSessionRow>().WithMany().HasForeignKey(x => x.ResultSessionId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<TotpPendingAuthenticationRow>().WithMany().HasForeignKey(x => x.PendingMfaId).OnDelete(DeleteBehavior.Restrict);
        });
        IdentityColumnNames.UseSnakeCase(model, typeof(FederationAttemptRow));
    }
}
