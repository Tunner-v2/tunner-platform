using Microsoft.EntityFrameworkCore;

namespace Tunner.Identity.Persistence;

internal sealed class PasskeyCredentialRow
{
    public Guid CredentialReference { get; set; }
    public string AccountId { get; set; } = string.Empty;
    public byte[] CredentialId { get; set; } = [];
    public string CredentialDigest { get; set; } = string.Empty;
    public string State { get; set; } = "ACTIVE";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public DateTimeOffset? LastUsedAt { get; set; }
    public string? RevocationSessionEffect { get; set; }
    public string? RevocationPolicyVersion { get; set; }
    public string? RevocationOperationId { get; set; }
    public string? RevocationCorrelationId { get; set; }
    public string PolicyVersion { get; set; } = string.Empty;
    public string ProcessingReference { get; set; } = string.Empty;
    public string RetentionReference { get; set; } = string.Empty;
    public long Version { get; set; } = 1;
}
internal sealed class PasskeyCeremonyRow
{
    public Guid CeremonyId { get; set; }
    public Guid RequestId { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string BrowserDigest { get; set; } = string.Empty;
    public string? AccountId { get; set; }
    public Guid? SessionId { get; set; }
    public string Origin { get; set; } = string.Empty;
    public string PolicyVersion { get; set; } = string.Empty;
    public string AuthenticationPolicyVersion { get; set; } = string.Empty;
    public string SecurityStampDigest { get; set; } = string.Empty;
    public string ProtectedState { get; set; } = string.Empty;
    public Guid? ProofReference { get; set; }
    public string AssurancePolicyVersion { get; set; } = string.Empty;
    public DateTimeOffset? AuthorizedAt { get; set; }
    public string OptionsJson { get; set; } = string.Empty;
    public string RequestDigest { get; set; } = string.Empty;
    public string? EligibilityPolicyVersion { get; set; }
    public string? EligibilityEvidenceReference { get; set; }
    public DateTimeOffset? EligibilityFrom { get; set; }
    public bool? EligibilityIncludesFrom { get; set; }
    public DateTimeOffset? EligibilityUntil { get; set; }
    public bool? EligibilityIncludesUntil { get; set; }
    public bool RememberMe { get; set; }
    public Guid? PreviousSessionId { get; set; }
    public Guid? ResultSessionId { get; set; }
    public Guid? CredentialReference { get; set; }
    public long ExpectedVersion { get; set; }
    public string State { get; set; } = "PENDING";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? EndedAt { get; set; }
    public DateTimeOffset RecoveryExpiresAt { get; set; }
    public string ProcessingReference { get; set; } = string.Empty;
    public string RetentionReference { get; set; } = string.Empty;
    public long Version { get; set; } = 1;
}
internal static class PasskeyModelConfiguration
{
    internal static void Configure(ModelBuilder model)
    {
        model.Entity<PasskeyCredentialRow>(b =>
        {
            b.ToTable("passkey_credentials", "identity", t =>
            {
                t.HasCheckConstraint("ck_passkey_state", "state IN ('ACTIVE','REVOKED') AND version >= 1");
                t.HasCheckConstraint("ck_passkey_public_id", "octet_length(credential_id) BETWEEN 1 AND 1023 AND length(credential_digest)=64");
                t.HasCheckConstraint("ck_passkey_revoked", "(state='ACTIVE' AND revoked_at IS NULL AND revocation_session_effect IS NULL AND revocation_policy_version IS NULL AND revocation_operation_id IS NULL AND revocation_correlation_id IS NULL) OR (state='REVOKED' AND revoked_at IS NOT NULL AND revoked_at >= created_at AND revocation_session_effect IS NOT NULL AND revocation_session_effect IN ('PreserveExistingSessions','RevokeCredentialSessions') AND revocation_policy_version IS NOT NULL AND revocation_operation_id IS NOT NULL AND revocation_correlation_id IS NOT NULL)");
            });
            b.HasKey(x => x.CredentialReference); b.Property(x => x.CredentialReference).ValueGeneratedNever();
            b.HasIndex(x => x.CredentialDigest).IsUnique(); b.HasIndex(x => x.AccountId); b.Property(x => x.Version).IsConcurrencyToken();
            b.HasOne<AccountRow>().WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<AuthenticationSessionRow>().HasOne<PasskeyCredentialRow>().WithMany().HasForeignKey(x => x.PasskeyCredentialReference).OnDelete(DeleteBehavior.Restrict);
        model.Entity<PasskeyCeremonyRow>(b =>
        {
            b.ToTable("passkey_ceremonies", "identity", t =>
            {
                t.HasCheckConstraint("ck_passkey_eligibility_evidence", "kind<>'REVOKE' OR state<>'COMPLETED' OR (eligibility_policy_version IS NOT NULL AND eligibility_evidence_reference IS NOT NULL AND eligibility_from IS NOT NULL AND eligibility_includes_from IS NOT NULL AND eligibility_includes_until IS NOT NULL)");
                t.HasCheckConstraint("ck_passkey_ceremony_state", "state IN ('PENDING','AUTHORIZED','COMPLETED','REJECTED','EXPIRED') AND kind IN ('ENROLL','SIGN_IN','REVOKE')");
                t.HasCheckConstraint("ck_passkey_ceremony_bounds", "expires_at > created_at AND recovery_expires_at > created_at AND version >= 1 AND expected_version >= 0 AND length(browser_digest)=64");
                t.HasCheckConstraint("ck_passkey_ceremony_end", "(state IN ('PENDING','AUTHORIZED') AND ended_at IS NULL) OR (state NOT IN ('PENDING','AUTHORIZED') AND ended_at IS NOT NULL AND ended_at >= created_at AND protected_state='')");
            });
            b.HasKey(x => x.CeremonyId); b.Property(x => x.CeremonyId).ValueGeneratedNever();
            b.HasIndex(x => new { x.BrowserDigest, x.RequestId, x.Kind }).IsUnique(); b.HasIndex(x => x.ExpiresAt);
            b.Property(x => x.Version).IsConcurrencyToken();
            b.HasOne<AccountRow>().WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Restrict);
        });
        IdentityColumnNames.UseSnakeCase(model, typeof(PasskeyCredentialRow), typeof(PasskeyCeremonyRow));
    }
}
