namespace Tunner.PresentationContent;

/// <summary>Versioned owner SQL: materialize the selected profile JSON once per validity invocation; no validity result is cached.</summary>
internal static class ContentIntegritySnapshotReadSql
{
    internal const string Upgrade = """
        CREATE OR REPLACE FUNCTION presentation_content.has_effect(event_ref uuid, scope_ref text, profile_ref text, operation_ref text,
          target_ref text, digest_ref text, actor_ref text) RETURNS boolean LANGUAGE sql STABLE AS $$
          WITH profile_value AS MATERIALIZED (
            SELECT p."Snapshot"::jsonb AS snapshot
            FROM presentation_content.profiles p
            WHERE p."Scope"=scope_ref AND p."Version"=profile_ref
          )
          SELECT COALESCE(EXISTS (
            SELECT 1 FROM platform.outbox_messages o JOIN audit.events a ON a.event_id=o.event_id
            JOIN profile_value p ON true
            WHERE o.event_id=event_ref AND o.subject=target_ref
              AND o.source=p.snapshot->'Effects'->operation_ref->>'Source'
              AND o.envelope_version=p.snapshot->'Effects'->operation_ref->>'EnvelopeVersion'
              AND o.event_type=p.snapshot->'Effects'->operation_ref->>'EventType'
              AND o.event_schema_version=p.snapshot->'Effects'->operation_ref->>'EventSchemaVersion'
              AND o.data_classification=p.snapshot->>'Classification'
              AND o.payload->>'reference'=target_ref AND o.payload->>'scope'=scope_ref
              AND o.payload->>'profileVersion'=profile_ref AND o.payload->>'contentDigest'=digest_ref
              AND o.payload->>'revision'=o.resource_version
              AND a.operation_id=o.operation_id AND a.correlation_id=o.correlation_id
              AND a.data_classification=o.data_classification
              AND a.retention_class_ref=p.snapshot->>'AuditRetentionClassReference'
              AND a.envelope::jsonb->>'action'=p.snapshot->'Effects'->operation_ref->>'AuditAction'
              AND a.envelope::jsonb->>'result'=p.snapshot->'Effects'->operation_ref->>'AuditResult'
              AND (actor_ref IS NULL OR a.envelope::jsonb->'actor'->>'principal_id'=actor_ref)
              AND a.envelope::jsonb->'policy_version_refs' @> jsonb_build_array(profile_ref)
              AND a.envelope::jsonb->'evidence_refs' @> jsonb_build_array(o.payload->>'authorizationEvidenceReference')
              AND EXISTS(SELECT 1 FROM jsonb_array_elements(a.envelope::jsonb->'targets') t
                WHERE t->>'resource_id'=target_ref AND t->>'version'=o.resource_version)
          ),false)
        $$;

        CREATE OR REPLACE FUNCTION presentation_content.valid_publication(publication_ref uuid) RETURNS boolean LANGUAGE sql STABLE AS $$
          WITH profile_value AS MATERIALIZED (
            SELECT p."Scope",p."Version",p."Snapshot"::jsonb AS snapshot
            FROM presentation_content.profiles p
            JOIN presentation_content.publications selected
              ON selected."Scope"=p."Scope" AND selected."ProfileVersion"=p."Version"
            WHERE selected."Id"=publication_ref
          )
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
            AND (NOT (profile.snapshot->>'SeparateAuthorApprover')::boolean OR x."ApproverReference" IS NULL OR NOT(v."ContributorsJson"::jsonb ? x."ApproverReference"))
            AND (NOT (profile.snapshot->>'SeparateAuthorPublisher')::boolean OR NOT(v."ContributorsJson"::jsonb ? x."PublisherReference"))
            AND (NOT (profile.snapshot->>'SeparateApproverPublisher')::boolean OR x."ApproverReference" IS DISTINCT FROM x."PublisherReference")
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
            JOIN profile_value profile ON profile."Scope"=x."Scope" AND profile."Version"=x."ProfileVersion"
            JOIN presentation_content.definitions definition ON definition."Scope"=x."Scope" AND definition."ProfileVersion"=x."ProfileVersion" AND definition."Key"=x."Key"
            WHERE x."Id"=publication_ref),false)
        $$;
        """;

    // Exact pre-upgrade definitions for this reversible, data-preserving owner migration.
    internal const string Restore = """
        CREATE OR REPLACE FUNCTION presentation_content.has_effect(event_ref uuid, scope_ref text, profile_ref text, operation_ref text,
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

        CREATE OR REPLACE FUNCTION presentation_content.valid_publication(publication_ref uuid) RETURNS boolean LANGUAGE sql STABLE AS $$
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
        """;
}
