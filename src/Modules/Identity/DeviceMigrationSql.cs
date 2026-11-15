namespace Tunner.Identity.Persistence;

internal static class DeviceMigrationSql
{
    internal const string BlockPopulatedDowngrade = """
        DO $$ DECLARE owned_table record; has_data boolean;
        BEGIN
          LOCK TABLE identity.device_records,identity.device_trust_versions,identity.device_session_evidence,identity.device_operations IN ACCESS EXCLUSIVE MODE;
          FOR owned_table IN SELECT schemaname,tablename FROM pg_tables WHERE schemaname='identity'
            AND tablename IN ('device_records','device_trust_versions','device_session_evidence','device_operations')
          LOOP
            EXECUTE format('SELECT EXISTS (SELECT 1 FROM %I.%I)',owned_table.schemaname,owned_table.tablename) INTO has_data;
            IF has_data THEN RAISE EXCEPTION 'Device evidence contains data; use a governed forward migration' USING ERRCODE='23514'; END IF;
          END LOOP;
        END; $$;
        """;
    internal const string Guards = """
        CREATE FUNCTION identity.guard_device_record() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN
          IF ROW(NEW.device_id,NEW.account_id,NEW.origin,NEW.correlation_digest,NEW.created_at)
              IS DISTINCT FROM ROW(OLD.device_id,OLD.account_id,OLD.origin,OLD.correlation_digest,OLD.created_at)
             OR NEW.version<>OLD.version+1 OR NEW.last_observed_at<OLD.last_observed_at THEN
            RAISE EXCEPTION 'Device ownership and installation identity are immutable; evidence versions are monotonic' USING ERRCODE='23514';
          END IF;
          RETURN NEW;
        END; $$;
        CREATE TRIGGER device_record_guard BEFORE UPDATE ON identity.device_records FOR EACH ROW EXECUTE FUNCTION identity.guard_device_record();

        CREATE FUNCTION identity.guard_device_history() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN
          RAISE EXCEPTION 'Device observation and trust history cannot be rewritten' USING ERRCODE='23514';
        END; $$;
        CREATE TRIGGER device_trust_history_guard BEFORE UPDATE OR DELETE ON identity.device_trust_versions FOR EACH ROW EXECUTE FUNCTION identity.guard_device_history();
        CREATE TRIGGER device_session_history_guard BEFORE UPDATE OR DELETE ON identity.device_session_evidence FOR EACH ROW EXECUTE FUNCTION identity.guard_device_history();
        CREATE TRIGGER device_trust_history_truncate_guard BEFORE TRUNCATE ON identity.device_trust_versions FOR EACH STATEMENT EXECUTE FUNCTION identity.guard_device_history();
        CREATE TRIGGER device_session_history_truncate_guard BEFORE TRUNCATE ON identity.device_session_evidence FOR EACH STATEMENT EXECUTE FUNCTION identity.guard_device_history();

        CREATE FUNCTION identity.guard_device_trust_sequence() RETURNS trigger LANGUAGE plpgsql AS $$
        DECLARE prior_version bigint;
        BEGIN
          PERFORM 1 FROM identity.device_records WHERE device_id=NEW.device_id FOR UPDATE;
          SELECT COALESCE(MAX(version),0) INTO prior_version FROM identity.device_trust_versions WHERE device_id=NEW.device_id;
          IF NEW.version<>prior_version+1 THEN
            RAISE EXCEPTION 'Device trust history requires its exact next version' USING ERRCODE='23514';
          END IF;
          RETURN NEW;
        END; $$;
        CREATE TRIGGER device_trust_sequence_guard BEFORE INSERT ON identity.device_trust_versions FOR EACH ROW EXECUTE FUNCTION identity.guard_device_trust_sequence();

        CREATE FUNCTION identity.guard_device_operation() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN
          IF (to_jsonb(NEW)-ARRAY['state','version','proof_id','result_trust_version','completed_at','outbox_event_id']) IS DISTINCT FROM
             (to_jsonb(OLD)-ARRAY['state','version','proof_id','result_trust_version','completed_at','outbox_event_id']) OR
             OLD.state<>'PENDING' OR NEW.state<>'COMPLETED' OR NEW.version<>OLD.version+1 THEN
            RAISE EXCEPTION 'Device original operation is immutable and completion is one-way' USING ERRCODE='23514';
          END IF;
          RETURN NEW;
        END; $$;
        CREATE TRIGGER device_operation_guard BEFORE UPDATE ON identity.device_operations FOR EACH ROW EXECUTE FUNCTION identity.guard_device_operation();

        CREATE FUNCTION identity.check_device_trust_evidence() RETURNS trigger LANGUAGE plpgsql AS $$
        DECLARE prior identity.device_trust_versions; operation identity.device_operations; source_session identity.tunner_sessions;
        BEGIN
          SELECT * INTO source_session FROM identity.tunner_sessions WHERE session_id=NEW.source_session_id;
          SELECT * INTO prior FROM identity.device_trust_versions WHERE device_id=NEW.device_id AND version=NEW.version-1;
          IF source_session.account_id IS DISTINCT FROM NEW.account_id OR source_session.origin IS DISTINCT FROM NEW.origin OR
             source_session.scheme<>'Tunner.User.Bff.Session' OR NEW.recorded_at<source_session.authenticated_at OR
             NOT EXISTS(SELECT 1 FROM platform.outbox_messages e WHERE e.event_id=NEW.outbox_event_id AND e.source='tunner/identity'
               AND e.subject=replace(NEW.device_id::text,'-','') AND e.resource_version=NEW.version::text
               AND e.operation_id=NEW.operation_id AND e.correlation_id=NEW.correlation_id AND e.occurred_at_utc=NEW.recorded_at
               AND e.event_type=CASE WHEN NEW.state='REVOKED' THEN 'DeviceTrustRevoked' ELSE 'DeviceTrustEvaluated' END) THEN
            RAISE EXCEPTION 'Device trust requires exact owned authentication and causal outbox evidence' USING ERRCODE='23514';
          END IF;
          IF NEW.state='REVOKED' THEN
            SELECT * INTO operation FROM identity.device_operations WHERE id=NEW.removal_operation_id;
            IF prior.device_id IS NULL OR prior.state='REVOKED' OR NEW.source_session_id<>prior.source_session_id OR
               operation.state IS DISTINCT FROM 'COMPLETED' OR operation.account_id<>NEW.account_id OR operation.origin<>NEW.origin OR
               operation.device_id<>NEW.device_id OR operation.expected_device_version<>NEW.device_version OR
               operation.expected_trust_version<>NEW.version-1 OR operation.result_trust_version<>NEW.version OR
               operation.outbox_event_id<>NEW.outbox_event_id OR operation.completed_at<>NEW.recorded_at OR
               NEW.operation_id<>'OP-DEVICE-TRUST-REVOKE-'||replace(operation.id::text,'-','') OR NEW.correlation_id<>operation.correlation_id OR
               NOT EXISTS(SELECT 1 FROM identity.step_up_proofs p WHERE p.proof_id=operation.proof_id AND p.state='CONSUMED'
                 AND p.account_id=operation.account_id AND p.session_id=operation.session_id AND p.origin=operation.origin
                 AND p.operation_id=NEW.operation_id AND p.correlation_id=NEW.correlation_id AND p.operation='DEVICE_TRUST_REVOKE'
                 AND p.context='USER_BFF' AND p.resource_id=replace(NEW.device_id::text,'-','')
                 AND p.expected_version=operation.expected_trust_version AND p.context_digest=operation.context_digest
                 AND p.policy_version=operation.assurance_policy_version AND p.ended_at>=NEW.recorded_at) THEN
              RAISE EXCEPTION 'Device trust revocation requires the original operation and consumed exact proof' USING ERRCODE='23514';
            END IF;
          ELSE
            IF NEW.eligibility_facts_json::jsonb->>'AccountId' IS DISTINCT FROM NEW.account_id OR
               NEW.eligibility_facts_json::jsonb->>'Origin' IS DISTINCT FROM NEW.origin OR
               (NEW.eligibility_facts_json::jsonb->>'DeviceId')::uuid IS DISTINCT FROM NEW.device_id OR
               (NEW.eligibility_facts_json::jsonb->>'DeviceVersion')::bigint IS DISTINCT FROM NEW.device_version OR
               (NEW.eligibility_facts_json::jsonb->>'PriorTrustVersion')::bigint IS DISTINCT FROM NEW.version-1 OR
               NEW.eligibility_facts_json::jsonb->>'PriorTrustState' IS DISTINCT FROM prior.state OR
               (NEW.eligibility_facts_json::jsonb->>'AuthenticationSessionId')::uuid IS DISTINCT FROM NEW.source_session_id OR
               NEW.eligibility_facts_json::jsonb->>'AuthenticationMethods' IS DISTINCT FROM source_session.authentication_methods OR
               NEW.eligibility_facts_json::jsonb->>'Assurance' IS DISTINCT FROM source_session.assurance OR
               NEW.eligibility_facts_json::jsonb->>'PolicyVersion' IS DISTINCT FROM NEW.policy_version OR
               NEW.eligibility_decision_json::jsonb->>'PolicyVersion' IS DISTINCT FROM NEW.policy_snapshot::jsonb->>'EligibilityPolicyVersion' OR
               (NEW.eligibility_decision_json::jsonb->>'Allowed')::boolean IS DISTINCT FROM (NEW.state='TRUSTED') OR
               NOT EXISTS(SELECT 1 FROM identity.device_session_evidence d WHERE d.session_id=NEW.source_session_id AND
                 d.account_id=NEW.account_id AND d.origin=NEW.origin AND d.device_id=NEW.device_id AND d.device_version=NEW.device_version AND d.trust_version=NEW.version) THEN
              RAISE EXCEPTION 'Device trust eligibility must bind actual authentication, prior trust state and observation' USING ERRCODE='23514';
            END IF;
          END IF;
          RETURN NEW;
        END; $$;
        CREATE CONSTRAINT TRIGGER device_trust_evidence_guard AFTER INSERT ON identity.device_trust_versions
          DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION identity.check_device_trust_evidence();

        CREATE FUNCTION identity.check_device_session_evidence() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN
          IF NOT EXISTS(SELECT 1 FROM identity.tunner_sessions s WHERE s.session_id=NEW.session_id AND s.account_id=NEW.account_id
               AND s.origin=NEW.origin AND s.scheme='Tunner.User.Bff.Session' AND s.authenticated_at<=NEW.observed_at) OR
             NOT EXISTS(SELECT 1 FROM platform.outbox_messages e WHERE e.event_id=NEW.outbox_event_id AND e.source='tunner/identity'
               AND e.event_type='DeviceEvidenceObserved' AND e.subject=replace(NEW.device_id::text,'-','')
               AND e.resource_version=NEW.device_version::text AND e.occurred_at_utc=NEW.observed_at) THEN
            RAISE EXCEPTION 'Device session evidence requires owned actual authentication and its observation event' USING ERRCODE='23514';
          END IF;
          RETURN NEW;
        END; $$;
        CREATE CONSTRAINT TRIGGER device_session_evidence_guard AFTER INSERT ON identity.device_session_evidence
          DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION identity.check_device_session_evidence();
        """;
    internal const string RemoveGuards = """
        DROP TRIGGER device_session_evidence_guard ON identity.device_session_evidence;
        DROP FUNCTION identity.check_device_session_evidence();
        DROP TRIGGER device_trust_evidence_guard ON identity.device_trust_versions;
        DROP FUNCTION identity.check_device_trust_evidence();
        DROP TRIGGER device_operation_guard ON identity.device_operations;
        DROP FUNCTION identity.guard_device_operation();
        DROP TRIGGER device_trust_sequence_guard ON identity.device_trust_versions;
        DROP FUNCTION identity.guard_device_trust_sequence();
        DROP TRIGGER device_session_history_truncate_guard ON identity.device_session_evidence;
        DROP TRIGGER device_trust_history_truncate_guard ON identity.device_trust_versions;
        DROP TRIGGER device_session_history_guard ON identity.device_session_evidence;
        DROP TRIGGER device_trust_history_guard ON identity.device_trust_versions;
        DROP FUNCTION identity.guard_device_history();
        DROP TRIGGER device_record_guard ON identity.device_records;
        DROP FUNCTION identity.guard_device_record();
        """;
}
