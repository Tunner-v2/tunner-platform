using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tunner.Identity.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class P2RecoveryPasswordRepair : Migration
    {
        private static readonly string[] OriginalBrowserRequestColumns = ["browser_digest", "request_id"];
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_recovery_result_shape",
                schema: "identity",
                table: "recovery_results");

            migrationBuilder.DropCheckConstraint(
                name: "ck_recovery_case_binding",
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

            migrationBuilder.AlterColumn<Guid>(
                name: "target_session_id",
                schema: "identity",
                table: "recovery_results",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<Guid>(
                name: "session_outbox_event_id",
                schema: "identity",
                table: "recovery_results",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<long>(
                name: "prior_session_version",
                schema: "identity",
                table: "recovery_results",
                type: "bigint",
                nullable: true,
                oldClrType: typeof(long),
                oldType: "bigint");

            migrationBuilder.AlterColumn<long>(
                name: "ended_session_version",
                schema: "identity",
                table: "recovery_results",
                type: "bigint",
                nullable: true,
                oldClrType: typeof(long),
                oldType: "bigint");

            migrationBuilder.AddColumn<string>(
                name: "prior_password_digest",
                schema: "identity",
                table: "recovery_results",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "prior_security_stamp_digest",
                schema: "identity",
                table: "recovery_results",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "repair_outbox_event_id",
                schema: "identity",
                table: "recovery_results",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "repaired_password_digest",
                schema: "identity",
                table: "recovery_results",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "repaired_security_stamp_digest",
                schema: "identity",
                table: "recovery_results",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "session_effect",
                schema: "identity",
                table: "recovery_results",
                type: "text",
                nullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "target_session_id",
                schema: "identity",
                table: "recovery_cases",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<long>(
                name: "expected_session_version",
                schema: "identity",
                table: "recovery_cases",
                type: "bigint",
                nullable: true,
                oldClrType: typeof(long),
                oldType: "bigint");

            migrationBuilder.AddColumn<Guid>(
                name: "entry_id",
                schema: "identity",
                table: "recovery_cases",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "prior_password_digest",
                schema: "identity",
                table: "recovery_cases",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "prior_security_stamp_digest",
                schema: "identity",
                table: "recovery_cases",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "user_source_digest",
                schema: "identity",
                table: "recovery_cases",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "recovery_entries",
                schema: "identity",
                columns: table => new
                {
                    entry_id = table.Column<Guid>(type: "uuid", nullable: false),
                    request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    browser_digest = table.Column<string>(type: "text", nullable: false),
                    previous_cookie_digest = table.Column<string>(type: "text", nullable: false),
                    origin = table.Column<string>(type: "text", nullable: false),
                    network_digest = table.Column<string>(type: "text", nullable: false),
                    identifier_digest = table.Column<string>(type: "text", nullable: false),
                    goal_reference = table.Column<string>(type: "text", nullable: false),
                    policy_version = table.Column<string>(type: "text", nullable: false),
                    policy_snapshot = table.Column<string>(type: "text", nullable: false),
                    processing_reference = table.Column<string>(type: "text", nullable: false),
                    retention_reference = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    recovery_expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    state = table.Column<string>(type: "text", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    ended_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_recovery_entries", x => x.entry_id);
                    table.CheckConstraint("ck_recovery_entry_binding", "length(browser_digest)=64 AND length(previous_cookie_digest)=64 AND length(network_digest)=64 AND length(identifier_digest)=64 AND length(goal_reference)>0 AND length(policy_version)>0 AND length(processing_reference)>0 AND length(retention_reference)>0 AND jsonb_typeof(policy_snapshot::jsonb)='object'");
                    table.CheckConstraint("ck_recovery_entry_state", "expires_at>created_at AND recovery_expires_at>expires_at AND ((state='OPEN' AND version=1 AND ended_at IS NULL) OR (state IN ('CANCELLED','REPAIRED') AND version=2 AND ended_at>=created_at AND ended_at<expires_at))");
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_recovery_result_shape",
                schema: "identity",
                table: "recovery_results",
                sql: "set_lifecycle_version>=1 AND length(context_digest)=64 AND length(policy_version)>0 AND length(processing_reference)>0 AND length(retention_reference)>0\nAND jsonb_typeof(policy_snapshot::jsonb)='object' AND jsonb_typeof(hierarchy_snapshot::jsonb)='object' AND jsonb_typeof(unresolved_effects_json::jsonb)='array'\nAND ((outcome='CONTAINED_PENDING_RESTORATION' AND target_session_id IS NOT NULL AND prior_session_version IS NOT NULL AND prior_session_version>=1\n  AND ended_session_version IS NOT NULL AND ended_session_version=prior_session_version+1 AND session_outbox_event_id IS NOT NULL\n  AND repair_outbox_event_id IS NULL AND prior_password_digest IS NULL AND repaired_password_digest IS NULL\n  AND prior_security_stamp_digest IS NULL AND repaired_security_stamp_digest IS NULL AND session_effect IS NULL AND jsonb_array_length(unresolved_effects_json::jsonb)>0)\n  OR (outcome='PASSWORD_REPAIRED_NO_SESSION' AND target_session_id IS NULL AND prior_session_version IS NULL AND ended_session_version IS NULL\n    AND session_outbox_event_id IS NULL AND repair_outbox_event_id IS NOT NULL AND prior_password_digest IS NOT NULL AND length(prior_password_digest)=64\n    AND repaired_password_digest IS NOT NULL AND length(repaired_password_digest)=64 AND prior_security_stamp_digest IS NOT NULL AND length(prior_security_stamp_digest)=64\n    AND repaired_security_stamp_digest IS NOT NULL AND length(repaired_security_stamp_digest)=64 AND repaired_security_stamp_digest<>prior_security_stamp_digest\n    AND session_effect='InvalidateExistingTunnerSessions'))");

            migrationBuilder.CreateIndex(
                name: "IX_recovery_cases_entry_id",
                schema: "identity",
                table: "recovery_cases",
                column: "entry_id",
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_recovery_case_binding",
                schema: "identity",
                table: "recovery_cases",
                sql: "length(original_browser_digest)=64 AND length(network_digest)=64 AND length(context_digest)=64 AND length(facts_digest)=64\nAND length(policy_version)>0 AND length(processing_reference)>0 AND length(retention_reference)>0\nAND jsonb_typeof(policy_snapshot::jsonb)='object' AND jsonb_typeof(hierarchy_snapshot::jsonb)='object'\nAND jsonb_typeof(unresolved_effects_json::jsonb)='array'\nAND ((goal='ContainSelectedTunnerSession' AND session_effect='RevokeExactSelectedSession'\n  AND target_session_id IS NOT NULL AND expected_session_version IS NOT NULL AND expected_session_version>=1\n  AND entry_id IS NULL AND prior_password_digest IS NULL AND prior_security_stamp_digest IS NULL AND user_source_digest IS NULL\n  AND jsonb_array_length(unresolved_effects_json::jsonb)>0)\n  OR (goal='RepairPassword' AND session_effect='InvalidateExistingTunnerSessions' AND target_session_id IS NULL AND expected_session_version IS NULL\n    AND entry_id IS NOT NULL AND prior_password_digest IS NOT NULL AND length(prior_password_digest)=64\n    AND prior_security_stamp_digest IS NOT NULL AND length(prior_security_stamp_digest)=64\n    AND user_source_digest IS NOT NULL AND length(user_source_digest)=64))");

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

            migrationBuilder.CreateIndex(
                name: "IX_recovery_entries_browser_digest_request_id",
                schema: "identity",
                table: "recovery_entries",
                columns: OriginalBrowserRequestColumns,
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_recovery_entries_expires_at",
                schema: "identity",
                table: "recovery_entries",
                column: "expires_at");

            migrationBuilder.AddForeignKey(
                name: "FK_recovery_cases_recovery_entries_entry_id",
                schema: "identity",
                table: "recovery_cases",
                column: "entry_id",
                principalSchema: "identity",
                principalTable: "recovery_entries",
                principalColumn: "entry_id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.Sql(RecoveryPasswordMigrationSql.Harden);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(RecoveryPasswordMigrationSql.BlockPopulatedDowngrade);
            migrationBuilder.Sql(RecoveryPasswordMigrationSql.RemoveGuards);

            migrationBuilder.DropForeignKey(
                name: "FK_recovery_cases_recovery_entries_entry_id",
                schema: "identity",
                table: "recovery_cases");

            migrationBuilder.DropTable(
                name: "recovery_entries",
                schema: "identity");

            migrationBuilder.DropCheckConstraint(
                name: "ck_recovery_result_shape",
                schema: "identity",
                table: "recovery_results");

            migrationBuilder.DropIndex(
                name: "IX_recovery_cases_entry_id",
                schema: "identity",
                table: "recovery_cases");

            migrationBuilder.DropCheckConstraint(
                name: "ck_recovery_case_binding",
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
                name: "prior_password_digest",
                schema: "identity",
                table: "recovery_results");

            migrationBuilder.DropColumn(
                name: "prior_security_stamp_digest",
                schema: "identity",
                table: "recovery_results");

            migrationBuilder.DropColumn(
                name: "repair_outbox_event_id",
                schema: "identity",
                table: "recovery_results");

            migrationBuilder.DropColumn(
                name: "repaired_password_digest",
                schema: "identity",
                table: "recovery_results");

            migrationBuilder.DropColumn(
                name: "repaired_security_stamp_digest",
                schema: "identity",
                table: "recovery_results");

            migrationBuilder.DropColumn(
                name: "session_effect",
                schema: "identity",
                table: "recovery_results");

            migrationBuilder.DropColumn(
                name: "entry_id",
                schema: "identity",
                table: "recovery_cases");

            migrationBuilder.DropColumn(
                name: "prior_password_digest",
                schema: "identity",
                table: "recovery_cases");

            migrationBuilder.DropColumn(
                name: "prior_security_stamp_digest",
                schema: "identity",
                table: "recovery_cases");

            migrationBuilder.DropColumn(
                name: "user_source_digest",
                schema: "identity",
                table: "recovery_cases");

            migrationBuilder.AlterColumn<Guid>(
                name: "target_session_id",
                schema: "identity",
                table: "recovery_results",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "session_outbox_event_id",
                schema: "identity",
                table: "recovery_results",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<long>(
                name: "prior_session_version",
                schema: "identity",
                table: "recovery_results",
                type: "bigint",
                nullable: false,
                defaultValue: 0L,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldNullable: true);

            migrationBuilder.AlterColumn<long>(
                name: "ended_session_version",
                schema: "identity",
                table: "recovery_results",
                type: "bigint",
                nullable: false,
                defaultValue: 0L,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "target_session_id",
                schema: "identity",
                table: "recovery_cases",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<long>(
                name: "expected_session_version",
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
                sql: "outcome='CONTAINED_PENDING_RESTORATION' AND prior_session_version>=1 AND ended_session_version=prior_session_version+1 AND set_lifecycle_version>=1 AND length(context_digest)=64 AND length(policy_version)>0 AND length(processing_reference)>0 AND length(retention_reference)>0 AND jsonb_typeof(policy_snapshot::jsonb)='object' AND jsonb_typeof(hierarchy_snapshot::jsonb)='object' AND jsonb_typeof(unresolved_effects_json::jsonb)='array' AND jsonb_array_length(unresolved_effects_json::jsonb)>0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_recovery_case_binding",
                schema: "identity",
                table: "recovery_cases",
                sql: "length(original_browser_digest)=64 AND length(network_digest)=64 AND length(context_digest)=64 AND length(facts_digest)=64 AND goal='ContainSelectedTunnerSession' AND session_effect='RevokeExactSelectedSession' AND length(policy_version)>0 AND length(processing_reference)>0 AND length(retention_reference)>0 AND jsonb_typeof(policy_snapshot::jsonb)='object' AND jsonb_typeof(hierarchy_snapshot::jsonb)='object' AND jsonb_typeof(unresolved_effects_json::jsonb)='array' AND jsonb_array_length(unresolved_effects_json::jsonb)>0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_recovery_case_state",
                schema: "identity",
                table: "recovery_cases",
                sql: "expires_at>created_at AND recovery_expires_at>expires_at AND ((state='PENDING_CODE' AND version=1 AND result_id IS NULL AND ended_at IS NULL) OR (state='CONTAINED' AND version=2 AND result_id IS NOT NULL AND ended_at>=created_at AND ended_at<expires_at))");

            migrationBuilder.AddCheckConstraint(
                name: "ck_recovery_case_versions",
                schema: "identity",
                table: "recovery_cases",
                sql: "expected_session_version>=1 AND expected_account_version>=1 AND expected_set_lifecycle_version>=1 AND ((usage_admission='ExactUsageSnapshot' AND expected_usage_version IS NOT NULL AND expected_usage_version>=0) OR (usage_admission='IndependentUnusedEntry' AND expected_usage_version IS NULL))");
        }
    }
}
