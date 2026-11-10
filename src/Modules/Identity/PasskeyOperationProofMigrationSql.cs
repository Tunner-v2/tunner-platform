namespace Tunner.Identity.Persistence;

internal static class PasskeyOperationProofMigrationSql
{
    internal const string Guards = """
        CREATE OR REPLACE FUNCTION identity.guard_passkey_credential() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN
          IF (to_jsonb(NEW) - ARRAY['state','revoked_at','last_used_at','revocation_session_effect','revocation_policy_version','revocation_operation_id','revocation_correlation_id','version','usage_version']) IS DISTINCT FROM
             (to_jsonb(OLD) - ARRAY['state','revoked_at','last_used_at','revocation_session_effect','revocation_policy_version','revocation_operation_id','revocation_correlation_id','version','usage_version']) OR
             OLD.state<>'ACTIVE' OR NOT (
               (NEW.state='ACTIVE' AND NEW.version=OLD.version AND NEW.usage_version=OLD.usage_version+1 AND NEW.last_used_at IS NOT NULL AND
                 (OLD.last_used_at IS NULL OR NEW.last_used_at>=OLD.last_used_at)) OR
               (NEW.state='REVOKED' AND NEW.version=OLD.version+1 AND NEW.usage_version=OLD.usage_version AND NEW.last_used_at IS NOT DISTINCT FROM OLD.last_used_at))
          THEN RAISE EXCEPTION 'Passkey lifecycle is immutable; usage advances independently and ended evidence cannot resume' USING ERRCODE='23514'; END IF;
          RETURN NEW;
        END; $$;
        CREATE OR REPLACE FUNCTION identity.guard_passkey_ceremony() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN
          IF (to_jsonb(NEW) - ARRAY['state','ended_at','protected_state','options_json','proof_reference','authorized_at','expires_at','credential_reference','account_id','request_digest','result_session_id','eligibility_policy_version','eligibility_evidence_reference','eligibility_from','eligibility_includes_from','eligibility_until','eligibility_includes_until','version']) IS DISTINCT FROM
             (to_jsonb(OLD) - ARRAY['state','ended_at','protected_state','options_json','proof_reference','authorized_at','expires_at','credential_reference','account_id','request_digest','result_session_id','eligibility_policy_version','eligibility_evidence_reference','eligibility_from','eligibility_includes_from','eligibility_until','eligibility_includes_until','version']) OR
             NOT ((OLD.state='PENDING' AND NEW.state='AUTHORIZED' AND NEW.kind='ENROLL') OR
                  (OLD.state='AUTHORIZED' AND NEW.state='COMPLETED' AND NEW.kind='ENROLL') OR
                  (OLD.state='PENDING' AND NEW.state='COMPLETED' AND NEW.kind IN ('SIGN_IN','REVOKE','STEP_UP')) OR
                  (OLD.state IN ('PENDING','AUTHORIZED') AND NEW.state IN ('REJECTED','EXPIRED'))) OR
             NEW.version<>OLD.version+1 OR NEW.expires_at>OLD.expires_at OR
             (OLD.account_id IS NOT NULL AND NEW.account_id IS DISTINCT FROM OLD.account_id) OR
             (OLD.credential_reference IS NOT NULL AND NEW.credential_reference IS DISTINCT FROM OLD.credential_reference) OR
             (OLD.proof_reference IS NOT NULL AND NEW.proof_reference IS DISTINCT FROM OLD.proof_reference) OR
             (OLD.authorized_at IS NOT NULL AND NEW.authorized_at IS DISTINCT FROM OLD.authorized_at) OR
             (NEW.state='AUTHORIZED' AND NOT (NEW.proof_reference IS NOT NULL AND NEW.authorized_at IS NOT NULL))
          THEN RAISE EXCEPTION 'Passkey operation binding is immutable and one-use' USING ERRCODE='23514'; END IF;
          RETURN NEW;
        END; $$;
        """;
    internal static string RestorePrimaryGuards => Function("guard_passkey_credential") + Function("guard_passkey_ceremony");
    private static string Function(string name)
    {
        var start = PasskeyMigrationSql.Guards.IndexOf("CREATE FUNCTION identity." + name + "()", StringComparison.Ordinal);
        var end = PasskeyMigrationSql.Guards.IndexOf("CREATE TRIGGER", start, StringComparison.Ordinal);
        return PasskeyMigrationSql.Guards[start..end].Replace("CREATE FUNCTION", "CREATE OR REPLACE FUNCTION", StringComparison.Ordinal);
    }
}
