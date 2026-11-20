namespace Tunner.Identity.Persistence;

internal static class RecoveryPasswordMigrationSql
{
    internal const string BlockPopulatedDowngrade = """
        DO $$ BEGIN
          IF EXISTS(SELECT 1 FROM identity.recovery_entries) OR EXISTS(SELECT 1 FROM identity.recovery_cases WHERE goal='RepairPassword')
            OR EXISTS(SELECT 1 FROM identity.recovery_results WHERE outcome='PASSWORD_REPAIRED_NO_SESSION') THEN
            RAISE EXCEPTION 'Recovery repair history requires a governed data-preserving downgrade' USING ERRCODE='55000';
          END IF;
        END; $$;
        """;
    internal const string CaseBinding = """
        length(original_browser_digest)=64 AND length(network_digest)=64 AND length(context_digest)=64 AND length(facts_digest)=64
        AND length(policy_version)>0 AND length(processing_reference)>0 AND length(retention_reference)>0
        AND jsonb_typeof(policy_snapshot::jsonb)='object' AND jsonb_typeof(hierarchy_snapshot::jsonb)='object'
        AND jsonb_typeof(unresolved_effects_json::jsonb)='array'
        AND ((goal='ContainSelectedTunnerSession' AND session_effect='RevokeExactSelectedSession'
          AND target_session_id IS NOT NULL AND expected_session_version IS NOT NULL AND expected_session_version>=1
          AND entry_id IS NULL AND prior_password_digest IS NULL AND prior_security_stamp_digest IS NULL AND user_source_digest IS NULL
          AND jsonb_array_length(unresolved_effects_json::jsonb)>0)
          OR (goal='RepairPassword' AND session_effect='InvalidateExistingTunnerSessions' AND target_session_id IS NULL AND expected_session_version IS NULL
            AND entry_id IS NOT NULL AND prior_password_digest IS NOT NULL AND length(prior_password_digest)=64
            AND prior_security_stamp_digest IS NOT NULL AND length(prior_security_stamp_digest)=64
            AND user_source_digest IS NOT NULL AND length(user_source_digest)=64))
        """;
    internal const string CaseVersions = """
        expected_account_version>=1 AND expected_set_lifecycle_version>=1 AND ((usage_admission='ExactUsageSnapshot'
          AND expected_usage_version IS NOT NULL AND expected_usage_version>=0) OR (usage_admission='IndependentUnusedEntry' AND expected_usage_version IS NULL))
        """;
    internal const string CaseState = """
        expires_at>created_at AND recovery_expires_at>expires_at AND ((state='PENDING_CODE' AND version=1 AND result_id IS NULL AND ended_at IS NULL)
          OR (version=2 AND result_id IS NOT NULL AND ended_at>=created_at AND ended_at<expires_at
            AND ((state='CONTAINED' AND goal='ContainSelectedTunnerSession') OR (state='REPAIRED' AND goal='RepairPassword'))))
        """;
    internal const string ResultShape = """
        set_lifecycle_version>=1 AND length(context_digest)=64 AND length(policy_version)>0 AND length(processing_reference)>0 AND length(retention_reference)>0
        AND jsonb_typeof(policy_snapshot::jsonb)='object' AND jsonb_typeof(hierarchy_snapshot::jsonb)='object' AND jsonb_typeof(unresolved_effects_json::jsonb)='array'
        AND ((outcome='CONTAINED_PENDING_RESTORATION' AND target_session_id IS NOT NULL AND prior_session_version IS NOT NULL AND prior_session_version>=1
          AND ended_session_version IS NOT NULL AND ended_session_version=prior_session_version+1 AND session_outbox_event_id IS NOT NULL
          AND repair_outbox_event_id IS NULL AND prior_password_digest IS NULL AND repaired_password_digest IS NULL
          AND prior_security_stamp_digest IS NULL AND repaired_security_stamp_digest IS NULL AND session_effect IS NULL AND jsonb_array_length(unresolved_effects_json::jsonb)>0)
          OR (outcome='PASSWORD_REPAIRED_NO_SESSION' AND target_session_id IS NULL AND prior_session_version IS NULL AND ended_session_version IS NULL
            AND session_outbox_event_id IS NULL AND repair_outbox_event_id IS NOT NULL AND prior_password_digest IS NOT NULL AND length(prior_password_digest)=64
            AND repaired_password_digest IS NOT NULL AND length(repaired_password_digest)=64 AND prior_security_stamp_digest IS NOT NULL AND length(prior_security_stamp_digest)=64
            AND repaired_security_stamp_digest IS NOT NULL AND length(repaired_security_stamp_digest)=64 AND repaired_security_stamp_digest<>prior_security_stamp_digest
            AND session_effect='InvalidateExistingTunnerSessions'))
        """;
    // Existing containment trigger functions are retained byte-for-byte. Only explicit new shapes dispatch to new guards.
    internal const string Harden = """
        DROP TRIGGER recovery_case_guard ON identity.recovery_cases;
        CREATE TRIGGER recovery_case_guard BEFORE INSERT OR UPDATE ON identity.recovery_cases FOR EACH ROW
          WHEN(NEW.goal='ContainSelectedTunnerSession') EXECUTE FUNCTION identity.guard_recovery_case();
        DROP TRIGGER recovery_result_guard ON identity.recovery_results;
        CREATE CONSTRAINT TRIGGER recovery_result_guard AFTER INSERT OR UPDATE ON identity.recovery_results DEFERRABLE INITIALLY DEFERRED
          FOR EACH ROW WHEN(NEW.outcome='CONTAINED_PENDING_RESTORATION') EXECUTE FUNCTION identity.guard_recovery_result();
        CREATE FUNCTION identity.guard_recovery_entry() RETURNS trigger LANGUAGE plpgsql AS $$
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
        CREATE TRIGGER recovery_entry_guard BEFORE INSERT OR UPDATE ON identity.recovery_entries FOR EACH ROW EXECUTE FUNCTION identity.guard_recovery_entry();
        CREATE FUNCTION identity.guard_recovery_password_case() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN
          IF TG_OP='INSERT' THEN
            IF NEW.state<>'PENDING_CODE' OR NEW.version<>1 OR NEW.result_id IS NOT NULL OR NEW.ended_at IS NOT NULL THEN
              RAISE EXCEPTION 'Recovery repair must begin pending' USING ERRCODE='23514'; END IF;
          ELSIF (to_jsonb(NEW)-ARRAY['state','version','result_id','ended_at']) IS DISTINCT FROM (to_jsonb(OLD)-ARRAY['state','version','result_id','ended_at'])
            OR OLD.state<>'PENDING_CODE' OR NEW.state<>'REPAIRED' OR NEW.version<>OLD.version+1 THEN
            RAISE EXCEPTION 'Original recovery repair is immutable and one-use' USING ERRCODE='23514';
          END IF;
          RETURN NEW;
        END; $$;
        CREATE TRIGGER recovery_password_case_guard BEFORE INSERT OR UPDATE ON identity.recovery_cases FOR EACH ROW
          WHEN(NEW.goal='RepairPassword') EXECUTE FUNCTION identity.guard_recovery_password_case();
        CREATE FUNCTION identity.guard_recovery_password_result() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN
          IF TG_OP='UPDATE' THEN RAISE EXCEPTION 'Recovery repair result is immutable' USING ERRCODE='23514'; END IF;
          IF NOT EXISTS(SELECT 1 FROM identity.recovery_cases c JOIN identity.recovery_entries n ON n.entry_id=c.entry_id
            JOIN identity."AspNetUsers" u ON u."AccountId"=c.account_id
            WHERE c.case_id=NEW.case_id AND c.goal='RepairPassword' AND c.state='REPAIRED' AND c.version=2 AND c.result_id=NEW.result_id
              AND n.state='REPAIRED' AND n.version=2 AND n.ended_at=c.ended_at AND n.request_id=c.request_id AND n.browser_digest=c.original_browser_digest
              AND NEW.account_id=c.account_id AND NEW.recorded_at=c.ended_at AND NEW.context_digest=c.context_digest
              AND NEW.policy_version=c.policy_version AND NEW.policy_snapshot=c.policy_snapshot AND NEW.hierarchy_snapshot=c.hierarchy_snapshot
              AND NEW.set_reference=c.set_reference AND NEW.set_lifecycle_version=c.expected_set_lifecycle_version
              AND NEW.unresolved_effects_json=c.unresolved_effects_json AND NEW.processing_reference=c.processing_reference AND NEW.retention_reference=c.retention_reference
              AND NEW.prior_password_digest=c.prior_password_digest AND NEW.prior_security_stamp_digest=c.prior_security_stamp_digest
              AND NEW.session_effect=c.session_effect AND NEW.repaired_password_digest=upper(encode(sha256(convert_to(u."PasswordHash",'UTF8')),'hex'))
              AND NEW.repaired_security_stamp_digest=upper(encode(sha256(convert_to(u."SecurityStamp",'UTF8')),'hex'))
              AND EXISTS(SELECT 1 FROM identity.recovery_code_entries e WHERE e.account_id=c.account_id AND e.set_reference=c.set_reference
                AND e.consumed_by_operation_id='OP-RECOVERY-CASE-'||replace(c.case_id::text,'-','')
                AND e.consumption_correlation_id=replace(c.request_id::text,'-','') AND e.consumption_context_digest=c.context_digest
                AND e.consumption_owner_policy_version=c.policy_version AND e.consumption_set_lifecycle_version=c.expected_set_lifecycle_version
                AND e.consumption_outcome_reference=replace(NEW.result_id::text,'-','') AND e.version=2 AND e.consumed_at>=c.created_at AND e.consumed_at<=NEW.recorded_at)
              AND EXISTS(SELECT 1 FROM platform.outbox_messages o WHERE o.event_id=NEW.repair_outbox_event_id AND o.event_type='IdentitySecurityActivityRequested'
                AND o.operation_id='OP-RECOVERY-CASE-'||replace(c.case_id::text,'-','') AND o.correlation_id=replace(c.request_id::text,'-',''))) THEN
            RAISE EXCEPTION 'Recovery repair requires its actual original credential effect and proof' USING ERRCODE='23514';
          END IF;
          RETURN NEW;
        END; $$;
        CREATE CONSTRAINT TRIGGER recovery_password_result_guard AFTER INSERT OR UPDATE ON identity.recovery_results DEFERRABLE INITIALLY DEFERRED
          FOR EACH ROW WHEN(NEW.outcome='PASSWORD_REPAIRED_NO_SESSION') EXECUTE FUNCTION identity.guard_recovery_password_result();
        CREATE FUNCTION identity.guard_recovery_password_terminal() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN
          IF NOT EXISTS(SELECT 1 FROM identity.recovery_entries e JOIN identity.recovery_cases c ON c.entry_id=e.entry_id
            JOIN identity.recovery_results r ON r.case_id=c.case_id AND r.result_id=c.result_id
            WHERE e.state='REPAIRED' AND c.state='REPAIRED' AND r.outcome='PASSWORD_REPAIRED_NO_SESSION' AND e.entry_id=NEW.entry_id) THEN
            RAISE EXCEPTION 'Recovery repair terminal state requires its immutable result' USING ERRCODE='23514';
          END IF;
          RETURN NEW;
        END; $$;
        CREATE CONSTRAINT TRIGGER recovery_entry_result_guard AFTER UPDATE ON identity.recovery_entries DEFERRABLE INITIALLY DEFERRED
          FOR EACH ROW WHEN(NEW.state='REPAIRED') EXECUTE FUNCTION identity.guard_recovery_password_terminal();
        CREATE CONSTRAINT TRIGGER recovery_password_case_result_guard AFTER UPDATE ON identity.recovery_cases DEFERRABLE INITIALLY DEFERRED
          FOR EACH ROW WHEN(NEW.state='REPAIRED') EXECUTE FUNCTION identity.guard_recovery_password_terminal();
        """;
    internal const string RemoveGuards = """
        DROP TRIGGER recovery_password_case_result_guard ON identity.recovery_cases;
        DROP TRIGGER recovery_entry_result_guard ON identity.recovery_entries;
        DROP FUNCTION identity.guard_recovery_password_terminal();
        DROP TRIGGER recovery_password_result_guard ON identity.recovery_results;
        DROP FUNCTION identity.guard_recovery_password_result();
        DROP TRIGGER recovery_password_case_guard ON identity.recovery_cases;
        DROP FUNCTION identity.guard_recovery_password_case();
        DROP TRIGGER recovery_entry_guard ON identity.recovery_entries;
        DROP FUNCTION identity.guard_recovery_entry();
        DROP TRIGGER recovery_case_guard ON identity.recovery_cases;
        CREATE TRIGGER recovery_case_guard BEFORE INSERT OR UPDATE ON identity.recovery_cases FOR EACH ROW EXECUTE FUNCTION identity.guard_recovery_case();
        DROP TRIGGER recovery_result_guard ON identity.recovery_results;
        CREATE CONSTRAINT TRIGGER recovery_result_guard AFTER INSERT OR UPDATE ON identity.recovery_results DEFERRABLE INITIALLY DEFERRED
          FOR EACH ROW EXECUTE FUNCTION identity.guard_recovery_result();
        """;
}
