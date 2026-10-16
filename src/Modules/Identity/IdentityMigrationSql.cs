namespace Tunner.Identity.Persistence;

internal static class IdentityMigrationSql
{
    internal const string Harden = """
        CREATE FUNCTION accounts.capture_account_version() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN
          IF TG_OP = 'INSERT' THEN
            IF NEW.version <> 1 THEN RAISE EXCEPTION 'Initial Account version must be one' USING ERRCODE='23514'; END IF;
          ELSE
            IF NEW.account_id IS DISTINCT FROM OLD.account_id OR NEW.version <> OLD.version + 1 THEN
              RAISE EXCEPTION 'Account identity is immutable and version must advance once' USING ERRCODE='23514';
            END IF;
          END IF;
          INSERT INTO accounts.account_state_versions(account_id,version,state,recorded_at_utc,operation_id,correlation_id,evidence_reference)
            VALUES(NEW.account_id,NEW.version,NEW.state,NEW.recorded_at_utc,NEW.operation_id,NEW.correlation_id,NEW.evidence_reference);
          RETURN NEW;
        END; $$;
        CREATE TRIGGER account_version_capture AFTER INSERT OR UPDATE ON accounts.accounts
          FOR EACH ROW EXECUTE FUNCTION accounts.capture_account_version();
        CREATE FUNCTION accounts.reject_identity_history_mutation() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN RAISE EXCEPTION 'Account identity and version history cannot be deleted or rewritten' USING ERRCODE='23514'; END; $$;
        CREATE TRIGGER immutable_account_identity BEFORE DELETE ON accounts.accounts
          FOR EACH ROW EXECUTE FUNCTION accounts.reject_identity_history_mutation();
        CREATE TRIGGER immutable_account_versions BEFORE UPDATE OR DELETE ON accounts.account_state_versions
          FOR EACH ROW EXECUTE FUNCTION accounts.reject_identity_history_mutation();
        CREATE TRIGGER immutable_account_versions_truncate BEFORE TRUNCATE ON accounts.account_state_versions
          FOR EACH STATEMENT EXECUTE FUNCTION accounts.reject_identity_history_mutation();
        CREATE FUNCTION accounts.guard_contact_version() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN
          IF TG_OP = 'INSERT' THEN
            IF NEW.version <> 1 THEN RAISE EXCEPTION 'Initial contact version must be one' USING ERRCODE='23514'; END IF;
          ELSIF NEW.contact_id IS DISTINCT FROM OLD.contact_id OR NEW.account_id IS DISTINCT FROM OLD.account_id OR NEW.version <> OLD.version + 1 THEN
            RAISE EXCEPTION 'Contact identity is immutable and version must advance once' USING ERRCODE='23514';
          END IF;
          RETURN NEW;
        END; $$;
        CREATE TRIGGER contact_version_guard BEFORE INSERT OR UPDATE ON accounts.verified_contacts
          FOR EACH ROW EXECUTE FUNCTION accounts.guard_contact_version();
        CREATE FUNCTION identity.guard_external_identity() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN
          IF TG_OP = 'INSERT' THEN
            IF NEW.version <> 1 THEN RAISE EXCEPTION 'Initial link version must be one' USING ERRCODE='23514'; END IF;
          ELSIF NEW.link_id IS DISTINCT FROM OLD.link_id OR NEW.account_id IS DISTINCT FROM OLD.account_id OR NEW.provider IS DISTINCT FROM OLD.provider OR NEW.subject IS DISTINCT FROM OLD.subject OR NEW.version <> OLD.version + 1 THEN
            RAISE EXCEPTION 'External identity key is immutable and version must advance once' USING ERRCODE='23514';
          END IF;
          RETURN NEW;
        END; $$;
        CREATE TRIGGER external_identity_guard BEFORE INSERT OR UPDATE ON identity.external_identity_links
          FOR EACH ROW EXECUTE FUNCTION identity.guard_external_identity();
        CREATE FUNCTION identity.guard_framework_account_binding() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN
          IF NEW."Id" IS DISTINCT FROM OLD."Id" OR NEW."AccountId" IS DISTINCT FROM OLD."AccountId" THEN
            RAISE EXCEPTION 'Framework Account binding is immutable' USING ERRCODE='23514';
          END IF;
          RETURN NEW;
        END; $$;
        CREATE TRIGGER framework_account_binding_guard BEFORE UPDATE ON identity."AspNetUsers"
          FOR EACH ROW EXECUTE FUNCTION identity.guard_framework_account_binding();
        """;

    internal const string GuardDowngrade = """
        DO $$ DECLARE owned_table record; has_data boolean;
        BEGIN
          FOR owned_table IN SELECT schemaname,tablename FROM pg_tables
            WHERE schemaname IN ('accounts','identity') AND tablename <> '__EFMigrationsHistory'
          LOOP
            EXECUTE format('SELECT EXISTS (SELECT 1 FROM %I.%I)',owned_table.schemaname,owned_table.tablename) INTO has_data;
            IF has_data THEN RAISE EXCEPTION 'Identity foundation contains data; use a governed forward migration' USING ERRCODE='23514'; END IF;
          END LOOP;
        END; $$;
        """;

    internal const string DropFunctions = """
        DROP FUNCTION IF EXISTS identity.guard_framework_account_binding();
        DROP FUNCTION IF EXISTS identity.guard_external_identity();
        DROP FUNCTION IF EXISTS accounts.guard_contact_version();
        DROP FUNCTION IF EXISTS accounts.capture_account_version();
        DROP FUNCTION IF EXISTS accounts.reject_identity_history_mutation();
        """;
}
