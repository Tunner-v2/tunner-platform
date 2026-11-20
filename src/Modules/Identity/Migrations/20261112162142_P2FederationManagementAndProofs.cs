using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tunner.Identity.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class P2FederationManagementAndProofs : Migration
    {
        private static readonly string[] SelectedProofOwnerColumns = ["owner_account_id", "selected_proof_link_id"];
        private static readonly string[] LinkOwnerColumns = ["account_id", "link_id"];
        private static readonly string[] SessionRequestColumns = ["session_id", "request_id"];
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_session_termination",
                schema: "identity",
                table: "tunner_sessions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_step_up_method",
                schema: "identity",
                table: "step_up_proofs");

            migrationBuilder.DropCheckConstraint(
                name: "ck_federation_result",
                schema: "identity",
                table: "federation_attempts");

            migrationBuilder.DropCheckConstraint(
                name: "ck_federation_verified",
                schema: "identity",
                table: "federation_attempts");

            migrationBuilder.AddColumn<Guid>(
                name: "federation_attempt_id",
                schema: "identity",
                table: "step_up_proofs",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "owner_account_id",
                schema: "identity",
                table: "federation_attempts",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "owner_account_version",
                schema: "identity",
                table: "federation_attempts",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "owner_assurance_policy_version",
                schema: "identity",
                table: "federation_attempts",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "owner_context_digest",
                schema: "identity",
                table: "federation_attempts",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "owner_expected_version",
                schema: "identity",
                table: "federation_attempts",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "owner_operation_id",
                schema: "identity",
                table: "federation_attempts",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "owner_security_stamp_digest",
                schema: "identity",
                table: "federation_attempts",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "owner_session_id",
                schema: "identity",
                table: "federation_attempts",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "owner_session_version",
                schema: "identity",
                table: "federation_attempts",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "protected_link_subject",
                schema: "identity",
                table: "federation_attempts",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "provider_authenticated_at",
                schema: "identity",
                table: "federation_attempts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "provider_methods_json",
                schema: "identity",
                table: "federation_attempts",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "result_proof_id",
                schema: "identity",
                table: "federation_attempts",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "selected_proof_link_id",
                schema: "identity",
                table: "federation_attempts",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "selected_proof_link_version",
                schema: "identity",
                table: "federation_attempts",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "federation_link_operations",
                schema: "identity",
                columns: table => new
                {
                    operation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "text", nullable: false),
                    account_id = table.Column<string>(type: "text", nullable: false),
                    session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    origin = table.Column<string>(type: "text", nullable: false),
                    provider = table.Column<string>(type: "text", nullable: false),
                    profile_version = table.Column<string>(type: "text", nullable: false),
                    profile_snapshot = table.Column<string>(type: "text", nullable: false),
                    attempt_id = table.Column<Guid>(type: "uuid", nullable: true),
                    target_link_id = table.Column<Guid>(type: "uuid", nullable: true),
                    expected_link_version = table.Column<long>(type: "bigint", nullable: false),
                    expected_account_version = table.Column<long>(type: "bigint", nullable: false),
                    policy_version = table.Column<string>(type: "text", nullable: false),
                    policy_snapshot = table.Column<string>(type: "text", nullable: false),
                    assurance_policy_version = table.Column<string>(type: "text", nullable: false),
                    authentication_policy_version = table.Column<string>(type: "text", nullable: false),
                    security_stamp_digest = table.Column<string>(type: "text", nullable: false),
                    original_cookie_digest = table.Column<string>(type: "text", nullable: true),
                    context_digest = table.Column<string>(type: "text", nullable: false),
                    session_effect = table.Column<string>(type: "text", nullable: false),
                    session_use_policy_version = table.Column<string>(type: "text", nullable: false),
                    session_effect_evidence_reference = table.Column<string>(type: "text", nullable: false),
                    processing_reference = table.Column<string>(type: "text", nullable: false),
                    retention_reference = table.Column<string>(type: "text", nullable: false),
                    state = table.Column<string>(type: "text", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    recovery_expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    confirmed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ended_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    proof_reference = table.Column<Guid>(type: "uuid", nullable: true),
                    result_link_id = table.Column<Guid>(type: "uuid", nullable: true),
                    result_link_version = table.Column<long>(type: "bigint", nullable: true),
                    posture_facts_digest = table.Column<string>(type: "text", nullable: true),
                    posture_policy_version = table.Column<string>(type: "text", nullable: true),
                    posture_evidence_reference = table.Column<string>(type: "text", nullable: true),
                    posture_from = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    posture_includes_from = table.Column<bool>(type: "boolean", nullable: true),
                    posture_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    posture_includes_until = table.Column<bool>(type: "boolean", nullable: true),
                    error_code = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_federation_link_operations", x => x.operation_id);
                    table.CheckConstraint("ck_federation_management_binding", "length(context_digest)=64 AND length(security_stamp_digest)=64 AND length(policy_version)>0 AND length(profile_version)>0 AND length(assurance_policy_version)>0 AND length(authentication_policy_version)>0 AND jsonb_typeof(policy_snapshot::jsonb)='object' AND jsonb_typeof(profile_snapshot::jsonb)='object' AND length(processing_reference)>0 AND length(retention_reference)>0 AND session_effect IN ('PreserveExistingSessions','RevokeLinkedSessions') AND length(session_use_policy_version)>0 AND length(session_effect_evidence_reference)>0");
                    table.CheckConstraint("ck_federation_management_confirmation", "state NOT IN ('PENDING_PROOF','COMPLETED') OR confirmed_at IS NOT NULL");
                    table.CheckConstraint("ck_federation_management_cookie", "original_cookie_digest IS NULL OR length(original_cookie_digest)=64");
                    table.CheckConstraint("ck_federation_management_kind", "kind IN ('LINK','UNLINK') AND provider IN ('Google','Microsoft') AND ((kind='LINK' AND attempt_id IS NOT NULL) OR (kind='UNLINK' AND attempt_id IS NULL AND target_link_id IS NOT NULL AND expected_link_version>=1))");
                    table.CheckConstraint("ck_federation_management_posture", "(kind='UNLINK' AND state='COMPLETED' AND posture_facts_digest IS NOT NULL AND length(posture_facts_digest)=64 AND posture_policy_version IS NOT NULL AND length(posture_policy_version)>0 AND posture_evidence_reference IS NOT NULL AND length(posture_evidence_reference)>0 AND posture_from IS NOT NULL AND posture_includes_from IS NOT NULL AND posture_includes_until IS NOT NULL AND (posture_until IS NULL OR posture_until>=posture_from)) OR (NOT(kind='UNLINK' AND state='COMPLETED') AND posture_facts_digest IS NULL AND posture_policy_version IS NULL AND posture_evidence_reference IS NULL AND posture_from IS NULL AND posture_includes_from IS NULL AND posture_until IS NULL AND posture_includes_until IS NULL)");
                    table.CheckConstraint("ck_federation_management_state", "state IN ('PENDING_PROVIDER','PENDING_CONFIRMATION','PENDING_PROOF','COMPLETED','REJECTED','EXPIRED') AND version>=1 AND expected_link_version>=0 AND expected_account_version>=1");
                    table.CheckConstraint("ck_federation_management_time", "expires_at>created_at AND recovery_expires_at>expires_at AND (confirmed_at IS NULL OR (confirmed_at>=created_at AND confirmed_at<expires_at)) AND ((state IN ('PENDING_PROVIDER','PENDING_CONFIRMATION','PENDING_PROOF') AND ended_at IS NULL AND proof_reference IS NULL AND result_link_id IS NULL AND result_link_version IS NULL AND error_code IS NULL) OR (state='COMPLETED' AND confirmed_at IS NOT NULL AND ended_at>=confirmed_at AND proof_reference IS NOT NULL AND result_link_id IS NOT NULL AND result_link_version>=1 AND error_code IS NULL) OR (state IN ('REJECTED','EXPIRED') AND ended_at>=created_at AND error_code IN ('AUTHENTICATION_FAILED','VERSION_CONFLICT','RATE_OR_ABUSE_LIMITED','STEP_UP_REQUIRED') AND proof_reference IS NULL AND result_link_id IS NULL AND result_link_version IS NULL))");
                    table.ForeignKey(
                        name: "FK_federation_link_operations_external_identity_links_result_l~",
                        column: x => x.result_link_id,
                        principalSchema: "identity",
                        principalTable: "external_identity_links",
                        principalColumn: "link_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_federation_link_operations_external_identity_links_target_l~",
                        column: x => x.target_link_id,
                        principalSchema: "identity",
                        principalTable: "external_identity_links",
                        principalColumn: "link_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_federation_link_operations_federation_attempts_attempt_id",
                        column: x => x.attempt_id,
                        principalSchema: "identity",
                        principalTable: "federation_attempts",
                        principalColumn: "attempt_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_federation_link_operations_step_up_proofs_proof_reference",
                        column: x => x.proof_reference,
                        principalSchema: "identity",
                        principalTable: "step_up_proofs",
                        principalColumn: "proof_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_federation_link_operations_tunner_sessions_session_id",
                        column: x => x.session_id,
                        principalSchema: "identity",
                        principalTable: "tunner_sessions",
                        principalColumn: "session_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_session_termination",
                schema: "identity",
                table: "tunner_sessions",
                sql: "(termination_cause IS NULL AND termination_policy_version IS NULL AND termination_credential_version IS NULL) OR (state='REVOKED' AND termination_cause IN ('PASSKEY_REVOKED','TOTP_DISABLED','FEDERATION_UNLINKED') AND termination_policy_version IS NOT NULL AND termination_credential_version IS NOT NULL AND termination_credential_version >= 2)");

            migrationBuilder.CreateIndex(
                name: "IX_step_up_proofs_federation_attempt_id",
                schema: "identity",
                table: "step_up_proofs",
                column: "federation_attempt_id");

            migrationBuilder.AddCheckConstraint(
                name: "ck_step_up_method",
                schema: "identity",
                table: "step_up_proofs",
                sql: "((((authentication_method='pwd' AND passkey_credential_reference IS NULL AND passkey_policy_version IS NULL AND passkey_assurance_key IS NULL) OR (authentication_method LIKE 'passkey:%' AND passkey_credential_reference IS NOT NULL AND passkey_policy_version IS NOT NULL AND passkey_assurance_key IS NOT NULL)) AND totp_authenticator_reference IS NULL AND totp_generation IS NULL AND totp_authenticator_version IS NULL AND totp_policy_version IS NULL OR (authentication_method='totp' AND passkey_credential_reference IS NULL AND passkey_policy_version IS NULL AND passkey_assurance_key IS NULL AND totp_authenticator_reference IS NOT NULL AND totp_generation IS NOT NULL AND totp_authenticator_version>=2 AND totp_policy_version IS NOT NULL)) AND federation_attempt_id IS NULL) OR (authentication_method IN ('federation:Google','federation:Microsoft') AND federation_attempt_id IS NOT NULL AND passkey_credential_reference IS NULL AND passkey_policy_version IS NULL AND passkey_assurance_key IS NULL AND totp_authenticator_reference IS NULL AND totp_generation IS NULL AND totp_authenticator_version IS NULL AND totp_policy_version IS NULL)");

            migrationBuilder.CreateIndex(
                name: "IX_federation_attempts_owner_account_id_selected_proof_link_id",
                schema: "identity",
                table: "federation_attempts",
                columns: SelectedProofOwnerColumns);

            migrationBuilder.CreateIndex(
                name: "IX_federation_attempts_result_proof_id",
                schema: "identity",
                table: "federation_attempts",
                column: "result_proof_id");

            migrationBuilder.AddCheckConstraint(
                name: "ck_federation_fresh_evidence",
                schema: "identity",
                table: "federation_attempts",
                sql: "(purpose<>'OperationProof' AND provider_authenticated_at IS NULL AND provider_methods_json IS NULL AND result_proof_id IS NULL)\nOR (purpose='OperationProof' AND ((verified_at IS NULL AND provider_authenticated_at IS NULL AND provider_methods_json IS NULL)\n  OR (verified_at IS NOT NULL AND provider_authenticated_at IS NOT NULL AND provider_authenticated_at<=verified_at\n    AND link_id=selected_proof_link_id AND link_version=selected_proof_link_version\n    AND provider_methods_json IS NOT NULL AND jsonb_typeof(provider_methods_json::jsonb)='array' AND jsonb_array_length(provider_methods_json::jsonb)>0)))");

            migrationBuilder.AddCheckConstraint(
                name: "ck_federation_owner",
                schema: "identity",
                table: "federation_attempts",
                sql: "(purpose NOT IN ('Link','OperationProof') AND owner_account_id IS NULL AND owner_session_id IS NULL AND owner_account_version IS NULL\n  AND owner_session_version IS NULL AND owner_security_stamp_digest IS NULL AND owner_operation_id IS NULL\n  AND owner_context_digest IS NULL AND owner_expected_version IS NULL AND protected_link_subject IS NULL AND owner_assurance_policy_version IS NULL\n  AND selected_proof_link_id IS NULL AND selected_proof_link_version IS NULL)\nOR (purpose IN ('Link','OperationProof') AND owner_account_id IS NOT NULL AND length(owner_account_id)>0 AND owner_session_id IS NOT NULL\n  AND owner_session_id=previous_session_id AND owner_account_version IS NOT NULL AND owner_account_version>=1\n  AND owner_session_version IS NOT NULL AND owner_session_version>=1 AND owner_security_stamp_digest IS NOT NULL\n  AND length(owner_security_stamp_digest)=64 AND owner_operation_id IS NOT NULL AND length(owner_operation_id)>0\n  AND owner_context_digest IS NOT NULL AND length(owner_context_digest)=64 AND owner_expected_version IS NOT NULL AND owner_expected_version>=0\n  AND ((purpose='Link' AND owner_assurance_policy_version IS NULL AND selected_proof_link_id IS NULL AND selected_proof_link_version IS NULL)\n    OR (purpose='OperationProof' AND owner_assurance_policy_version IS NOT NULL AND length(owner_assurance_policy_version)>0 AND protected_link_subject IS NULL\n      AND selected_proof_link_id IS NOT NULL AND selected_proof_link_version IS NOT NULL AND selected_proof_link_version>0)))");

            migrationBuilder.AddCheckConstraint(
                name: "ck_federation_result",
                schema: "identity",
                table: "federation_attempts",
                sql: "(state IN ('PREPARED','CHALLENGE_ADMITTED','VERIFIED') AND ended_at IS NULL AND result_expires_at IS NULL\n  AND result_session_id IS NULL AND pending_mfa_id IS NULL AND result_proof_id IS NULL AND error_code IS NULL)\nOR (state IN ('COMPLETED','REJECTED','EXPIRED') AND ended_at IS NOT NULL AND result_expires_at IS NOT NULL\n  AND ended_at>=created_at AND result_expires_at>ended_at AND ((state='COMPLETED' AND verified_at IS NOT NULL AND error_code IS NULL\n    AND ((purpose='SignIn' AND result_proof_id IS NULL AND ((result_session_id IS NOT NULL AND pending_mfa_id IS NULL) OR (result_session_id IS NULL AND pending_mfa_id IS NOT NULL)))\n      OR (purpose='Link' AND result_session_id IS NULL AND pending_mfa_id IS NULL AND result_proof_id IS NULL)\n      OR (purpose='OperationProof' AND result_session_id IS NULL AND pending_mfa_id IS NULL AND result_proof_id IS NOT NULL)))\n    OR (state IN ('REJECTED','EXPIRED') AND error_code IS NOT NULL AND result_session_id IS NULL AND pending_mfa_id IS NULL AND result_proof_id IS NULL)))");

            migrationBuilder.AddCheckConstraint(
                name: "ck_federation_verified",
                schema: "identity",
                table: "federation_attempts",
                sql: "(verified_at IS NULL AND verification_expires_at IS NULL AND validated_issuer IS NULL AND link_id IS NULL AND link_version IS NULL\n  AND link_qualification_evidence_reference IS NULL AND link_qualification_profile_version IS NULL AND protected_link_subject IS NULL\n  AND state NOT IN ('VERIFIED','COMPLETED'))\nOR (verified_at IS NOT NULL AND verification_expires_at IS NOT NULL AND validated_issuer IS NOT NULL\n  AND verified_at>=created_at AND verified_at<expires_at AND verification_expires_at>verified_at AND verification_expires_at<=expires_at\n  AND length(validated_issuer)>0 AND ((purpose IN ('SignIn','OperationProof') AND link_id IS NOT NULL AND link_version IS NOT NULL AND link_version>=1\n    AND link_qualification_evidence_reference IS NOT NULL AND length(link_qualification_evidence_reference)>0\n    AND link_qualification_profile_version IS NOT NULL AND length(link_qualification_profile_version)>0 AND protected_link_subject IS NULL)\n  OR (purpose='Link' AND protected_link_subject IS NOT NULL AND length(protected_link_subject)>0\n    AND link_id IS NULL AND link_version IS NULL AND link_qualification_evidence_reference IS NULL AND link_qualification_profile_version IS NULL)))");

            migrationBuilder.CreateIndex(
                name: "IX_federation_link_operations_attempt_id",
                schema: "identity",
                table: "federation_link_operations",
                column: "attempt_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_federation_link_operations_expires_at",
                schema: "identity",
                table: "federation_link_operations",
                column: "expires_at");

            migrationBuilder.CreateIndex(
                name: "IX_federation_link_operations_proof_reference",
                schema: "identity",
                table: "federation_link_operations",
                column: "proof_reference");

            migrationBuilder.CreateIndex(
                name: "IX_federation_link_operations_result_link_id",
                schema: "identity",
                table: "federation_link_operations",
                column: "result_link_id");

            migrationBuilder.CreateIndex(
                name: "IX_federation_link_operations_session_id_request_id",
                schema: "identity",
                table: "federation_link_operations",
                columns: SessionRequestColumns,
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_federation_link_operations_target_link_id",
                schema: "identity",
                table: "federation_link_operations",
                column: "target_link_id");

            migrationBuilder.AddForeignKey(
                name: "FK_federation_attempts_external_identity_links_owner_account_i~",
                schema: "identity",
                table: "federation_attempts",
                columns: SelectedProofOwnerColumns,
                principalSchema: "identity",
                principalTable: "external_identity_links",
                principalColumns: LinkOwnerColumns,
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_federation_attempts_step_up_proofs_result_proof_id",
                schema: "identity",
                table: "federation_attempts",
                column: "result_proof_id",
                principalSchema: "identity",
                principalTable: "step_up_proofs",
                principalColumn: "proof_id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_step_up_proofs_federation_attempts_federation_attempt_id",
                schema: "identity",
                table: "step_up_proofs",
                column: "federation_attempt_id",
                principalSchema: "identity",
                principalTable: "federation_attempts",
                principalColumn: "attempt_id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.Sql(FederationLinkManagementMigrationSql.UpgradeAttemptGuard);
            migrationBuilder.Sql(FederationLinkManagementMigrationSql.Harden);
            migrationBuilder.Sql(FederationSessionEffectsMigrationSql.SessionGuard);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(IdentityMigrationSql.GuardDowngrade);
            migrationBuilder.Sql("DROP TRIGGER federation_link_operation_guard ON identity.federation_link_operations; DROP FUNCTION identity.guard_federation_link_operation(); DROP TRIGGER federation_attempt_guard ON identity.federation_attempts; DROP FUNCTION identity.guard_federation_attempt();");
            migrationBuilder.Sql(FederationAttemptMigrationSql.Harden);
            migrationBuilder.Sql(TotpMigrationSql.SessionGuard);

            migrationBuilder.DropForeignKey(
                name: "FK_federation_attempts_external_identity_links_owner_account_i~",
                schema: "identity",
                table: "federation_attempts");

            migrationBuilder.DropForeignKey(
                name: "FK_federation_attempts_step_up_proofs_result_proof_id",
                schema: "identity",
                table: "federation_attempts");

            migrationBuilder.DropForeignKey(
                name: "FK_step_up_proofs_federation_attempts_federation_attempt_id",
                schema: "identity",
                table: "step_up_proofs");

            migrationBuilder.DropTable(
                name: "federation_link_operations",
                schema: "identity");

            migrationBuilder.DropCheckConstraint(
                name: "ck_session_termination",
                schema: "identity",
                table: "tunner_sessions");

            migrationBuilder.DropIndex(
                name: "IX_step_up_proofs_federation_attempt_id",
                schema: "identity",
                table: "step_up_proofs");

            migrationBuilder.DropCheckConstraint(
                name: "ck_step_up_method",
                schema: "identity",
                table: "step_up_proofs");

            migrationBuilder.DropIndex(
                name: "IX_federation_attempts_owner_account_id_selected_proof_link_id",
                schema: "identity",
                table: "federation_attempts");

            migrationBuilder.DropIndex(
                name: "IX_federation_attempts_result_proof_id",
                schema: "identity",
                table: "federation_attempts");

            migrationBuilder.DropCheckConstraint(
                name: "ck_federation_fresh_evidence",
                schema: "identity",
                table: "federation_attempts");

            migrationBuilder.DropCheckConstraint(
                name: "ck_federation_owner",
                schema: "identity",
                table: "federation_attempts");

            migrationBuilder.DropCheckConstraint(
                name: "ck_federation_result",
                schema: "identity",
                table: "federation_attempts");

            migrationBuilder.DropCheckConstraint(
                name: "ck_federation_verified",
                schema: "identity",
                table: "federation_attempts");

            migrationBuilder.DropColumn(
                name: "federation_attempt_id",
                schema: "identity",
                table: "step_up_proofs");

            migrationBuilder.DropColumn(
                name: "owner_account_id",
                schema: "identity",
                table: "federation_attempts");

            migrationBuilder.DropColumn(
                name: "owner_account_version",
                schema: "identity",
                table: "federation_attempts");

            migrationBuilder.DropColumn(
                name: "owner_assurance_policy_version",
                schema: "identity",
                table: "federation_attempts");

            migrationBuilder.DropColumn(
                name: "owner_context_digest",
                schema: "identity",
                table: "federation_attempts");

            migrationBuilder.DropColumn(
                name: "owner_expected_version",
                schema: "identity",
                table: "federation_attempts");

            migrationBuilder.DropColumn(
                name: "owner_operation_id",
                schema: "identity",
                table: "federation_attempts");

            migrationBuilder.DropColumn(
                name: "owner_security_stamp_digest",
                schema: "identity",
                table: "federation_attempts");

            migrationBuilder.DropColumn(
                name: "owner_session_id",
                schema: "identity",
                table: "federation_attempts");

            migrationBuilder.DropColumn(
                name: "owner_session_version",
                schema: "identity",
                table: "federation_attempts");

            migrationBuilder.DropColumn(
                name: "protected_link_subject",
                schema: "identity",
                table: "federation_attempts");

            migrationBuilder.DropColumn(
                name: "provider_authenticated_at",
                schema: "identity",
                table: "federation_attempts");

            migrationBuilder.DropColumn(
                name: "provider_methods_json",
                schema: "identity",
                table: "federation_attempts");

            migrationBuilder.DropColumn(
                name: "result_proof_id",
                schema: "identity",
                table: "federation_attempts");

            migrationBuilder.DropColumn(
                name: "selected_proof_link_id",
                schema: "identity",
                table: "federation_attempts");

            migrationBuilder.DropColumn(
                name: "selected_proof_link_version",
                schema: "identity",
                table: "federation_attempts");

            migrationBuilder.AddCheckConstraint(
                name: "ck_session_termination",
                schema: "identity",
                table: "tunner_sessions",
                sql: "(termination_cause IS NULL AND termination_policy_version IS NULL AND termination_credential_version IS NULL) OR (state='REVOKED' AND termination_cause IN ('PASSKEY_REVOKED','TOTP_DISABLED') AND termination_policy_version IS NOT NULL AND termination_credential_version IS NOT NULL AND termination_credential_version >= 2)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_step_up_method",
                schema: "identity",
                table: "step_up_proofs",
                sql: "((authentication_method='pwd' AND passkey_credential_reference IS NULL AND passkey_policy_version IS NULL AND passkey_assurance_key IS NULL) OR (authentication_method LIKE 'passkey:%' AND passkey_credential_reference IS NOT NULL AND passkey_policy_version IS NOT NULL AND passkey_assurance_key IS NOT NULL)) AND totp_authenticator_reference IS NULL AND totp_generation IS NULL AND totp_authenticator_version IS NULL AND totp_policy_version IS NULL OR (authentication_method='totp' AND passkey_credential_reference IS NULL AND passkey_policy_version IS NULL AND passkey_assurance_key IS NULL AND totp_authenticator_reference IS NOT NULL AND totp_generation IS NOT NULL AND totp_authenticator_version>=2 AND totp_policy_version IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_federation_result",
                schema: "identity",
                table: "federation_attempts",
                sql: "(state IN ('PREPARED','CHALLENGE_ADMITTED','VERIFIED') AND ended_at IS NULL AND result_expires_at IS NULL AND result_session_id IS NULL AND pending_mfa_id IS NULL AND error_code IS NULL) OR (state IN ('COMPLETED','REJECTED','EXPIRED') AND ended_at IS NOT NULL AND result_expires_at IS NOT NULL AND ended_at>=created_at AND result_expires_at>ended_at AND ((state='COMPLETED' AND purpose='SignIn' AND verified_at IS NOT NULL AND error_code IS NULL AND ((result_session_id IS NOT NULL AND pending_mfa_id IS NULL) OR (result_session_id IS NULL AND pending_mfa_id IS NOT NULL))) OR (state IN ('REJECTED','EXPIRED') AND error_code IS NOT NULL AND result_session_id IS NULL AND pending_mfa_id IS NULL)))");

            migrationBuilder.AddCheckConstraint(
                name: "ck_federation_verified",
                schema: "identity",
                table: "federation_attempts",
                sql: "(state NOT IN ('VERIFIED','COMPLETED') AND verified_at IS NULL AND verification_expires_at IS NULL AND validated_issuer IS NULL AND link_id IS NULL AND link_version IS NULL AND link_qualification_evidence_reference IS NULL AND link_qualification_profile_version IS NULL) OR (verified_at IS NOT NULL AND verification_expires_at IS NOT NULL AND validated_issuer IS NOT NULL AND link_id IS NOT NULL AND link_version IS NOT NULL AND link_qualification_evidence_reference IS NOT NULL AND link_qualification_profile_version IS NOT NULL AND verified_at>=created_at AND verified_at<expires_at AND verification_expires_at>verified_at AND verification_expires_at<=expires_at AND length(validated_issuer)>0 AND link_version>=1 AND length(link_qualification_evidence_reference)>0 AND length(link_qualification_profile_version)>0)");
        }
    }
}
