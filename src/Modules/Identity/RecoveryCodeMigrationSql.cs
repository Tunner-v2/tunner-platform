namespace Tunner.Identity.Persistence;

internal static class RecoveryCodeMigrationSql
{
    internal const string Guards = """
        CREATE FUNCTION identity.guard_recovery_code_entry() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN
          IF (to_jsonb(NEW)-ARRAY['consumed_at','consumed_by_operation_id','consumption_correlation_id','consumption_context_digest','consumption_policy_version','consumption_owner_policy_version','consumption_outcome_reference','consumption_set_lifecycle_version','version']) IS DISTINCT FROM
             (to_jsonb(OLD)-ARRAY['consumed_at','consumed_by_operation_id','consumption_correlation_id','consumption_context_digest','consumption_policy_version','consumption_owner_policy_version','consumption_outcome_reference','consumption_set_lifecycle_version','version']) OR
             OLD.consumed_at IS NOT NULL OR OLD.version<>1 OR NEW.version<>2 OR NEW.consumed_at IS NULL OR
             NEW.consumed_by_operation_id IS NULL OR NEW.consumption_correlation_id IS NULL
          THEN RAISE EXCEPTION 'Recovery verifier identity is immutable and use is one-way' USING ERRCODE='23514'; END IF;
          RETURN NEW;
        END; $$;
        CREATE TRIGGER recovery_code_entry_guard BEFORE UPDATE ON identity.recovery_code_entries FOR EACH ROW EXECUTE FUNCTION identity.guard_recovery_code_entry();
        CREATE FUNCTION identity.guard_recovery_code_set() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN
          IF (to_jsonb(NEW)-ARRAY['state','lifecycle_version','usage_version','retired_at','retired_by_operation_id']) IS DISTINCT FROM
             (to_jsonb(OLD)-ARRAY['state','lifecycle_version','usage_version','retired_at','retired_by_operation_id']) OR OLD.state<>'ACTIVE' OR NOT (
             (NEW.state='ACTIVE' AND NEW.lifecycle_version=OLD.lifecycle_version AND NEW.usage_version=OLD.usage_version+1 AND
               NEW.retired_at IS NOT DISTINCT FROM OLD.retired_at AND NEW.retired_by_operation_id IS NOT DISTINCT FROM OLD.retired_by_operation_id) OR
             (NEW.state='REPLACED' AND NEW.lifecycle_version=OLD.lifecycle_version+1 AND NEW.usage_version=OLD.usage_version AND
               NEW.retired_at IS NOT NULL AND NEW.retired_by_operation_id IS NOT NULL))
          THEN RAISE EXCEPTION 'Recovery set lifecycle and usage are monotonic' USING ERRCODE='23514'; END IF;
          RETURN NEW;
        END; $$;
        CREATE TRIGGER recovery_code_set_guard BEFORE UPDATE ON identity.recovery_code_sets FOR EACH ROW EXECUTE FUNCTION identity.guard_recovery_code_set();
        CREATE FUNCTION identity.guard_recovery_code_operation() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN
          IF (to_jsonb(NEW)-ARRAY['state','proof_reference','completed_at','first_response_committed_at','version']) IS DISTINCT FROM
             (to_jsonb(OLD)-ARRAY['state','proof_reference','completed_at','first_response_committed_at','version']) OR
             OLD.state<>'PENDING_PROOF' OR NEW.state NOT IN ('COMPLETED','REJECTED','EXPIRED') OR NEW.version<>OLD.version+1
          THEN RAISE EXCEPTION 'Recovery original operation and first-delivery claim are immutable' USING ERRCODE='23514'; END IF;
          RETURN NEW;
        END; $$;
        CREATE TRIGGER recovery_code_operation_guard BEFORE UPDATE ON identity.recovery_code_operations FOR EACH ROW EXECUTE FUNCTION identity.guard_recovery_code_operation();
        """;
    internal const string RemoveGuards = """
        DROP TRIGGER recovery_code_operation_guard ON identity.recovery_code_operations;
        DROP FUNCTION identity.guard_recovery_code_operation();
        DROP TRIGGER recovery_code_set_guard ON identity.recovery_code_sets;
        DROP FUNCTION identity.guard_recovery_code_set();
        DROP TRIGGER recovery_code_entry_guard ON identity.recovery_code_entries;
        DROP FUNCTION identity.guard_recovery_code_entry();
        """;
}
