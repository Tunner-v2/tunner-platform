namespace Tunner.Identity.Persistence;

internal static class FederationAttemptMigrationSql
{
    // Called by the root-owned forward migration after creating the model table. No existing migration is rewritten.
    internal const string Harden = """
        CREATE FUNCTION identity.guard_federation_attempt() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN
          IF TG_OP='INSERT' THEN
            IF NEW.state<>'PREPARED' OR NEW.version<>1 OR NEW.verified_at IS NOT NULL OR NEW.ended_at IS NOT NULL THEN
              RAISE EXCEPTION 'Federation attempt must begin prepared' USING ERRCODE='23514';
            END IF;
            RETURN NEW;
          END IF;
          IF (to_jsonb(NEW)-ARRAY['state','version','verified_at','verification_expires_at','validated_issuer','link_id','link_version','link_qualification_evidence_reference','link_qualification_profile_version','result_session_id','pending_mfa_id','error_code','ended_at','result_expires_at']) IS DISTINCT FROM
             (to_jsonb(OLD)-ARRAY['state','version','verified_at','verification_expires_at','validated_issuer','link_id','link_version','link_qualification_evidence_reference','link_qualification_profile_version','result_session_id','pending_mfa_id','error_code','ended_at','result_expires_at']) OR
             NEW.version<>OLD.version+1 OR NOT (
               (OLD.state='PREPARED' AND NEW.state='CHALLENGE_ADMITTED') OR
               (OLD.state='CHALLENGE_ADMITTED' AND NEW.state='VERIFIED' AND NEW.purpose='SignIn') OR
               (OLD.state='VERIFIED' AND NEW.state='COMPLETED' AND NEW.purpose='SignIn') OR
               (OLD.state IN ('PREPARED','CHALLENGE_ADMITTED','VERIFIED') AND NEW.state IN ('REJECTED','EXPIRED'))) THEN
            RAISE EXCEPTION 'Federation attempt binding/result is immutable and one-use' USING ERRCODE='23514';
          END IF;
          IF OLD.state<>'CHALLENGE_ADMITTED' OR NEW.state<>'VERIFIED' THEN
            IF ROW(NEW.verified_at,NEW.verification_expires_at,NEW.validated_issuer,NEW.link_id,NEW.link_version,NEW.link_qualification_evidence_reference,NEW.link_qualification_profile_version)
               IS DISTINCT FROM ROW(OLD.verified_at,OLD.verification_expires_at,OLD.validated_issuer,OLD.link_id,OLD.link_version,OLD.link_qualification_evidence_reference,OLD.link_qualification_profile_version) THEN
              RAISE EXCEPTION 'Federation verification cannot be replaced or extended' USING ERRCODE='23514';
            END IF;
          END IF;
          RETURN NEW;
        END; $$;
        CREATE TRIGGER federation_attempt_guard BEFORE INSERT OR UPDATE ON identity.federation_attempts
          FOR EACH ROW EXECUTE FUNCTION identity.guard_federation_attempt();
        """;
}
