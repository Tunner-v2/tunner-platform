namespace Tunner.Identity.Persistence;

internal static class AuthenticationMigrationSql
{
    internal const string Guard = """
        CREATE FUNCTION identity.guard_tunner_session() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN
          IF NEW.session_id IS DISTINCT FROM OLD.session_id OR NEW.account_id IS DISTINCT FROM OLD.account_id OR
             NEW.key_digest IS DISTINCT FROM OLD.key_digest OR NEW.browser_digest IS DISTINCT FROM OLD.browser_digest OR
             NEW.idempotency_key IS DISTINCT FROM OLD.idempotency_key OR NEW.identifier_digest IS DISTINCT FROM OLD.identifier_digest OR
             NEW.authenticated_at IS DISTINCT FROM OLD.authenticated_at OR NEW.expires_at IS DISTINCT FROM OLD.expires_at OR
             NEW.scheme IS DISTINCT FROM OLD.scheme OR NEW.origin IS DISTINCT FROM OLD.origin OR
             NEW.security_stamp_digest IS DISTINCT FROM OLD.security_stamp_digest OR NEW.authentication_methods IS DISTINCT FROM OLD.authentication_methods OR
             NEW.assurance IS DISTINCT FROM OLD.assurance OR NEW.remember_me IS DISTINCT FROM OLD.remember_me OR NEW.idle_ticks IS DISTINCT FROM OLD.idle_ticks OR
             NEW.version <> OLD.version + 1 OR NEW.last_seen_at < OLD.last_seen_at OR
             (OLD.state <> 'ACTIVE' AND NEW.state IS DISTINCT FROM OLD.state) OR
             (NEW.state = 'ACTIVE' AND (NEW.protected_key IS DISTINCT FROM OLD.protected_key OR NEW.protected_ticket IS DISTINCT FROM OLD.protected_ticket))
          THEN RAISE EXCEPTION 'Session identity and expiry are immutable; ended sessions cannot resume' USING ERRCODE='23514'; END IF;
          RETURN NEW;
        END; $$;
        CREATE TRIGGER tunner_session_guard BEFORE UPDATE ON identity.tunner_sessions
          FOR EACH ROW EXECUTE FUNCTION identity.guard_tunner_session();
        """;
    internal const string GuardDowngrade = """
        DO $$ BEGIN
          IF EXISTS(SELECT 1 FROM identity.tunner_sessions) OR EXISTS(SELECT 1 FROM identity.authentication_abuse)
          THEN RAISE EXCEPTION 'Authentication state exists; use a governed forward migration' USING ERRCODE='23514'; END IF;
        END; $$;
        DROP TRIGGER tunner_session_guard ON identity.tunner_sessions;
        DROP FUNCTION identity.guard_tunner_session();
        """;
}
