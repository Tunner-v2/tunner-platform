using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tunner.Identity.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class P2TotpLifecycle : Migration
    {
        private static readonly string[] SessionOwnerColumns = ["session_id", "account_id"];
        private static readonly string[] SecretVersionColumns = ["secret_store_reference", "secret_resource_reference", "secret_version"];
        private static readonly string[] SessionRequestColumns = ["session_id", "request_id"];
        private static readonly string[] BrowserRequestColumns = ["browser_digest", "request_id"];
        private static readonly string[] ReplayColumns = ["authenticator_reference", "generation", "expires_at"];
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_session_method",
                schema: "identity",
                table: "tunner_sessions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_session_termination",
                schema: "identity",
                table: "tunner_sessions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_step_up_method",
                schema: "identity",
                table: "step_up_proofs");

            migrationBuilder.AddColumn<string>(
                name: "mfa_requirement_policy_version",
                schema: "identity",
                table: "tunner_sessions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "totp_authenticator_reference",
                schema: "identity",
                table: "tunner_sessions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "totp_authenticator_version",
                schema: "identity",
                table: "tunner_sessions",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "totp_generation",
                schema: "identity",
                table: "tunner_sessions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "totp_policy_version",
                schema: "identity",
                table: "tunner_sessions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "totp_authenticator_reference",
                schema: "identity",
                table: "step_up_proofs",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "totp_authenticator_version",
                schema: "identity",
                table: "step_up_proofs",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "totp_generation",
                schema: "identity",
                table: "step_up_proofs",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "totp_policy_version",
                schema: "identity",
                table: "step_up_proofs",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "totp_authenticators",
                schema: "identity",
                columns: table => new
                {
                    authenticator_reference = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<string>(type: "text", nullable: false),
                    generation = table.Column<Guid>(type: "uuid", nullable: false),
                    enrollment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    secret_store_reference = table.Column<string>(type: "text", nullable: false),
                    secret_resource_reference = table.Column<string>(type: "text", nullable: false),
                    secret_version = table.Column<long>(type: "bigint", nullable: false),
                    secret_binding_version = table.Column<string>(type: "text", nullable: false),
                    state = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    activated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    disabled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_used_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    clock_high_water_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    disable_session_effect = table.Column<string>(type: "text", nullable: true),
                    disable_policy_version = table.Column<string>(type: "text", nullable: true),
                    disable_operation_id = table.Column<string>(type: "text", nullable: true),
                    disable_correlation_id = table.Column<string>(type: "text", nullable: true),
                    policy_version = table.Column<string>(type: "text", nullable: false),
                    processing_reference = table.Column<string>(type: "text", nullable: false),
                    retention_reference = table.Column<string>(type: "text", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    usage_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_totp_authenticators", x => x.authenticator_reference);
                    table.CheckConstraint("ck_totp_lifecycle", "(state='INACTIVE' AND activated_at IS NULL AND disabled_at IS NULL) OR (state='ACTIVE' AND activated_at>=created_at AND disabled_at IS NULL) OR (state='DISABLED' AND disabled_at>=created_at AND disable_session_effect IN ('PreserveExistingSessions','RevokeFactorSessions') AND disable_policy_version IS NOT NULL AND disable_operation_id IS NOT NULL AND disable_correlation_id IS NOT NULL)");
                    table.CheckConstraint("ck_totp_state", "state IN ('INACTIVE','ACTIVE','DISABLED') AND version>=1 AND usage_version>=0 AND secret_version=1");
                    table.ForeignKey(
                        name: "FK_totp_authenticators_accounts_account_id",
                        column: x => x.account_id,
                        principalSchema: "accounts",
                        principalTable: "accounts",
                        principalColumn: "account_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "totp_operations",
                schema: "identity",
                columns: table => new
                {
                    enrollment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<string>(type: "text", nullable: false),
                    session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    origin = table.Column<string>(type: "text", nullable: false),
                    kind = table.Column<string>(type: "text", nullable: false),
                    authenticator_reference = table.Column<Guid>(type: "uuid", nullable: false),
                    generation = table.Column<Guid>(type: "uuid", nullable: false),
                    expected_version = table.Column<long>(type: "bigint", nullable: false),
                    state = table.Column<string>(type: "text", nullable: false),
                    policy_version = table.Column<string>(type: "text", nullable: false),
                    authentication_policy_version = table.Column<string>(type: "text", nullable: false),
                    security_stamp_digest = table.Column<string>(type: "text", nullable: false),
                    assurance_policy_version = table.Column<string>(type: "text", nullable: false),
                    proof_reference = table.Column<Guid>(type: "uuid", nullable: true),
                    authorized_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    disclosure_claimed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    activation_request_id = table.Column<Guid>(type: "uuid", nullable: true),
                    eligibility_policy_version = table.Column<string>(type: "text", nullable: true),
                    eligibility_evidence_reference = table.Column<string>(type: "text", nullable: true),
                    eligibility_from = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    eligibility_includes_from = table.Column<bool>(type: "boolean", nullable: true),
                    eligibility_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    eligibility_includes_until = table.Column<bool>(type: "boolean", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    recovery_expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ended_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    processing_reference = table.Column<string>(type: "text", nullable: false),
                    retention_reference = table.Column<string>(type: "text", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_totp_operations", x => x.enrollment_id);
                    table.CheckConstraint("ck_totp_disclosure", "disclosure_claimed_at IS NULL OR (kind='ENROLL' AND proof_reference IS NOT NULL AND authorized_at IS NOT NULL)");
                    table.CheckConstraint("ck_totp_operation", "kind IN ('ENROLL','DISABLE','RESET') AND state IN ('PENDING_PROOF','AUTHORIZED','PREPARING','AWAITING_FIRST_CODE','COMPLETED','EXPIRED','REJECTED') AND version>=1 AND expected_version>=0 AND expires_at>created_at AND recovery_expires_at>expires_at");
                    table.ForeignKey(
                        name: "FK_totp_operations_tunner_sessions_session_id_account_id",
                        columns: x => new { x.session_id, x.account_id },
                        principalSchema: "identity",
                        principalTable: "tunner_sessions",
                        principalColumns: SessionOwnerColumns,
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "totp_pending_authentication",
                schema: "identity",
                columns: table => new
                {
                    pending_id = table.Column<Guid>(type: "uuid", nullable: false),
                    request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    browser_digest = table.Column<string>(type: "text", nullable: false),
                    account_id = table.Column<string>(type: "text", nullable: false),
                    origin = table.Column<string>(type: "text", nullable: false),
                    remember_me = table.Column<bool>(type: "boolean", nullable: false),
                    previous_session_id = table.Column<Guid>(type: "uuid", nullable: true),
                    identifier_digest = table.Column<string>(type: "text", nullable: false),
                    primary_method = table.Column<string>(type: "text", nullable: false),
                    primary_assurance = table.Column<string>(type: "text", nullable: false),
                    primary_source_digest = table.Column<string>(type: "text", nullable: false),
                    passkey_credential_reference = table.Column<Guid>(type: "uuid", nullable: true),
                    passkey_credential_version = table.Column<long>(type: "bigint", nullable: true),
                    passkey_policy_version = table.Column<string>(type: "text", nullable: true),
                    passkey_assurance_key = table.Column<string>(type: "text", nullable: true),
                    security_stamp_digest = table.Column<string>(type: "text", nullable: false),
                    authentication_policy_version = table.Column<string>(type: "text", nullable: false),
                    totp_policy_version = table.Column<string>(type: "text", nullable: false),
                    requirement_policy_version = table.Column<string>(type: "text", nullable: false),
                    requirement_evidence_reference = table.Column<string>(type: "text", nullable: false),
                    primary_verified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    recovery_expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    state = table.Column<string>(type: "text", nullable: false),
                    result_session_id = table.Column<Guid>(type: "uuid", nullable: true),
                    accepted_authenticator_reference = table.Column<Guid>(type: "uuid", nullable: true),
                    accepted_generation = table.Column<Guid>(type: "uuid", nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_totp_pending_authentication", x => x.pending_id);
                    table.CheckConstraint("ck_totp_pending", "state IN ('PENDING','COMPLETED','EXPIRED') AND primary_method IN ('pwd','passkey') AND expires_at>primary_verified_at AND recovery_expires_at>expires_at AND version>=1");
                    table.CheckConstraint("ck_totp_pending_result", "(state='COMPLETED' AND result_session_id IS NOT NULL AND accepted_authenticator_reference IS NOT NULL AND accepted_generation IS NOT NULL) OR (state<>'COMPLETED' AND result_session_id IS NULL AND accepted_authenticator_reference IS NULL AND accepted_generation IS NULL)");
                    table.ForeignKey(
                        name: "FK_totp_pending_authentication_accounts_account_id",
                        column: x => x.account_id,
                        principalSchema: "accounts",
                        principalTable: "accounts",
                        principalColumn: "account_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "totp_proof_attempts",
                schema: "identity",
                columns: table => new
                {
                    attempt_id = table.Column<Guid>(type: "uuid", nullable: false),
                    session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<string>(type: "text", nullable: false),
                    operation_id = table.Column<string>(type: "text", nullable: false),
                    context_digest = table.Column<string>(type: "text", nullable: false),
                    authenticator_reference = table.Column<Guid>(type: "uuid", nullable: false),
                    generation = table.Column<Guid>(type: "uuid", nullable: false),
                    proof_id = table.Column<Guid>(type: "uuid", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_totp_proof_attempts", x => new { x.session_id, x.attempt_id });
                    table.ForeignKey(
                        name: "FK_totp_proof_attempts_step_up_proofs_proof_id",
                        column: x => x.proof_id,
                        principalSchema: "identity",
                        principalTable: "step_up_proofs",
                        principalColumn: "proof_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "totp_used_codes",
                schema: "identity",
                columns: table => new
                {
                    claim_id = table.Column<Guid>(type: "uuid", nullable: false),
                    authenticator_reference = table.Column<Guid>(type: "uuid", nullable: false),
                    generation = table.Column<Guid>(type: "uuid", nullable: false),
                    verifier = table.Column<string>(type: "text", nullable: false),
                    accepted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    policy_version = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_totp_used_codes", x => x.claim_id);
                    table.CheckConstraint("ck_totp_replay_bounds", "expires_at>accepted_at AND length(verifier)>0");
                    table.ForeignKey(
                        name: "FK_totp_used_codes_totp_authenticators_authenticator_reference",
                        column: x => x.authenticator_reference,
                        principalSchema: "identity",
                        principalTable: "totp_authenticators",
                        principalColumn: "authenticator_reference",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_tunner_sessions_totp_authenticator_reference",
                schema: "identity",
                table: "tunner_sessions",
                column: "totp_authenticator_reference");

            migrationBuilder.AddCheckConstraint(
                name: "ck_session_method",
                schema: "identity",
                table: "tunner_sessions",
                sql: "((authentication_methods IN ('pwd','pwd+totp') AND passkey_credential_reference IS NULL AND passkey_policy_version IS NULL AND passkey_assurance_key IS NULL) OR (authentication_methods IN ('passkey','passkey+totp') AND passkey_credential_reference IS NOT NULL AND passkey_policy_version IS NOT NULL AND passkey_assurance_key IS NOT NULL)) AND ((authentication_methods IN ('pwd','passkey') AND totp_authenticator_reference IS NULL AND totp_generation IS NULL AND totp_authenticator_version IS NULL AND totp_policy_version IS NULL AND mfa_requirement_policy_version IS NULL) OR (authentication_methods IN ('pwd+totp','passkey+totp') AND totp_authenticator_reference IS NOT NULL AND totp_generation IS NOT NULL AND totp_authenticator_version>=2 AND totp_policy_version IS NOT NULL AND mfa_requirement_policy_version IS NOT NULL))");

            migrationBuilder.AddCheckConstraint(
                name: "ck_session_termination",
                schema: "identity",
                table: "tunner_sessions",
                sql: "(termination_cause IS NULL AND termination_policy_version IS NULL AND termination_credential_version IS NULL) OR (state='REVOKED' AND termination_cause IN ('PASSKEY_REVOKED','TOTP_DISABLED') AND termination_policy_version IS NOT NULL AND termination_credential_version IS NOT NULL AND termination_credential_version >= 2)");

            migrationBuilder.CreateIndex(
                name: "IX_step_up_proofs_totp_authenticator_reference",
                schema: "identity",
                table: "step_up_proofs",
                column: "totp_authenticator_reference");

            migrationBuilder.AddCheckConstraint(
                name: "ck_step_up_method",
                schema: "identity",
                table: "step_up_proofs",
                sql: "((authentication_method='pwd' AND passkey_credential_reference IS NULL AND passkey_policy_version IS NULL AND passkey_assurance_key IS NULL) OR (authentication_method LIKE 'passkey:%' AND passkey_credential_reference IS NOT NULL AND passkey_policy_version IS NOT NULL AND passkey_assurance_key IS NOT NULL)) AND totp_authenticator_reference IS NULL AND totp_generation IS NULL AND totp_authenticator_version IS NULL AND totp_policy_version IS NULL OR (authentication_method='totp' AND passkey_credential_reference IS NULL AND passkey_policy_version IS NULL AND passkey_assurance_key IS NULL AND totp_authenticator_reference IS NOT NULL AND totp_generation IS NOT NULL AND totp_authenticator_version>=2 AND totp_policy_version IS NOT NULL)");

            migrationBuilder.CreateIndex(
                name: "IX_totp_authenticators_account_id",
                schema: "identity",
                table: "totp_authenticators",
                column: "account_id");

            migrationBuilder.CreateIndex(
                name: "IX_totp_authenticators_generation",
                schema: "identity",
                table: "totp_authenticators",
                column: "generation",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_totp_authenticators_secret_store_reference_secret_resource_~",
                schema: "identity",
                table: "totp_authenticators",
                columns: SecretVersionColumns,
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_totp_operations_session_id_account_id",
                schema: "identity",
                table: "totp_operations",
                columns: SessionOwnerColumns);

            migrationBuilder.CreateIndex(
                name: "IX_totp_operations_session_id_request_id",
                schema: "identity",
                table: "totp_operations",
                columns: SessionRequestColumns,
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_totp_pending_authentication_account_id",
                schema: "identity",
                table: "totp_pending_authentication",
                column: "account_id");

            migrationBuilder.CreateIndex(
                name: "IX_totp_pending_authentication_browser_digest_request_id",
                schema: "identity",
                table: "totp_pending_authentication",
                columns: BrowserRequestColumns,
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_totp_proof_attempts_proof_id",
                schema: "identity",
                table: "totp_proof_attempts",
                column: "proof_id");

            migrationBuilder.CreateIndex(
                name: "IX_totp_used_codes_authenticator_reference_generation_expires_~",
                schema: "identity",
                table: "totp_used_codes",
                columns: ReplayColumns);

            migrationBuilder.AddForeignKey(
                name: "FK_step_up_proofs_totp_authenticators_totp_authenticator_refer~",
                schema: "identity",
                table: "step_up_proofs",
                column: "totp_authenticator_reference",
                principalSchema: "identity",
                principalTable: "totp_authenticators",
                principalColumn: "authenticator_reference",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_tunner_sessions_totp_authenticators_totp_authenticator_refe~",
                schema: "identity",
                table: "tunner_sessions",
                column: "totp_authenticator_reference",
                principalSchema: "identity",
                principalTable: "totp_authenticators",
                principalColumn: "authenticator_reference",
                onDelete: ReferentialAction.Restrict);
            migrationBuilder.Sql(TotpMigrationSql.Guards);
            migrationBuilder.Sql(TotpMigrationSql.SessionGuard);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(IdentityMigrationSql.GuardDowngrade);
            migrationBuilder.Sql(TotpMigrationSql.RemoveGuards);
            migrationBuilder.Sql(TotpMigrationSql.RestoreSessionGuard);
            migrationBuilder.DropForeignKey(
                name: "FK_step_up_proofs_totp_authenticators_totp_authenticator_refer~",
                schema: "identity",
                table: "step_up_proofs");

            migrationBuilder.DropForeignKey(
                name: "FK_tunner_sessions_totp_authenticators_totp_authenticator_refe~",
                schema: "identity",
                table: "tunner_sessions");

            migrationBuilder.DropTable(
                name: "totp_operations",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "totp_pending_authentication",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "totp_proof_attempts",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "totp_used_codes",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "totp_authenticators",
                schema: "identity");

            migrationBuilder.DropIndex(
                name: "IX_tunner_sessions_totp_authenticator_reference",
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

            migrationBuilder.DropIndex(
                name: "IX_step_up_proofs_totp_authenticator_reference",
                schema: "identity",
                table: "step_up_proofs");

            migrationBuilder.DropCheckConstraint(
                name: "ck_step_up_method",
                schema: "identity",
                table: "step_up_proofs");

            migrationBuilder.DropColumn(
                name: "mfa_requirement_policy_version",
                schema: "identity",
                table: "tunner_sessions");

            migrationBuilder.DropColumn(
                name: "totp_authenticator_reference",
                schema: "identity",
                table: "tunner_sessions");

            migrationBuilder.DropColumn(
                name: "totp_authenticator_version",
                schema: "identity",
                table: "tunner_sessions");

            migrationBuilder.DropColumn(
                name: "totp_generation",
                schema: "identity",
                table: "tunner_sessions");

            migrationBuilder.DropColumn(
                name: "totp_policy_version",
                schema: "identity",
                table: "tunner_sessions");

            migrationBuilder.DropColumn(
                name: "totp_authenticator_reference",
                schema: "identity",
                table: "step_up_proofs");

            migrationBuilder.DropColumn(
                name: "totp_authenticator_version",
                schema: "identity",
                table: "step_up_proofs");

            migrationBuilder.DropColumn(
                name: "totp_generation",
                schema: "identity",
                table: "step_up_proofs");

            migrationBuilder.DropColumn(
                name: "totp_policy_version",
                schema: "identity",
                table: "step_up_proofs");

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

            migrationBuilder.AddCheckConstraint(
                name: "ck_step_up_method",
                schema: "identity",
                table: "step_up_proofs",
                sql: "(authentication_method='pwd' AND passkey_credential_reference IS NULL AND passkey_policy_version IS NULL AND passkey_assurance_key IS NULL) OR (authentication_method LIKE 'passkey:%' AND passkey_credential_reference IS NOT NULL AND passkey_policy_version IS NOT NULL AND passkey_assurance_key IS NOT NULL)");
        }
    }
}
