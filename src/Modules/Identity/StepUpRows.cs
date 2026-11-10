using Microsoft.EntityFrameworkCore;

namespace Tunner.Identity.Persistence;

internal sealed class StepUpProofRow
{
    public Guid ProofId { get; set; }
    public Guid SessionId { get; set; }
    public string AccountId { get; set; } = string.Empty;
    public string KeyDigest { get; set; } = string.Empty;
    public string ProtectedKey { get; set; } = string.Empty;
    public string Origin { get; set; } = string.Empty;
    public string Scheme { get; set; } = string.Empty;
    public string SecurityStampDigest { get; set; } = string.Empty;
    public string OperationId { get; set; } = string.Empty;
    public string CorrelationId { get; set; } = string.Empty;
    public string Operation { get; set; } = string.Empty;
    public string Context { get; set; } = string.Empty;
    public string ResourceId { get; set; } = string.Empty;
    public long ExpectedVersion { get; set; }
    public string ContextDigest { get; set; } = string.Empty;
    public string PolicyVersion { get; set; } = string.Empty;
    public string AuthenticationPolicyVersion { get; set; } = string.Empty;
    public string AuthenticationMethod { get; set; } = string.Empty;
    public Guid? PasskeyCredentialReference { get; set; }
    public string? PasskeyPolicyVersion { get; set; }
    public string? PasskeyAssuranceKey { get; set; }
    public Guid? TotpAuthenticatorReference { get; set; }
    public Guid? TotpGeneration { get; set; }
    public long? TotpAuthenticatorVersion { get; set; }
    public string? TotpPolicyVersion { get; set; }
    public string Assurance { get; set; } = string.Empty;
    public string ProcessingReference { get; set; } = string.Empty;
    public string RetentionReference { get; set; } = string.Empty;
    public DateTimeOffset AuthenticatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public string State { get; set; } = "ACTIVE";
    public DateTimeOffset? EndedAt { get; set; }
    public long Version { get; set; } = 1;
}
internal static class StepUpModelConfiguration
{
    internal static void Configure(ModelBuilder model)
    {
        model.Entity<AuthenticationSessionRow>().HasAlternateKey(x => new { x.SessionId, x.AccountId });
        model.Entity<StepUpProofRow>(b =>
        {
            b.ToTable("step_up_proofs", "identity", t =>
            {
                t.HasCheckConstraint("ck_step_up_method", "((authentication_method='pwd' AND passkey_credential_reference IS NULL AND passkey_policy_version IS NULL AND passkey_assurance_key IS NULL) OR (authentication_method LIKE 'passkey:%' AND passkey_credential_reference IS NOT NULL AND passkey_policy_version IS NOT NULL AND passkey_assurance_key IS NOT NULL)) AND totp_authenticator_reference IS NULL AND totp_generation IS NULL AND totp_authenticator_version IS NULL AND totp_policy_version IS NULL OR (authentication_method='totp' AND passkey_credential_reference IS NULL AND passkey_policy_version IS NULL AND passkey_assurance_key IS NULL AND totp_authenticator_reference IS NOT NULL AND totp_generation IS NOT NULL AND totp_authenticator_version>=2 AND totp_policy_version IS NOT NULL)");
                t.HasCheckConstraint("ck_step_up_state", "state IN ('ACTIVE','CONSUMED','EXPIRED')");
                t.HasCheckConstraint("ck_step_up_bounds", "expires_at > authenticated_at AND expected_version >= 0 AND version >= 1");
                t.HasCheckConstraint("ck_step_up_digests", "key_digest ~ '^[0-9A-F]{64}$' AND context_digest ~ '^[0-9A-F]{64}$'");
                t.HasCheckConstraint("ck_step_up_end", "(state='ACTIVE' AND ended_at IS NULL AND length(protected_key)>0) OR (state<>'ACTIVE' AND ended_at IS NOT NULL AND ended_at >= authenticated_at AND protected_key='')");
                t.HasCheckConstraint("ck_step_up_consumed", "state<>'CONSUMED' OR ended_at < expires_at");
            });
            b.HasOne<PasskeyCredentialRow>().WithMany().HasForeignKey(x => x.PasskeyCredentialReference).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<TotpAuthenticatorRow>().WithMany().HasForeignKey(x => x.TotpAuthenticatorReference).OnDelete(DeleteBehavior.Restrict);
            b.HasKey(x => x.ProofId); b.Property(x => x.ProofId).ValueGeneratedNever();
            b.HasIndex(x => x.KeyDigest).IsUnique(); b.HasIndex(x => new { x.SessionId, x.OperationId }).IsUnique();
            b.Property(x => x.Version).IsConcurrencyToken();
            b.HasOne<AuthenticationSessionRow>().WithMany().HasForeignKey(x => new { x.SessionId, x.AccountId })
                .HasPrincipalKey(x => new { x.SessionId, x.AccountId }).OnDelete(DeleteBehavior.Restrict);

        });
        IdentityColumnNames.UseSnakeCase(model, typeof(StepUpProofRow));
    }
    internal const string Guard = """
        CREATE FUNCTION identity.guard_step_up_proof() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN
          IF (to_jsonb(NEW) - ARRAY['state','ended_at','protected_key','version']) IS DISTINCT FROM
             (to_jsonb(OLD) - ARRAY['state','ended_at','protected_key','version']) OR
             OLD.state <> 'ACTIVE' OR NEW.state NOT IN ('CONSUMED','EXPIRED') OR NEW.version <> OLD.version + 1 OR
             (NEW.state='EXPIRED' AND NEW.ended_at < NEW.expires_at)
          THEN RAISE EXCEPTION 'Step-up binding is immutable and evidence cannot be reused' USING ERRCODE='23514'; END IF;
          RETURN NEW;
        END; $$;
        CREATE TRIGGER step_up_proof_guard BEFORE UPDATE ON identity.step_up_proofs FOR EACH ROW EXECUTE FUNCTION identity.guard_step_up_proof();
        """;
    internal const string GuardDowngrade = """
        DROP TRIGGER step_up_proof_guard ON identity.step_up_proofs;
        DROP FUNCTION identity.guard_step_up_proof();
        """;
}
