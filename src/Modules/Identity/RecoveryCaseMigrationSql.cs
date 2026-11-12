namespace Tunner.Identity.Persistence;

internal static class RecoveryCaseMigrationSql
{
    // Apply only in the new forward migration, after the model-created tables. Existing009 guards remain unchanged.
    internal const string Harden = """
        CREATE FUNCTION identity.guard_recovery_case() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN
          IF TG_OP='INSERT' THEN
            IF NEW.state<>'PENDING_CODE' OR NEW.version<>1 OR NEW.result_id IS NOT NULL OR NEW.ended_at IS NOT NULL THEN
              RAISE EXCEPTION 'Recovery case must begin pending' USING ERRCODE='23514';
            END IF;
          ELSIF (to_jsonb(NEW)-ARRAY['state','version','result_id','ended_at']) IS DISTINCT FROM
                (to_jsonb(OLD)-ARRAY['state','version','result_id','ended_at']) OR
                OLD.state<>'PENDING_CODE' OR NEW.state<>'CONTAINED' OR NEW.version<>OLD.version+1 THEN
            RAISE EXCEPTION 'Recovery original binding and outcome are immutable' USING ERRCODE='23514';
          END IF;
          RETURN NEW;
        END; $$;
        CREATE TRIGGER recovery_case_guard BEFORE INSERT OR UPDATE ON identity.recovery_cases
          FOR EACH ROW EXECUTE FUNCTION identity.guard_recovery_case();
        CREATE FUNCTION identity.guard_recovery_result() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN
          IF TG_OP='UPDATE' THEN
            RAISE EXCEPTION 'Recovery containment result is immutable' USING ERRCODE='23514';
          END IF;
          IF NOT EXISTS(SELECT 1 FROM identity.recovery_cases c JOIN identity.tunner_sessions s ON s.session_id=c.target_session_id
            WHERE c.case_id=NEW.case_id AND c.result_id=NEW.result_id AND c.state='CONTAINED' AND c.version=2
              AND c.account_id=NEW.account_id AND s.account_id=c.account_id AND s.state='REVOKED'
              AND NEW.target_session_id=c.target_session_id AND NEW.prior_session_version=c.expected_session_version
              AND NEW.ended_session_version=s.version AND NEW.recorded_at=c.ended_at AND s.revoked_at=c.ended_at
              AND NEW.context_digest=c.context_digest AND NEW.policy_version=c.policy_version
              AND NEW.policy_snapshot=c.policy_snapshot AND NEW.hierarchy_snapshot=c.hierarchy_snapshot
              AND NEW.set_reference=c.set_reference AND NEW.set_lifecycle_version=c.expected_set_lifecycle_version
              AND NEW.unresolved_effects_json=c.unresolved_effects_json AND NEW.processing_reference=c.processing_reference
              AND NEW.retention_reference=c.retention_reference
              AND s.operation_id='OP-RECOVERY-CASE-'||replace(c.case_id::text,'-','')
              AND s.correlation_id=replace(c.request_id::text,'-','')
              AND EXISTS(SELECT 1 FROM identity.recovery_code_entries e WHERE e.account_id=c.account_id
                AND e.set_reference=c.set_reference AND e.consumed_by_operation_id=s.operation_id
                AND e.consumption_correlation_id=s.correlation_id AND e.consumption_context_digest=c.context_digest
                AND e.consumption_owner_policy_version=c.policy_version AND e.consumption_set_lifecycle_version=c.expected_set_lifecycle_version
                AND e.consumption_outcome_reference=replace(NEW.result_id::text,'-','') AND e.version=2
                AND e.consumed_at>=c.created_at AND e.consumed_at<=NEW.recorded_at)
              AND EXISTS(SELECT 1 FROM platform.outbox_messages o WHERE o.event_id=NEW.session_outbox_event_id
                AND o.event_type='TunnerSessionRevoked' AND o.operation_id=s.operation_id AND o.correlation_id=s.correlation_id)) THEN
            RAISE EXCEPTION 'Recovery result requires its actual exact owner effect' USING ERRCODE='23514';
          END IF;
          RETURN NEW;
        END; $$;
        CREATE CONSTRAINT TRIGGER recovery_result_guard AFTER INSERT OR UPDATE ON identity.recovery_results DEFERRABLE INITIALLY DEFERRED
          FOR EACH ROW EXECUTE FUNCTION identity.guard_recovery_result();
        CREATE FUNCTION identity.guard_recovery_case_result() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN
          IF NEW.state='CONTAINED' AND NOT EXISTS(SELECT 1 FROM identity.recovery_results r
            WHERE r.result_id=NEW.result_id AND r.case_id=NEW.case_id AND r.account_id=NEW.account_id) THEN
            RAISE EXCEPTION 'Recovery containment requires its immutable result' USING ERRCODE='23514';
          END IF;
          RETURN NEW;
        END; $$;
        CREATE CONSTRAINT TRIGGER recovery_case_result_guard AFTER UPDATE ON identity.recovery_cases DEFERRABLE INITIALLY DEFERRED
          FOR EACH ROW EXECUTE FUNCTION identity.guard_recovery_case_result();
        ALTER TABLE identity.recovery_cases ADD CONSTRAINT fk_recovery_case_result
          FOREIGN KEY(result_id) REFERENCES identity.recovery_results(result_id)
          ON DELETE NO ACTION DEFERRABLE INITIALLY DEFERRED;
        """;
    internal const string BlockPopulatedDowngrade = """
        DO $$ BEGIN
          IF EXISTS(SELECT 1 FROM identity.recovery_cases) OR EXISTS(SELECT 1 FROM identity.recovery_results) THEN
            RAISE EXCEPTION 'Cannot downgrade populated recovery-case evidence' USING ERRCODE='23514';
          END IF;
        END; $$;
        """;
    internal const string RemoveGuards = """
        ALTER TABLE identity.recovery_cases DROP CONSTRAINT fk_recovery_case_result;
        DROP TRIGGER recovery_case_result_guard ON identity.recovery_cases;
        DROP FUNCTION identity.guard_recovery_case_result();
        DROP TRIGGER recovery_result_guard ON identity.recovery_results;
        DROP FUNCTION identity.guard_recovery_result();
        DROP TRIGGER recovery_case_guard ON identity.recovery_cases;
        DROP FUNCTION identity.guard_recovery_case();
        """;
}
