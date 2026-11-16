namespace Tunner.PresentationContent;

/// <summary>Append-only owner history. The owner migration installs this in addition to the EF model; no foreign domain table is changed.</summary>
public static class ContentIntegritySql
{
    public const string Install = """
        CREATE FUNCTION presentation_content.reject_history_mutation() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN RAISE EXCEPTION 'Presentation content history is immutable' USING ERRCODE='55000'; END $$;
        CREATE TRIGGER immutable_content_profile BEFORE UPDATE OR DELETE OR TRUNCATE ON presentation_content.profiles
          FOR EACH STATEMENT EXECUTE FUNCTION presentation_content.reject_history_mutation();
        CREATE TRIGGER immutable_content_definition BEFORE UPDATE OR DELETE OR TRUNCATE ON presentation_content.definitions
          FOR EACH STATEMENT EXECUTE FUNCTION presentation_content.reject_history_mutation();
        CREATE TRIGGER immutable_content_variant BEFORE UPDATE OR DELETE OR TRUNCATE ON presentation_content.variants
          FOR EACH STATEMENT EXECUTE FUNCTION presentation_content.reject_history_mutation();
        CREATE TRIGGER immutable_content_version BEFORE UPDATE OR DELETE OR TRUNCATE ON presentation_content.versions
          FOR EACH STATEMENT EXECUTE FUNCTION presentation_content.reject_history_mutation();
        CREATE TRIGGER immutable_content_publication BEFORE UPDATE OR DELETE OR TRUNCATE ON presentation_content.publications
          FOR EACH STATEMENT EXECUTE FUNCTION presentation_content.reject_history_mutation();

        CREATE FUNCTION presentation_content.has_effect(event_ref uuid, scope_ref text, profile_ref text, operation_ref text,
          target_ref text, digest_ref text, actor_ref text) RETURNS boolean LANGUAGE sql STABLE AS $$
          SELECT COALESCE(EXISTS (
            SELECT 1 FROM platform.outbox_messages o JOIN audit.events a ON a.event_id=o.event_id
            JOIN presentation_content.profiles p ON p."Scope"=scope_ref AND p."Version"=profile_ref
            WHERE o.event_id=event_ref AND o.subject=target_ref
              AND o.source=p."Snapshot"::jsonb->'Effects'->operation_ref->>'Source'
              AND o.envelope_version=p."Snapshot"::jsonb->'Effects'->operation_ref->>'EnvelopeVersion'
              AND o.event_type=p."Snapshot"::jsonb->'Effects'->operation_ref->>'EventType'
              AND o.event_schema_version=p."Snapshot"::jsonb->'Effects'->operation_ref->>'EventSchemaVersion'
              AND o.data_classification=p."Snapshot"::jsonb->>'Classification'
              AND o.payload->>'reference'=target_ref AND o.payload->>'scope'=scope_ref
              AND o.payload->>'profileVersion'=profile_ref AND o.payload->>'contentDigest'=digest_ref
              AND o.payload->>'revision'=o.resource_version
              AND a.operation_id=o.operation_id AND a.correlation_id=o.correlation_id
              AND a.data_classification=o.data_classification
              AND a.retention_class_ref=p."Snapshot"::jsonb->>'AuditRetentionClassReference'
              AND a.envelope::jsonb->>'action'=p."Snapshot"::jsonb->'Effects'->operation_ref->>'AuditAction'
              AND a.envelope::jsonb->>'result'=p."Snapshot"::jsonb->'Effects'->operation_ref->>'AuditResult'
              AND (actor_ref IS NULL OR a.envelope::jsonb->'actor'->>'principal_id'=actor_ref)
              AND a.envelope::jsonb->'policy_version_refs' @> jsonb_build_array(profile_ref)
              AND a.envelope::jsonb->'evidence_refs' @> jsonb_build_array(o.payload->>'authorizationEvidenceReference')
              AND EXISTS(SELECT 1 FROM jsonb_array_elements(a.envelope::jsonb->'targets') t
                WHERE t->>'resource_id'=target_ref AND t->>'version'=o.resource_version)
          ),false)
        $$;

        CREATE FUNCTION presentation_content.valid_version(version_ref uuid) RETURNS boolean LANGUAGE sql STABLE AS $$
          SELECT COALESCE((SELECT c."State" IN ('VALIDATED','APPROVED','PUBLISHED') AND c."VersionReference"=v."Id"
            AND c."Scope"=v."Scope" AND c."ProfileVersion"=v."ProfileVersion" AND c."Key"=v."Key"
            AND c."VariantReference"=v."VariantReference" AND c."Text"=v."Text" AND c."LastDigest"=v."TextDigest"
            AND c."ContributorsJson"=v."ContributorsJson" AND c."ModelOrigin"=v."ModelOrigin"
            AND c."SourceEvidenceReference"=v."SourceEvidenceReference" AND variant."Snapshot"::jsonb->>'Locale'=v."Locale"
            AND presentation_content.has_effect(v."ValidationEventReference",v."Scope",v."ProfileVersion",'Validate',
              replace(v."Id"::text,'-',''),v."TextDigest",NULL)
            FROM presentation_content.versions v JOIN presentation_content.proposals c ON c."Id"=v."ProposalReference"
            JOIN presentation_content.variants variant ON variant."Scope"=v."Scope" AND variant."ProfileVersion"=v."ProfileVersion" AND variant."Reference"=v."VariantReference"
            WHERE v."Id"=version_ref),false)
        $$;

        CREATE FUNCTION presentation_content.valid_publication(publication_ref uuid) RETURNS boolean LANGUAGE sql STABLE AS $$
          SELECT COALESCE((SELECT presentation_content.valid_version(v."Id") AND c."State"='PUBLISHED'
            AND x."Scope"=v."Scope" AND x."ProfileVersion"=v."ProfileVersion" AND x."Key"=v."Key" AND x."VariantReference"=v."VariantReference"
            AND x."ApproverReference" IS NOT DISTINCT FROM c."ApproverReference"
            AND x."ApprovalEvidenceReference" IS NOT DISTINCT FROM c."ApprovalEvidenceReference"
            AND x."ApprovalEventReference" IS NOT DISTINCT FROM c."ApprovalEventReference"
            AND (x."ApproverReference" IS NULL OR (
              presentation_content.has_effect(x."ApprovalEventReference",x."Scope",x."ProfileVersion",'Approve',
                replace(c."Id"::text,'-',''),v."TextDigest",x."ApproverReference")
              AND EXISTS(SELECT 1 FROM platform.outbox_messages approval WHERE approval.event_id=x."ApprovalEventReference"
                AND approval.payload->>'versionReference'=replace(v."Id"::text,'-','')
                AND approval.payload->>'authorizationEvidenceReference'=x."ApprovalEvidenceReference")))
            AND (NOT (definition."Snapshot"::jsonb->>'RequiresApproval')::boolean OR
              (length(x."ApproverReference")>0 AND length(x."ApprovalEvidenceReference")>0))
            AND (NOT (profile."Snapshot"::jsonb->>'SeparateAuthorApprover')::boolean OR x."ApproverReference" IS NULL OR NOT(v."ContributorsJson"::jsonb ? x."ApproverReference"))
            AND (NOT (profile."Snapshot"::jsonb->>'SeparateAuthorPublisher')::boolean OR NOT(v."ContributorsJson"::jsonb ? x."PublisherReference"))
            AND (NOT (profile."Snapshot"::jsonb->>'SeparateApproverPublisher')::boolean OR x."ApproverReference" IS DISTINCT FROM x."PublisherReference")
            AND x."Operation" IN ('Publish','Rollback')
            AND presentation_content.has_effect(x."PublicationEventReference",x."Scope",x."ProfileVersion",x."Operation",
              replace(x."Id"::text,'-',''),v."TextDigest",x."PublisherReference")
            AND EXISTS(SELECT 1 FROM audit.events a WHERE a.event_id=x."PublicationEventReference"
              AND a.envelope::jsonb->'evidence_refs' @> jsonb_build_array(x."PublisherEvidenceReference")
              AND a.envelope::jsonb->'policy_version_refs' @> jsonb_build_array(x."PublisherAuthorizationVersion"))
            AND (x."SupersedesPublicationReference" IS NULL OR EXISTS(SELECT 1 FROM presentation_content.publications prior
              WHERE prior."Id"=x."SupersedesPublicationReference" AND prior."Scope"=x."Scope" AND prior."ProfileVersion"=x."ProfileVersion"
                AND prior."Key"=x."Key" AND prior."VariantReference"=x."VariantReference" AND prior."From"<x."From"))
            FROM presentation_content.publications x JOIN presentation_content.versions v ON v."Id"=x."VersionReference"
            JOIN presentation_content.proposals c ON c."Id"=v."ProposalReference"
            JOIN presentation_content.profiles profile ON profile."Scope"=x."Scope" AND profile."Version"=x."ProfileVersion"
            JOIN presentation_content.definitions definition ON definition."Scope"=x."Scope" AND definition."ProfileVersion"=x."ProfileVersion" AND definition."Key"=x."Key"
            WHERE x."Id"=publication_ref),false)
        $$;

        CREATE FUNCTION presentation_content.guard_proposal_transition() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN
          IF TG_OP='INSERT' THEN
            IF NEW."State"<>'DRAFT' OR NEW."Revision"<>1 OR NEW."VersionReference" IS NOT NULL OR NEW."ApproverReference" IS NOT NULL
              OR NEW."ApprovalEvidenceReference" IS NOT NULL OR NEW."ApprovalEventReference" IS NOT NULL OR NEW."LastOperation"<>'CreateDraft' OR NEW."LastTargetReference"<>NEW."Id"
            THEN RAISE EXCEPTION 'Invalid content draft insertion' USING ERRCODE='23514'; END IF;
          ELSE
            IF OLD."State"='PUBLISHED' OR NEW."Revision"<>OLD."Revision"+1 OR
              (NEW."Id",NEW."Scope",NEW."ProfileVersion",NEW."Key",NEW."VariantReference",NEW."SourceEvidenceReference") IS DISTINCT FROM
              (OLD."Id",OLD."Scope",OLD."ProfileVersion",OLD."Key",OLD."VariantReference",OLD."SourceEvidenceReference")
            THEN RAISE EXCEPTION 'Invalid content transition identity' USING ERRCODE='23514'; END IF;
            IF NOT ((NEW."LastOperation"='UpdateDraft' AND NEW."State"='DRAFT' AND NEW."VersionReference" IS NULL
                AND NEW."ApproverReference" IS NULL AND NEW."ApprovalEvidenceReference" IS NULL AND NEW."ApprovalEventReference" IS NULL AND NEW."LastTargetReference"=NEW."Id"
                AND OLD."ContributorsJson"::jsonb <@ NEW."ContributorsJson"::jsonb AND (NOT OLD."ModelOrigin" OR NEW."ModelOrigin"))
              OR (NEW."LastOperation"='Validate' AND OLD."State"='DRAFT' AND NEW."State"='VALIDATED' AND NEW."VersionReference"=NEW."LastTargetReference"
                AND NEW."Text"=OLD."Text" AND NEW."ContributorsJson"=OLD."ContributorsJson" AND NEW."ModelOrigin"=OLD."ModelOrigin"
                AND NEW."ApproverReference" IS NULL AND NEW."ApprovalEvidenceReference" IS NULL AND NEW."ApprovalEventReference" IS NULL)
              OR (NEW."LastOperation"='Approve' AND OLD."State"='VALIDATED' AND NEW."State"='APPROVED' AND NEW."LastTargetReference"=NEW."Id"
                AND NEW."VersionReference"=OLD."VersionReference" AND NEW."Text"=OLD."Text" AND NEW."ContributorsJson"=OLD."ContributorsJson"
                AND NEW."ModelOrigin"=OLD."ModelOrigin" AND NEW."ApproverReference"=NEW."LastActorReference" AND length(NEW."ApprovalEvidenceReference")>0
                AND NEW."ApprovalEventReference"=NEW."LastEventReference")
              OR (NEW."LastOperation"='Publish' AND OLD."State" IN ('VALIDATED','APPROVED') AND NEW."State"='PUBLISHED'
                AND NEW."VersionReference"=OLD."VersionReference" AND NEW."Text"=OLD."Text" AND NEW."ContributorsJson"=OLD."ContributorsJson"
                AND NEW."ModelOrigin"=OLD."ModelOrigin" AND NEW."ApproverReference" IS NOT DISTINCT FROM OLD."ApproverReference"
                AND NEW."ApprovalEvidenceReference" IS NOT DISTINCT FROM OLD."ApprovalEvidenceReference"
                AND NEW."ApprovalEventReference" IS NOT DISTINCT FROM OLD."ApprovalEventReference"))
            THEN RAISE EXCEPTION 'Invalid content state transition' USING ERRCODE='23514'; END IF;
          END IF;
          RETURN NEW;
        END $$;
        CREATE TRIGGER content_proposal_transition BEFORE INSERT OR UPDATE ON presentation_content.proposals
          FOR EACH ROW EXECUTE FUNCTION presentation_content.guard_proposal_transition();

        CREATE FUNCTION presentation_content.guard_causal_effect() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN
          IF TG_TABLE_NAME='proposals' THEN
            IF NOT presentation_content.has_effect(NEW."LastEventReference",NEW."Scope",NEW."ProfileVersion",NEW."LastOperation",
              replace(NEW."LastTargetReference"::text,'-',''),NEW."LastDigest",NEW."LastActorReference")
              OR (NEW."State"='PUBLISHED' AND NOT EXISTS(SELECT 1 FROM presentation_content.publications x
                WHERE x."Id"=NEW."LastTargetReference" AND x."VersionReference"=NEW."VersionReference" AND x."PublicationEventReference"=NEW."LastEventReference"))
            THEN RAISE EXCEPTION 'Content transition requires its exact causal effect' USING ERRCODE='23514'; END IF;
          ELSIF TG_TABLE_NAME='versions' THEN
            IF NOT presentation_content.valid_version(NEW."Id") THEN RAISE EXCEPTION 'Content version requires exact validated owner evidence' USING ERRCODE='23514'; END IF;
          ELSE
            IF NOT presentation_content.valid_publication(NEW."Id") THEN RAISE EXCEPTION 'Content publication requires exact approved owner evidence' USING ERRCODE='23514'; END IF;
          END IF;
          RETURN NEW;
        END $$;
        CREATE CONSTRAINT TRIGGER content_proposal_effect AFTER INSERT OR UPDATE ON presentation_content.proposals
          DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION presentation_content.guard_causal_effect();
        CREATE CONSTRAINT TRIGGER content_version_effect AFTER INSERT ON presentation_content.versions
          DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION presentation_content.guard_causal_effect();
        CREATE CONSTRAINT TRIGGER content_publication_effect AFTER INSERT ON presentation_content.publications
          DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION presentation_content.guard_causal_effect();
        """;
}
