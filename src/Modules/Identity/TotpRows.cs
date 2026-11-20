using Microsoft.EntityFrameworkCore;

namespace Tunner.Identity.Persistence;

internal sealed class TotpAuthenticatorRow
{
    public Guid AuthenticatorReference { get; set; }
    public string AccountId { get; set; } = string.Empty;
    public Guid Generation { get; set; }
    public Guid EnrollmentId { get; set; }
    public string SecretStoreReference { get; set; } = string.Empty;
    public string SecretResourceReference { get; set; } = string.Empty;
    public long SecretVersion { get; set; }
    public string SecretBindingVersion { get; set; } = string.Empty;
    public string State { get; set; } = "INACTIVE";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ActivatedAt { get; set; }
    public DateTimeOffset? DisabledAt { get; set; }
    public DateTimeOffset? LastUsedAt { get; set; }
    public DateTimeOffset? ClockHighWaterAt { get; set; }
    public string? DisableSessionEffect { get; set; }
    public string? DisablePolicyVersion { get; set; }
    public string? DisableOperationId { get; set; }
    public string? DisableCorrelationId { get; set; }
    public string PolicyVersion { get; set; } = string.Empty;
    public string ProcessingReference { get; set; } = string.Empty;
    public string RetentionReference { get; set; } = string.Empty;
    public long Version { get; set; } = 1;
    public long UsageVersion { get; set; }
    internal TotpSeedBinding Binding => new(AccountId, AuthenticatorReference, Generation, EnrollmentId, SecretBindingVersion);
    internal TotpSeedReference Reference => new(SecretStoreReference, SecretResourceReference, SecretVersion);
}
internal sealed class TotpOperationRow
{
    public Guid EnrollmentId { get; set; }
    public Guid RequestId { get; set; }
    public string AccountId { get; set; } = string.Empty;
    public Guid SessionId { get; set; }
    public string Origin { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public Guid AuthenticatorReference { get; set; }
    public Guid Generation { get; set; }
    public long ExpectedVersion { get; set; }
    public string State { get; set; } = "PENDING_PROOF";
    public string PolicyVersion { get; set; } = string.Empty;
    public string AuthenticationPolicyVersion { get; set; } = string.Empty;
    public string SecurityStampDigest { get; set; } = string.Empty;
    public string AssurancePolicyVersion { get; set; } = string.Empty;
    public Guid? ProofReference { get; set; }
    public DateTimeOffset? AuthorizedAt { get; set; }
    public DateTimeOffset? DisclosureClaimedAt { get; set; }
    public Guid? ActivationRequestId { get; set; }
    public string? EligibilityPolicyVersion { get; set; }
    public string? EligibilityEvidenceReference { get; set; }
    public DateTimeOffset? EligibilityFrom { get; set; }
    public bool? EligibilityIncludesFrom { get; set; }
    public DateTimeOffset? EligibilityUntil { get; set; }
    public bool? EligibilityIncludesUntil { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset RecoveryExpiresAt { get; set; }
    public DateTimeOffset? EndedAt { get; set; }
    public string ProcessingReference { get; set; } = string.Empty;
    public string RetentionReference { get; set; } = string.Empty;
    public long Version { get; set; } = 1;
}
internal sealed class TotpUsedCodeRow
{
    public Guid ClaimId { get; set; }
    public Guid AuthenticatorReference { get; set; }
    public Guid Generation { get; set; }
    public string Verifier { get; set; } = string.Empty;
    public DateTimeOffset AcceptedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public string PolicyVersion { get; set; } = string.Empty;
}
internal sealed class TotpProofAttemptRow
{
    public Guid AttemptId { get; set; }
    public Guid SessionId { get; set; }
    public string AccountId { get; set; } = string.Empty;
    public string OperationId { get; set; } = string.Empty;
    public string ContextDigest { get; set; } = string.Empty;
    public Guid AuthenticatorReference { get; set; }
    public Guid Generation { get; set; }
    public Guid ProofId { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
}
internal sealed class TotpPendingAuthenticationRow
{
    public Guid PendingId { get; set; }
    public Guid RequestId { get; set; }
    public string BrowserDigest { get; set; } = string.Empty;
    public string AccountId { get; set; } = string.Empty;
    public string Origin { get; set; } = string.Empty;
    public bool RememberMe { get; set; }
    public Guid? PreviousSessionId { get; set; }
    public string IdentifierDigest { get; set; } = string.Empty;
    public string PrimaryMethod { get; set; } = string.Empty;
    public string PrimaryAssurance { get; set; } = string.Empty;
    public string PrimarySourceDigest { get; set; } = string.Empty;
    public Guid? PasskeyCredentialReference { get; set; }
    public long? PasskeyCredentialVersion { get; set; }
    public string? PasskeyPolicyVersion { get; set; }
    public string? PasskeyAssuranceKey { get; set; }
    public Guid? FederationAttemptId { get; set; }
    public string? FederationProvider { get; set; }
    public Guid? FederationLinkId { get; set; }
    public long? FederationLinkVersion { get; set; }
    public string? FederationNamespace { get; set; }
    public string? FederationClientId { get; set; }
    public string? FederationIssuer { get; set; }
    public string? FederationProfileVersion { get; set; }
    public string? FederationIssuerMappingEvidenceReference { get; set; }
    public string? FederationQualificationProfileVersion { get; set; }
    public string? FederationQualificationEvidenceReference { get; set; }
    public string? FederationSignInAdmissionPolicyVersion { get; set; }
    public string? FederationAssuranceMappingEvidenceReference { get; set; }
    public string? FederationSessionUsePolicyVersion { get; set; }
    public string SecurityStampDigest { get; set; } = string.Empty;
    public string AuthenticationPolicyVersion { get; set; } = string.Empty;
    public string TotpPolicyVersion { get; set; } = string.Empty;
    public string RequirementPolicyVersion { get; set; } = string.Empty;
    public string RequirementEvidenceReference { get; set; } = string.Empty;
    public DateTimeOffset PrimaryVerifiedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset RecoveryExpiresAt { get; set; }
    public string State { get; set; } = "PENDING";
    public Guid? ResultSessionId { get; set; }
    public Guid? AcceptedAuthenticatorReference { get; set; }
    public Guid? AcceptedGeneration { get; set; }
    public long Version { get; set; } = 1;
}
internal static class TotpModelConfiguration
{
    internal static void Configure(ModelBuilder model)
    {
        model.Entity<TotpAuthenticatorRow>(b =>
        {
            b.ToTable("totp_authenticators", "identity", t =>
            {
                t.HasCheckConstraint("ck_totp_state", "state IN ('INACTIVE','ACTIVE','DISABLED') AND version>=1 AND usage_version>=0 AND secret_version=1");
                t.HasCheckConstraint("ck_totp_lifecycle", "(state='INACTIVE' AND activated_at IS NULL AND disabled_at IS NULL) OR (state='ACTIVE' AND activated_at>=created_at AND disabled_at IS NULL) OR (state='DISABLED' AND disabled_at>=created_at AND disable_session_effect IN ('PreserveExistingSessions','RevokeFactorSessions') AND disable_policy_version IS NOT NULL AND disable_operation_id IS NOT NULL AND disable_correlation_id IS NOT NULL)");
            });
            b.HasKey(x => x.AuthenticatorReference); b.Property(x => x.AuthenticatorReference).ValueGeneratedNever();
            b.HasIndex(x => x.Generation).IsUnique(); b.HasIndex(x => new { x.SecretStoreReference, x.SecretResourceReference, x.SecretVersion }).IsUnique();
            b.Property(x => x.Version).IsConcurrencyToken(); b.Property(x => x.UsageVersion).IsConcurrencyToken();
            b.HasOne<AccountRow>().WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<TotpOperationRow>(b =>
        {
            b.ToTable("totp_operations", "identity", t =>
            {
                t.HasCheckConstraint("ck_totp_operation", "kind IN ('ENROLL','DISABLE','RESET') AND state IN ('PENDING_PROOF','AUTHORIZED','PREPARING','AWAITING_FIRST_CODE','COMPLETED','EXPIRED','REJECTED') AND version>=1 AND expected_version>=0 AND expires_at>created_at AND recovery_expires_at>expires_at");
                t.HasCheckConstraint("ck_totp_disclosure", "disclosure_claimed_at IS NULL OR (kind='ENROLL' AND proof_reference IS NOT NULL AND authorized_at IS NOT NULL)");
            });
            b.HasKey(x => x.EnrollmentId); b.Property(x => x.EnrollmentId).ValueGeneratedNever();
            b.HasIndex(x => new { x.SessionId, x.RequestId }).IsUnique(); b.Property(x => x.Version).IsConcurrencyToken();
            b.HasOne<AuthenticationSessionRow>().WithMany().HasForeignKey(x => new { x.SessionId, x.AccountId }).HasPrincipalKey(x => new { x.SessionId, x.AccountId }).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<TotpUsedCodeRow>(b =>
        {
            b.ToTable("totp_used_codes", "identity", t => t.HasCheckConstraint("ck_totp_replay_bounds", "expires_at>accepted_at AND length(verifier)>0"));
            b.HasKey(x => x.ClaimId); b.Property(x => x.ClaimId).ValueGeneratedNever();
            b.HasIndex(x => new { x.AuthenticatorReference, x.Generation, x.ExpiresAt });
            b.HasOne<TotpAuthenticatorRow>().WithMany().HasForeignKey(x => x.AuthenticatorReference).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<TotpProofAttemptRow>(b =>
        {
            b.ToTable("totp_proof_attempts", "identity"); b.HasKey(x => new { x.SessionId, x.AttemptId });
            b.HasOne<StepUpProofRow>().WithMany().HasForeignKey(x => x.ProofId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<TotpPendingAuthenticationRow>(b =>
        {
            b.ToTable("totp_pending_authentication", "identity", t =>
            {
                t.HasCheckConstraint("ck_totp_pending", "state IN ('PENDING','COMPLETED','EXPIRED') AND primary_method IN ('pwd','passkey','federation') AND expires_at>primary_verified_at AND recovery_expires_at>expires_at AND version>=1");
                t.HasCheckConstraint("ck_totp_pending_federation", FederationAuthenticationSource.PendingShapeConstraint);
                t.HasCheckConstraint("ck_totp_pending_result", "(state='COMPLETED' AND result_session_id IS NOT NULL AND accepted_authenticator_reference IS NOT NULL AND accepted_generation IS NOT NULL) OR (state<>'COMPLETED' AND result_session_id IS NULL AND accepted_authenticator_reference IS NULL AND accepted_generation IS NULL)");
            });
            b.HasKey(x => x.PendingId); b.Property(x => x.PendingId).ValueGeneratedNever(); b.HasIndex(x => new { x.BrowserDigest, x.RequestId }).IsUnique();
            b.Property(x => x.Version).IsConcurrencyToken(); b.HasOne<AccountRow>().WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<ExternalIdentityLinkRow>().WithMany().HasForeignKey(x => new { x.AccountId, x.FederationLinkId })
                .HasPrincipalKey(x => new { x.AccountId, x.LinkId }).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<FederationAttemptRow>().WithMany().HasForeignKey(x => x.FederationAttemptId).OnDelete(DeleteBehavior.Restrict);
        });
        IdentityColumnNames.UseSnakeCase(model, typeof(TotpAuthenticatorRow), typeof(TotpOperationRow), typeof(TotpUsedCodeRow), typeof(TotpProofAttemptRow), typeof(TotpPendingAuthenticationRow));
    }
}
