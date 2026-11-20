namespace Tunner.Identity.Persistence;

internal static class PasskeyMigrationSql
{
    internal const string Guards = """
        CREATE OR REPLACE FUNCTION identity.guard_tunner_session() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN
          IF NEW.session_id IS DISTINCT FROM OLD.session_id OR NEW.account_id IS DISTINCT FROM OLD.account_id OR
             NEW.key_digest IS DISTINCT FROM OLD.key_digest OR NEW.browser_digest IS DISTINCT FROM OLD.browser_digest OR
             NEW.idempotency_key IS DISTINCT FROM OLD.idempotency_key OR NEW.identifier_digest IS DISTINCT FROM OLD.identifier_digest OR
             NEW.authenticated_at IS DISTINCT FROM OLD.authenticated_at OR NEW.expires_at IS DISTINCT FROM OLD.expires_at OR
             NEW.scheme IS DISTINCT FROM OLD.scheme OR NEW.origin IS DISTINCT FROM OLD.origin OR
             NEW.security_stamp_digest IS DISTINCT FROM OLD.security_stamp_digest OR NEW.authentication_methods IS DISTINCT FROM OLD.authentication_methods OR
             NEW.passkey_credential_reference IS DISTINCT FROM OLD.passkey_credential_reference OR
             NEW.passkey_policy_version IS DISTINCT FROM OLD.passkey_policy_version OR NEW.passkey_assurance_key IS DISTINCT FROM OLD.passkey_assurance_key OR
             NEW.assurance IS DISTINCT FROM OLD.assurance OR NEW.remember_me IS DISTINCT FROM OLD.remember_me OR NEW.idle_ticks IS DISTINCT FROM OLD.idle_ticks OR
             NEW.version <> OLD.version + 1 OR NEW.last_seen_at < OLD.last_seen_at OR
             (OLD.state <> 'ACTIVE' AND NEW.state IS DISTINCT FROM OLD.state) OR
             (NEW.state = 'ACTIVE' AND (NEW.protected_key IS DISTINCT FROM OLD.protected_key OR NEW.protected_ticket IS DISTINCT FROM OLD.protected_ticket)) OR
             ((NEW.termination_cause IS DISTINCT FROM OLD.termination_cause OR NEW.termination_policy_version IS DISTINCT FROM OLD.termination_policy_version OR
                NEW.termination_credential_version IS DISTINCT FROM OLD.termination_credential_version) AND
                NOT (OLD.state='ACTIVE' AND NEW.state='REVOKED' AND NEW.termination_cause='PASSKEY_REVOKED'))
          THEN RAISE EXCEPTION 'Session identity and expiry are immutable; ended sessions cannot resume' USING ERRCODE='23514'; END IF;
          RETURN NEW;
        END; $$;
        CREATE FUNCTION identity.guard_passkey_credential() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN
          IF (to_jsonb(NEW) - ARRAY['state','revoked_at','last_used_at','revocation_session_effect','revocation_policy_version','revocation_operation_id','revocation_correlation_id','version']) IS DISTINCT FROM
             (to_jsonb(OLD) - ARRAY['state','revoked_at','last_used_at','revocation_session_effect','revocation_policy_version','revocation_operation_id','revocation_correlation_id','version']) OR
             OLD.state<>'ACTIVE' OR NEW.version<>OLD.version+1 OR
             (OLD.last_used_at IS NOT NULL AND (NEW.last_used_at IS NULL OR NEW.last_used_at < OLD.last_used_at)) OR
             (NEW.state='ACTIVE' AND (NEW.revocation_session_effect IS DISTINCT FROM OLD.revocation_session_effect OR NEW.revocation_policy_version IS DISTINCT FROM OLD.revocation_policy_version))
          THEN RAISE EXCEPTION 'Passkey identity and ended evidence are immutable' USING ERRCODE='23514'; END IF;
          RETURN NEW;
        END; $$;
        CREATE TRIGGER passkey_credential_guard BEFORE UPDATE ON identity.passkey_credentials FOR EACH ROW EXECUTE FUNCTION identity.guard_passkey_credential();
        CREATE FUNCTION identity.guard_passkey_ceremony() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN
          IF (to_jsonb(NEW) - ARRAY['state','ended_at','protected_state','options_json','proof_reference','authorized_at','expires_at','credential_reference','account_id','request_digest','result_session_id','eligibility_policy_version','eligibility_evidence_reference','eligibility_from','eligibility_includes_from','eligibility_until','eligibility_includes_until','version']) IS DISTINCT FROM
             (to_jsonb(OLD) - ARRAY['state','ended_at','protected_state','options_json','proof_reference','authorized_at','expires_at','credential_reference','account_id','request_digest','result_session_id','eligibility_policy_version','eligibility_evidence_reference','eligibility_from','eligibility_includes_from','eligibility_until','eligibility_includes_until','version']) OR
             NOT ((OLD.state='PENDING' AND NEW.state='AUTHORIZED' AND NEW.kind='ENROLL') OR
                  (OLD.state='AUTHORIZED' AND NEW.state='COMPLETED' AND NEW.kind='ENROLL') OR
                  (OLD.state='PENDING' AND NEW.state='COMPLETED' AND NEW.kind IN ('SIGN_IN','REVOKE')) OR
                  (OLD.state IN ('PENDING','AUTHORIZED') AND NEW.state IN ('REJECTED','EXPIRED'))) OR
             NEW.version<>OLD.version+1 OR NEW.expires_at>OLD.expires_at OR
             (OLD.account_id IS NOT NULL AND NEW.account_id IS DISTINCT FROM OLD.account_id) OR
             (OLD.credential_reference IS NOT NULL AND NEW.credential_reference IS DISTINCT FROM OLD.credential_reference) OR
             (OLD.proof_reference IS NOT NULL AND NEW.proof_reference IS DISTINCT FROM OLD.proof_reference) OR
             (OLD.authorized_at IS NOT NULL AND NEW.authorized_at IS DISTINCT FROM OLD.authorized_at) OR
             (NEW.state='AUTHORIZED' AND NOT (OLD.state='PENDING' AND NEW.kind='ENROLL' AND NEW.proof_reference IS NOT NULL AND NEW.authorized_at IS NOT NULL)) OR
             (NEW.state='COMPLETED' AND NOT ((NEW.kind='ENROLL' AND OLD.state='AUTHORIZED') OR (NEW.kind IN ('SIGN_IN','REVOKE') AND OLD.state='PENDING')))
          THEN RAISE EXCEPTION 'Passkey operation binding is immutable and one-use' USING ERRCODE='23514'; END IF;
          RETURN NEW;
        END; $$;
        CREATE TRIGGER passkey_ceremony_guard BEFORE UPDATE ON identity.passkey_ceremonies FOR EACH ROW EXECUTE FUNCTION identity.guard_passkey_ceremony();
        """;
    internal static string RestoreSessionGuard => AuthenticationMigrationSql.Guard[..AuthenticationMigrationSql.Guard.IndexOf("CREATE TRIGGER", StringComparison.Ordinal)]
        .Replace("CREATE FUNCTION", "CREATE OR REPLACE FUNCTION", StringComparison.Ordinal);
    internal const string RemovePasskeyGuards = """
        DROP TRIGGER passkey_ceremony_guard ON identity.passkey_ceremonies;
        DROP FUNCTION identity.guard_passkey_ceremony();
        DROP TRIGGER passkey_credential_guard ON identity.passkey_credentials;
        DROP FUNCTION identity.guard_passkey_credential();
        """;
}
