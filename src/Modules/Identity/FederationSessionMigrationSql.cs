namespace Tunner.Identity.Persistence;

internal static class FederationSessionMigrationSql
{
    // Complements the existing session guard without replacing password/passkey invariants.
    internal const string Harden = """
        CREATE FUNCTION identity.guard_session_federation_source() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN
          IF ROW(NEW.federation_attempt_id,NEW.federation_provider,NEW.federation_link_id,NEW.federation_link_version,
                 NEW.federation_namespace,NEW.federation_client_id,NEW.federation_issuer,NEW.federation_profile_version,
                 NEW.federation_issuer_mapping_evidence_reference,NEW.federation_qualification_profile_version,
                 NEW.federation_qualification_evidence_reference,NEW.federation_sign_in_admission_policy_version,
                 NEW.federation_assurance_mapping_evidence_reference,NEW.federation_session_use_policy_version,NEW.federation_primary_assurance)
             IS DISTINCT FROM
             ROW(OLD.federation_attempt_id,OLD.federation_provider,OLD.federation_link_id,OLD.federation_link_version,
                 OLD.federation_namespace,OLD.federation_client_id,OLD.federation_issuer,OLD.federation_profile_version,
                 OLD.federation_issuer_mapping_evidence_reference,OLD.federation_qualification_profile_version,
                 OLD.federation_qualification_evidence_reference,OLD.federation_sign_in_admission_policy_version,
                 OLD.federation_assurance_mapping_evidence_reference,OLD.federation_session_use_policy_version,OLD.federation_primary_assurance) THEN
            RAISE EXCEPTION 'Session federation source is immutable' USING ERRCODE='23514';
          END IF;
          RETURN NEW;
        END; $$;
        CREATE TRIGGER session_federation_source_guard BEFORE UPDATE ON identity.tunner_sessions
          FOR EACH ROW EXECUTE FUNCTION identity.guard_session_federation_source();
        """;

    internal const string Remove = """
        DROP TRIGGER session_federation_source_guard ON identity.tunner_sessions;
        DROP FUNCTION identity.guard_session_federation_source();
        """;
}
