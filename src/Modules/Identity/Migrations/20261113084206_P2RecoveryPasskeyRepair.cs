using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tunner.Identity.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class P2RecoveryPasskeyRepair : Migration
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

            migrationBuilder.DropCheckConstraint(
                name: "ck_passkey_ceremony_state",
                schema: "identity",
                table: "passkey_ceremonies");

            migrationBuilder.AlterColumn<Guid>(
                name: "set_reference",
                schema: "identity",
                table: "recovery_results",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<long>(
                name: "set_lifecycle_version",
                schema: "identity",
                table: "recovery_results",
                type: "bigint",
                nullable: true,
                oldClrType: typeof(long),
                oldType: "bigint");

            migrationBuilder.AddColumn<string>(
                name: "native_after_digest",
                schema: "identity",
                table: "recovery_results",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "native_before_digest",
                schema: "identity",
                table: "recovery_results",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "passkey_assurance",
                schema: "identity",
                table: "recovery_results",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "passkey_ceremony_id",
                schema: "identity",
                table: "recovery_results",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "passkey_credential_reference",
                schema: "identity",
                table: "recovery_results",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "passkey_method",
                schema: "identity",
                table: "recovery_results",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "passkey_usage_version",
                schema: "identity",
                table: "recovery_results",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "passkey_version",
                schema: "identity",
                table: "recovery_results",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "proof_kind",
                schema: "identity",
                table: "recovery_results",
                type: "text",
                nullable: false,
                defaultValue: "CODE");

            migrationBuilder.AddColumn<Guid>(
                name: "passkey_ceremony_id",
                schema: "identity",
                table: "recovery_entries",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "passkey_expires_at",
                schema: "identity",
                table: "recovery_entries",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "passkey_options_json",
                schema: "identity",
                table: "recovery_entries",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "passkey_policy_version",
                schema: "identity",
                table: "recovery_entries",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "passkey_profile_version",
                schema: "identity",
                table: "recovery_entries",
                type: "text",
                nullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "usage_admission",
                schema: "identity",
                table: "recovery_cases",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<Guid>(
                name: "set_reference",
                schema: "identity",
                table: "recovery_cases",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<long>(
                name: "expected_set_lifecycle_version",
                schema: "identity",
                table: "recovery_cases",
                type: "bigint",
                nullable: true,
                oldClrType: typeof(long),
                oldType: "bigint");

            migrationBuilder.AddColumn<long>(
                name: "expected_passkey_usage_version",
                schema: "identity",
                table: "recovery_cases",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "expected_passkey_version",
                schema: "identity",
                table: "recovery_cases",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "passkey_ceremony_id",
                schema: "identity",
                table: "recovery_cases",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "passkey_credential_reference",
                schema: "identity",
                table: "recovery_cases",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "proof_kind",
                schema: "identity",
                table: "recovery_cases",
                type: "text",
                nullable: false,
                defaultValue: "CODE");

            migrationBuilder.CreateIndex(
                name: "IX_recovery_results_passkey_credential_reference",
                schema: "identity",
                table: "recovery_results",
                column: "passkey_credential_reference");

            migrationBuilder.AddCheckConstraint(
                name: "ck_recovery_result_shape",
                schema: "identity",
                table: "recovery_results",
                sql: "(proof_kind='CODE' AND set_reference IS NOT NULL AND set_lifecycle_version IS NOT NULL AND passkey_credential_reference IS NULL AND passkey_version IS NULL AND passkey_usage_version IS NULL AND passkey_ceremony_id IS NULL AND passkey_method IS NULL AND passkey_assurance IS NULL AND native_before_digest IS NULL AND native_after_digest IS NULL AND (set_lifecycle_version>=1 AND length(context_digest)=64 AND length(policy_version)>0 AND length(processing_reference)>0 AND length(retention_reference)>0\nAND jsonb_typeof(policy_snapshot::jsonb)='object' AND jsonb_typeof(hierarchy_snapshot::jsonb)='object' AND jsonb_typeof(unresolved_effects_json::jsonb)='array'\nAND ((outcome='CONTAINED_PENDING_RESTORATION' AND target_session_id IS NOT NULL AND prior_session_version IS NOT NULL AND prior_session_version>=1\n  AND ended_session_version IS NOT NULL AND ended_session_version=prior_session_version+1 AND session_outbox_event_id IS NOT NULL\n  AND repair_outbox_event_id IS NULL AND prior_password_digest IS NULL AND repaired_password_digest IS NULL\n  AND prior_security_stamp_digest IS NULL AND repaired_security_stamp_digest IS NULL AND session_effect IS NULL AND jsonb_array_length(unresolved_effects_json::jsonb)>0)\n  OR (outcome='PASSWORD_REPAIRED_NO_SESSION' AND target_session_id IS NULL AND prior_session_version IS NULL AND ended_session_version IS NULL\n    AND session_outbox_event_id IS NULL AND repair_outbox_event_id IS NOT NULL AND prior_password_digest IS NOT NULL AND length(prior_password_digest)=64\n    AND repaired_password_digest IS NOT NULL AND length(repaired_password_digest)=64 AND prior_security_stamp_digest IS NOT NULL AND length(prior_security_stamp_digest)=64\n    AND repaired_security_stamp_digest IS NOT NULL AND length(repaired_security_stamp_digest)=64 AND repaired_security_stamp_digest<>prior_security_stamp_digest\n    AND session_effect='InvalidateExistingTunnerSessions')))) OR (proof_kind='PASSKEY' AND set_reference IS NULL AND set_lifecycle_version IS NULL AND passkey_credential_reference IS NOT NULL\r\nAND passkey_version IS NOT NULL AND passkey_version>=1 AND passkey_usage_version IS NOT NULL AND passkey_usage_version>=1\r\nAND passkey_ceremony_id IS NOT NULL AND passkey_method IS NOT NULL AND length(passkey_method)>0\r\nAND passkey_assurance IS NOT NULL AND length(passkey_assurance)>0 AND lower(passkey_assurance)<>'aal3'\r\nAND native_before_digest IS NOT NULL AND length(native_before_digest)=64 AND native_after_digest IS NOT NULL AND length(native_after_digest)=64\r\nAND outcome='PASSWORD_REPAIRED_NO_SESSION' AND target_session_id IS NULL AND prior_session_version IS NULL AND ended_session_version IS NULL\r\nAND session_outbox_event_id IS NULL AND repair_outbox_event_id IS NOT NULL AND prior_password_digest IS NOT NULL AND length(prior_password_digest)=64\r\nAND repaired_password_digest IS NOT NULL AND length(repaired_password_digest)=64 AND prior_security_stamp_digest IS NOT NULL AND length(prior_security_stamp_digest)=64\r\nAND repaired_security_stamp_digest IS NOT NULL AND length(repaired_security_stamp_digest)=64 AND repaired_security_stamp_digest<>prior_security_stamp_digest\r\nAND session_effect IS NOT NULL AND session_effect='InvalidateExistingTunnerSessions' AND length(context_digest)=64 AND length(policy_version)>0\r\nAND length(processing_reference)>0 AND length(retention_reference)>0 AND jsonb_typeof(policy_snapshot::jsonb)='object'\r\nAND jsonb_typeof(hierarchy_snapshot::jsonb)='object' AND jsonb_typeof(unresolved_effects_json::jsonb)='array')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_recovery_entry_passkey",
                schema: "identity",
                table: "recovery_entries",
                sql: "(passkey_ceremony_id IS NULL AND passkey_options_json IS NULL AND passkey_profile_version IS NULL AND passkey_policy_version IS NULL AND passkey_expires_at IS NULL)\r\nOR (passkey_ceremony_id IS NOT NULL AND passkey_options_json IS NOT NULL AND jsonb_typeof(passkey_options_json::jsonb)='object'\r\n  AND passkey_profile_version IS NOT NULL AND length(passkey_profile_version)>0 AND passkey_policy_version IS NOT NULL AND length(passkey_policy_version)>0\r\n  AND passkey_expires_at IS NOT NULL AND passkey_expires_at>created_at AND passkey_expires_at<=expires_at)");

            migrationBuilder.CreateIndex(
                name: "IX_recovery_cases_passkey_credential_reference",
                schema: "identity",
                table: "recovery_cases",
                column: "passkey_credential_reference");

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

            migrationBuilder.AddCheckConstraint(
                name: "ck_passkey_ceremony_state",
                schema: "identity",
                table: "passkey_ceremonies",
                sql: "state IN ('PENDING','AUTHORIZED','COMPLETED','REJECTED','EXPIRED') AND kind IN ('ENROLL','SIGN_IN','REVOKE','STEP_UP','RECOVERY')");

            migrationBuilder.AddForeignKey(
                name: "fk_recovery_case_passkey",
                schema: "identity",
                table: "recovery_cases",
                column: "passkey_credential_reference",
                principalSchema: "identity",
                principalTable: "passkey_credentials",
                principalColumn: "credential_reference",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_recovery_result_passkey",
                schema: "identity",
                table: "recovery_results",
                column: "passkey_credential_reference",
                principalSchema: "identity",
                principalTable: "passkey_credentials",
                principalColumn: "credential_reference",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.Sql(RecoveryPasskeyMigrationSql.Harden);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(RecoveryPasskeyMigrationSql.BlockPopulatedDowngrade);
            migrationBuilder.Sql(RecoveryPasskeyMigrationSql.RemoveGuards);

            migrationBuilder.DropForeignKey(
                name: "fk_recovery_case_passkey",
                schema: "identity",
                table: "recovery_cases");

            migrationBuilder.DropForeignKey(
                name: "fk_recovery_result_passkey",
                schema: "identity",
                table: "recovery_results");

            migrationBuilder.DropIndex(
                name: "IX_recovery_results_passkey_credential_reference",
                schema: "identity",
                table: "recovery_results");

            migrationBuilder.DropCheckConstraint(
                name: "ck_recovery_result_shape",
                schema: "identity",
                table: "recovery_results");

            migrationBuilder.DropCheckConstraint(
                name: "ck_recovery_entry_passkey",
                schema: "identity",
                table: "recovery_entries");

            migrationBuilder.DropIndex(
                name: "IX_recovery_cases_passkey_credential_reference",
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

            migrationBuilder.DropCheckConstraint(
                name: "ck_passkey_ceremony_state",
                schema: "identity",
                table: "passkey_ceremonies");

            migrationBuilder.DropColumn(
                name: "native_after_digest",
                schema: "identity",
                table: "recovery_results");

            migrationBuilder.DropColumn(
                name: "native_before_digest",
                schema: "identity",
                table: "recovery_results");

            migrationBuilder.DropColumn(
                name: "passkey_assurance",
                schema: "identity",
                table: "recovery_results");

            migrationBuilder.DropColumn(
                name: "passkey_ceremony_id",
                schema: "identity",
                table: "recovery_results");

            migrationBuilder.DropColumn(
                name: "passkey_credential_reference",
                schema: "identity",
                table: "recovery_results");

            migrationBuilder.DropColumn(
                name: "passkey_method",
                schema: "identity",
                table: "recovery_results");

            migrationBuilder.DropColumn(
                name: "passkey_usage_version",
                schema: "identity",
                table: "recovery_results");

            migrationBuilder.DropColumn(
                name: "passkey_version",
                schema: "identity",
                table: "recovery_results");

            migrationBuilder.DropColumn(
                name: "proof_kind",
                schema: "identity",
                table: "recovery_results");

            migrationBuilder.DropColumn(
                name: "passkey_ceremony_id",
                schema: "identity",
                table: "recovery_entries");

            migrationBuilder.DropColumn(
                name: "passkey_expires_at",
                schema: "identity",
                table: "recovery_entries");

            migrationBuilder.DropColumn(
                name: "passkey_options_json",
                schema: "identity",
                table: "recovery_entries");

            migrationBuilder.DropColumn(
                name: "passkey_policy_version",
                schema: "identity",
                table: "recovery_entries");

            migrationBuilder.DropColumn(
                name: "passkey_profile_version",
                schema: "identity",
                table: "recovery_entries");

            migrationBuilder.DropColumn(
                name: "expected_passkey_usage_version",
                schema: "identity",
                table: "recovery_cases");

            migrationBuilder.DropColumn(
                name: "expected_passkey_version",
                schema: "identity",
                table: "recovery_cases");

            migrationBuilder.DropColumn(
                name: "passkey_ceremony_id",
                schema: "identity",
                table: "recovery_cases");

            migrationBuilder.DropColumn(
                name: "passkey_credential_reference",
                schema: "identity",
                table: "recovery_cases");

            migrationBuilder.DropColumn(
                name: "proof_kind",
                schema: "identity",
                table: "recovery_cases");

            migrationBuilder.AlterColumn<Guid>(
                name: "set_reference",
                schema: "identity",
                table: "recovery_results",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<long>(
                name: "set_lifecycle_version",
                schema: "identity",
                table: "recovery_results",
                type: "bigint",
                nullable: false,
                defaultValue: 0L,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "usage_admission",
                schema: "identity",
                table: "recovery_cases",
                type: "text",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "set_reference",
                schema: "identity",
                table: "recovery_cases",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<long>(
                name: "expected_set_lifecycle_version",
                schema: "identity",
                table: "recovery_cases",
                type: "bigint",
                nullable: false,
                defaultValue: 0L,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldNullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_recovery_result_shape",
                schema: "identity",
                table: "recovery_results",
                sql: "set_lifecycle_version>=1 AND length(context_digest)=64 AND length(policy_version)>0 AND length(processing_reference)>0 AND length(retention_reference)>0\nAND jsonb_typeof(policy_snapshot::jsonb)='object' AND jsonb_typeof(hierarchy_snapshot::jsonb)='object' AND jsonb_typeof(unresolved_effects_json::jsonb)='array'\nAND ((outcome='CONTAINED_PENDING_RESTORATION' AND target_session_id IS NOT NULL AND prior_session_version IS NOT NULL AND prior_session_version>=1\n  AND ended_session_version IS NOT NULL AND ended_session_version=prior_session_version+1 AND session_outbox_event_id IS NOT NULL\n  AND repair_outbox_event_id IS NULL AND prior_password_digest IS NULL AND repaired_password_digest IS NULL\n  AND prior_security_stamp_digest IS NULL AND repaired_security_stamp_digest IS NULL AND session_effect IS NULL AND jsonb_array_length(unresolved_effects_json::jsonb)>0)\n  OR (outcome='PASSWORD_REPAIRED_NO_SESSION' AND target_session_id IS NULL AND prior_session_version IS NULL AND ended_session_version IS NULL\n    AND session_outbox_event_id IS NULL AND repair_outbox_event_id IS NOT NULL AND prior_password_digest IS NOT NULL AND length(prior_password_digest)=64\n    AND repaired_password_digest IS NOT NULL AND length(repaired_password_digest)=64 AND prior_security_stamp_digest IS NOT NULL AND length(prior_security_stamp_digest)=64\n    AND repaired_security_stamp_digest IS NOT NULL AND length(repaired_security_stamp_digest)=64 AND repaired_security_stamp_digest<>prior_security_stamp_digest\n    AND session_effect='InvalidateExistingTunnerSessions'))");

            migrationBuilder.AddCheckConstraint(
                name: "ck_recovery_case_state",
                schema: "identity",
                table: "recovery_cases",
                sql: "expires_at>created_at AND recovery_expires_at>expires_at AND ((state='PENDING_CODE' AND version=1 AND result_id IS NULL AND ended_at IS NULL)\n  OR (version=2 AND result_id IS NOT NULL AND ended_at>=created_at AND ended_at<expires_at\n    AND ((state='CONTAINED' AND goal='ContainSelectedTunnerSession') OR (state='REPAIRED' AND goal='RepairPassword'))))");

            migrationBuilder.AddCheckConstraint(
                name: "ck_recovery_case_versions",
                schema: "identity",
                table: "recovery_cases",
                sql: "expected_account_version>=1 AND expected_set_lifecycle_version>=1 AND ((usage_admission='ExactUsageSnapshot'\n  AND expected_usage_version IS NOT NULL AND expected_usage_version>=0) OR (usage_admission='IndependentUnusedEntry' AND expected_usage_version IS NULL))");

            migrationBuilder.AddCheckConstraint(
                name: "ck_passkey_ceremony_state",
                schema: "identity",
                table: "passkey_ceremonies",
                sql: "state IN ('PENDING','AUTHORIZED','COMPLETED','REJECTED','EXPIRED') AND kind IN ('ENROLL','SIGN_IN','REVOKE','STEP_UP')");
        }
    }
}
