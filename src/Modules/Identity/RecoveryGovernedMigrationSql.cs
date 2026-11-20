namespace Tunner.Identity.Persistence;

/// <summary>Append guards to an officially generated forward migration after the reviewed contact increment.</summary>
internal static class RecoveryGovernedMigrationSql
{
    internal const string CaseVersions = "((" + RecoveryContactMigrationSql.CaseVersions + ") AND governed_contacts_digest IS NULL) OR (" + """
        proof_kind='GOVERNED_REVIEW' AND goal='RepairPassword' AND expected_account_version>=1 AND set_reference IS NULL
        AND expected_set_lifecycle_version IS NULL AND expected_usage_version IS NULL AND usage_admission IS NULL
        AND passkey_credential_reference IS NULL AND expected_passkey_version IS NULL AND expected_passkey_usage_version IS NULL AND passkey_ceremony_id IS NULL
        AND contact_reference IS NULL AND expected_contact_version IS NULL AND contact_destination_digest IS NULL AND contact_challenge_id IS NULL AND initial_contact_risk_snapshot IS NULL
        AND governed_contacts_digest IS NOT NULL AND length(governed_contacts_digest)=64)
        """;
    internal const string CaseState = """
        expires_at>created_at AND recovery_expires_at>expires_at AND (((state='PENDING_CODE' AND proof_kind='CODE')
          OR (state='PENDING_PASSKEY' AND proof_kind='PASSKEY') OR (state='PENDING_CONTACT_RISK' AND proof_kind='CONTACT_RISK')
          OR (state='PENDING_GOVERNED_REVIEW' AND proof_kind='GOVERNED_REVIEW')) AND version=1 AND result_id IS NULL AND ended_at IS NULL
          OR (version=2 AND result_id IS NOT NULL AND ended_at>=created_at AND ended_at<expires_at
            AND ((state='CONTAINED' AND goal='ContainSelectedTunnerSession' AND proof_kind='CODE') OR (state='REPAIRED' AND goal='RepairPassword'))))
        """;
    internal const string ResultShape = "((" + RecoveryContactMigrationSql.ResultShape + ") AND governed_disposition_reference IS NULL AND governed_disposition_version IS NULL AND governed_disposition_snapshot IS NULL) OR (" + """
        proof_kind='GOVERNED_REVIEW' AND outcome='PASSWORD_REPAIRED_NO_SESSION' AND set_reference IS NULL AND set_lifecycle_version IS NULL
        AND target_session_id IS NULL AND prior_session_version IS NULL AND ended_session_version IS NULL AND session_outbox_event_id IS NULL
        AND passkey_credential_reference IS NULL AND passkey_version IS NULL AND passkey_usage_version IS NULL AND passkey_ceremony_id IS NULL
        AND passkey_method IS NULL AND passkey_assurance IS NULL AND native_before_digest IS NULL AND native_after_digest IS NULL
        AND contact_reference IS NULL AND contact_version IS NULL AND contact_challenge_id IS NULL AND contact_destination_digest IS NULL AND contact_risk_snapshot IS NULL AND contact_notification_delivery_id IS NULL
        AND governed_disposition_reference IS NOT NULL AND length(governed_disposition_reference)>0 AND governed_disposition_version IS NOT NULL AND governed_disposition_version>=1
        AND governed_disposition_snapshot IS NOT NULL AND jsonb_typeof(governed_disposition_snapshot::jsonb)='object' AND repair_outbox_event_id IS NOT NULL
        AND prior_password_digest IS NOT NULL AND length(prior_password_digest)=64 AND repaired_password_digest IS NOT NULL AND length(repaired_password_digest)=64
        AND prior_security_stamp_digest IS NOT NULL AND length(prior_security_stamp_digest)=64 AND repaired_security_stamp_digest IS NOT NULL AND length(repaired_security_stamp_digest)=64
        AND repaired_security_stamp_digest<>prior_security_stamp_digest AND session_effect IS NOT NULL AND session_effect='InvalidateExistingTunnerSessions'
        AND length(context_digest)=64 AND length(policy_version)>0 AND length(processing_reference)>0 AND length(retention_reference)>0
        AND jsonb_typeof(policy_snapshot::jsonb)='object' AND jsonb_typeof(hierarchy_snapshot::jsonb)='object' AND jsonb_typeof(unresolved_effects_json::jsonb)='array')
        """;
    internal const string BlockPopulatedDowngrade = """
        DO $$ BEGIN
          IF EXISTS(SELECT 1 FROM identity.recovery_cases WHERE proof_kind='GOVERNED_REVIEW') OR
            EXISTS(SELECT 1 FROM identity.recovery_results WHERE proof_kind='GOVERNED_REVIEW') OR
            EXISTS(SELECT 1 FROM identity.recovery_entries WHERE policy_snapshot::jsonb->'Values'->'GovernedRecoveryProfile' IS NOT NULL) THEN
            RAISE EXCEPTION 'Governed recovery history requires a data-preserving downgrade' USING ERRCODE='55000'; END IF;
        END; $$;
        """;
    internal const string Harden = """
        CREATE FUNCTION identity.guard_recovery_governed_case() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN
          IF TG_OP='INSERT' THEN
            IF NEW.state<>'PENDING_GOVERNED_REVIEW' OR NEW.version<>1 OR NEW.result_id IS NOT NULL OR NEW.ended_at IS NOT NULL OR
              COALESCE(length(NEW.policy_snapshot::jsonb#>>'{Values,GovernedRecoveryProfile,DispositionContractReference}'),0)=0 OR
              COALESCE(length(NEW.policy_snapshot::jsonb#>>'{Values,GovernedRecoveryProfile,Version}'),0)=0 OR
              NEW.hierarchy_snapshot::jsonb->>'StrongAuthenticatorUnavailable' IS DISTINCT FROM 'true' OR
              NEW.hierarchy_snapshot::jsonb->>'RecoveryCodeAllowed' IS DISTINCT FROM 'false' OR
              NEW.hierarchy_snapshot::jsonb#>>'{GovernedSelection,RecoveryCodesUnavailable}' IS DISTINCT FROM 'true' OR
              NEW.hierarchy_snapshot::jsonb#>>'{GovernedSelection,SafeContactUnavailable}' IS DISTINCT FROM 'true' OR
              COALESCE(length(NEW.hierarchy_snapshot::jsonb#>>'{GovernedSelection,UnavailabilityEvidenceReference}'),0)=0 OR
              NEW.hierarchy_snapshot::jsonb#>>'{GovernedSelection,ProfileVersion}' IS DISTINCT FROM NEW.policy_snapshot::jsonb#>>'{Values,GovernedRecoveryProfile,Version}' OR
              NOT EXISTS(SELECT 1 FROM accounts.accounts a JOIN identity."AspNetUsers" u ON u."AccountId"=a.account_id
                WHERE a.account_id=NEW.account_id AND a.version=NEW.expected_account_version
                  AND NEW.prior_password_digest=upper(encode(sha256(convert_to(u."PasswordHash",'UTF8')),'hex'))
                  AND NEW.prior_security_stamp_digest=upper(encode(sha256(convert_to(u."SecurityStamp",'UTF8')),'hex'))) THEN
              RAISE EXCEPTION 'Governed pending recovery requires its exact original owner and qualified hierarchy' USING ERRCODE='23514'; END IF;
          ELSIF (to_jsonb(NEW)-ARRAY['state','version','result_id','ended_at']) IS DISTINCT FROM (to_jsonb(OLD)-ARRAY['state','version','result_id','ended_at']) OR
            OLD.state<>'PENDING_GOVERNED_REVIEW' OR NEW.state<>'REPAIRED' OR NEW.version<>OLD.version+1 THEN
            RAISE EXCEPTION 'Original governed recovery is immutable and one-use' USING ERRCODE='23514'; END IF;
          RETURN NEW;
        END; $$;
        CREATE TRIGGER recovery_governed_case_guard BEFORE INSERT OR UPDATE ON identity.recovery_cases FOR EACH ROW
          WHEN(NEW.proof_kind='GOVERNED_REVIEW') EXECUTE FUNCTION identity.guard_recovery_governed_case();
        CREATE FUNCTION identity.guard_recovery_governed_result() RETURNS trigger LANGUAGE plpgsql AS $$
        DECLARE approved_interval jsonb; from_parts text[]; until_parts text[];
          approved_from numeric; approved_until numeric; recorded_instant numeric; includes_from boolean; includes_until boolean;
        BEGIN
          IF TG_OP='UPDATE' THEN RAISE EXCEPTION 'Governed recovery result is immutable' USING ERRCODE='23514'; END IF;
          approved_interval := NEW.governed_disposition_snapshot::jsonb->'EffectiveInterval';
          IF jsonb_typeof(approved_interval) IS DISTINCT FROM 'object' OR
            NOT (approved_interval ?& ARRAY['From','IncludesFrom','Until','IncludesUntil']) OR
            jsonb_typeof(approved_interval->'From') IS DISTINCT FROM 'string' OR
            jsonb_typeof(approved_interval->'IncludesFrom') IS DISTINCT FROM 'boolean' OR
            jsonb_typeof(approved_interval->'IncludesUntil') IS DISTINCT FROM 'boolean' OR
            COALESCE(jsonb_typeof(approved_interval->'Until'),'') NOT IN ('string','null') THEN
            RAISE EXCEPTION 'Governed disposition requires its complete typed execution interval' USING ERRCODE='23514'; END IF;
          -- Canonical DateTimeOffset JSON only. Compare 100ns numeric instants: PostgreSQL must not round an approved bound into acceptance.
          -- Group 1 is whole seconds, group 3 the optional fractional digits, group 4 the explicit offset.
          from_parts := regexp_match(approved_interval->>'From', '^([0-9]{4}-(?:0[1-9]|1[0-2])-(?:0[1-9]|[12][0-9]|3[01])T(?:[01][0-9]|2[0-3]):[0-5][0-9]:[0-5][0-9])([.]([0-9]{1,7}))?(Z|[+-](?:(?:0[0-9]|1[0-3]):[0-5][0-9]|14:00))$');
          IF from_parts IS NULL THEN RAISE EXCEPTION 'Governed disposition interval must use canonical offset timestamps' USING ERRCODE='23514'; END IF;
          IF jsonb_typeof(approved_interval->'Until')='string' THEN
            until_parts := regexp_match(approved_interval->>'Until', '^([0-9]{4}-(?:0[1-9]|1[0-2])-(?:0[1-9]|[12][0-9]|3[01])T(?:[01][0-9]|2[0-3]):[0-5][0-9]:[0-5][0-9])([.]([0-9]{1,7}))?(Z|[+-](?:(?:0[0-9]|1[0-3]):[0-5][0-9]|14:00))$');
            IF until_parts IS NULL THEN RAISE EXCEPTION 'Governed disposition interval must use canonical offset timestamps' USING ERRCODE='23514'; END IF;
          END IF;
          BEGIN
            approved_from := extract(epoch FROM ((from_parts[1]||from_parts[4])::timestamptz))*10000000 + rpad(COALESCE(from_parts[3],''),7,'0')::numeric;
            IF until_parts IS NOT NULL THEN
              approved_until := extract(epoch FROM ((until_parts[1]||until_parts[4])::timestamptz))*10000000 + rpad(COALESCE(until_parts[3],''),7,'0')::numeric;
            END IF;
          EXCEPTION WHEN invalid_datetime_format OR datetime_field_overflow OR invalid_text_representation THEN
            RAISE EXCEPTION 'Governed disposition interval contains an invalid timestamp' USING ERRCODE='23514';
          END;
          includes_from := (approved_interval->>'IncludesFrom')::boolean; includes_until := (approved_interval->>'IncludesUntil')::boolean;
          recorded_instant := extract(epoch FROM NEW.recorded_at)*10000000;
          IF approved_until<approved_from OR (approved_until=approved_from AND NOT (includes_from AND includes_until)) OR
            NOT (recorded_instant>approved_from OR (includes_from AND recorded_instant=approved_from)) OR
            (approved_until IS NOT NULL AND NOT (recorded_instant<approved_until OR (includes_until AND recorded_instant=approved_until))) THEN
            RAISE EXCEPTION 'Governed recovery execution is outside the exact approved interval' USING ERRCODE='23514'; END IF;
          IF NOT EXISTS(SELECT 1 FROM identity.recovery_cases c JOIN identity.recovery_entries n ON n.entry_id=c.entry_id
            JOIN accounts.accounts a ON a.account_id=c.account_id JOIN identity."AspNetUsers" u ON u."AccountId"=c.account_id
            WHERE c.case_id=NEW.case_id AND c.proof_kind='GOVERNED_REVIEW' AND c.state='REPAIRED' AND c.version=2 AND c.result_id=NEW.result_id
              AND n.state='REPAIRED' AND n.version=2 AND n.request_id=c.request_id AND n.browser_digest=c.original_browser_digest AND n.ended_at=c.ended_at
              AND NEW.account_id=c.account_id AND NEW.recorded_at=c.ended_at AND NEW.context_digest=c.context_digest AND NEW.policy_version=c.policy_version
              AND NEW.policy_snapshot=c.policy_snapshot AND NEW.hierarchy_snapshot=c.hierarchy_snapshot AND NEW.unresolved_effects_json=c.unresolved_effects_json
              AND NEW.processing_reference=c.processing_reference AND NEW.retention_reference=c.retention_reference AND NEW.session_effect=c.session_effect
              AND NEW.prior_password_digest=c.prior_password_digest AND NEW.prior_security_stamp_digest=c.prior_security_stamp_digest
              AND NEW.repaired_password_digest=upper(encode(sha256(convert_to(u."PasswordHash",'UTF8')),'hex'))
              AND NEW.repaired_security_stamp_digest=upper(encode(sha256(convert_to(u."SecurityStamp",'UTF8')),'hex')) AND a.version=c.expected_account_version
              AND (NEW.governed_disposition_snapshot::jsonb->>'AuthorizesExactEffect')::boolean
              AND NEW.governed_disposition_snapshot::jsonb->>'DispositionReference'=NEW.governed_disposition_reference
              AND (NEW.governed_disposition_snapshot::jsonb->>'DispositionVersion')::bigint=NEW.governed_disposition_version
              AND NEW.governed_disposition_snapshot::jsonb->>'CaseId'=c.case_id::text AND NEW.governed_disposition_snapshot::jsonb->>'RequestId'=c.request_id::text
              AND NEW.governed_disposition_snapshot::jsonb->>'AccountId'=c.account_id AND (NEW.governed_disposition_snapshot::jsonb->>'OriginalCaseVersion')::bigint=1
              AND (NEW.governed_disposition_snapshot::jsonb->>'AccountVersion')::bigint=c.expected_account_version
              AND NEW.governed_disposition_snapshot::jsonb->>'ContextDigest'=c.context_digest AND NEW.governed_disposition_snapshot::jsonb->>'FactsDigest'=c.facts_digest
              AND NEW.governed_disposition_snapshot::jsonb->>'UserSourceDigest'=c.user_source_digest AND NEW.governed_disposition_snapshot::jsonb->>'ContactsDigest'=c.governed_contacts_digest
              AND NEW.governed_disposition_snapshot::jsonb->>'Goal'=c.goal AND NEW.governed_disposition_snapshot::jsonb->>'SessionEffect'=c.session_effect
              AND NEW.governed_disposition_snapshot::jsonb->>'UnresolvedEffectsJson'=c.unresolved_effects_json
              AND NEW.governed_disposition_snapshot::jsonb->>'EffectsEncoding'='UTF8_BYTE_LENGTH_PREFIXED_FIELDS_V1'
              AND NEW.governed_disposition_snapshot::jsonb->>'EffectsDigest'=upper(encode(sha256(convert_to(
                octet_length(convert_to(c.goal,'UTF8'))::text||':'||c.goal||
                octet_length(convert_to(c.session_effect,'UTF8'))::text||':'||c.session_effect||
                octet_length(convert_to(c.unresolved_effects_json,'UTF8'))::text||':'||c.unresolved_effects_json,'UTF8')),'hex'))
              AND NEW.governed_disposition_snapshot::jsonb->>'RecoveryPolicyVersion'=c.policy_version
              AND NEW.governed_disposition_snapshot::jsonb->>'ProfileVersion'=c.policy_snapshot::jsonb#>>'{Values,GovernedRecoveryProfile,Version}'
              AND NEW.governed_disposition_snapshot::jsonb->>'DispositionContractReference'=c.policy_snapshot::jsonb#>>'{Values,GovernedRecoveryProfile,DispositionContractReference}'
              AND length(NEW.governed_disposition_snapshot::jsonb->>'AuthorityPolicyVersion')>0 AND length(NEW.governed_disposition_snapshot::jsonb->>'ControlEvidenceReference')>0
              AND EXISTS(SELECT 1 FROM platform.outbox_messages o WHERE o.event_id=NEW.repair_outbox_event_id AND o.event_type='IdentitySecurityActivityRequested'
                AND o.operation_id='OP-RECOVERY-CASE-'||replace(c.case_id::text,'-','') AND o.correlation_id=replace(c.request_id::text,'-',''))) THEN
            RAISE EXCEPTION 'Governed result requires the actual exact disposition/password/stamp/outbox effects' USING ERRCODE='23514'; END IF;
          RETURN NEW;
        END; $$;
        CREATE CONSTRAINT TRIGGER recovery_governed_result_guard AFTER INSERT OR UPDATE ON identity.recovery_results DEFERRABLE INITIALLY DEFERRED FOR EACH ROW
          WHEN(NEW.proof_kind='GOVERNED_REVIEW') EXECUTE FUNCTION identity.guard_recovery_governed_result();
        """;
    internal const string RemoveGuards = """
        DROP TRIGGER recovery_governed_result_guard ON identity.recovery_results;
        DROP FUNCTION identity.guard_recovery_governed_result();
        DROP TRIGGER recovery_governed_case_guard ON identity.recovery_cases;
        DROP FUNCTION identity.guard_recovery_governed_case();
        """;
}
