using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tunner.Identity.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class P2RecoveryContactRiskRepair : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_recovery_result_shape",
                schema: "identity",
                table: "recovery_results");

            migrationBuilder.DropCheckConstraint(
                name: "ck_recovery_case_state",
                schema: "identity",
                table: "recovery_cases");

            migrationBuilder.DropCheckConstraint(
                name: "ck_recovery_case_versions",
                schema: "identity",
                table: "recovery_cases");

            migrationBuilder.AddColumn<Guid>(
                name: "contact_challenge_id",
                schema: "identity",
                table: "recovery_results",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "contact_destination_digest",
                schema: "identity",
                table: "recovery_results",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "contact_notification_delivery_id",
                schema: "identity",
                table: "recovery_results",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "contact_reference",
                schema: "identity",
                table: "recovery_results",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "contact_risk_snapshot",
                schema: "identity",
                table: "recovery_results",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "contact_version",
                schema: "identity",
                table: "recovery_results",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "contact_challenge_id",
                schema: "identity",
                table: "recovery_entries",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "contact_expires_at",
                schema: "identity",
                table: "recovery_entries",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "contact_profile_version",
                schema: "identity",
                table: "recovery_entries",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "contact_challenge_id",
                schema: "identity",
                table: "recovery_cases",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "contact_destination_digest",
                schema: "identity",
                table: "recovery_cases",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "contact_reference",
                schema: "identity",
                table: "recovery_cases",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "expected_contact_version",
                schema: "identity",
                table: "recovery_cases",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "initial_contact_risk_snapshot",
                schema: "identity",
                table: "recovery_cases",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "recovery_contact_challenges",
                schema: "identity",
                columns: table => new
                {
                    challenge_id = table.Column<Guid>(type: "uuid", nullable: false),
                    case_id = table.Column<Guid>(type: "uuid", nullable: false),
                    entry_id = table.Column<Guid>(type: "uuid", nullable: false),
                    contact_reference = table.Column<Guid>(type: "uuid", nullable: false),
                    contact_version = table.Column<long>(type: "bigint", nullable: false),
                    context_digest = table.Column<string>(type: "text", nullable: false),
                    destination_digest = table.Column<string>(type: "text", nullable: false),
                    profile_version = table.Column<string>(type: "text", nullable: false),
                    protected_code = table.Column<string>(type: "text", nullable: true),
                    protected_digest = table.Column<string>(type: "text", nullable: true),
                    protected_address = table.Column<string>(type: "text", nullable: true),
                    maximum_attempts = table.Column<int>(type: "integer", nullable: false),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    state = table.Column<string>(type: "text", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    delivery_id = table.Column<Guid>(type: "uuid", nullable: false),
                    notification_outbox_event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    notification_snapshot = table.Column<string>(type: "text", nullable: false),
                    processing_reference = table.Column<string>(type: "text", nullable: false),
                    retention_reference = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    purge_after = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ended_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    material_purged_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    result_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_recovery_contact_challenges", x => x.challenge_id);
                    table.CheckConstraint("ck_recovery_contact_challenge", "length(context_digest)=64 AND length(destination_digest)=64 AND length(profile_version)>0 AND contact_version>=1 AND maximum_attempts>0\r\nAND attempts>=0 AND attempts<=maximum_attempts AND version>=1 AND expires_at>created_at AND purge_after>=expires_at\r\nAND jsonb_typeof(notification_snapshot::jsonb)='object' AND length(processing_reference)>0 AND length(retention_reference)>0\r\nAND ((state='PENDING' AND version=attempts+1 AND attempts<maximum_attempts AND ended_at IS NULL AND result_id IS NULL\r\n  AND protected_code IS NOT NULL AND protected_digest IS NOT NULL AND protected_address IS NOT NULL AND material_purged_at IS NULL)\r\n  OR (state='REJECTED' AND attempts=maximum_attempts AND ended_at>=created_at AND ended_at<expires_at AND result_id IS NULL\r\n    AND protected_code IS NULL AND protected_digest IS NULL)\r\n  OR (state='CONSUMED' AND version>=attempts+2 AND result_id IS NOT NULL AND ended_at>=created_at AND ended_at<expires_at\r\n    AND protected_code IS NULL AND protected_digest IS NULL)\r\n  OR (state='EXPIRED' AND ended_at>=expires_at AND result_id IS NULL AND protected_code IS NULL AND protected_digest IS NULL))\r\nAND (material_purged_at IS NULL OR (material_purged_at>=purge_after AND protected_code IS NULL AND protected_digest IS NULL AND protected_address IS NULL))");
                    table.ForeignKey(
                        name: "FK_recovery_contact_challenges_recovery_cases_case_id",
                        column: x => x.case_id,
                        principalSchema: "identity",
                        principalTable: "recovery_cases",
                        principalColumn: "case_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_recovery_contact_challenges_recovery_entries_entry_id",
                        column: x => x.entry_id,
                        principalSchema: "identity",
                        principalTable: "recovery_entries",
                        principalColumn: "entry_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_recovery_contact_challenges_recovery_results_result_id",
                        column: x => x.result_id,
                        principalSchema: "identity",
                        principalTable: "recovery_results",
                        principalColumn: "result_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_recovery_contact_challenges_verified_contacts_contact_refer~",
                        column: x => x.contact_reference,
                        principalSchema: "accounts",
                        principalTable: "verified_contacts",
                        principalColumn: "contact_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_recovery_results_contact_reference",
                schema: "identity",
                table: "recovery_results",
                column: "contact_reference");

            migrationBuilder.AddCheckConstraint(
                name: "ck_recovery_result_shape",
                schema: "identity",
                table: "recovery_results",
                sql: "(((proof_kind='CODE' AND set_reference IS NOT NULL AND set_lifecycle_version IS NOT NULL AND passkey_credential_reference IS NULL AND passkey_version IS NULL AND passkey_usage_version IS NULL AND passkey_ceremony_id IS NULL AND passkey_method IS NULL AND passkey_assurance IS NULL AND native_before_digest IS NULL AND native_after_digest IS NULL AND (set_lifecycle_version>=1 AND length(context_digest)=64 AND length(policy_version)>0 AND length(processing_reference)>0 AND length(retention_reference)>0\nAND jsonb_typeof(policy_snapshot::jsonb)='object' AND jsonb_typeof(hierarchy_snapshot::jsonb)='object' AND jsonb_typeof(unresolved_effects_json::jsonb)='array'\nAND ((outcome='CONTAINED_PENDING_RESTORATION' AND target_session_id IS NOT NULL AND prior_session_version IS NOT NULL AND prior_session_version>=1\n  AND ended_session_version IS NOT NULL AND ended_session_version=prior_session_version+1 AND session_outbox_event_id IS NOT NULL\n  AND repair_outbox_event_id IS NULL AND prior_password_digest IS NULL AND repaired_password_digest IS NULL\n  AND prior_security_stamp_digest IS NULL AND repaired_security_stamp_digest IS NULL AND session_effect IS NULL AND jsonb_array_length(unresolved_effects_json::jsonb)>0)\n  OR (outcome='PASSWORD_REPAIRED_NO_SESSION' AND target_session_id IS NULL AND prior_session_version IS NULL AND ended_session_version IS NULL\n    AND session_outbox_event_id IS NULL AND repair_outbox_event_id IS NOT NULL AND prior_password_digest IS NOT NULL AND length(prior_password_digest)=64\n    AND repaired_password_digest IS NOT NULL AND length(repaired_password_digest)=64 AND prior_security_stamp_digest IS NOT NULL AND length(prior_security_stamp_digest)=64\n    AND repaired_security_stamp_digest IS NOT NULL AND length(repaired_security_stamp_digest)=64 AND repaired_security_stamp_digest<>prior_security_stamp_digest\n    AND session_effect='InvalidateExistingTunnerSessions')))) OR (proof_kind='PASSKEY' AND set_reference IS NULL AND set_lifecycle_version IS NULL AND passkey_credential_reference IS NOT NULL\r\nAND passkey_version IS NOT NULL AND passkey_version>=1 AND passkey_usage_version IS NOT NULL AND passkey_usage_version>=1\r\nAND passkey_ceremony_id IS NOT NULL AND passkey_method IS NOT NULL AND length(passkey_method)>0\r\nAND passkey_assurance IS NOT NULL AND length(passkey_assurance)>0 AND lower(passkey_assurance)<>'aal3'\r\nAND native_before_digest IS NOT NULL AND length(native_before_digest)=64 AND native_after_digest IS NOT NULL AND length(native_after_digest)=64\r\nAND outcome='PASSWORD_REPAIRED_NO_SESSION' AND target_session_id IS NULL AND prior_session_version IS NULL AND ended_session_version IS NULL\r\nAND session_outbox_event_id IS NULL AND repair_outbox_event_id IS NOT NULL AND prior_password_digest IS NOT NULL AND length(prior_password_digest)=64\r\nAND repaired_password_digest IS NOT NULL AND length(repaired_password_digest)=64 AND prior_security_stamp_digest IS NOT NULL AND length(prior_security_stamp_digest)=64\r\nAND repaired_security_stamp_digest IS NOT NULL AND length(repaired_security_stamp_digest)=64 AND repaired_security_stamp_digest<>prior_security_stamp_digest\r\nAND session_effect IS NOT NULL AND session_effect='InvalidateExistingTunnerSessions' AND length(context_digest)=64 AND length(policy_version)>0\r\nAND length(processing_reference)>0 AND length(retention_reference)>0 AND jsonb_typeof(policy_snapshot::jsonb)='object'\r\nAND jsonb_typeof(hierarchy_snapshot::jsonb)='object' AND jsonb_typeof(unresolved_effects_json::jsonb)='array')) AND contact_reference IS NULL AND contact_version IS NULL AND contact_challenge_id IS NULL AND contact_destination_digest IS NULL AND contact_risk_snapshot IS NULL AND contact_notification_delivery_id IS NULL) OR (proof_kind='CONTACT_RISK' AND outcome='PASSWORD_REPAIRED_NO_SESSION' AND set_reference IS NULL AND set_lifecycle_version IS NULL\r\nAND target_session_id IS NULL AND prior_session_version IS NULL AND ended_session_version IS NULL AND session_outbox_event_id IS NULL\r\nAND passkey_credential_reference IS NULL AND passkey_version IS NULL AND passkey_usage_version IS NULL AND passkey_ceremony_id IS NULL\r\nAND passkey_method IS NULL AND passkey_assurance IS NULL AND native_before_digest IS NULL AND native_after_digest IS NULL\r\nAND contact_reference IS NOT NULL AND contact_version IS NOT NULL AND contact_version>=1 AND contact_challenge_id IS NOT NULL\r\nAND contact_destination_digest IS NOT NULL AND length(contact_destination_digest)=64 AND contact_risk_snapshot IS NOT NULL AND jsonb_typeof(contact_risk_snapshot::jsonb)='object'\r\nAND contact_notification_delivery_id IS NOT NULL AND repair_outbox_event_id IS NOT NULL\r\nAND prior_password_digest IS NOT NULL AND length(prior_password_digest)=64 AND repaired_password_digest IS NOT NULL AND length(repaired_password_digest)=64\r\nAND prior_security_stamp_digest IS NOT NULL AND length(prior_security_stamp_digest)=64 AND repaired_security_stamp_digest IS NOT NULL AND length(repaired_security_stamp_digest)=64\r\nAND repaired_security_stamp_digest<>prior_security_stamp_digest AND session_effect IS NOT NULL AND session_effect='InvalidateExistingTunnerSessions'\r\nAND length(context_digest)=64 AND length(policy_version)>0 AND length(processing_reference)>0 AND length(retention_reference)>0\r\nAND jsonb_typeof(policy_snapshot::jsonb)='object' AND jsonb_typeof(hierarchy_snapshot::jsonb)='object' AND jsonb_typeof(unresolved_effects_json::jsonb)='array')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_recovery_entry_contact",
                schema: "identity",
                table: "recovery_entries",
                sql: "(contact_challenge_id IS NULL AND contact_profile_version IS NULL AND contact_expires_at IS NULL)\r\nOR (contact_challenge_id IS NOT NULL AND contact_profile_version IS NOT NULL AND length(contact_profile_version)>0\r\n  AND contact_expires_at IS NOT NULL AND contact_expires_at>created_at AND contact_expires_at<=expires_at)");

            migrationBuilder.CreateIndex(
                name: "IX_recovery_cases_contact_reference",
                schema: "identity",
                table: "recovery_cases",
                column: "contact_reference");

            migrationBuilder.AddCheckConstraint(
                name: "ck_recovery_case_state",
                schema: "identity",
                table: "recovery_cases",
                sql: "expires_at>created_at AND recovery_expires_at>expires_at AND (((state='PENDING_CODE' AND proof_kind='CODE')\r\n  OR (state='PENDING_PASSKEY' AND proof_kind='PASSKEY') OR (state='PENDING_CONTACT_RISK' AND proof_kind='CONTACT_RISK'))\r\n  AND version=1 AND result_id IS NULL AND ended_at IS NULL OR (version=2 AND result_id IS NOT NULL AND ended_at>=created_at AND ended_at<expires_at\r\n    AND ((state='CONTAINED' AND goal='ContainSelectedTunnerSession' AND proof_kind='CODE') OR (state='REPAIRED' AND goal='RepairPassword'))))");

            migrationBuilder.AddCheckConstraint(
                name: "ck_recovery_case_versions",
                schema: "identity",
                table: "recovery_cases",
                sql: "((expected_account_version>=1 AND ((proof_kind='CODE' AND set_reference IS NOT NULL AND expected_set_lifecycle_version IS NOT NULL\r\n  AND expected_set_lifecycle_version>=1 AND usage_admission IS NOT NULL AND passkey_credential_reference IS NULL AND expected_passkey_version IS NULL\r\n  AND expected_passkey_usage_version IS NULL AND passkey_ceremony_id IS NULL AND ((usage_admission='ExactUsageSnapshot'\r\n    AND expected_usage_version IS NOT NULL AND expected_usage_version>=0) OR (usage_admission='IndependentUnusedEntry' AND expected_usage_version IS NULL)))\r\n  OR (proof_kind='PASSKEY' AND goal='RepairPassword' AND set_reference IS NULL AND expected_set_lifecycle_version IS NULL\r\n    AND expected_usage_version IS NULL AND usage_admission IS NULL AND passkey_credential_reference IS NOT NULL\r\n    AND expected_passkey_version IS NOT NULL AND expected_passkey_version>=1 AND expected_passkey_usage_version IS NOT NULL\r\n    AND expected_passkey_usage_version>=0 AND passkey_ceremony_id IS NOT NULL))) AND contact_reference IS NULL AND expected_contact_version IS NULL AND contact_destination_digest IS NULL AND contact_challenge_id IS NULL AND initial_contact_risk_snapshot IS NULL) OR (proof_kind='CONTACT_RISK' AND goal='RepairPassword' AND expected_account_version>=1 AND set_reference IS NULL\r\nAND expected_set_lifecycle_version IS NULL AND expected_usage_version IS NULL AND usage_admission IS NULL\r\nAND passkey_credential_reference IS NULL AND expected_passkey_version IS NULL AND expected_passkey_usage_version IS NULL AND passkey_ceremony_id IS NULL\r\nAND contact_reference IS NOT NULL AND expected_contact_version IS NOT NULL AND expected_contact_version>=1\r\nAND contact_destination_digest IS NOT NULL AND length(contact_destination_digest)=64 AND contact_challenge_id IS NOT NULL\r\nAND initial_contact_risk_snapshot IS NOT NULL AND jsonb_typeof(initial_contact_risk_snapshot::jsonb)='object')");

            migrationBuilder.CreateIndex(
                name: "IX_recovery_contact_challenges_case_id",
                schema: "identity",
                table: "recovery_contact_challenges",
                column: "case_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_recovery_contact_challenges_contact_reference",
                schema: "identity",
                table: "recovery_contact_challenges",
                column: "contact_reference");

            migrationBuilder.CreateIndex(
                name: "IX_recovery_contact_challenges_delivery_id",
                schema: "identity",
                table: "recovery_contact_challenges",
                column: "delivery_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_recovery_contact_challenges_entry_id",
                schema: "identity",
                table: "recovery_contact_challenges",
                column: "entry_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_recovery_contact_challenges_purge_after",
                schema: "identity",
                table: "recovery_contact_challenges",
                column: "purge_after");

            migrationBuilder.CreateIndex(
                name: "IX_recovery_contact_challenges_result_id",
                schema: "identity",
                table: "recovery_contact_challenges",
                column: "result_id");

            migrationBuilder.AddForeignKey(
                name: "fk_recovery_case_contact",
                schema: "identity",
                table: "recovery_cases",
                column: "contact_reference",
                principalSchema: "accounts",
                principalTable: "verified_contacts",
                principalColumn: "contact_id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_recovery_result_contact",
                schema: "identity",
                table: "recovery_results",
                column: "contact_reference",
                principalSchema: "accounts",
                principalTable: "verified_contacts",
                principalColumn: "contact_id",
                onDelete: ReferentialAction.Restrict);
            migrationBuilder.Sql(RecoveryContactMigrationSql.Harden);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(RecoveryContactMigrationSql.BlockPopulatedDowngrade);
            migrationBuilder.Sql(RecoveryContactMigrationSql.RemoveGuards);
            migrationBuilder.DropForeignKey(
                name: "fk_recovery_case_contact",
                schema: "identity",
                table: "recovery_cases");

            migrationBuilder.DropForeignKey(
                name: "fk_recovery_result_contact",
                schema: "identity",
                table: "recovery_results");

            migrationBuilder.DropTable(
                name: "recovery_contact_challenges",
                schema: "identity");

            migrationBuilder.DropIndex(
                name: "IX_recovery_results_contact_reference",
                schema: "identity",
                table: "recovery_results");

            migrationBuilder.DropCheckConstraint(
                name: "ck_recovery_result_shape",
                schema: "identity",
                table: "recovery_results");

            migrationBuilder.DropCheckConstraint(
                name: "ck_recovery_entry_contact",
                schema: "identity",
                table: "recovery_entries");

            migrationBuilder.DropIndex(
                name: "IX_recovery_cases_contact_reference",
                schema: "identity",
                table: "recovery_cases");

            migrationBuilder.DropCheckConstraint(
                name: "ck_recovery_case_state",
                schema: "identity",
                table: "recovery_cases");

            migrationBuilder.DropCheckConstraint(
                name: "ck_recovery_case_versions",
                schema: "identity",
                table: "recovery_cases");

            migrationBuilder.DropColumn(
                name: "contact_challenge_id",
                schema: "identity",
                table: "recovery_results");

            migrationBuilder.DropColumn(
                name: "contact_destination_digest",
                schema: "identity",
                table: "recovery_results");

            migrationBuilder.DropColumn(
                name: "contact_notification_delivery_id",
                schema: "identity",
                table: "recovery_results");

            migrationBuilder.DropColumn(
                name: "contact_reference",
                schema: "identity",
                table: "recovery_results");

            migrationBuilder.DropColumn(
                name: "contact_risk_snapshot",
                schema: "identity",
                table: "recovery_results");

            migrationBuilder.DropColumn(
                name: "contact_version",
                schema: "identity",
                table: "recovery_results");

            migrationBuilder.DropColumn(
                name: "contact_challenge_id",
                schema: "identity",
                table: "recovery_entries");

            migrationBuilder.DropColumn(
                name: "contact_expires_at",
                schema: "identity",
                table: "recovery_entries");

            migrationBuilder.DropColumn(
                name: "contact_profile_version",
                schema: "identity",
                table: "recovery_entries");

            migrationBuilder.DropColumn(
                name: "contact_challenge_id",
                schema: "identity",
                table: "recovery_cases");

            migrationBuilder.DropColumn(
                name: "contact_destination_digest",
                schema: "identity",
                table: "recovery_cases");

            migrationBuilder.DropColumn(
                name: "contact_reference",
                schema: "identity",
                table: "recovery_cases");

            migrationBuilder.DropColumn(
                name: "expected_contact_version",
                schema: "identity",
                table: "recovery_cases");

            migrationBuilder.DropColumn(
                name: "initial_contact_risk_snapshot",
                schema: "identity",
                table: "recovery_cases");

            migrationBuilder.AddCheckConstraint(
                name: "ck_recovery_result_shape",
                schema: "identity",
                table: "recovery_results",
                sql: "(proof_kind='CODE' AND set_reference IS NOT NULL AND set_lifecycle_version IS NOT NULL AND passkey_credential_reference IS NULL AND passkey_version IS NULL AND passkey_usage_version IS NULL AND passkey_ceremony_id IS NULL AND passkey_method IS NULL AND passkey_assurance IS NULL AND native_before_digest IS NULL AND native_after_digest IS NULL AND (set_lifecycle_version>=1 AND length(context_digest)=64 AND length(policy_version)>0 AND length(processing_reference)>0 AND length(retention_reference)>0\nAND jsonb_typeof(policy_snapshot::jsonb)='object' AND jsonb_typeof(hierarchy_snapshot::jsonb)='object' AND jsonb_typeof(unresolved_effects_json::jsonb)='array'\nAND ((outcome='CONTAINED_PENDING_RESTORATION' AND target_session_id IS NOT NULL AND prior_session_version IS NOT NULL AND prior_session_version>=1\n  AND ended_session_version IS NOT NULL AND ended_session_version=prior_session_version+1 AND session_outbox_event_id IS NOT NULL\n  AND repair_outbox_event_id IS NULL AND prior_password_digest IS NULL AND repaired_password_digest IS NULL\n  AND prior_security_stamp_digest IS NULL AND repaired_security_stamp_digest IS NULL AND session_effect IS NULL AND jsonb_array_length(unresolved_effects_json::jsonb)>0)\n  OR (outcome='PASSWORD_REPAIRED_NO_SESSION' AND target_session_id IS NULL AND prior_session_version IS NULL AND ended_session_version IS NULL\n    AND session_outbox_event_id IS NULL AND repair_outbox_event_id IS NOT NULL AND prior_password_digest IS NOT NULL AND length(prior_password_digest)=64\n    AND repaired_password_digest IS NOT NULL AND length(repaired_password_digest)=64 AND prior_security_stamp_digest IS NOT NULL AND length(prior_security_stamp_digest)=64\n    AND repaired_security_stamp_digest IS NOT NULL AND length(repaired_security_stamp_digest)=64 AND repaired_security_stamp_digest<>prior_security_stamp_digest\n    AND session_effect='InvalidateExistingTunnerSessions')))) OR (proof_kind='PASSKEY' AND set_reference IS NULL AND set_lifecycle_version IS NULL AND passkey_credential_reference IS NOT NULL\r\nAND passkey_version IS NOT NULL AND passkey_version>=1 AND passkey_usage_version IS NOT NULL AND passkey_usage_version>=1\r\nAND passkey_ceremony_id IS NOT NULL AND passkey_method IS NOT NULL AND length(passkey_method)>0\r\nAND passkey_assurance IS NOT NULL AND length(passkey_assurance)>0 AND lower(passkey_assurance)<>'aal3'\r\nAND native_before_digest IS NOT NULL AND length(native_before_digest)=64 AND native_after_digest IS NOT NULL AND length(native_after_digest)=64\r\nAND outcome='PASSWORD_REPAIRED_NO_SESSION' AND target_session_id IS NULL AND prior_session_version IS NULL AND ended_session_version IS NULL\r\nAND session_outbox_event_id IS NULL AND repair_outbox_event_id IS NOT NULL AND prior_password_digest IS NOT NULL AND length(prior_password_digest)=64\r\nAND repaired_password_digest IS NOT NULL AND length(repaired_password_digest)=64 AND prior_security_stamp_digest IS NOT NULL AND length(prior_security_stamp_digest)=64\r\nAND repaired_security_stamp_digest IS NOT NULL AND length(repaired_security_stamp_digest)=64 AND repaired_security_stamp_digest<>prior_security_stamp_digest\r\nAND session_effect IS NOT NULL AND session_effect='InvalidateExistingTunnerSessions' AND length(context_digest)=64 AND length(policy_version)>0\r\nAND length(processing_reference)>0 AND length(retention_reference)>0 AND jsonb_typeof(policy_snapshot::jsonb)='object'\r\nAND jsonb_typeof(hierarchy_snapshot::jsonb)='object' AND jsonb_typeof(unresolved_effects_json::jsonb)='array')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_recovery_case_state",
                schema: "identity",
                table: "recovery_cases",
                sql: "expires_at>created_at AND recovery_expires_at>expires_at AND (((state='PENDING_CODE' AND proof_kind='CODE')\r\n  OR (state='PENDING_PASSKEY' AND proof_kind='PASSKEY')) AND version=1 AND result_id IS NULL AND ended_at IS NULL\r\n  OR (version=2 AND result_id IS NOT NULL AND ended_at>=created_at AND ended_at<expires_at\r\n    AND ((state='CONTAINED' AND goal='ContainSelectedTunnerSession' AND proof_kind='CODE') OR (state='REPAIRED' AND goal='RepairPassword'))))");

            migrationBuilder.AddCheckConstraint(
                name: "ck_recovery_case_versions",
                schema: "identity",
                table: "recovery_cases",
                sql: "expected_account_version>=1 AND ((proof_kind='CODE' AND set_reference IS NOT NULL AND expected_set_lifecycle_version IS NOT NULL\r\n  AND expected_set_lifecycle_version>=1 AND usage_admission IS NOT NULL AND passkey_credential_reference IS NULL AND expected_passkey_version IS NULL\r\n  AND expected_passkey_usage_version IS NULL AND passkey_ceremony_id IS NULL AND ((usage_admission='ExactUsageSnapshot'\r\n    AND expected_usage_version IS NOT NULL AND expected_usage_version>=0) OR (usage_admission='IndependentUnusedEntry' AND expected_usage_version IS NULL)))\r\n  OR (proof_kind='PASSKEY' AND goal='RepairPassword' AND set_reference IS NULL AND expected_set_lifecycle_version IS NULL\r\n    AND expected_usage_version IS NULL AND usage_admission IS NULL AND passkey_credential_reference IS NOT NULL\r\n    AND expected_passkey_version IS NOT NULL AND expected_passkey_version>=1 AND expected_passkey_usage_version IS NOT NULL\r\n    AND expected_passkey_usage_version>=0 AND passkey_ceremony_id IS NOT NULL))");
        }
    }
}
