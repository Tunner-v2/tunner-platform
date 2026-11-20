namespace Tunner.Identity.Persistence;

internal static class FederationLinkManagementMigrationSql
{
    internal const string AttemptOwnerConstraint = """
        (purpose NOT IN ('Link','OperationProof') AND owner_account_id IS NULL AND owner_session_id IS NULL AND owner_account_version IS NULL
          AND owner_session_version IS NULL AND owner_security_stamp_digest IS NULL AND owner_operation_id IS NULL
          AND owner_context_digest IS NULL AND owner_expected_version IS NULL AND protected_link_subject IS NULL AND owner_assurance_policy_version IS NULL
          AND selected_proof_link_id IS NULL AND selected_proof_link_version IS NULL)
        OR (purpose IN ('Link','OperationProof') AND owner_account_id IS NOT NULL AND length(owner_account_id)>0 AND owner_session_id IS NOT NULL
          AND owner_session_id=previous_session_id AND owner_account_version IS NOT NULL AND owner_account_version>=1
          AND owner_session_version IS NOT NULL AND owner_session_version>=1 AND owner_security_stamp_digest IS NOT NULL
          AND length(owner_security_stamp_digest)=64 AND owner_operation_id IS NOT NULL AND length(owner_operation_id)>0
          AND owner_context_digest IS NOT NULL AND length(owner_context_digest)=64 AND owner_expected_version IS NOT NULL AND owner_expected_version>=0
          AND ((purpose='Link' AND owner_assurance_policy_version IS NULL AND selected_proof_link_id IS NULL AND selected_proof_link_version IS NULL)
            OR (purpose='OperationProof' AND owner_assurance_policy_version IS NOT NULL AND length(owner_assurance_policy_version)>0 AND protected_link_subject IS NULL
              AND selected_proof_link_id IS NOT NULL AND selected_proof_link_version IS NOT NULL AND selected_proof_link_version>0)))
        """;
    internal const string AttemptFreshEvidenceConstraint = """
        (purpose<>'OperationProof' AND provider_authenticated_at IS NULL AND provider_methods_json IS NULL AND result_proof_id IS NULL)
        OR (purpose='OperationProof' AND ((verified_at IS NULL AND provider_authenticated_at IS NULL AND provider_methods_json IS NULL)
          OR (verified_at IS NOT NULL AND provider_authenticated_at IS NOT NULL AND provider_authenticated_at<=verified_at
            AND link_id=selected_proof_link_id AND link_version=selected_proof_link_version
            AND provider_methods_json IS NOT NULL AND jsonb_typeof(provider_methods_json::jsonb)='array' AND jsonb_array_length(provider_methods_json::jsonb)>0)))
        """;
    internal const string AttemptVerifiedConstraint = """
        (verified_at IS NULL AND verification_expires_at IS NULL AND validated_issuer IS NULL AND link_id IS NULL AND link_version IS NULL
          AND link_qualification_evidence_reference IS NULL AND link_qualification_profile_version IS NULL AND protected_link_subject IS NULL
          AND state NOT IN ('VERIFIED','COMPLETED'))
        OR (verified_at IS NOT NULL AND verification_expires_at IS NOT NULL AND validated_issuer IS NOT NULL
          AND verified_at>=created_at AND verified_at<expires_at AND verification_expires_at>verified_at AND verification_expires_at<=expires_at
          AND length(validated_issuer)>0 AND ((purpose IN ('SignIn','OperationProof') AND link_id IS NOT NULL AND link_version IS NOT NULL AND link_version>=1
            AND link_qualification_evidence_reference IS NOT NULL AND length(link_qualification_evidence_reference)>0
            AND link_qualification_profile_version IS NOT NULL AND length(link_qualification_profile_version)>0 AND protected_link_subject IS NULL)
          OR (purpose='Link' AND protected_link_subject IS NOT NULL AND length(protected_link_subject)>0
            AND link_id IS NULL AND link_version IS NULL AND link_qualification_evidence_reference IS NULL AND link_qualification_profile_version IS NULL)))
        """;
    internal const string AttemptResultConstraint = """
        (state IN ('PREPARED','CHALLENGE_ADMITTED','VERIFIED') AND ended_at IS NULL AND result_expires_at IS NULL
          AND result_session_id IS NULL AND pending_mfa_id IS NULL AND result_proof_id IS NULL AND error_code IS NULL)
        OR (state IN ('COMPLETED','REJECTED','EXPIRED') AND ended_at IS NOT NULL AND result_expires_at IS NOT NULL
          AND ended_at>=created_at AND result_expires_at>ended_at AND ((state='COMPLETED' AND verified_at IS NOT NULL AND error_code IS NULL
            AND ((purpose='SignIn' AND result_proof_id IS NULL AND ((result_session_id IS NOT NULL AND pending_mfa_id IS NULL) OR (result_session_id IS NULL AND pending_mfa_id IS NOT NULL)))
              OR (purpose='Link' AND result_session_id IS NULL AND pending_mfa_id IS NULL AND result_proof_id IS NULL)
              OR (purpose='OperationProof' AND result_session_id IS NULL AND pending_mfa_id IS NULL AND result_proof_id IS NOT NULL)))
            OR (state IN ('REJECTED','EXPIRED') AND error_code IS NOT NULL AND result_session_id IS NULL AND pending_mfa_id IS NULL AND result_proof_id IS NULL)))
        """;

    // Append in the new management migration only; do not change the original attempt migration/Harden constant.
    internal const string UpgradeAttemptGuard = """
        CREATE OR REPLACE FUNCTION identity.guard_federation_attempt() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN
          IF TG_OP='INSERT' THEN
            IF NEW.state<>'PREPARED' OR NEW.version<>1 OR NEW.verified_at IS NOT NULL OR NEW.ended_at IS NOT NULL THEN
              RAISE EXCEPTION 'Federation attempt must begin prepared' USING ERRCODE='23514';
            END IF;
            RETURN NEW;
          END IF;
          IF (to_jsonb(NEW)-ARRAY['state','version','verified_at','verification_expires_at','validated_issuer','link_id','link_version','link_qualification_evidence_reference','link_qualification_profile_version','protected_link_subject','provider_authenticated_at','provider_methods_json','result_proof_id','result_session_id','pending_mfa_id','error_code','ended_at','result_expires_at']) IS DISTINCT FROM
             (to_jsonb(OLD)-ARRAY['state','version','verified_at','verification_expires_at','validated_issuer','link_id','link_version','link_qualification_evidence_reference','link_qualification_profile_version','protected_link_subject','provider_authenticated_at','provider_methods_json','result_proof_id','result_session_id','pending_mfa_id','error_code','ended_at','result_expires_at']) OR
             NEW.version<>OLD.version+1 OR NOT ((OLD.state='PREPARED' AND NEW.state='CHALLENGE_ADMITTED') OR
               (OLD.state='CHALLENGE_ADMITTED' AND NEW.state='VERIFIED' AND NEW.purpose IN ('SignIn','Link','OperationProof')) OR
               (OLD.state='VERIFIED' AND NEW.state='COMPLETED' AND NEW.purpose IN ('SignIn','Link','OperationProof')) OR
               (OLD.state IN ('PREPARED','CHALLENGE_ADMITTED','VERIFIED') AND NEW.state IN ('REJECTED','EXPIRED'))) THEN
            RAISE EXCEPTION 'Federation attempt binding/result is immutable and one-use' USING ERRCODE='23514';
          END IF;
          IF OLD.state<>'CHALLENGE_ADMITTED' OR NEW.state<>'VERIFIED' THEN
            IF ROW(NEW.verified_at,NEW.verification_expires_at,NEW.validated_issuer,NEW.link_id,NEW.link_version,
                   NEW.link_qualification_evidence_reference,NEW.link_qualification_profile_version,NEW.protected_link_subject,NEW.provider_authenticated_at,NEW.provider_methods_json)
               IS DISTINCT FROM ROW(OLD.verified_at,OLD.verification_expires_at,OLD.validated_issuer,OLD.link_id,OLD.link_version,
                   OLD.link_qualification_evidence_reference,OLD.link_qualification_profile_version,OLD.protected_link_subject,OLD.provider_authenticated_at,OLD.provider_methods_json) THEN
              RAISE EXCEPTION 'Federation verification cannot be replaced or extended' USING ERRCODE='23514';
            END IF;
          END IF;
          RETURN NEW;
        END; $$;
        """;

    internal const string Harden = """
        CREATE FUNCTION identity.guard_federation_link_operation() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN
          IF TG_OP='INSERT' THEN
            IF NEW.version<>1 OR NEW.state<>(CASE WHEN NEW.kind='LINK' THEN 'PENDING_PROVIDER' ELSE 'PENDING_CONFIRMATION' END)
              OR NEW.confirmed_at IS NOT NULL OR NEW.ended_at IS NOT NULL THEN
              RAISE EXCEPTION 'Federation management operation must begin pending' USING ERRCODE='23514';
            END IF;
            RETURN NEW;
          END IF;
          IF (to_jsonb(NEW)-ARRAY['state','version','confirmed_at','ended_at','proof_reference','result_link_id','result_link_version','posture_facts_digest','posture_policy_version','posture_evidence_reference','posture_from','posture_includes_from','posture_until','posture_includes_until','error_code']) IS DISTINCT FROM
             (to_jsonb(OLD)-ARRAY['state','version','confirmed_at','ended_at','proof_reference','result_link_id','result_link_version','posture_facts_digest','posture_policy_version','posture_evidence_reference','posture_from','posture_includes_from','posture_until','posture_includes_until','error_code'])
             OR NEW.version<>OLD.version+1 OR NOT (
               (OLD.state IN ('PENDING_PROVIDER','PENDING_CONFIRMATION') AND NEW.state='PENDING_PROOF') OR
               (OLD.state='PENDING_PROOF' AND NEW.state='COMPLETED') OR
               (OLD.state IN ('PENDING_PROVIDER','PENDING_CONFIRMATION','PENDING_PROOF') AND NEW.state IN ('REJECTED','EXPIRED'))) THEN
            RAISE EXCEPTION 'Federation management binding/result is immutable and one-use' USING ERRCODE='23514';
          END IF;
          IF OLD.confirmed_at IS NOT NULL AND NEW.confirmed_at IS DISTINCT FROM OLD.confirmed_at THEN
            RAISE EXCEPTION 'Original explicit confirmation is immutable' USING ERRCODE='23514';
          END IF;
          RETURN NEW;
        END; $$;
        CREATE TRIGGER federation_link_operation_guard BEFORE INSERT OR UPDATE ON identity.federation_link_operations
          FOR EACH ROW EXECUTE FUNCTION identity.guard_federation_link_operation();
        """;
}
