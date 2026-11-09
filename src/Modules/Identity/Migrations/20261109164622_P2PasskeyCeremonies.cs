using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tunner.Identity.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class P2PasskeyCeremonies : Migration
    {
        private static readonly string[] CeremonyIdentityColumns = ["browser_digest", "request_id", "kind"];
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "passkey_assurance_key",
                schema: "identity",
                table: "tunner_sessions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "passkey_credential_reference",
                schema: "identity",
                table: "tunner_sessions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "passkey_policy_version",
                schema: "identity",
                table: "tunner_sessions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "termination_cause",
                schema: "identity",
                table: "tunner_sessions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "termination_credential_version",
                schema: "identity",
                table: "tunner_sessions",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "termination_policy_version",
                schema: "identity",
                table: "tunner_sessions",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "passkey_ceremonies",
                schema: "identity",
                columns: table => new
                {
                    ceremony_id = table.Column<Guid>(type: "uuid", nullable: false),
                    request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "text", nullable: false),
                    browser_digest = table.Column<string>(type: "text", nullable: false),
                    account_id = table.Column<string>(type: "text", nullable: true),
                    session_id = table.Column<Guid>(type: "uuid", nullable: true),
                    origin = table.Column<string>(type: "text", nullable: false),
                    policy_version = table.Column<string>(type: "text", nullable: false),
                    authentication_policy_version = table.Column<string>(type: "text", nullable: false),
                    security_stamp_digest = table.Column<string>(type: "text", nullable: false),
                    protected_state = table.Column<string>(type: "text", nullable: false),
                    proof_reference = table.Column<Guid>(type: "uuid", nullable: true),
                    assurance_policy_version = table.Column<string>(type: "text", nullable: false),
                    authorized_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    options_json = table.Column<string>(type: "text", nullable: false),
                    request_digest = table.Column<string>(type: "text", nullable: false),
                    eligibility_policy_version = table.Column<string>(type: "text", nullable: true),
                    eligibility_evidence_reference = table.Column<string>(type: "text", nullable: true),
                    eligibility_from = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    eligibility_includes_from = table.Column<bool>(type: "boolean", nullable: true),
                    eligibility_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    eligibility_includes_until = table.Column<bool>(type: "boolean", nullable: true),
                    remember_me = table.Column<bool>(type: "boolean", nullable: false),
                    previous_session_id = table.Column<Guid>(type: "uuid", nullable: true),
                    result_session_id = table.Column<Guid>(type: "uuid", nullable: true),
                    credential_reference = table.Column<Guid>(type: "uuid", nullable: true),
                    expected_version = table.Column<long>(type: "bigint", nullable: false),
                    state = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ended_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    recovery_expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    processing_reference = table.Column<string>(type: "text", nullable: false),
                    retention_reference = table.Column<string>(type: "text", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_passkey_ceremonies", x => x.ceremony_id);
                    table.CheckConstraint("ck_passkey_eligibility_evidence", "kind<>'REVOKE' OR state<>'COMPLETED' OR (eligibility_policy_version IS NOT NULL AND eligibility_evidence_reference IS NOT NULL AND eligibility_from IS NOT NULL AND eligibility_includes_from IS NOT NULL AND eligibility_includes_until IS NOT NULL)");
                    table.CheckConstraint("ck_passkey_ceremony_bounds", "expires_at > created_at AND recovery_expires_at > created_at AND version >= 1 AND expected_version >= 0 AND length(browser_digest)=64");
                    table.CheckConstraint("ck_passkey_ceremony_end", "(state IN ('PENDING','AUTHORIZED') AND ended_at IS NULL) OR (state NOT IN ('PENDING','AUTHORIZED') AND ended_at IS NOT NULL AND ended_at >= created_at AND protected_state='')");
                    table.CheckConstraint("ck_passkey_ceremony_state", "state IN ('PENDING','AUTHORIZED','COMPLETED','REJECTED','EXPIRED') AND kind IN ('ENROLL','SIGN_IN','REVOKE')");
                    table.ForeignKey(
                        name: "FK_passkey_ceremonies_accounts_account_id",
                        column: x => x.account_id,
                        principalSchema: "accounts",
                        principalTable: "accounts",
                        principalColumn: "account_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "passkey_credentials",
                schema: "identity",
                columns: table => new
                {
                    credential_reference = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<string>(type: "text", nullable: false),
                    credential_id = table.Column<byte[]>(type: "bytea", nullable: false),
                    credential_digest = table.Column<string>(type: "text", nullable: false),
                    state = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_used_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revocation_session_effect = table.Column<string>(type: "text", nullable: true),
                    revocation_policy_version = table.Column<string>(type: "text", nullable: true),
                    revocation_operation_id = table.Column<string>(type: "text", nullable: true),
                    revocation_correlation_id = table.Column<string>(type: "text", nullable: true),
                    policy_version = table.Column<string>(type: "text", nullable: false),
                    processing_reference = table.Column<string>(type: "text", nullable: false),
                    retention_reference = table.Column<string>(type: "text", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_passkey_credentials", x => x.credential_reference);
                    table.CheckConstraint("ck_passkey_public_id", "octet_length(credential_id) BETWEEN 1 AND 1023 AND length(credential_digest)=64");
                    table.CheckConstraint("ck_passkey_revoked", "(state='ACTIVE' AND revoked_at IS NULL AND revocation_session_effect IS NULL AND revocation_policy_version IS NULL AND revocation_operation_id IS NULL AND revocation_correlation_id IS NULL) OR (state='REVOKED' AND revoked_at IS NOT NULL AND revoked_at >= created_at AND revocation_session_effect IS NOT NULL AND revocation_session_effect IN ('PreserveExistingSessions','RevokeCredentialSessions') AND revocation_policy_version IS NOT NULL AND revocation_operation_id IS NOT NULL AND revocation_correlation_id IS NOT NULL)");
                    table.CheckConstraint("ck_passkey_state", "state IN ('ACTIVE','REVOKED') AND version >= 1");
                    table.ForeignKey(
                        name: "FK_passkey_credentials_accounts_account_id",
                        column: x => x.account_id,
                        principalSchema: "accounts",
                        principalTable: "accounts",
                        principalColumn: "account_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_tunner_sessions_passkey_credential_reference",
                schema: "identity",
                table: "tunner_sessions",
                column: "passkey_credential_reference");

            migrationBuilder.AddCheckConstraint(
                name: "ck_session_method",
                schema: "identity",
                table: "tunner_sessions",
                sql: "(authentication_methods='pwd' AND passkey_credential_reference IS NULL AND passkey_policy_version IS NULL AND passkey_assurance_key IS NULL) OR (authentication_methods='passkey' AND passkey_credential_reference IS NOT NULL AND passkey_policy_version IS NOT NULL AND passkey_assurance_key IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_session_termination",
                schema: "identity",
                table: "tunner_sessions",
                sql: "(termination_cause IS NULL AND termination_policy_version IS NULL AND termination_credential_version IS NULL) OR (state='REVOKED' AND authentication_methods='passkey' AND termination_cause='PASSKEY_REVOKED' AND termination_policy_version IS NOT NULL AND termination_credential_version IS NOT NULL AND termination_credential_version >= 2)");

            migrationBuilder.CreateIndex(
                name: "IX_passkey_ceremonies_account_id",
                schema: "identity",
                table: "passkey_ceremonies",
                column: "account_id");

            migrationBuilder.CreateIndex(
                name: "IX_passkey_ceremonies_browser_digest_request_id_kind",
                schema: "identity",
                table: "passkey_ceremonies",
                columns: CeremonyIdentityColumns,
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_passkey_ceremonies_expires_at",
                schema: "identity",
                table: "passkey_ceremonies",
                column: "expires_at");

            migrationBuilder.CreateIndex(
                name: "IX_passkey_credentials_account_id",
                schema: "identity",
                table: "passkey_credentials",
                column: "account_id");

            migrationBuilder.CreateIndex(
                name: "IX_passkey_credentials_credential_digest",
                schema: "identity",
                table: "passkey_credentials",
                column: "credential_digest",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_tunner_sessions_passkey_credentials_passkey_credential_refe~",
                schema: "identity",
                table: "tunner_sessions",
                column: "passkey_credential_reference",
                principalSchema: "identity",
                principalTable: "passkey_credentials",
                principalColumn: "credential_reference",
                onDelete: ReferentialAction.Restrict);
            migrationBuilder.Sql(PasskeyMigrationSql.Guards);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(IdentityMigrationSql.GuardDowngrade);
            migrationBuilder.Sql(PasskeyMigrationSql.RemovePasskeyGuards);
            migrationBuilder.Sql(PasskeyMigrationSql.RestoreSessionGuard);
            migrationBuilder.DropForeignKey(
                name: "FK_tunner_sessions_passkey_credentials_passkey_credential_refe~",
                schema: "identity",
                table: "tunner_sessions");

            migrationBuilder.DropTable(
                name: "passkey_ceremonies",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "passkey_credentials",
                schema: "identity");

            migrationBuilder.DropIndex(
                name: "IX_tunner_sessions_passkey_credential_reference",
                schema: "identity",
                table: "tunner_sessions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_session_method",
                schema: "identity",
                table: "tunner_sessions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_session_termination",
                schema: "identity",
                table: "tunner_sessions");

            migrationBuilder.DropColumn(
                name: "passkey_assurance_key",
                schema: "identity",
                table: "tunner_sessions");

            migrationBuilder.DropColumn(
                name: "passkey_credential_reference",
                schema: "identity",
                table: "tunner_sessions");

            migrationBuilder.DropColumn(
                name: "passkey_policy_version",
                schema: "identity",
                table: "tunner_sessions");

            migrationBuilder.DropColumn(
                name: "termination_cause",
                schema: "identity",
                table: "tunner_sessions");

            migrationBuilder.DropColumn(
                name: "termination_credential_version",
                schema: "identity",
                table: "tunner_sessions");

            migrationBuilder.DropColumn(
                name: "termination_policy_version",
                schema: "identity",
                table: "tunner_sessions");
        }
    }
}
