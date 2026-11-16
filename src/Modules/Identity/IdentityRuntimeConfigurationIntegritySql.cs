namespace Tunner.Identity.Persistence;

/// <summary>Installed by the Identity owner migration. Existing platform audit/outbox records are the causal receipts, not candidate claims.</summary>
public static class IdentityRuntimeConfigurationIntegritySql
{
    public const string Install = """
        CREATE FUNCTION identity.runtime_configuration_immutable() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN RAISE EXCEPTION 'Identity configuration history is immutable' USING ERRCODE='55000'; END $$;
        CREATE TRIGGER immutable_runtime_configuration_governance BEFORE UPDATE OR DELETE OR TRUNCATE ON identity.runtime_configuration_governance
          FOR EACH STATEMENT EXECUTE FUNCTION identity.runtime_configuration_immutable();
        CREATE TRIGGER immutable_runtime_configuration_version BEFORE UPDATE OR DELETE OR TRUNCATE ON identity.runtime_configuration_versions
          FOR EACH STATEMENT EXECUTE FUNCTION identity.runtime_configuration_immutable();
        CREATE TRIGGER immutable_runtime_configuration_publication BEFORE UPDATE OR DELETE OR TRUNCATE ON identity.runtime_configuration_publications
          FOR EACH STATEMENT EXECUTE FUNCTION identity.runtime_configuration_immutable();

        CREATE FUNCTION identity.runtime_configuration_has_effect(event_ref uuid, scope_ref text, governance_ref text, operation_ref text,
          target_ref text, digest_ref text, actor_ref text) RETURNS boolean LANGUAGE sql STABLE AS $$
          SELECT COALESCE(EXISTS(SELECT 1 FROM platform.outbox_messages o JOIN audit.events a ON a.event_id=o.event_id
            JOIN identity.runtime_configuration_governance g ON g.scope_key=scope_ref AND g.version=governance_ref
            WHERE o.event_id=event_ref AND o.subject=target_ref AND o.payload->>'reference'=target_ref
              AND g.snapshot::jsonb->'Metadata'->'Scope'=scope_ref::jsonb AND g.snapshot::jsonb->'Metadata'->>'VersionId'=governance_ref
              AND o.source=g.snapshot::jsonb->'Effects'->operation_ref->>'Source'
              AND o.envelope_version=g.snapshot::jsonb->'Effects'->operation_ref->>'EnvelopeVersion'
              AND o.event_type=g.snapshot::jsonb->'Effects'->operation_ref->>'EventType'
              AND o.event_schema_version=g.snapshot::jsonb->'Effects'->operation_ref->>'EventSchemaVersion'
              AND o.data_classification=g.snapshot::jsonb->>'Classification'
              AND o.payload->>'scopeKey'=scope_ref AND o.payload->>'governanceVersion'=governance_ref
              AND o.payload->>'candidateDigest'=digest_ref AND o.payload->>'revision'=o.resource_version
              AND a.operation_id=o.operation_id AND a.correlation_id=o.correlation_id AND a.data_classification=o.data_classification
              AND a.retention_class_ref=g.snapshot::jsonb->>'AuditRetentionClassReference'
              AND a.envelope::jsonb->>'action'=g.snapshot::jsonb->'Effects'->operation_ref->>'AuditAction'
              AND a.envelope::jsonb->>'result'=g.snapshot::jsonb->'Effects'->operation_ref->>'AuditResult'
              AND (actor_ref IS NULL OR a.envelope::jsonb->'actor'->>'principal_id'=actor_ref)
              AND a.envelope::jsonb->'policy_version_refs' @> jsonb_build_array(governance_ref)
              AND a.envelope::jsonb->'evidence_refs' @> jsonb_build_array(g.snapshot::jsonb->>'EvidenceReference',o.payload->>'authorizationEvidenceReference')
              AND EXISTS(SELECT 1 FROM jsonb_array_elements(a.envelope::jsonb->'targets') target
                WHERE target->>'resource_id'=target_ref AND target->>'version'=o.resource_version)),false)
        $$;
        CREATE FUNCTION identity.runtime_configuration_valid_version(version_ref uuid) RETURNS boolean LANGUAGE sql STABLE AS $$
          SELECT COALESCE((SELECT p.state IN ('VALIDATED','APPROVED','PUBLISHED') AND p.version_reference=v.id
            AND (p.scope_key,p.governance_version,p.policy_version_reference,p.payload,p.digest,p.contributors_json,p.model_origin)=
                (v.scope_key,v.governance_version,v.policy_version_reference,v.payload,v.digest,v.contributors_json,v.model_origin)
            AND identity.runtime_configuration_has_effect(v.validation_event_reference,v.scope_key,v.governance_version,'Validate',replace(v.id::text,'-',''),v.digest,NULL)
            FROM identity.runtime_configuration_versions v JOIN identity.runtime_configuration_proposals p ON p.id=v.proposal_reference
            WHERE v.id=version_ref),false)
        $$;
        CREATE FUNCTION identity.runtime_configuration_valid_publication(publication_ref uuid) RETURNS boolean LANGUAGE sql STABLE AS $$
          SELECT COALESCE((SELECT identity.runtime_configuration_valid_version(v.id) AND p.state='PUBLISHED' AND x.scope_key=v.scope_key
            AND (x.approver_reference,x.approval_evidence_reference,x.approval_event_reference)=
                (p.approver_reference,p.approval_evidence_reference,p.approval_event_reference)
            AND length(x.approver_reference)>0 AND length(x.approval_evidence_reference)>0
            AND identity.runtime_configuration_has_effect(x.approval_event_reference,v.scope_key,v.governance_version,'Approve',replace(p.id::text,'-',''),v.digest,x.approver_reference)
            AND EXISTS(SELECT 1 FROM platform.outbox_messages approval WHERE approval.event_id=x.approval_event_reference
              AND approval.payload->>'versionReference'=replace(v.id::text,'-','') AND approval.payload->>'authorizationEvidenceReference'=x.approval_evidence_reference)
            AND (NOT(g.snapshot::jsonb->>'SeparateAuthorApprover')::boolean OR NOT(v.contributors_json::jsonb ? x.approver_reference))
            AND (NOT(g.snapshot::jsonb->>'SeparateAuthorPublisher')::boolean OR NOT(v.contributors_json::jsonb ? x.publisher_reference))
            AND (NOT(g.snapshot::jsonb->>'SeparateApproverPublisher')::boolean OR x.approver_reference<>x.publisher_reference)
            AND x.operation IN ('Publish','Rollback')
            AND identity.runtime_configuration_has_effect(x.publication_event_reference,v.scope_key,v.governance_version,x.operation,replace(x.id::text,'-',''),v.digest,x.publisher_reference)
            AND EXISTS(SELECT 1 FROM platform.outbox_messages event JOIN audit.events a ON a.event_id=event.event_id
              WHERE event.event_id=x.publication_event_reference AND event.payload->>'versionReference'=replace(v.id::text,'-','')
                AND (event.payload->>'effectiveFrom')::timestamptz=x.effective_from AND (event.payload->>'includesFrom')::boolean=x.includes_from
                AND (event.payload->>'effectiveUntil')::timestamptz IS NOT DISTINCT FROM x.effective_until
                AND (event.payload->>'includesUntil')::boolean=x.includes_until
                AND event.payload->>'supersedesPublicationReference' IS NOT DISTINCT FROM replace(x.supersedes_publication_reference::text,'-','')
                AND event.payload->>'authorizationEvidenceReference'=x.publisher_evidence_reference
                AND a.envelope::jsonb->'policy_version_refs' @> jsonb_build_array(x.publisher_authorization_version))
            AND (x.supersedes_publication_reference IS NULL OR EXISTS(SELECT 1 FROM identity.runtime_configuration_publications prior
              WHERE prior.id=x.supersedes_publication_reference AND prior.scope_key=x.scope_key AND prior.effective_from<x.effective_from))
            FROM identity.runtime_configuration_publications x JOIN identity.runtime_configuration_versions v ON v.id=x.version_reference
            JOIN identity.runtime_configuration_proposals p ON p.id=v.proposal_reference
            JOIN identity.runtime_configuration_governance g ON g.scope_key=v.scope_key AND g.version=v.governance_version WHERE x.id=publication_ref),false)
        $$;
        CREATE FUNCTION identity.runtime_configuration_transition() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN
          IF TG_OP='INSERT' THEN
            IF NEW.state<>'DRAFT' OR NEW.revision<>1 OR NEW.version_reference IS NOT NULL OR NEW.approver_reference IS NOT NULL
              OR NEW.approval_evidence_reference IS NOT NULL OR NEW.approval_event_reference IS NOT NULL OR NEW.last_operation<>'CreateDraft' OR NEW.last_target_reference<>NEW.id
            THEN RAISE EXCEPTION 'Invalid Identity configuration draft' USING ERRCODE='23514'; END IF;
          ELSE
            IF OLD.state='PUBLISHED' OR NEW.revision<>OLD.revision+1 OR (NEW.id,NEW.scope_key,NEW.governance_version) IS DISTINCT FROM (OLD.id,OLD.scope_key,OLD.governance_version)
            THEN RAISE EXCEPTION 'Invalid Identity configuration identity' USING ERRCODE='23514'; END IF;
            IF NOT COALESCE(((NEW.last_operation='UpdateDraft' AND NEW.state='DRAFT' AND NEW.version_reference IS NULL AND NEW.approver_reference IS NULL
                AND NEW.approval_evidence_reference IS NULL AND NEW.approval_event_reference IS NULL AND NEW.last_target_reference=NEW.id
                AND OLD.contributors_json::jsonb <@ NEW.contributors_json::jsonb AND (NOT OLD.model_origin OR NEW.model_origin))
              OR (NEW.last_operation='Validate' AND OLD.state='DRAFT' AND NEW.state='VALIDATED' AND NEW.version_reference=NEW.last_target_reference
                AND NEW.approver_reference IS NULL AND NEW.approval_evidence_reference IS NULL AND NEW.approval_event_reference IS NULL
                AND (NEW.payload,NEW.digest,NEW.policy_version_reference,NEW.contributors_json,NEW.model_origin)=(OLD.payload,OLD.digest,OLD.policy_version_reference,OLD.contributors_json,OLD.model_origin))
              OR (NEW.last_operation='Approve' AND OLD.state='VALIDATED' AND NEW.state='APPROVED' AND NEW.last_target_reference=NEW.id
                AND NEW.approval_event_reference=NEW.last_event_reference AND NEW.approver_reference=NEW.last_actor_reference AND length(NEW.approval_evidence_reference)>0
                AND (NEW.version_reference,NEW.payload,NEW.digest,NEW.policy_version_reference,NEW.contributors_json,NEW.model_origin)=(OLD.version_reference,OLD.payload,OLD.digest,OLD.policy_version_reference,OLD.contributors_json,OLD.model_origin))
              OR (NEW.last_operation='Publish' AND OLD.state='APPROVED' AND NEW.state='PUBLISHED'
                AND (NEW.version_reference,NEW.payload,NEW.digest,NEW.policy_version_reference,NEW.contributors_json,NEW.model_origin,NEW.approver_reference,NEW.approval_evidence_reference,NEW.approval_event_reference)=
                    (OLD.version_reference,OLD.payload,OLD.digest,OLD.policy_version_reference,OLD.contributors_json,OLD.model_origin,OLD.approver_reference,OLD.approval_evidence_reference,OLD.approval_event_reference))),false)
            THEN RAISE EXCEPTION 'Invalid Identity configuration transition' USING ERRCODE='23514'; END IF;
          END IF;
          RETURN NEW;
        END $$;
        CREATE TRIGGER runtime_configuration_transition BEFORE INSERT OR UPDATE ON identity.runtime_configuration_proposals
          FOR EACH ROW EXECUTE FUNCTION identity.runtime_configuration_transition();
        CREATE FUNCTION identity.runtime_configuration_effect_guard() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN
          IF TG_TABLE_NAME='runtime_configuration_proposals' THEN
            IF NOT identity.runtime_configuration_has_effect(NEW.last_event_reference,NEW.scope_key,NEW.governance_version,NEW.last_operation,
              replace(NEW.last_target_reference::text,'-',''),NEW.digest,NEW.last_actor_reference)
              OR NOT EXISTS(SELECT 1 FROM platform.outbox_messages effect WHERE effect.event_id=NEW.last_event_reference AND effect.resource_version=NEW.revision::text)
              OR (NEW.state<>'DRAFT' AND NOT EXISTS(SELECT 1 FROM identity.runtime_configuration_versions v
                WHERE v.id=NEW.version_reference AND v.proposal_reference=NEW.id AND v.scope_key=NEW.scope_key AND v.governance_version=NEW.governance_version
                  AND v.payload=NEW.payload AND v.digest=NEW.digest AND v.policy_version_reference=NEW.policy_version_reference))
              OR (NEW.state='PUBLISHED' AND NOT EXISTS(SELECT 1 FROM identity.runtime_configuration_publications x
                WHERE x.id=NEW.last_target_reference AND x.version_reference=NEW.version_reference AND x.publication_event_reference=NEW.last_event_reference))
            THEN RAISE EXCEPTION 'Identity configuration requires its causal effect' USING ERRCODE='23514'; END IF;
          ELSIF TG_TABLE_NAME='runtime_configuration_versions' THEN
            IF NOT identity.runtime_configuration_valid_version(NEW.id) THEN RAISE EXCEPTION 'Identity configuration requires validated version' USING ERRCODE='23514'; END IF;
          ELSE
            IF NOT identity.runtime_configuration_valid_publication(NEW.id) THEN RAISE EXCEPTION 'Identity configuration requires approved publication' USING ERRCODE='23514'; END IF;
          END IF;
          RETURN NEW;
        END $$;
        CREATE CONSTRAINT TRIGGER runtime_configuration_proposal_effect AFTER INSERT OR UPDATE ON identity.runtime_configuration_proposals
          DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION identity.runtime_configuration_effect_guard();
        CREATE CONSTRAINT TRIGGER runtime_configuration_version_effect AFTER INSERT ON identity.runtime_configuration_versions
          DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION identity.runtime_configuration_effect_guard();
        CREATE CONSTRAINT TRIGGER runtime_configuration_publication_effect AFTER INSERT ON identity.runtime_configuration_publications
          DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION identity.runtime_configuration_effect_guard();
        """;
}
