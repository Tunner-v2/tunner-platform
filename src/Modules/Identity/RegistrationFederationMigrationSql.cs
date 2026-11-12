namespace Tunner.Identity.Persistence;

// Root creates a FORWARD migration only after the complete native producer/attempt-owner patch is reviewed.
internal static class RegistrationFederationMigrationSql
{
    internal const string ModeConstraint = """
        ("Mode"='PASSWORD' AND "FederationAttemptId" IS NULL AND "FederationLinkId" IS NULL AND "FederationProvider" IS NULL
          AND "FederationOrigin" IS NULL AND "FederationProfileSnapshot" IS NULL AND "FederationContactMappingEvidenceReference" IS NULL
          AND "FederationVerifiedContactDigest" IS NULL AND "FederationContactVerifiedAt" IS NULL AND "FederationContactVersion" IS NULL)
        OR ("Mode"='FEDERATED' AND "FederationAttemptId" IS NOT NULL AND "FederationLinkId" IS NOT NULL
          AND "AccountId" IS NOT NULL AND "ContactId" IS NOT NULL AND "ProtectedCredential" IS NULL
          AND "FederationProvider" IN ('Google','Microsoft') AND "FederationOrigin" IS NOT NULL
          AND "FederationProfileSnapshot" IS NOT NULL AND jsonb_typeof("FederationProfileSnapshot"::jsonb)='object'
          AND "FederationContactMappingEvidenceReference" IS NOT NULL AND length("FederationContactMappingEvidenceReference")>0
          AND "FederationVerifiedContactDigest" IS NOT NULL AND length("FederationVerifiedContactDigest")=64
          AND "FederationContactVerifiedAt" IS NOT NULL AND "FederationContactVerifiedAt"<="CreatedAt"
          AND "FederationContactVersion" IS NOT NULL AND "FederationContactVersion">=1
          AND "Stage" IN ('PERSONAL_INFORMATION','CONSENT_REQUIRED','COMPLETE'))
        """;

    internal const string Harden = """
        CREATE FUNCTION identity.guard_registration_federation_source() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN
          IF ROW(NEW."Mode",NEW."FederationAttemptId",NEW."FederationLinkId",NEW."FederationProvider",NEW."FederationOrigin",
                 NEW."FederationProfileSnapshot",NEW."FederationContactMappingEvidenceReference",NEW."FederationVerifiedContactDigest",NEW."FederationContactVerifiedAt",NEW."FederationContactVersion")
             IS DISTINCT FROM ROW(OLD."Mode",OLD."FederationAttemptId",OLD."FederationLinkId",OLD."FederationProvider",OLD."FederationOrigin",
                 OLD."FederationProfileSnapshot",OLD."FederationContactMappingEvidenceReference",OLD."FederationVerifiedContactDigest",OLD."FederationContactVerifiedAt",OLD."FederationContactVersion") THEN
            RAISE EXCEPTION 'Original registration source is immutable' USING ERRCODE='23514';
          END IF;
          RETURN NEW;
        END; $$;
        CREATE TRIGGER registration_federation_source_guard BEFORE UPDATE ON identity.registration_operations
          FOR EACH ROW EXECUTE FUNCTION identity.guard_registration_federation_source();
        """;

    internal const string AttemptRegistrationConstraint = """
        resolution IN ('UNRESOLVED','LINKED_SIGN_IN','REGISTRATION_INTAKE')
        AND (resolution<>'LINKED_SIGN_IN' OR purpose='SignIn')
        AND ((registration_browser_digest IS NULL AND registration_idempotency_key IS NULL AND registration_browser_expires_at IS NULL)
          OR (purpose='SignIn' AND provider='Google' AND registration_browser_digest IS NOT NULL AND length(registration_browser_digest)=64
            AND registration_idempotency_key IS NOT NULL AND registration_browser_expires_at IS NOT NULL AND registration_browser_expires_at>created_at))
        AND (resolution<>'REGISTRATION_INTAKE' OR (purpose='SignIn' AND provider='Google' AND registration_browser_digest IS NOT NULL
          AND link_id IS NULL AND link_version IS NULL AND link_qualification_evidence_reference IS NULL AND link_qualification_profile_version IS NULL))
        AND ((protected_registration_evidence IS NULL AND NOT (resolution='REGISTRATION_INTAKE' AND state='VERIFIED'))
          OR (protected_registration_evidence IS NOT NULL AND length(protected_registration_evidence)>0 AND resolution='REGISTRATION_INTAKE' AND state='VERIFIED'))
        AND ((result_registration_operation_id IS NULL AND NOT (resolution='REGISTRATION_INTAKE' AND state='COMPLETED'))
          OR (result_registration_operation_id IS NOT NULL AND resolution='REGISTRATION_INTAKE' AND state='COMPLETED'))
        """;

    internal const string AttemptVerifiedConstraint = "(" + FederationLinkManagementMigrationSql.AttemptVerifiedConstraint + ") OR (" + """
        purpose='SignIn' AND provider='Google' AND resolution='REGISTRATION_INTAKE' AND state IN ('VERIFIED','COMPLETED','REJECTED','EXPIRED')
        AND verified_at IS NOT NULL AND verification_expires_at IS NOT NULL AND validated_issuer IS NOT NULL AND length(validated_issuer)>0
        AND verified_at>=created_at AND verified_at<expires_at AND verification_expires_at>verified_at AND verification_expires_at<=expires_at
        AND link_id IS NULL AND link_version IS NULL AND link_qualification_evidence_reference IS NULL AND link_qualification_profile_version IS NULL
        AND protected_link_subject IS NULL
        """ + ")";
    internal const string AttemptResultConstraint = "(" + FederationLinkManagementMigrationSql.AttemptResultConstraint + ") OR (" + """
        purpose='SignIn' AND resolution='REGISTRATION_INTAKE' AND state='COMPLETED' AND verified_at IS NOT NULL AND error_code IS NULL
        AND result_registration_operation_id IS NOT NULL AND result_session_id IS NULL AND pending_mfa_id IS NULL AND result_proof_id IS NULL
        AND ended_at IS NOT NULL AND ended_at>=created_at AND result_expires_at IS NOT NULL AND result_expires_at>ended_at
        """ + ")";

    internal const string UpgradeAttemptGuard = """
        CREATE OR REPLACE FUNCTION identity.guard_federation_attempt() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN
          IF TG_OP='INSERT' THEN
            IF NEW.state<>'PREPARED' OR NEW.version<>1 OR NEW.verified_at IS NOT NULL OR NEW.ended_at IS NOT NULL
              OR NEW.resolution<>'UNRESOLVED' OR NEW.protected_registration_evidence IS NOT NULL OR NEW.result_registration_operation_id IS NOT NULL THEN
              RAISE EXCEPTION 'Federation attempt must begin prepared' USING ERRCODE='23514';
            END IF;
            RETURN NEW;
          END IF;
          IF (to_jsonb(NEW)-ARRAY['state','version','verified_at','verification_expires_at','validated_issuer','link_id','link_version','link_qualification_evidence_reference','link_qualification_profile_version','protected_link_subject','provider_authenticated_at','provider_methods_json','result_proof_id','result_session_id','pending_mfa_id','resolution','protected_registration_evidence','result_registration_operation_id','error_code','ended_at','result_expires_at']) IS DISTINCT FROM
             (to_jsonb(OLD)-ARRAY['state','version','verified_at','verification_expires_at','validated_issuer','link_id','link_version','link_qualification_evidence_reference','link_qualification_profile_version','protected_link_subject','provider_authenticated_at','provider_methods_json','result_proof_id','result_session_id','pending_mfa_id','resolution','protected_registration_evidence','result_registration_operation_id','error_code','ended_at','result_expires_at']) OR
             NEW.version<>OLD.version+1 OR NOT ((OLD.state='PREPARED' AND NEW.state='CHALLENGE_ADMITTED') OR
               (OLD.state='CHALLENGE_ADMITTED' AND NEW.state='VERIFIED' AND NEW.purpose IN ('SignIn','Link','OperationProof')) OR
               (OLD.state='VERIFIED' AND NEW.state='COMPLETED' AND NEW.purpose IN ('SignIn','Link','OperationProof')) OR
               (OLD.state IN ('PREPARED','CHALLENGE_ADMITTED','VERIFIED') AND NEW.state IN ('REJECTED','EXPIRED'))) THEN
            RAISE EXCEPTION 'Federation attempt binding/result is immutable and one-use' USING ERRCODE='23514';
          END IF;
          IF OLD.state<>'CHALLENGE_ADMITTED' OR NEW.state<>'VERIFIED' THEN
            IF ROW(NEW.verified_at,NEW.verification_expires_at,NEW.validated_issuer,NEW.link_id,NEW.link_version,
                   NEW.link_qualification_evidence_reference,NEW.link_qualification_profile_version,NEW.protected_link_subject,NEW.provider_authenticated_at,NEW.provider_methods_json,NEW.resolution)
               IS DISTINCT FROM ROW(OLD.verified_at,OLD.verification_expires_at,OLD.validated_issuer,OLD.link_id,OLD.link_version,
                   OLD.link_qualification_evidence_reference,OLD.link_qualification_profile_version,OLD.protected_link_subject,OLD.provider_authenticated_at,OLD.provider_methods_json,OLD.resolution) THEN
              RAISE EXCEPTION 'Federation verification cannot be replaced or extended' USING ERRCODE='23514';
            END IF;
          END IF;
          IF NOT (OLD.state='CHALLENGE_ADMITTED' AND NEW.state='VERIFIED') AND
             NOT (OLD.resolution='REGISTRATION_INTAKE' AND OLD.state='VERIFIED' AND NEW.state IN ('COMPLETED','REJECTED','EXPIRED') AND NEW.protected_registration_evidence IS NULL) AND
             NEW.protected_registration_evidence IS DISTINCT FROM OLD.protected_registration_evidence THEN
            RAISE EXCEPTION 'Original verified contact evidence is immutable' USING ERRCODE='23514';
          END IF;
          RETURN NEW;
        END; $$;
        """;
}
