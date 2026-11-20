namespace Tunner.Identity.Persistence;

internal static class RecoveryPasskeyMigrationSql
{
    internal const string DispatchLegacyCeremonyGuard = """
        DROP TRIGGER passkey_ceremony_guard ON identity.passkey_ceremonies;
        CREATE TRIGGER passkey_ceremony_guard BEFORE UPDATE ON identity.passkey_ceremonies FOR EACH ROW
          WHEN(OLD.kind<>'RECOVERY' AND NEW.kind<>'RECOVERY') EXECUTE FUNCTION identity.guard_passkey_ceremony();
        """;
    internal const string RestoreLegacyCeremonyGuard = """
        DROP TRIGGER passkey_ceremony_guard ON identity.passkey_ceremonies;
        CREATE TRIGGER passkey_ceremony_guard BEFORE UPDATE ON identity.passkey_ceremonies FOR EACH ROW
          EXECUTE FUNCTION identity.guard_passkey_ceremony();
        """;
    internal const string BlockPopulatedDowngrade = """
        DO $$ BEGIN
          IF EXISTS(SELECT 1 FROM identity.recovery_entries WHERE passkey_ceremony_id IS NOT NULL)
            OR EXISTS(SELECT 1 FROM identity.recovery_cases WHERE proof_kind='PASSKEY')
            OR EXISTS(SELECT 1 FROM identity.recovery_results WHERE proof_kind='PASSKEY')
            OR EXISTS(SELECT 1 FROM identity.passkey_ceremonies WHERE kind='RECOVERY') THEN
            RAISE EXCEPTION 'Strong recovery history requires a governed data-preserving downgrade' USING ERRCODE='55000';
          END IF;
        END; $$;
        """;
    internal const string EntryPresentation = """
        (passkey_ceremony_id IS NULL AND passkey_options_json IS NULL AND passkey_profile_version IS NULL AND passkey_policy_version IS NULL AND passkey_expires_at IS NULL)
        OR (passkey_ceremony_id IS NOT NULL AND passkey_options_json IS NOT NULL AND jsonb_typeof(passkey_options_json::jsonb)='object'
          AND passkey_profile_version IS NOT NULL AND length(passkey_profile_version)>0 AND passkey_policy_version IS NOT NULL AND length(passkey_policy_version)>0
          AND passkey_expires_at IS NOT NULL AND passkey_expires_at>created_at AND passkey_expires_at<=expires_at)
        """;
    internal const string CaseVersions = """
        expected_account_version>=1 AND ((proof_kind='CODE' AND set_reference IS NOT NULL AND expected_set_lifecycle_version IS NOT NULL
          AND expected_set_lifecycle_version>=1 AND usage_admission IS NOT NULL AND passkey_credential_reference IS NULL AND expected_passkey_version IS NULL
          AND expected_passkey_usage_version IS NULL AND passkey_ceremony_id IS NULL AND ((usage_admission='ExactUsageSnapshot'
            AND expected_usage_version IS NOT NULL AND expected_usage_version>=0) OR (usage_admission='IndependentUnusedEntry' AND expected_usage_version IS NULL)))
          OR (proof_kind='PASSKEY' AND goal='RepairPassword' AND set_reference IS NULL AND expected_set_lifecycle_version IS NULL
            AND expected_usage_version IS NULL AND usage_admission IS NULL AND passkey_credential_reference IS NOT NULL
            AND expected_passkey_version IS NOT NULL AND expected_passkey_version>=1 AND expected_passkey_usage_version IS NOT NULL
            AND expected_passkey_usage_version>=0 AND passkey_ceremony_id IS NOT NULL))
        """;
    internal const string CaseState = """
        expires_at>created_at AND recovery_expires_at>expires_at AND (((state='PENDING_CODE' AND proof_kind='CODE')
          OR (state='PENDING_PASSKEY' AND proof_kind='PASSKEY')) AND version=1 AND result_id IS NULL AND ended_at IS NULL
          OR (version=2 AND result_id IS NOT NULL AND ended_at>=created_at AND ended_at<expires_at
            AND ((state='CONTAINED' AND goal='ContainSelectedTunnerSession' AND proof_kind='CODE') OR (state='REPAIRED' AND goal='RepairPassword'))))
        """;
    private const string PasskeyResult = """
        proof_kind='PASSKEY' AND set_reference IS NULL AND set_lifecycle_version IS NULL AND passkey_credential_reference IS NOT NULL
        AND passkey_version IS NOT NULL AND passkey_version>=1 AND passkey_usage_version IS NOT NULL AND passkey_usage_version>=1
        AND passkey_ceremony_id IS NOT NULL AND passkey_method IS NOT NULL AND length(passkey_method)>0
        AND passkey_assurance IS NOT NULL AND length(passkey_assurance)>0 AND lower(passkey_assurance)<>'aal3'
        AND native_before_digest IS NOT NULL AND length(native_before_digest)=64 AND native_after_digest IS NOT NULL AND length(native_after_digest)=64
        AND outcome='PASSWORD_REPAIRED_NO_SESSION' AND target_session_id IS NULL AND prior_session_version IS NULL AND ended_session_version IS NULL
        AND session_outbox_event_id IS NULL AND repair_outbox_event_id IS NOT NULL AND prior_password_digest IS NOT NULL AND length(prior_password_digest)=64
        AND repaired_password_digest IS NOT NULL AND length(repaired_password_digest)=64 AND prior_security_stamp_digest IS NOT NULL AND length(prior_security_stamp_digest)=64
        AND repaired_security_stamp_digest IS NOT NULL AND length(repaired_security_stamp_digest)=64 AND repaired_security_stamp_digest<>prior_security_stamp_digest
        AND session_effect IS NOT NULL AND session_effect='InvalidateExistingTunnerSessions' AND length(context_digest)=64 AND length(policy_version)>0
        AND length(processing_reference)>0 AND length(retention_reference)>0 AND jsonb_typeof(policy_snapshot::jsonb)='object'
        AND jsonb_typeof(hierarchy_snapshot::jsonb)='object' AND jsonb_typeof(unresolved_effects_json::jsonb)='array'
        """;
    internal const string ResultShape = "(proof_kind='CODE' AND set_reference IS NOT NULL AND set_lifecycle_version IS NOT NULL AND " +
        "passkey_credential_reference IS NULL AND passkey_version IS NULL AND passkey_usage_version IS NULL AND passkey_ceremony_id IS NULL AND " +
        "passkey_method IS NULL AND passkey_assurance IS NULL AND native_before_digest IS NULL AND native_after_digest IS NULL AND (" +
        RecoveryPasswordMigrationSql.ResultShape + ")) OR (" + PasskeyResult + ")";
    internal const string CeremonyState = "state IN ('PENDING','AUTHORIZED','COMPLETED','REJECTED','EXPIRED') AND kind IN ('ENROLL','SIGN_IN','REVOKE','STEP_UP','RECOVERY')";
    internal const string Harden = """
        CREATE OR REPLACE FUNCTION identity.guard_recovery_entry() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN
          IF TG_OP='INSERT' THEN
            IF NEW.state<>'OPEN' OR NEW.version<>1 OR NEW.ended_at IS NOT NULL OR NEW.passkey_ceremony_id IS NOT NULL THEN
              RAISE EXCEPTION 'Recovery entry must begin neutral' USING ERRCODE='23514'; END IF;
          ELSIF OLD.state='OPEN' AND NEW.state='OPEN' AND OLD.version=1 AND NEW.version=1 AND OLD.passkey_ceremony_id IS NULL
            AND NEW.passkey_ceremony_id IS NOT NULL AND NEW.passkey_profile_version=NEW.policy_snapshot::jsonb#>>'{Values,PasskeyRecoveryProfile,Version}'
            AND NEW.passkey_policy_version=NEW.policy_snapshot::jsonb#>>'{Values,PasskeyRecoveryProfile,NativePolicyVersion}'
            AND (to_jsonb(NEW)-ARRAY['passkey_ceremony_id','passkey_options_json','passkey_profile_version','passkey_policy_version','passkey_expires_at'])
              IS NOT DISTINCT FROM (to_jsonb(OLD)-ARRAY['passkey_ceremony_id','passkey_options_json','passkey_profile_version','passkey_policy_version','passkey_expires_at']) THEN
            RETURN NEW;
          ELSIF (to_jsonb(NEW)-ARRAY['state','version','ended_at']) IS DISTINCT FROM (to_jsonb(OLD)-ARRAY['state','version','ended_at'])
            OR OLD.state<>'OPEN' OR NEW.state NOT IN ('CANCELLED','REPAIRED') OR NEW.version<>OLD.version+1 THEN
            RAISE EXCEPTION 'Original recovery entry and native presentation are immutable and terminal' USING ERRCODE='23514';
          END IF;
          RETURN NEW;
        END; $$;
        -- Original CODE/containment functions remain byte-for-byte unchanged.
        DROP TRIGGER recovery_password_case_guard ON identity.recovery_cases;
        CREATE TRIGGER recovery_password_case_guard BEFORE INSERT OR UPDATE ON identity.recovery_cases FOR EACH ROW
          WHEN(NEW.goal='RepairPassword' AND NEW.proof_kind='CODE') EXECUTE FUNCTION identity.guard_recovery_password_case();
        DROP TRIGGER recovery_password_result_guard ON identity.recovery_results;
        CREATE CONSTRAINT TRIGGER recovery_password_result_guard AFTER INSERT OR UPDATE ON identity.recovery_results DEFERRABLE INITIALLY DEFERRED
          FOR EACH ROW WHEN(NEW.outcome='PASSWORD_REPAIRED_NO_SESSION' AND NEW.proof_kind='CODE') EXECUTE FUNCTION identity.guard_recovery_password_result();
        CREATE FUNCTION identity.guard_recovery_passkey_case() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN
          IF TG_OP='INSERT' THEN
            IF NEW.state<>'PENDING_PASSKEY' OR NEW.version<>1 OR NEW.result_id IS NOT NULL OR NEW.ended_at IS NOT NULL OR
              NOT EXISTS(SELECT 1 FROM identity.passkey_credentials k JOIN identity."AspNetUsers" u ON u."AccountId"=k.account_id
                WHERE k.credential_reference=NEW.passkey_credential_reference AND k.account_id=NEW.account_id AND k.state='ACTIVE'
                  AND k.version=NEW.expected_passkey_version AND k.usage_version=NEW.expected_passkey_usage_version
                  AND NEW.prior_password_digest=upper(encode(sha256(convert_to(u."PasswordHash",'UTF8')),'hex'))
                  AND NEW.prior_security_stamp_digest=upper(encode(sha256(convert_to(u."SecurityStamp",'UTF8')),'hex'))) THEN
              RAISE EXCEPTION 'Strong recovery must begin with its exact current native owner' USING ERRCODE='23514'; END IF;
          ELSIF (to_jsonb(NEW)-ARRAY['state','version','result_id','ended_at']) IS DISTINCT FROM
            (to_jsonb(OLD)-ARRAY['state','version','result_id','ended_at']) OR OLD.proof_kind<>'PASSKEY' OR OLD.state<>'PENDING_PASSKEY'
            OR NEW.state<>'REPAIRED' OR NEW.version<>OLD.version+1 THEN
            RAISE EXCEPTION 'Original strong recovery is immutable and one-use' USING ERRCODE='23514';
          END IF;
          RETURN NEW;
        END; $$;
        CREATE TRIGGER recovery_passkey_case_guard BEFORE INSERT OR UPDATE ON identity.recovery_cases FOR EACH ROW
          WHEN(NEW.proof_kind='PASSKEY') EXECUTE FUNCTION identity.guard_recovery_passkey_case();
        CREATE FUNCTION identity.guard_recovery_passkey_ceremony() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN
          IF TG_OP='UPDATE' THEN
            IF OLD.kind<>'RECOVERY' AND NEW.kind<>'RECOVERY' THEN RETURN NEW; END IF;
            IF OLD.kind<>'RECOVERY' OR NEW.kind<>'RECOVERY' OR
              (to_jsonb(NEW)-ARRAY['state','version','ended_at','protected_state','options_json','request_digest','proof_reference']) IS DISTINCT FROM
              (to_jsonb(OLD)-ARRAY['state','version','ended_at','protected_state','options_json','request_digest','proof_reference']) OR
              OLD.state<>'PENDING' OR NEW.state NOT IN ('COMPLETED','REJECTED','EXPIRED') OR NEW.version<>OLD.version+1 OR
              (NEW.state='COMPLETED' AND (NEW.proof_reference IS NULL OR length(NEW.request_digest)<>64)) THEN
              RAISE EXCEPTION 'Original native recovery ceremony is immutable and one-use' USING ERRCODE='23514'; END IF;
          ELSE
            IF NEW.kind<>'RECOVERY' THEN RETURN NEW; END IF;
            IF NEW.state<>'PENDING' OR NEW.version<>1 OR NEW.proof_reference IS NOT NULL OR NEW.request_digest<>'' OR
              NOT EXISTS(SELECT 1 FROM identity.recovery_cases c WHERE c.proof_kind='PASSKEY' AND c.state='PENDING_PASSKEY'
                AND c.passkey_ceremony_id=NEW.ceremony_id AND c.account_id=NEW.account_id AND c.request_id=NEW.request_id
                AND c.original_browser_digest=NEW.browser_digest AND c.origin=NEW.origin AND c.context_digest=NEW.bound_context_digest
                AND replace(c.case_id::text,'-','')=NEW.bound_resource_id AND NEW.bound_operation_id='OP-RECOVERY-CASE-'||replace(c.case_id::text,'-','')
                AND NEW.bound_correlation_id=replace(c.request_id::text,'-','') AND NEW.bound_operation=c.goal AND NEW.bound_context='ACCOUNT_RECOVERY'
                AND NEW.bound_expected_version=1 AND NEW.session_id IS NULL AND NEW.step_up_request_id IS NULL
                AND NEW.previous_session_id IS NULL AND NEW.result_session_id IS NULL AND NEW.credential_reference=c.passkey_credential_reference
                AND NEW.expected_version=c.expected_passkey_version AND NEW.security_stamp_digest=c.prior_security_stamp_digest
                AND NEW.policy_version=c.policy_snapshot::jsonb#>>'{Values,PasskeyRecoveryProfile,NativePolicyVersion}'
                AND NEW.assurance_policy_version=c.policy_snapshot::jsonb#>>'{Values,PasskeyRecoveryProfile,Version}'
                AND NEW.created_at>=c.created_at AND NEW.expires_at<=c.expires_at AND NEW.recovery_expires_at=c.recovery_expires_at) THEN
              RAISE EXCEPTION 'Native recovery ceremony requires the exact original case and goal' USING ERRCODE='23514'; END IF;
          END IF;
          RETURN NEW;
        END; $$;
        CREATE TRIGGER recovery_passkey_ceremony_guard BEFORE INSERT OR UPDATE ON identity.passkey_ceremonies FOR EACH ROW
          EXECUTE FUNCTION identity.guard_recovery_passkey_ceremony();
        CREATE FUNCTION identity.guard_recovery_passkey_result() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN
          IF TG_OP='UPDATE' THEN RAISE EXCEPTION 'Strong recovery result is immutable' USING ERRCODE='23514'; END IF;
          IF NOT EXISTS(SELECT 1 FROM identity.recovery_cases c JOIN identity.recovery_entries n ON n.entry_id=c.entry_id
            JOIN identity."AspNetUsers" u ON u."AccountId"=c.account_id
            JOIN identity.passkey_credentials k ON k.credential_reference=c.passkey_credential_reference AND k.account_id=c.account_id
            JOIN identity."AspNetUserPasskeys" p ON p."CredentialId"=k.credential_id AND p."UserId"=u."Id"
            JOIN identity.passkey_ceremonies a ON a.ceremony_id=c.passkey_ceremony_id
            WHERE c.case_id=NEW.case_id AND c.proof_kind='PASSKEY' AND c.goal='RepairPassword' AND c.state='REPAIRED' AND c.version=2 AND c.result_id=NEW.result_id
              AND n.state='REPAIRED' AND n.version=2 AND n.ended_at=c.ended_at AND n.request_id=c.request_id AND n.browser_digest=c.original_browser_digest
              AND n.passkey_ceremony_id=c.passkey_ceremony_id AND n.passkey_expires_at>NEW.recorded_at
              AND n.passkey_profile_version=c.policy_snapshot::jsonb#>>'{Values,PasskeyRecoveryProfile,Version}'
              AND n.passkey_policy_version=c.policy_snapshot::jsonb#>>'{Values,PasskeyRecoveryProfile,NativePolicyVersion}'
              AND NEW.account_id=c.account_id AND NEW.recorded_at=c.ended_at AND NEW.context_digest=c.context_digest
              AND NEW.policy_version=c.policy_version AND NEW.policy_snapshot=c.policy_snapshot AND NEW.hierarchy_snapshot=c.hierarchy_snapshot
              AND NEW.unresolved_effects_json=c.unresolved_effects_json AND NEW.processing_reference=c.processing_reference AND NEW.retention_reference=c.retention_reference
              AND NEW.prior_password_digest=c.prior_password_digest AND NEW.prior_security_stamp_digest=c.prior_security_stamp_digest AND NEW.session_effect=c.session_effect
              AND NEW.repaired_password_digest=upper(encode(sha256(convert_to(u."PasswordHash",'UTF8')),'hex'))
              AND NEW.repaired_security_stamp_digest=upper(encode(sha256(convert_to(u."SecurityStamp",'UTF8')),'hex'))
              AND NEW.passkey_credential_reference=k.credential_reference AND NEW.passkey_version=c.expected_passkey_version AND k.version=NEW.passkey_version
              AND k.state='ACTIVE' AND k.usage_version=c.expected_passkey_usage_version+1 AND NEW.passkey_usage_version=k.usage_version AND k.last_used_at=NEW.recorded_at
              AND NEW.native_after_digest=upper(encode(sha256(convert_to(p."Data"::text,'UTF8')),'hex'))
              AND NEW.passkey_assurance=c.policy_snapshot::jsonb#>>'{Values,PasskeyRecoveryProfile,RequiredAssurance}'
              AND NEW.passkey_assurance=c.policy_snapshot::jsonb->'Values'->'PasskeyRecoveryProfile'->'MethodAssurances'->>NEW.passkey_method
              AND NEW.passkey_ceremony_id=a.ceremony_id AND a.kind='RECOVERY' AND a.state='COMPLETED' AND a.version=2 AND a.proof_reference=NEW.result_id
              AND a.ended_at=NEW.recorded_at AND a.account_id=c.account_id AND a.browser_digest=c.original_browser_digest AND a.request_id=c.request_id
              AND a.bound_context_digest=c.context_digest AND a.bound_resource_id=replace(c.case_id::text,'-','') AND a.bound_operation=c.goal
              AND a.bound_operation_id='OP-RECOVERY-CASE-'||replace(c.case_id::text,'-','') AND a.bound_correlation_id=replace(c.request_id::text,'-','')
              AND a.credential_reference=k.credential_reference AND a.expected_version=k.version AND a.result_session_id IS NULL AND a.session_id IS NULL
              AND a.protected_state='' AND a.options_json='' AND length(a.request_digest)=64 AND NEW.recorded_at>=a.created_at AND NEW.recorded_at<a.expires_at
              AND EXISTS(SELECT 1 FROM platform.outbox_messages o WHERE o.event_id=NEW.repair_outbox_event_id AND o.event_type='IdentitySecurityActivityRequested'
                AND o.operation_id='OP-RECOVERY-CASE-'||replace(c.case_id::text,'-','') AND o.correlation_id=replace(c.request_id::text,'-',''))) THEN
            RAISE EXCEPTION 'Strong recovery requires its actual original native/password/result effects' USING ERRCODE='23514';
          END IF;
          RETURN NEW;
        END; $$;
        CREATE CONSTRAINT TRIGGER recovery_passkey_result_guard AFTER INSERT OR UPDATE ON identity.recovery_results DEFERRABLE INITIALLY DEFERRED
          FOR EACH ROW WHEN(NEW.proof_kind='PASSKEY') EXECUTE FUNCTION identity.guard_recovery_passkey_result();
        CREATE FUNCTION identity.guard_recovery_passkey_terminal() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN
          IF NOT EXISTS(SELECT 1 FROM identity.recovery_cases c JOIN identity.recovery_results r ON r.case_id=c.case_id AND r.result_id=c.result_id
            WHERE c.passkey_ceremony_id=NEW.ceremony_id AND c.proof_kind='PASSKEY' AND c.state='REPAIRED'
              AND r.proof_kind='PASSKEY' AND r.passkey_ceremony_id=NEW.ceremony_id AND r.result_id=NEW.proof_reference) THEN
            RAISE EXCEPTION 'Native recovery completion requires its immutable actual result' USING ERRCODE='23514'; END IF;
          RETURN NEW;
        END; $$;
        CREATE CONSTRAINT TRIGGER recovery_passkey_ceremony_result_guard AFTER UPDATE ON identity.passkey_ceremonies DEFERRABLE INITIALLY DEFERRED
          FOR EACH ROW WHEN(NEW.kind='RECOVERY' AND NEW.state='COMPLETED') EXECUTE FUNCTION identity.guard_recovery_passkey_terminal();
        """;
    internal const string RemoveGuards = """
        CREATE OR REPLACE FUNCTION identity.guard_recovery_entry() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN
          IF TG_OP='INSERT' THEN
            IF NEW.state<>'OPEN' OR NEW.version<>1 OR NEW.ended_at IS NOT NULL THEN
              RAISE EXCEPTION 'Recovery entry must begin neutral' USING ERRCODE='23514'; END IF;
          ELSIF (to_jsonb(NEW)-ARRAY['state','version','ended_at']) IS DISTINCT FROM (to_jsonb(OLD)-ARRAY['state','version','ended_at'])
            OR OLD.state<>'OPEN' OR NEW.state NOT IN ('CANCELLED','REPAIRED') OR NEW.version<>OLD.version+1 THEN
            RAISE EXCEPTION 'Original recovery entry is immutable and terminal' USING ERRCODE='23514';
          END IF;
          RETURN NEW;
        END; $$;
        DROP TRIGGER recovery_passkey_ceremony_result_guard ON identity.passkey_ceremonies;
        DROP FUNCTION identity.guard_recovery_passkey_terminal();
        DROP TRIGGER recovery_passkey_result_guard ON identity.recovery_results;
        DROP FUNCTION identity.guard_recovery_passkey_result();
        DROP TRIGGER recovery_passkey_ceremony_guard ON identity.passkey_ceremonies;
        DROP FUNCTION identity.guard_recovery_passkey_ceremony();
        DROP TRIGGER recovery_passkey_case_guard ON identity.recovery_cases;
        DROP FUNCTION identity.guard_recovery_passkey_case();
        DROP TRIGGER recovery_password_case_guard ON identity.recovery_cases;
        CREATE TRIGGER recovery_password_case_guard BEFORE INSERT OR UPDATE ON identity.recovery_cases FOR EACH ROW
          WHEN(NEW.goal='RepairPassword') EXECUTE FUNCTION identity.guard_recovery_password_case();
        DROP TRIGGER recovery_password_result_guard ON identity.recovery_results;
        CREATE CONSTRAINT TRIGGER recovery_password_result_guard AFTER INSERT OR UPDATE ON identity.recovery_results DEFERRABLE INITIALLY DEFERRED
          FOR EACH ROW WHEN(NEW.outcome='PASSWORD_REPAIRED_NO_SESSION') EXECUTE FUNCTION identity.guard_recovery_password_result();
        """;
}
