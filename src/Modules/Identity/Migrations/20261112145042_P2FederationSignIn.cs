using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tunner.Identity.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class P2FederationSignIn : Migration
    {
        private static readonly string[] LinkOwnerColumns = ["account_id", "link_id"];
        private static readonly string[] FederatedLinkOwnerColumns = ["account_id", "federation_link_id"];
        private static readonly string[] BrowserRequestColumns = ["browser_digest", "request_id"];
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_session_method",
                schema: "identity",
                table: "tunner_sessions");

            migrationBuilder.DropIndex(
                name: "IX_totp_pending_authentication_account_id",
                schema: "identity",
                table: "totp_pending_authentication");

            migrationBuilder.DropCheckConstraint(
                name: "ck_totp_pending",
                schema: "identity",
                table: "totp_pending_authentication");

            migrationBuilder.DropIndex(
                name: "IX_external_identity_links_account_id",
                schema: "identity",
                table: "external_identity_links");

            migrationBuilder.AddColumn<string>(
                name: "federation_assurance_mapping_evidence_reference",
                schema: "identity",
                table: "tunner_sessions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "federation_attempt_id",
                schema: "identity",
                table: "tunner_sessions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "federation_client_id",
                schema: "identity",
                table: "tunner_sessions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "federation_issuer",
                schema: "identity",
                table: "tunner_sessions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "federation_issuer_mapping_evidence_reference",
                schema: "identity",
                table: "tunner_sessions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "federation_link_id",
                schema: "identity",
                table: "tunner_sessions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "federation_link_version",
                schema: "identity",
                table: "tunner_sessions",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "federation_namespace",
                schema: "identity",
                table: "tunner_sessions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "federation_primary_assurance",
                schema: "identity",
                table: "tunner_sessions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "federation_profile_version",
                schema: "identity",
                table: "tunner_sessions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "federation_provider",
                schema: "identity",
                table: "tunner_sessions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "federation_qualification_evidence_reference",
                schema: "identity",
                table: "tunner_sessions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "federation_qualification_profile_version",
                schema: "identity",
                table: "tunner_sessions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "federation_session_use_policy_version",
                schema: "identity",
                table: "tunner_sessions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "federation_sign_in_admission_policy_version",
                schema: "identity",
                table: "tunner_sessions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "federation_assurance_mapping_evidence_reference",
                schema: "identity",
                table: "totp_pending_authentication",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "federation_attempt_id",
                schema: "identity",
                table: "totp_pending_authentication",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "federation_client_id",
                schema: "identity",
                table: "totp_pending_authentication",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "federation_issuer",
                schema: "identity",
                table: "totp_pending_authentication",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "federation_issuer_mapping_evidence_reference",
                schema: "identity",
                table: "totp_pending_authentication",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "federation_link_id",
                schema: "identity",
                table: "totp_pending_authentication",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "federation_link_version",
                schema: "identity",
                table: "totp_pending_authentication",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "federation_namespace",
                schema: "identity",
                table: "totp_pending_authentication",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "federation_profile_version",
                schema: "identity",
                table: "totp_pending_authentication",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "federation_provider",
                schema: "identity",
                table: "totp_pending_authentication",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "federation_qualification_evidence_reference",
                schema: "identity",
                table: "totp_pending_authentication",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "federation_qualification_profile_version",
                schema: "identity",
                table: "totp_pending_authentication",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "federation_session_use_policy_version",
                schema: "identity",
                table: "totp_pending_authentication",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "federation_sign_in_admission_policy_version",
                schema: "identity",
                table: "totp_pending_authentication",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "issuer_mapping_evidence_reference",
                schema: "identity",
                table: "external_identity_links",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "link_state",
                schema: "identity",
                table: "external_identity_links",
                type: "text",
                nullable: false,
                defaultValue: "LEGACY_UNQUALIFIED");

            migrationBuilder.AddColumn<string>(
                name: "qualification_evidence_reference",
                schema: "identity",
                table: "external_identity_links",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "qualification_profile_version",
                schema: "identity",
                table: "external_identity_links",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "qualified_client_id",
                schema: "identity",
                table: "external_identity_links",
                type: "text",
                nullable: true);

            migrationBuilder.AddUniqueConstraint(
                name: "AK_external_identity_links_account_id_link_id",
                schema: "identity",
                table: "external_identity_links",
                columns: LinkOwnerColumns);

            migrationBuilder.CreateTable(
                name: "federation_attempts",
                schema: "identity",
                columns: table => new
                {
                    attempt_id = table.Column<Guid>(type: "uuid", nullable: false),
                    request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    correlation_id = table.Column<string>(type: "text", nullable: false),
                    provider = table.Column<string>(type: "text", nullable: false),
                    purpose = table.Column<string>(type: "text", nullable: false),
                    browser_key = table.Column<Guid>(type: "uuid", nullable: false),
                    browser_digest = table.Column<string>(type: "text", nullable: false),
                    origin = table.Column<string>(type: "text", nullable: false),
                    previous_cookie_digest = table.Column<string>(type: "text", nullable: false),
                    previous_session_id = table.Column<Guid>(type: "uuid", nullable: true),
                    remember_me = table.Column<bool>(type: "boolean", nullable: false),
                    authentication_policy_version = table.Column<string>(type: "text", nullable: false),
                    provider_namespace = table.Column<string>(type: "text", nullable: false),
                    client_id = table.Column<string>(type: "text", nullable: false),
                    profile_version = table.Column<string>(type: "text", nullable: false),
                    mapping_evidence_reference = table.Column<string>(type: "text", nullable: false),
                    profile_snapshot = table.Column<string>(type: "text", nullable: false),
                    continuation_path = table.Column<string>(type: "text", nullable: false),
                    processing_reference = table.Column<string>(type: "text", nullable: false),
                    retention_reference = table.Column<string>(type: "text", nullable: false),
                    result_recovery_lifetime_ticks = table.Column<long>(type: "bigint", nullable: false),
                    state = table.Column<string>(type: "text", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    verified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    verification_expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    validated_issuer = table.Column<string>(type: "text", nullable: true),
                    link_id = table.Column<Guid>(type: "uuid", nullable: true),
                    link_version = table.Column<long>(type: "bigint", nullable: true),
                    link_qualification_evidence_reference = table.Column<string>(type: "text", nullable: true),
                    link_qualification_profile_version = table.Column<string>(type: "text", nullable: true),
                    result_session_id = table.Column<Guid>(type: "uuid", nullable: true),
                    pending_mfa_id = table.Column<Guid>(type: "uuid", nullable: true),
                    error_code = table.Column<string>(type: "text", nullable: true),
                    ended_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    result_expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_federation_attempts", x => x.attempt_id);
                    table.CheckConstraint("ck_federation_bounds", "expires_at>created_at AND result_recovery_lifetime_ticks>0 AND length(browser_digest)=64 AND length(previous_cookie_digest)=64");
                    table.CheckConstraint("ck_federation_error", "error_code IS NULL OR error_code IN ('AUTHENTICATION_FAILED','VERSION_CONFLICT','RATE_OR_ABUSE_LIMITED','STEP_UP_REQUIRED')");
                    table.CheckConstraint("ck_federation_profile", "length(provider_namespace)>0 AND length(client_id)>0 AND length(profile_version)>0 AND length(mapping_evidence_reference)>0 AND length(correlation_id)>0 AND length(processing_reference)>0 AND length(retention_reference)>0 AND jsonb_typeof(profile_snapshot::jsonb)='object'");
                    table.CheckConstraint("ck_federation_purpose", "provider IN ('Google','Microsoft') AND purpose IN ('SignIn','Registration','Link','OperationProof')");
                    table.CheckConstraint("ck_federation_result", "(state IN ('PREPARED','CHALLENGE_ADMITTED','VERIFIED') AND ended_at IS NULL AND result_expires_at IS NULL AND result_session_id IS NULL AND pending_mfa_id IS NULL AND error_code IS NULL) OR (state IN ('COMPLETED','REJECTED','EXPIRED') AND ended_at IS NOT NULL AND result_expires_at IS NOT NULL AND ended_at>=created_at AND result_expires_at>ended_at AND ((state='COMPLETED' AND purpose='SignIn' AND verified_at IS NOT NULL AND error_code IS NULL AND ((result_session_id IS NOT NULL AND pending_mfa_id IS NULL) OR (result_session_id IS NULL AND pending_mfa_id IS NOT NULL))) OR (state IN ('REJECTED','EXPIRED') AND error_code IS NOT NULL AND result_session_id IS NULL AND pending_mfa_id IS NULL)))");
                    table.CheckConstraint("ck_federation_state", "state IN ('PREPARED','CHALLENGE_ADMITTED','VERIFIED','COMPLETED','REJECTED','EXPIRED') AND version>=1");
                    table.CheckConstraint("ck_federation_verified", "(state NOT IN ('VERIFIED','COMPLETED') AND verified_at IS NULL AND verification_expires_at IS NULL AND validated_issuer IS NULL AND link_id IS NULL AND link_version IS NULL AND link_qualification_evidence_reference IS NULL AND link_qualification_profile_version IS NULL) OR (verified_at IS NOT NULL AND verification_expires_at IS NOT NULL AND validated_issuer IS NOT NULL AND link_id IS NOT NULL AND link_version IS NOT NULL AND link_qualification_evidence_reference IS NOT NULL AND link_qualification_profile_version IS NOT NULL AND verified_at>=created_at AND verified_at<expires_at AND verification_expires_at>verified_at AND verification_expires_at<=expires_at AND length(validated_issuer)>0 AND link_version>=1 AND length(link_qualification_evidence_reference)>0 AND length(link_qualification_profile_version)>0)");
                    table.ForeignKey(
                        name: "FK_federation_attempts_external_identity_links_link_id",
                        column: x => x.link_id,
                        principalSchema: "identity",
                        principalTable: "external_identity_links",
                        principalColumn: "link_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_federation_attempts_totp_pending_authentication_pending_mfa~",
                        column: x => x.pending_mfa_id,
                        principalSchema: "identity",
                        principalTable: "totp_pending_authentication",
                        principalColumn: "pending_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_federation_attempts_tunner_sessions_result_session_id",
                        column: x => x.result_session_id,
                        principalSchema: "identity",
                        principalTable: "tunner_sessions",
                        principalColumn: "session_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_tunner_sessions_account_id_federation_link_id",
                schema: "identity",
                table: "tunner_sessions",
                columns: FederatedLinkOwnerColumns);

            migrationBuilder.CreateIndex(
                name: "IX_tunner_sessions_federation_attempt_id",
                schema: "identity",
                table: "tunner_sessions",
                column: "federation_attempt_id");

            migrationBuilder.AddCheckConstraint(
                name: "ck_session_federation",
                schema: "identity",
                table: "tunner_sessions",
                sql: "((authentication_methods NOT IN ('federation','federation+totp') AND federation_attempt_id IS NULL AND federation_provider IS NULL AND federation_link_id IS NULL AND federation_link_version IS NULL AND federation_namespace IS NULL AND federation_client_id IS NULL AND federation_issuer IS NULL AND federation_profile_version IS NULL AND federation_issuer_mapping_evidence_reference IS NULL AND federation_qualification_profile_version IS NULL AND federation_qualification_evidence_reference IS NULL AND federation_sign_in_admission_policy_version IS NULL AND federation_assurance_mapping_evidence_reference IS NULL AND federation_session_use_policy_version IS NULL AND federation_primary_assurance IS NULL) OR (authentication_methods IN ('federation','federation+totp') AND federation_attempt_id IS NOT NULL AND federation_provider IS NOT NULL AND federation_link_id IS NOT NULL AND federation_link_version IS NOT NULL AND federation_namespace IS NOT NULL AND length(federation_namespace)>0 AND federation_client_id IS NOT NULL AND length(federation_client_id)>0 AND federation_issuer IS NOT NULL AND length(federation_issuer)>0 AND federation_profile_version IS NOT NULL AND length(federation_profile_version)>0 AND federation_issuer_mapping_evidence_reference IS NOT NULL AND length(federation_issuer_mapping_evidence_reference)>0 AND federation_qualification_profile_version IS NOT NULL AND length(federation_qualification_profile_version)>0 AND federation_qualification_evidence_reference IS NOT NULL AND length(federation_qualification_evidence_reference)>0 AND federation_sign_in_admission_policy_version IS NOT NULL AND length(federation_sign_in_admission_policy_version)>0 AND federation_assurance_mapping_evidence_reference IS NOT NULL AND length(federation_assurance_mapping_evidence_reference)>0 AND federation_session_use_policy_version IS NOT NULL AND length(federation_session_use_policy_version)>0 AND federation_link_version>=1 AND federation_provider IN ('Google','Microsoft') AND federation_primary_assurance IS NOT NULL AND length(federation_primary_assurance)>0))");

            migrationBuilder.AddCheckConstraint(
                name: "ck_session_method",
                schema: "identity",
                table: "tunner_sessions",
                sql: "((authentication_methods IN ('pwd','pwd+totp','federation','federation+totp') AND passkey_credential_reference IS NULL AND passkey_policy_version IS NULL AND passkey_assurance_key IS NULL) OR (authentication_methods IN ('passkey','passkey+totp') AND passkey_credential_reference IS NOT NULL AND passkey_policy_version IS NOT NULL AND passkey_assurance_key IS NOT NULL)) AND ((authentication_methods IN ('pwd','passkey','federation') AND totp_authenticator_reference IS NULL AND totp_generation IS NULL AND totp_authenticator_version IS NULL AND totp_policy_version IS NULL AND mfa_requirement_policy_version IS NULL) OR (authentication_methods IN ('pwd+totp','passkey+totp','federation+totp') AND totp_authenticator_reference IS NOT NULL AND totp_generation IS NOT NULL AND totp_authenticator_version>=2 AND totp_policy_version IS NOT NULL AND mfa_requirement_policy_version IS NOT NULL))");

            migrationBuilder.CreateIndex(
                name: "IX_totp_pending_authentication_account_id_federation_link_id",
                schema: "identity",
                table: "totp_pending_authentication",
                columns: FederatedLinkOwnerColumns);

            migrationBuilder.CreateIndex(
                name: "IX_totp_pending_authentication_federation_attempt_id",
                schema: "identity",
                table: "totp_pending_authentication",
                column: "federation_attempt_id");

            migrationBuilder.AddCheckConstraint(
                name: "ck_totp_pending",
                schema: "identity",
                table: "totp_pending_authentication",
                sql: "state IN ('PENDING','COMPLETED','EXPIRED') AND primary_method IN ('pwd','passkey','federation') AND expires_at>primary_verified_at AND recovery_expires_at>expires_at AND version>=1");

            migrationBuilder.AddCheckConstraint(
                name: "ck_totp_pending_federation",
                schema: "identity",
                table: "totp_pending_authentication",
                sql: "((primary_method<>'federation' AND federation_attempt_id IS NULL AND federation_provider IS NULL AND federation_link_id IS NULL AND federation_link_version IS NULL AND federation_namespace IS NULL AND federation_client_id IS NULL AND federation_issuer IS NULL AND federation_profile_version IS NULL AND federation_issuer_mapping_evidence_reference IS NULL AND federation_qualification_profile_version IS NULL AND federation_qualification_evidence_reference IS NULL AND federation_sign_in_admission_policy_version IS NULL AND federation_assurance_mapping_evidence_reference IS NULL AND federation_session_use_policy_version IS NULL) OR (primary_method='federation' AND federation_attempt_id IS NOT NULL AND federation_provider IS NOT NULL AND federation_link_id IS NOT NULL AND federation_link_version IS NOT NULL AND federation_namespace IS NOT NULL AND length(federation_namespace)>0 AND federation_client_id IS NOT NULL AND length(federation_client_id)>0 AND federation_issuer IS NOT NULL AND length(federation_issuer)>0 AND federation_profile_version IS NOT NULL AND length(federation_profile_version)>0 AND federation_issuer_mapping_evidence_reference IS NOT NULL AND length(federation_issuer_mapping_evidence_reference)>0 AND federation_qualification_profile_version IS NOT NULL AND length(federation_qualification_profile_version)>0 AND federation_qualification_evidence_reference IS NOT NULL AND length(federation_qualification_evidence_reference)>0 AND federation_sign_in_admission_policy_version IS NOT NULL AND length(federation_sign_in_admission_policy_version)>0 AND federation_assurance_mapping_evidence_reference IS NOT NULL AND length(federation_assurance_mapping_evidence_reference)>0 AND federation_session_use_policy_version IS NOT NULL AND length(federation_session_use_policy_version)>0 AND federation_link_version>=1 AND federation_provider IN ('Google','Microsoft') AND passkey_credential_reference IS NULL AND passkey_credential_version IS NULL AND passkey_policy_version IS NULL AND passkey_assurance_key IS NULL))");

            migrationBuilder.AddCheckConstraint(
                name: "ck_external_qualification",
                schema: "identity",
                table: "external_identity_links",
                sql: "(link_state='LEGACY_UNQUALIFIED' AND qualified_client_id IS NULL AND qualification_profile_version IS NULL\n  AND issuer_mapping_evidence_reference IS NULL AND qualification_evidence_reference IS NULL)\nOR (link_state IN ('ACTIVE','UNLINKED') AND qualified_client_id IS NOT NULL AND length(qualified_client_id)>0\n  AND qualification_profile_version IS NOT NULL AND length(qualification_profile_version)>0\n  AND issuer_mapping_evidence_reference IS NOT NULL AND length(issuer_mapping_evidence_reference)>0\n  AND qualification_evidence_reference IS NOT NULL AND length(qualification_evidence_reference)>0)");

            migrationBuilder.CreateIndex(
                name: "IX_federation_attempts_browser_digest_request_id",
                schema: "identity",
                table: "federation_attempts",
                columns: BrowserRequestColumns,
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_federation_attempts_expires_at",
                schema: "identity",
                table: "federation_attempts",
                column: "expires_at");

            migrationBuilder.CreateIndex(
                name: "IX_federation_attempts_link_id",
                schema: "identity",
                table: "federation_attempts",
                column: "link_id");

            migrationBuilder.CreateIndex(
                name: "IX_federation_attempts_pending_mfa_id",
                schema: "identity",
                table: "federation_attempts",
                column: "pending_mfa_id");

            migrationBuilder.CreateIndex(
                name: "IX_federation_attempts_result_session_id",
                schema: "identity",
                table: "federation_attempts",
                column: "result_session_id");

            migrationBuilder.AddForeignKey(
                name: "FK_totp_pending_authentication_external_identity_links_account~",
                schema: "identity",
                table: "totp_pending_authentication",
                columns: FederatedLinkOwnerColumns,
                principalSchema: "identity",
                principalTable: "external_identity_links",
                principalColumns: LinkOwnerColumns,
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_totp_pending_authentication_federation_attempts_federation_~",
                schema: "identity",
                table: "totp_pending_authentication",
                column: "federation_attempt_id",
                principalSchema: "identity",
                principalTable: "federation_attempts",
                principalColumn: "attempt_id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_tunner_sessions_external_identity_links_account_id_federati~",
                schema: "identity",
                table: "tunner_sessions",
                columns: FederatedLinkOwnerColumns,
                principalSchema: "identity",
                principalTable: "external_identity_links",
                principalColumns: LinkOwnerColumns,
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_tunner_sessions_federation_attempts_federation_attempt_id",
                schema: "identity",
                table: "tunner_sessions",
                column: "federation_attempt_id",
                principalSchema: "identity",
                principalTable: "federation_attempts",
                principalColumn: "attempt_id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.Sql(FederationAttemptMigrationSql.Harden);
            migrationBuilder.Sql(FederationLinkLifecycle.Harden);
            migrationBuilder.Sql(FederationSessionMigrationSql.Harden);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Identity downgrade is permitted only for an empty foundation. Preserve every
            // existing owner and federation lineage by using the established forward-only guard.
            migrationBuilder.Sql(IdentityMigrationSql.GuardDowngrade);
            migrationBuilder.Sql(FederationSessionMigrationSql.Remove);
            migrationBuilder.Sql("DROP TRIGGER federation_attempt_guard ON identity.federation_attempts; DROP FUNCTION identity.guard_federation_attempt(); DROP TRIGGER external_qualification_guard ON identity.external_identity_links; DROP FUNCTION identity.guard_external_qualification();");

            migrationBuilder.DropForeignKey(
                name: "FK_totp_pending_authentication_external_identity_links_account~",
                schema: "identity",
                table: "totp_pending_authentication");

            migrationBuilder.DropForeignKey(
                name: "FK_totp_pending_authentication_federation_attempts_federation_~",
                schema: "identity",
                table: "totp_pending_authentication");

            migrationBuilder.DropForeignKey(
                name: "FK_tunner_sessions_external_identity_links_account_id_federati~",
                schema: "identity",
                table: "tunner_sessions");

            migrationBuilder.DropForeignKey(
                name: "FK_tunner_sessions_federation_attempts_federation_attempt_id",
                schema: "identity",
                table: "tunner_sessions");

            migrationBuilder.DropTable(
                name: "federation_attempts",
                schema: "identity");

            migrationBuilder.DropIndex(
                name: "IX_tunner_sessions_account_id_federation_link_id",
                schema: "identity",
                table: "tunner_sessions");

            migrationBuilder.DropIndex(
                name: "IX_tunner_sessions_federation_attempt_id",
                schema: "identity",
                table: "tunner_sessions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_session_federation",
                schema: "identity",
                table: "tunner_sessions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_session_method",
                schema: "identity",
                table: "tunner_sessions");

            migrationBuilder.DropIndex(
                name: "IX_totp_pending_authentication_account_id_federation_link_id",
                schema: "identity",
                table: "totp_pending_authentication");

            migrationBuilder.DropIndex(
                name: "IX_totp_pending_authentication_federation_attempt_id",
                schema: "identity",
                table: "totp_pending_authentication");

            migrationBuilder.DropCheckConstraint(
                name: "ck_totp_pending",
                schema: "identity",
                table: "totp_pending_authentication");

            migrationBuilder.DropCheckConstraint(
                name: "ck_totp_pending_federation",
                schema: "identity",
                table: "totp_pending_authentication");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_external_identity_links_account_id_link_id",
                schema: "identity",
                table: "external_identity_links");

            migrationBuilder.DropCheckConstraint(
                name: "ck_external_qualification",
                schema: "identity",
                table: "external_identity_links");

            migrationBuilder.DropColumn(
                name: "federation_assurance_mapping_evidence_reference",
                schema: "identity",
                table: "tunner_sessions");

            migrationBuilder.DropColumn(
                name: "federation_attempt_id",
                schema: "identity",
                table: "tunner_sessions");

            migrationBuilder.DropColumn(
                name: "federation_client_id",
                schema: "identity",
                table: "tunner_sessions");

            migrationBuilder.DropColumn(
                name: "federation_issuer",
                schema: "identity",
                table: "tunner_sessions");

            migrationBuilder.DropColumn(
                name: "federation_issuer_mapping_evidence_reference",
                schema: "identity",
                table: "tunner_sessions");

            migrationBuilder.DropColumn(
                name: "federation_link_id",
                schema: "identity",
                table: "tunner_sessions");

            migrationBuilder.DropColumn(
                name: "federation_link_version",
                schema: "identity",
                table: "tunner_sessions");

            migrationBuilder.DropColumn(
                name: "federation_namespace",
                schema: "identity",
                table: "tunner_sessions");

            migrationBuilder.DropColumn(
                name: "federation_primary_assurance",
                schema: "identity",
                table: "tunner_sessions");

            migrationBuilder.DropColumn(
                name: "federation_profile_version",
                schema: "identity",
                table: "tunner_sessions");

            migrationBuilder.DropColumn(
                name: "federation_provider",
                schema: "identity",
                table: "tunner_sessions");

            migrationBuilder.DropColumn(
                name: "federation_qualification_evidence_reference",
                schema: "identity",
                table: "tunner_sessions");

            migrationBuilder.DropColumn(
                name: "federation_qualification_profile_version",
                schema: "identity",
                table: "tunner_sessions");

            migrationBuilder.DropColumn(
                name: "federation_session_use_policy_version",
                schema: "identity",
                table: "tunner_sessions");

            migrationBuilder.DropColumn(
                name: "federation_sign_in_admission_policy_version",
                schema: "identity",
                table: "tunner_sessions");

            migrationBuilder.DropColumn(
                name: "federation_assurance_mapping_evidence_reference",
                schema: "identity",
                table: "totp_pending_authentication");

            migrationBuilder.DropColumn(
                name: "federation_attempt_id",
                schema: "identity",
                table: "totp_pending_authentication");

            migrationBuilder.DropColumn(
                name: "federation_client_id",
                schema: "identity",
                table: "totp_pending_authentication");

            migrationBuilder.DropColumn(
                name: "federation_issuer",
                schema: "identity",
                table: "totp_pending_authentication");

            migrationBuilder.DropColumn(
                name: "federation_issuer_mapping_evidence_reference",
                schema: "identity",
                table: "totp_pending_authentication");

            migrationBuilder.DropColumn(
                name: "federation_link_id",
                schema: "identity",
                table: "totp_pending_authentication");

            migrationBuilder.DropColumn(
                name: "federation_link_version",
                schema: "identity",
                table: "totp_pending_authentication");

            migrationBuilder.DropColumn(
                name: "federation_namespace",
                schema: "identity",
                table: "totp_pending_authentication");

            migrationBuilder.DropColumn(
                name: "federation_profile_version",
                schema: "identity",
                table: "totp_pending_authentication");

            migrationBuilder.DropColumn(
                name: "federation_provider",
                schema: "identity",
                table: "totp_pending_authentication");

            migrationBuilder.DropColumn(
                name: "federation_qualification_evidence_reference",
                schema: "identity",
                table: "totp_pending_authentication");

            migrationBuilder.DropColumn(
                name: "federation_qualification_profile_version",
                schema: "identity",
                table: "totp_pending_authentication");

            migrationBuilder.DropColumn(
                name: "federation_session_use_policy_version",
                schema: "identity",
                table: "totp_pending_authentication");

            migrationBuilder.DropColumn(
                name: "federation_sign_in_admission_policy_version",
                schema: "identity",
                table: "totp_pending_authentication");

            migrationBuilder.DropColumn(
                name: "issuer_mapping_evidence_reference",
                schema: "identity",
                table: "external_identity_links");

            migrationBuilder.DropColumn(
                name: "link_state",
                schema: "identity",
                table: "external_identity_links");

            migrationBuilder.DropColumn(
                name: "qualification_evidence_reference",
                schema: "identity",
                table: "external_identity_links");

            migrationBuilder.DropColumn(
                name: "qualification_profile_version",
                schema: "identity",
                table: "external_identity_links");

            migrationBuilder.DropColumn(
                name: "qualified_client_id",
                schema: "identity",
                table: "external_identity_links");

            migrationBuilder.AddCheckConstraint(
                name: "ck_session_method",
                schema: "identity",
                table: "tunner_sessions",
                sql: "((authentication_methods IN ('pwd','pwd+totp') AND passkey_credential_reference IS NULL AND passkey_policy_version IS NULL AND passkey_assurance_key IS NULL) OR (authentication_methods IN ('passkey','passkey+totp') AND passkey_credential_reference IS NOT NULL AND passkey_policy_version IS NOT NULL AND passkey_assurance_key IS NOT NULL)) AND ((authentication_methods IN ('pwd','passkey') AND totp_authenticator_reference IS NULL AND totp_generation IS NULL AND totp_authenticator_version IS NULL AND totp_policy_version IS NULL AND mfa_requirement_policy_version IS NULL) OR (authentication_methods IN ('pwd+totp','passkey+totp') AND totp_authenticator_reference IS NOT NULL AND totp_generation IS NOT NULL AND totp_authenticator_version>=2 AND totp_policy_version IS NOT NULL AND mfa_requirement_policy_version IS NOT NULL))");

            migrationBuilder.CreateIndex(
                name: "IX_totp_pending_authentication_account_id",
                schema: "identity",
                table: "totp_pending_authentication",
                column: "account_id");

            migrationBuilder.AddCheckConstraint(
                name: "ck_totp_pending",
                schema: "identity",
                table: "totp_pending_authentication",
                sql: "state IN ('PENDING','COMPLETED','EXPIRED') AND primary_method IN ('pwd','passkey') AND expires_at>primary_verified_at AND recovery_expires_at>expires_at AND version>=1");

            migrationBuilder.CreateIndex(
                name: "IX_external_identity_links_account_id",
                schema: "identity",
                table: "external_identity_links",
                column: "account_id");
        }
    }
}
