namespace Tunner.Identity.Persistence;

internal static class FederationSessionEffectsMigrationSql
{
    // Root-owned forward migration only. All existing session identity, source and transition guards remain intact.
    internal static string SessionGuard => TotpMigrationSql.SessionGuard.Replace(
        "NEW.termination_cause IN ('PASSKEY_REVOKED','TOTP_DISABLED')",
        "NEW.termination_cause IN ('PASSKEY_REVOKED','TOTP_DISABLED','FEDERATION_UNLINKED')", StringComparison.Ordinal)
        .Replace("THEN RAISE EXCEPTION 'Session identity", """
            OR (NEW.termination_cause='FEDERATION_UNLINKED' AND
              (NEW.authentication_methods NOT IN ('federation','federation+totp') OR NEW.federation_link_id IS NULL OR
               NEW.termination_credential_version <= NEW.federation_link_version))
            THEN RAISE EXCEPTION 'Session identity
            """, StringComparison.Ordinal);
}
