namespace Tunner.Identity.Persistence;

internal static class TotpMigrationSql
{
    internal const string Guards = """
        CREATE FUNCTION identity.guard_totp_authenticator() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN
          IF (to_jsonb(NEW)-ARRAY['state','activated_at','disabled_at','last_used_at','clock_high_water_at','disable_session_effect','disable_policy_version','disable_operation_id','disable_correlation_id','version','usage_version']) IS DISTINCT FROM
             (to_jsonb(OLD)-ARRAY['state','activated_at','disabled_at','last_used_at','clock_high_water_at','disable_session_effect','disable_policy_version','disable_operation_id','disable_correlation_id','version','usage_version']) OR
             OLD.state='DISABLED' OR NOT (
              (OLD.state='INACTIVE' AND NEW.state='ACTIVE' AND NEW.version=OLD.version+1 AND NEW.usage_version=OLD.usage_version+1) OR
              (OLD.state='ACTIVE' AND NEW.state='ACTIVE' AND NEW.version=OLD.version AND NEW.usage_version=OLD.usage_version+1 AND NEW.activated_at IS NOT DISTINCT FROM OLD.activated_at) OR
              (OLD.state='ACTIVE' AND NEW.state='DISABLED' AND NEW.version=OLD.version+1 AND NEW.usage_version=OLD.usage_version AND
                NEW.activated_at IS NOT DISTINCT FROM OLD.activated_at AND NEW.last_used_at IS NOT DISTINCT FROM OLD.last_used_at AND NEW.clock_high_water_at IS NOT DISTINCT FROM OLD.clock_high_water_at)) OR
             (OLD.last_used_at IS NOT NULL AND (NEW.last_used_at IS NULL OR NEW.last_used_at<OLD.last_used_at)) OR
             (OLD.clock_high_water_at IS NOT NULL AND (NEW.clock_high_water_at IS NULL OR NEW.clock_high_water_at<OLD.clock_high_water_at))
          THEN RAISE EXCEPTION 'TOTP generation and ended lifecycle are immutable' USING ERRCODE='23514'; END IF;
          RETURN NEW;
        END; $$;
        CREATE TRIGGER totp_authenticator_guard BEFORE UPDATE ON identity.totp_authenticators FOR EACH ROW EXECUTE FUNCTION identity.guard_totp_authenticator();
        CREATE FUNCTION identity.guard_totp_operation() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN
          IF (to_jsonb(NEW)-ARRAY['state','proof_reference','authorized_at','expires_at','disclosure_claimed_at','activation_request_id','eligibility_policy_version','eligibility_evidence_reference','eligibility_from','eligibility_includes_from','eligibility_until','eligibility_includes_until','ended_at','version']) IS DISTINCT FROM
             (to_jsonb(OLD)-ARRAY['state','proof_reference','authorized_at','expires_at','disclosure_claimed_at','activation_request_id','eligibility_policy_version','eligibility_evidence_reference','eligibility_from','eligibility_includes_from','eligibility_until','eligibility_includes_until','ended_at','version']) OR
             NEW.version<>OLD.version+1 OR NEW.expires_at>OLD.expires_at OR
             (OLD.proof_reference IS NOT NULL AND NEW.proof_reference IS DISTINCT FROM OLD.proof_reference) OR
             (OLD.authorized_at IS NOT NULL AND NEW.authorized_at IS DISTINCT FROM OLD.authorized_at) OR
             (OLD.disclosure_claimed_at IS NOT NULL AND NEW.disclosure_claimed_at IS DISTINCT FROM OLD.disclosure_claimed_at) OR NOT (
               (OLD.state='PENDING_PROOF' AND NEW.state='AUTHORIZED' AND NEW.kind='ENROLL' AND NEW.proof_reference IS NOT NULL AND NEW.authorized_at IS NOT NULL) OR
               (OLD.state='PENDING_PROOF' AND NEW.state='COMPLETED' AND NEW.kind IN ('DISABLE','RESET') AND NEW.eligibility_policy_version IS NOT NULL AND NEW.eligibility_evidence_reference IS NOT NULL) OR
               (OLD.state='AUTHORIZED' AND NEW.state='PREPARING' AND NEW.kind='ENROLL') OR
               (OLD.state='PREPARING' AND NEW.state='AWAITING_FIRST_CODE' AND NEW.kind='ENROLL' AND NEW.disclosure_claimed_at IS NOT NULL) OR
               (OLD.state='AWAITING_FIRST_CODE' AND NEW.state='COMPLETED' AND NEW.kind='ENROLL' AND NEW.activation_request_id IS NOT NULL) OR
               (OLD.state IN ('PENDING_PROOF','AUTHORIZED','PREPARING','AWAITING_FIRST_CODE') AND NEW.state IN ('EXPIRED','REJECTED')))
          THEN RAISE EXCEPTION 'TOTP original operation and one-time disclosure are immutable' USING ERRCODE='23514'; END IF;
          RETURN NEW;
        END; $$;
        CREATE TRIGGER totp_operation_guard BEFORE UPDATE ON identity.totp_operations FOR EACH ROW EXECUTE FUNCTION identity.guard_totp_operation();
        CREATE FUNCTION identity.guard_totp_pending() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN
          IF (to_jsonb(NEW)-ARRAY['state','result_session_id','accepted_authenticator_reference','accepted_generation','version']) IS DISTINCT FROM
             (to_jsonb(OLD)-ARRAY['state','result_session_id','accepted_authenticator_reference','accepted_generation','version']) OR
             OLD.state<>'PENDING' OR NEW.state NOT IN ('COMPLETED','EXPIRED') OR NEW.version<>OLD.version+1
          THEN RAISE EXCEPTION 'Pending primary proof is immutable and one-use' USING ERRCODE='23514'; END IF;
          RETURN NEW;
        END; $$;
        CREATE TRIGGER totp_pending_guard BEFORE UPDATE ON identity.totp_pending_authentication FOR EACH ROW EXECUTE FUNCTION identity.guard_totp_pending();
        CREATE FUNCTION identity.guard_totp_replay() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN
          RAISE EXCEPTION 'TOTP replay claims and exact proof attempts are immutable' USING ERRCODE='23514';
        END; $$;
        CREATE TRIGGER totp_replay_guard BEFORE UPDATE ON identity.totp_used_codes FOR EACH ROW EXECUTE FUNCTION identity.guard_totp_replay();
        CREATE TRIGGER totp_attempt_guard BEFORE UPDATE ON identity.totp_proof_attempts FOR EACH ROW EXECUTE FUNCTION identity.guard_totp_replay();
        """;
    internal static string SessionGuard => PasskeyMigrationSql.Guards[..PasskeyMigrationSql.Guards.IndexOf("CREATE FUNCTION identity.guard_passkey_credential", StringComparison.Ordinal)]
        .Replace("NEW.assurance IS DISTINCT FROM OLD.assurance", "NEW.totp_authenticator_reference IS DISTINCT FROM OLD.totp_authenticator_reference OR NEW.totp_generation IS DISTINCT FROM OLD.totp_generation OR NEW.totp_authenticator_version IS DISTINCT FROM OLD.totp_authenticator_version OR NEW.totp_policy_version IS DISTINCT FROM OLD.totp_policy_version OR NEW.mfa_requirement_policy_version IS DISTINCT FROM OLD.mfa_requirement_policy_version OR NEW.assurance IS DISTINCT FROM OLD.assurance", StringComparison.Ordinal)
        .Replace("NEW.termination_cause='PASSKEY_REVOKED'", "NEW.termination_cause IN ('PASSKEY_REVOKED','TOTP_DISABLED')", StringComparison.Ordinal);
    internal static string RestoreSessionGuard => PasskeyMigrationSql.Guards[..PasskeyMigrationSql.Guards.IndexOf("CREATE FUNCTION identity.guard_passkey_credential", StringComparison.Ordinal)];
    internal const string RemoveGuards = """
        DROP TRIGGER totp_attempt_guard ON identity.totp_proof_attempts;
        DROP TRIGGER totp_replay_guard ON identity.totp_used_codes;
        DROP FUNCTION identity.guard_totp_replay();
        DROP TRIGGER totp_pending_guard ON identity.totp_pending_authentication;
        DROP FUNCTION identity.guard_totp_pending();
        DROP TRIGGER totp_operation_guard ON identity.totp_operations;
        DROP FUNCTION identity.guard_totp_operation();
        DROP TRIGGER totp_authenticator_guard ON identity.totp_authenticators;
        DROP FUNCTION identity.guard_totp_authenticator();
        """;
}
