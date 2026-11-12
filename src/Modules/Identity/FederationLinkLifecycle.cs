namespace Tunner.Identity.Persistence;

/// <summary>Private link integrity; policy and current owner proof remain independent authorities.</summary>
internal static class FederationLinkLifecycle
{
    internal const string QualificationConstraint = """
        (link_state='LEGACY_UNQUALIFIED' AND qualified_client_id IS NULL AND qualification_profile_version IS NULL
          AND issuer_mapping_evidence_reference IS NULL AND qualification_evidence_reference IS NULL)
        OR (link_state IN ('ACTIVE','UNLINKED') AND qualified_client_id IS NOT NULL AND length(qualified_client_id)>0
          AND qualification_profile_version IS NOT NULL AND length(qualification_profile_version)>0
          AND issuer_mapping_evidence_reference IS NOT NULL AND length(issuer_mapping_evidence_reference)>0
          AND qualification_evidence_reference IS NOT NULL AND length(qualification_evidence_reference)>0)
        """;

    // Add this alongside, never in place of, the existing immutable Account/namespace/subject/version guard.
    // State does not select an unlink session effect or authorize an operation.
    internal const string Harden = """
        CREATE FUNCTION identity.guard_external_qualification() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN
          IF TG_OP='INSERT' THEN
            IF NEW.link_state='UNLINKED' THEN
              RAISE EXCEPTION 'External identity must begin legacy or explicitly qualified' USING ERRCODE='23514';
            END IF;
            RETURN NEW;
          END IF;
          IF OLD.qualified_client_id IS NOT NULL AND NEW.qualified_client_id IS DISTINCT FROM OLD.qualified_client_id THEN
            RAISE EXCEPTION 'Qualified client identity requires an explicit identity migration' USING ERRCODE='23514';
          END IF;
          IF ROW(NEW.link_state,NEW.qualified_client_id,NEW.qualification_profile_version,NEW.issuer_mapping_evidence_reference,NEW.qualification_evidence_reference)
             IS NOT DISTINCT FROM ROW(OLD.link_state,OLD.qualified_client_id,OLD.qualification_profile_version,OLD.issuer_mapping_evidence_reference,OLD.qualification_evidence_reference) THEN
            RETURN NEW;
          END IF;
          IF NOT ((OLD.link_state='LEGACY_UNQUALIFIED' AND NEW.link_state='ACTIVE') OR
                  (OLD.link_state='ACTIVE' AND NEW.link_state='UNLINKED') OR
                  (OLD.link_state='UNLINKED' AND NEW.link_state='ACTIVE')) OR
             NEW.evidence_reference IS NOT DISTINCT FROM OLD.evidence_reference THEN
            RAISE EXCEPTION 'External identity lifecycle requires a fresh evidenced operation' USING ERRCODE='23514';
          END IF;
          IF NEW.link_state='UNLINKED' AND
             ROW(NEW.qualified_client_id,NEW.qualification_profile_version,NEW.issuer_mapping_evidence_reference,NEW.qualification_evidence_reference)
             IS DISTINCT FROM ROW(OLD.qualified_client_id,OLD.qualification_profile_version,OLD.issuer_mapping_evidence_reference,OLD.qualification_evidence_reference) THEN
            RAISE EXCEPTION 'Unlink preserves its original qualification lineage' USING ERRCODE='23514';
          END IF;
          RETURN NEW;
        END; $$;
        CREATE TRIGGER external_qualification_guard BEFORE INSERT OR UPDATE ON identity.external_identity_links
          FOR EACH ROW EXECUTE FUNCTION identity.guard_external_qualification();
        """;
}
