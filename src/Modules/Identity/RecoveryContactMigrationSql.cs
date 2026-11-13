namespace Tunner.Identity.Persistence;

/// <summary>Append to an officially generated forward migration. No historical migration/target model is edited.</summary>
internal static class RecoveryContactMigrationSql
{
    internal const string EntryPresentation = """
        (contact_challenge_id IS NULL AND contact_profile_version IS NULL AND contact_expires_at IS NULL)
        OR (contact_challenge_id IS NOT NULL AND contact_profile_version IS NOT NULL AND length(contact_profile_version)>0
          AND contact_expires_at IS NOT NULL AND contact_expires_at>created_at AND contact_expires_at<=expires_at)
        """;
    private const string NoCaseContact = "contact_reference IS NULL AND expected_contact_version IS NULL AND contact_destination_digest IS NULL AND contact_challenge_id IS NULL AND initial_contact_risk_snapshot IS NULL";
    internal const string CaseVersions = "((" + RecoveryPasskeyMigrationSql.CaseVersions + ") AND " + NoCaseContact + ") OR (" + """
        proof_kind='CONTACT_RISK' AND goal='RepairPassword' AND expected_account_version>=1 AND set_reference IS NULL
        AND expected_set_lifecycle_version IS NULL AND expected_usage_version IS NULL AND usage_admission IS NULL
        AND passkey_credential_reference IS NULL AND expected_passkey_version IS NULL AND expected_passkey_usage_version IS NULL AND passkey_ceremony_id IS NULL
        AND contact_reference IS NOT NULL AND expected_contact_version IS NOT NULL AND expected_contact_version>=1
        AND contact_destination_digest IS NOT NULL AND length(contact_destination_digest)=64 AND contact_challenge_id IS NOT NULL
        AND initial_contact_risk_snapshot IS NOT NULL AND jsonb_typeof(initial_contact_risk_snapshot::jsonb)='object')
        """;
    internal const string CaseState = """
        expires_at>created_at AND recovery_expires_at>expires_at AND (((state='PENDING_CODE' AND proof_kind='CODE')
          OR (state='PENDING_PASSKEY' AND proof_kind='PASSKEY') OR (state='PENDING_CONTACT_RISK' AND proof_kind='CONTACT_RISK'))
          AND version=1 AND result_id IS NULL AND ended_at IS NULL OR (version=2 AND result_id IS NOT NULL AND ended_at>=created_at AND ended_at<expires_at
            AND ((state='CONTAINED' AND goal='ContainSelectedTunnerSession' AND proof_kind='CODE') OR (state='REPAIRED' AND goal='RepairPassword'))))
        """;
    private const string NoResultContact = "contact_reference IS NULL AND contact_version IS NULL AND contact_challenge_id IS NULL AND contact_destination_digest IS NULL AND contact_risk_snapshot IS NULL AND contact_notification_delivery_id IS NULL";
    internal const string ResultShape = "((" + RecoveryPasskeyMigrationSql.ResultShape + ") AND " + NoResultContact + ") OR (" + """
        proof_kind='CONTACT_RISK' AND outcome='PASSWORD_REPAIRED_NO_SESSION' AND set_reference IS NULL AND set_lifecycle_version IS NULL
        AND target_session_id IS NULL AND prior_session_version IS NULL AND ended_session_version IS NULL AND session_outbox_event_id IS NULL
        AND passkey_credential_reference IS NULL AND passkey_version IS NULL AND passkey_usage_version IS NULL AND passkey_ceremony_id IS NULL
        AND passkey_method IS NULL AND passkey_assurance IS NULL AND native_before_digest IS NULL AND native_after_digest IS NULL
        AND contact_reference IS NOT NULL AND contact_version IS NOT NULL AND contact_version>=1 AND contact_challenge_id IS NOT NULL
        AND contact_destination_digest IS NOT NULL AND length(contact_destination_digest)=64 AND contact_risk_snapshot IS NOT NULL AND jsonb_typeof(contact_risk_snapshot::jsonb)='object'
        AND contact_notification_delivery_id IS NOT NULL AND repair_outbox_event_id IS NOT NULL
        AND prior_password_digest IS NOT NULL AND length(prior_password_digest)=64 AND repaired_password_digest IS NOT NULL AND length(repaired_password_digest)=64
        AND prior_security_stamp_digest IS NOT NULL AND length(prior_security_stamp_digest)=64 AND repaired_security_stamp_digest IS NOT NULL AND length(repaired_security_stamp_digest)=64
        AND repaired_security_stamp_digest<>prior_security_stamp_digest AND session_effect IS NOT NULL AND session_effect='InvalidateExistingTunnerSessions'
        AND length(context_digest)=64 AND length(policy_version)>0 AND length(processing_reference)>0 AND length(retention_reference)>0
        AND jsonb_typeof(policy_snapshot::jsonb)='object' AND jsonb_typeof(hierarchy_snapshot::jsonb)='object' AND jsonb_typeof(unresolved_effects_json::jsonb)='array')
        """;
    internal const string ChallengeShape = """
        length(context_digest)=64 AND length(destination_digest)=64 AND length(profile_version)>0 AND contact_version>=1 AND maximum_attempts>0
        AND attempts>=0 AND attempts<=maximum_attempts AND version>=1 AND expires_at>created_at AND purge_after>=expires_at
        AND jsonb_typeof(notification_snapshot::jsonb)='object' AND length(processing_reference)>0 AND length(retention_reference)>0
        AND ((state='PENDING' AND version=attempts+1 AND attempts<maximum_attempts AND ended_at IS NULL AND result_id IS NULL
          AND protected_code IS NOT NULL AND protected_digest IS NOT NULL AND protected_address IS NOT NULL AND material_purged_at IS NULL)
          OR (state='REJECTED' AND attempts=maximum_attempts AND ended_at>=created_at AND ended_at<expires_at AND result_id IS NULL
            AND protected_code IS NULL AND protected_digest IS NULL)
          OR (state='CONSUMED' AND version>=attempts+2 AND result_id IS NOT NULL AND ended_at>=created_at AND ended_at<expires_at
            AND protected_code IS NULL AND protected_digest IS NULL)
          OR (state='EXPIRED' AND ended_at>=expires_at AND result_id IS NULL AND protected_code IS NULL AND protected_digest IS NULL))
        AND (material_purged_at IS NULL OR (material_purged_at>=purge_after AND protected_code IS NULL AND protected_digest IS NULL AND protected_address IS NULL))
        """;
    internal const string BlockPopulatedDowngrade = """
        DO $$ BEGIN
          IF EXISTS(SELECT 1 FROM identity.recovery_entries WHERE contact_challenge_id IS NOT NULL)
            OR EXISTS(SELECT 1 FROM identity.recovery_cases WHERE proof_kind='CONTACT_RISK')
            OR EXISTS(SELECT 1 FROM identity.recovery_results WHERE proof_kind='CONTACT_RISK')
            OR EXISTS(SELECT 1 FROM identity.recovery_contact_challenges) THEN
            RAISE EXCEPTION 'Contact recovery history requires a governed data-preserving downgrade' USING ERRCODE='55000'; END IF;
        END; $$;
        """;
    internal const string Harden = """
        CREATE OR REPLACE FUNCTION identity.guard_recovery_entry() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN
          IF TG_OP='INSERT' THEN
            IF NEW.state<>'OPEN' OR NEW.version<>1 OR NEW.ended_at IS NOT NULL OR NEW.passkey_ceremony_id IS NOT NULL OR NEW.contact_challenge_id IS NOT NULL THEN
              RAISE EXCEPTION 'Recovery entry must begin neutral' USING ERRCODE='23514'; END IF;
          ELSIF OLD.state='OPEN' AND NEW.state='OPEN' AND OLD.version=1 AND NEW.version=1 AND OLD.contact_challenge_id IS NULL AND NEW.contact_challenge_id IS NOT NULL
            AND NEW.contact_profile_version=NEW.policy_snapshot::jsonb#>>'{Values,ContactRecoveryProfile,Version}'
            AND (to_jsonb(NEW)-ARRAY['contact_challenge_id','contact_profile_version','contact_expires_at']) IS NOT DISTINCT FROM
                (to_jsonb(OLD)-ARRAY['contact_challenge_id','contact_profile_version','contact_expires_at']) THEN RETURN NEW;
          ELSIF OLD.state='OPEN' AND NEW.state='OPEN' AND OLD.version=1 AND NEW.version=1 AND OLD.passkey_ceremony_id IS NULL AND NEW.passkey_ceremony_id IS NOT NULL
            AND NEW.passkey_profile_version=NEW.policy_snapshot::jsonb#>>'{Values,PasskeyRecoveryProfile,Version}'
            AND NEW.passkey_policy_version=NEW.policy_snapshot::jsonb#>>'{Values,PasskeyRecoveryProfile,NativePolicyVersion}'
            AND (to_jsonb(NEW)-ARRAY['passkey_ceremony_id','passkey_options_json','passkey_profile_version','passkey_policy_version','passkey_expires_at']) IS NOT DISTINCT FROM
                (to_jsonb(OLD)-ARRAY['passkey_ceremony_id','passkey_options_json','passkey_profile_version','passkey_policy_version','passkey_expires_at']) THEN RETURN NEW;
          ELSIF (to_jsonb(NEW)-ARRAY['state','version','ended_at']) IS DISTINCT FROM (to_jsonb(OLD)-ARRAY['state','version','ended_at'])
            OR OLD.state<>'OPEN' OR NEW.state NOT IN ('CANCELLED','REPAIRED') OR NEW.version<>OLD.version+1 THEN
            RAISE EXCEPTION 'Original recovery entry and presentation are immutable and terminal' USING ERRCODE='23514'; END IF;
          RETURN NEW;
        END; $$;
        CREATE FUNCTION identity.guard_recovery_contact_case() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN
          IF TG_OP='INSERT' THEN
            IF NEW.state<>'PENDING_CONTACT_RISK' OR NEW.version<>1 OR NEW.result_id IS NOT NULL OR NEW.ended_at IS NOT NULL OR
              NOT EXISTS(SELECT 1 FROM accounts.verified_contacts v JOIN identity."AspNetUsers" u ON u."AccountId"=v.account_id
                WHERE v.contact_id=NEW.contact_reference AND v.account_id=NEW.account_id AND v.version=NEW.expected_contact_version AND v.state='VERIFIED'
                AND v.anonymized_at_utc IS NULL AND v.normalized_email=u."NormalizedEmail" AND u."EmailConfirmed"
                AND NEW.contact_destination_digest=upper(encode(sha256(convert_to(v.normalized_email,'UTF8')),'hex')))) THEN
              RAISE EXCEPTION 'Contact recovery requires its exact current verified owner' USING ERRCODE='23514'; END IF;
          ELSIF (to_jsonb(NEW)-ARRAY['state','version','result_id','ended_at']) IS DISTINCT FROM (to_jsonb(OLD)-ARRAY['state','version','result_id','ended_at'])
            OR OLD.state<>'PENDING_CONTACT_RISK' OR NEW.state<>'REPAIRED' OR NEW.version<>OLD.version+1 THEN
            RAISE EXCEPTION 'Original contact recovery is immutable and one-use' USING ERRCODE='23514'; END IF;
          RETURN NEW;
        END; $$;
        CREATE TRIGGER recovery_contact_case_guard BEFORE INSERT OR UPDATE ON identity.recovery_cases FOR EACH ROW WHEN(NEW.proof_kind='CONTACT_RISK')
          EXECUTE FUNCTION identity.guard_recovery_contact_case();
        CREATE FUNCTION identity.guard_recovery_contact_challenge() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN
          IF TG_OP='DELETE' THEN RAISE EXCEPTION 'Contact proof history requires governed lifecycle handling' USING ERRCODE='55000'; END IF;
          IF TG_OP='INSERT' THEN
            IF NEW.state<>'PENDING' OR NEW.version<>1 OR NEW.attempts<>0 OR NEW.material_purged_at IS NOT NULL OR
              NOT EXISTS(SELECT 1 FROM identity.recovery_cases c WHERE c.case_id=NEW.case_id AND c.entry_id=NEW.entry_id AND c.proof_kind='CONTACT_RISK'
                AND c.state='PENDING_CONTACT_RISK' AND c.contact_challenge_id=NEW.challenge_id AND c.contact_reference=NEW.contact_reference
                AND c.expected_contact_version=NEW.contact_version AND c.context_digest=NEW.context_digest AND c.contact_destination_digest=NEW.destination_digest
                AND c.policy_snapshot::jsonb#>>'{Values,ContactRecoveryProfile,Version}'=NEW.profile_version AND NEW.expires_at<=c.expires_at
                AND NEW.created_at>=c.created_at AND NEW.processing_reference=c.processing_reference AND NEW.retention_reference=c.retention_reference) THEN
              RAISE EXCEPTION 'Contact challenge requires its exact actual recovery case' USING ERRCODE='23514'; END IF;
          ELSE
            IF (to_jsonb(NEW)-ARRAY['state','version','attempts','protected_code','protected_digest','protected_address','ended_at','result_id','material_purged_at']) IS DISTINCT FROM
               (to_jsonb(OLD)-ARRAY['state','version','attempts','protected_code','protected_digest','protected_address','ended_at','result_id','material_purged_at']) OR NEW.version<>OLD.version+1 THEN
              RAISE EXCEPTION 'Original contact proof/notification identity is immutable' USING ERRCODE='23514'; END IF;
            IF OLD.material_purged_at IS NULL AND NEW.material_purged_at>=OLD.purge_after AND NEW.protected_code IS NULL AND NEW.protected_digest IS NULL
              AND NEW.protected_address IS NULL AND NEW.attempts=OLD.attempts AND NEW.result_id IS NOT DISTINCT FROM OLD.result_id
              AND ((OLD.state='PENDING' AND NEW.state='EXPIRED' AND NEW.ended_at=NEW.material_purged_at) OR
                (OLD.state<>'PENDING' AND NEW.state=OLD.state AND NEW.ended_at=OLD.ended_at)) THEN RETURN NEW; END IF;
            IF OLD.state<>'PENDING' OR NEW.material_purged_at IS NOT NULL OR NEW.protected_address IS DISTINCT FROM OLD.protected_address OR
              NOT ((NEW.attempts=OLD.attempts+1 AND NEW.result_id IS NULL AND
                ((NEW.state='PENDING' AND NEW.protected_code=OLD.protected_code AND NEW.protected_digest=OLD.protected_digest AND NEW.ended_at IS NULL)
                OR (NEW.state='REJECTED' AND NEW.attempts=NEW.maximum_attempts AND NEW.protected_code IS NULL AND NEW.protected_digest IS NULL AND NEW.ended_at IS NOT NULL)))
                OR (NEW.state='CONSUMED' AND NEW.attempts=OLD.attempts AND NEW.result_id IS NOT NULL AND NEW.protected_code IS NULL AND NEW.protected_digest IS NULL AND NEW.ended_at IS NOT NULL)) THEN
              RAISE EXCEPTION 'Contact challenge is bounded, one-use and terminal' USING ERRCODE='23514'; END IF;
          END IF;
          RETURN NEW;
        END; $$;
        CREATE TRIGGER recovery_contact_challenge_guard BEFORE INSERT OR UPDATE OR DELETE ON identity.recovery_contact_challenges FOR EACH ROW EXECUTE FUNCTION identity.guard_recovery_contact_challenge();
        CREATE FUNCTION identity.guard_recovery_contact_started() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN
          IF NOT EXISTS(SELECT 1 FROM identity.recovery_cases c JOIN identity.recovery_entries n ON n.entry_id=c.entry_id
            WHERE c.case_id=NEW.case_id AND n.contact_challenge_id=NEW.challenge_id AND n.contact_profile_version=NEW.profile_version AND n.contact_expires_at>=NEW.expires_at
              AND EXISTS(SELECT 1 FROM platform.outbox_messages o WHERE o.event_id=NEW.notification_outbox_event_id AND o.event_type='IdentitySecurityNotificationRequested'
                AND o.operation_id='OP-RECOVERY-CASE-'||replace(c.case_id::text,'-','') AND o.correlation_id=replace(c.request_id::text,'-',''))) THEN
            RAISE EXCEPTION 'Contact challenge requires its stable original presentation and notification intent' USING ERRCODE='23514'; END IF;
          RETURN NEW;
        END; $$;
        CREATE CONSTRAINT TRIGGER recovery_contact_started_guard AFTER INSERT ON identity.recovery_contact_challenges DEFERRABLE INITIALLY DEFERRED FOR EACH ROW
          EXECUTE FUNCTION identity.guard_recovery_contact_started();
        CREATE FUNCTION identity.guard_recovery_contact_result() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN
          IF TG_OP='UPDATE' THEN RAISE EXCEPTION 'Contact recovery result is immutable' USING ERRCODE='23514'; END IF;
          IF NOT EXISTS(SELECT 1 FROM identity.recovery_cases c JOIN identity.recovery_entries n ON n.entry_id=c.entry_id
            JOIN identity.recovery_contact_challenges q ON q.challenge_id=c.contact_challenge_id AND q.case_id=c.case_id
            JOIN accounts.verified_contacts v ON v.contact_id=c.contact_reference AND v.account_id=c.account_id
            JOIN identity."AspNetUsers" u ON u."AccountId"=c.account_id
            WHERE c.case_id=NEW.case_id AND c.proof_kind='CONTACT_RISK' AND c.state='REPAIRED' AND c.version=2 AND c.result_id=NEW.result_id
              AND n.state='REPAIRED' AND n.version=2 AND n.ended_at=c.ended_at AND n.contact_challenge_id=q.challenge_id AND n.contact_expires_at>NEW.recorded_at
              AND NEW.account_id=c.account_id AND NEW.recorded_at=c.ended_at AND NEW.context_digest=c.context_digest AND NEW.policy_version=c.policy_version
              AND NEW.policy_snapshot=c.policy_snapshot AND NEW.hierarchy_snapshot=c.hierarchy_snapshot AND NEW.unresolved_effects_json=c.unresolved_effects_json
              AND NEW.processing_reference=c.processing_reference AND NEW.retention_reference=c.retention_reference AND NEW.session_effect=c.session_effect
              AND NEW.prior_password_digest=c.prior_password_digest AND NEW.prior_security_stamp_digest=c.prior_security_stamp_digest
              AND NEW.repaired_password_digest=upper(encode(sha256(convert_to(u."PasswordHash",'UTF8')),'hex'))
              AND NEW.repaired_security_stamp_digest=upper(encode(sha256(convert_to(u."SecurityStamp",'UTF8')),'hex'))
              AND NEW.contact_reference=v.contact_id AND NEW.contact_version=v.version AND v.version=c.expected_contact_version AND v.state='VERIFIED'
              AND v.anonymized_at_utc IS NULL AND v.normalized_email=u."NormalizedEmail" AND u."EmailConfirmed"
              AND NEW.contact_destination_digest=c.contact_destination_digest AND NEW.contact_destination_digest=upper(encode(sha256(convert_to(v.normalized_email,'UTF8')),'hex'))
              AND NEW.contact_challenge_id=q.challenge_id AND q.state='CONSUMED' AND q.result_id=NEW.result_id AND q.ended_at=NEW.recorded_at
              AND q.protected_code IS NULL AND q.protected_digest IS NULL AND NEW.recorded_at>=q.created_at AND NEW.recorded_at<q.expires_at
              AND NEW.contact_notification_delivery_id=q.delivery_id AND NEW.contact_risk_snapshot::jsonb->>'Outcome'='ALLOW'
              AND length(NEW.contact_risk_snapshot::jsonb->>'DecisionReference')>0 AND length(NEW.contact_risk_snapshot::jsonb->>'EvidenceReference')>0
              AND NEW.contact_risk_snapshot::jsonb->>'CaseId'=c.case_id::text AND NEW.contact_risk_snapshot::jsonb->>'RequestId'=c.request_id::text
              AND NEW.contact_risk_snapshot::jsonb->>'AccountId'=c.account_id AND NEW.contact_risk_snapshot::jsonb->>'ContextDigest'=c.context_digest
              AND NEW.contact_risk_snapshot::jsonb->>'ContactReference'=v.contact_id::text AND (NEW.contact_risk_snapshot::jsonb->>'ContactVersion')::bigint=v.version
              AND NEW.contact_risk_snapshot::jsonb->>'DestinationDigest'=c.contact_destination_digest AND NEW.contact_risk_snapshot::jsonb->>'FactsDigest'=c.facts_digest
              AND NEW.contact_risk_snapshot::jsonb->>'PolicyVersion'=c.policy_snapshot::jsonb#>>'{Values,ContactRecoveryProfile,RiskPolicyVersion}'
              AND EXISTS(SELECT 1 FROM platform.outbox_messages o WHERE o.event_id=NEW.repair_outbox_event_id AND o.event_type='IdentitySecurityActivityRequested'
                AND o.operation_id='OP-RECOVERY-CASE-'||replace(c.case_id::text,'-','') AND o.correlation_id=replace(c.request_id::text,'-',''))) THEN
            RAISE EXCEPTION 'Contact recovery requires its actual original verified proof/risk/password effects' USING ERRCODE='23514'; END IF;
          RETURN NEW;
        END; $$;
        CREATE CONSTRAINT TRIGGER recovery_contact_result_guard AFTER INSERT OR UPDATE ON identity.recovery_results DEFERRABLE INITIALLY DEFERRED FOR EACH ROW
          WHEN(NEW.proof_kind='CONTACT_RISK') EXECUTE FUNCTION identity.guard_recovery_contact_result();
        CREATE FUNCTION identity.guard_recovery_contact_terminal() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN
          IF NOT EXISTS(SELECT 1 FROM identity.recovery_results r JOIN identity.recovery_cases c ON c.case_id=r.case_id AND c.result_id=r.result_id
            WHERE r.proof_kind='CONTACT_RISK' AND r.contact_challenge_id=NEW.challenge_id AND r.result_id=NEW.result_id AND c.state='REPAIRED') THEN
            RAISE EXCEPTION 'Consumed contact proof requires its immutable actual result' USING ERRCODE='23514'; END IF;
          RETURN NEW;
        END; $$;
        CREATE CONSTRAINT TRIGGER recovery_contact_terminal_guard AFTER UPDATE ON identity.recovery_contact_challenges DEFERRABLE INITIALLY DEFERRED FOR EACH ROW
          WHEN(NEW.state='CONSUMED') EXECUTE FUNCTION identity.guard_recovery_contact_terminal();
        """;
    // Down: execute BlockPopulatedDowngrade FIRST; drop these guards before removing generated model objects/columns.
    // Restore the immediately previous guard_recovery_entry body from RecoveryPasskeyMigrationSql.Harden, without recreating other existing guards.
    internal const string RemoveGuards = """
        DROP TRIGGER recovery_contact_terminal_guard ON identity.recovery_contact_challenges;
        DROP FUNCTION identity.guard_recovery_contact_terminal();
        DROP TRIGGER recovery_contact_result_guard ON identity.recovery_results;
        DROP FUNCTION identity.guard_recovery_contact_result();
        DROP TRIGGER recovery_contact_started_guard ON identity.recovery_contact_challenges;
        DROP FUNCTION identity.guard_recovery_contact_started();
        DROP TRIGGER recovery_contact_challenge_guard ON identity.recovery_contact_challenges;
        DROP FUNCTION identity.guard_recovery_contact_challenge();
        DROP TRIGGER recovery_contact_case_guard ON identity.recovery_cases;
        DROP FUNCTION identity.guard_recovery_contact_case();
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
        """;
}
