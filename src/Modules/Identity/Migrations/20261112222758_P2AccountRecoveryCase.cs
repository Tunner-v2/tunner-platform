using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tunner.Identity.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class P2AccountRecoveryCase : Migration
    {
        private static readonly string[] AccountSetColumns = ["account_id", "set_reference"];
        private static readonly string[] OriginalRequestColumns = ["account_id", "original_browser_digest", "request_id"];
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "recovery_cases",
                schema: "identity",
                columns: table => new
                {
                    case_id = table.Column<Guid>(type: "uuid", nullable: false),
                    request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<string>(type: "text", nullable: false),
                    original_browser_digest = table.Column<string>(type: "text", nullable: false),
                    origin = table.Column<string>(type: "text", nullable: false),
                    network_digest = table.Column<string>(type: "text", nullable: false),
                    context_digest = table.Column<string>(type: "text", nullable: false),
                    goal = table.Column<string>(type: "text", nullable: false),
                    session_effect = table.Column<string>(type: "text", nullable: false),
                    target_session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    expected_session_version = table.Column<long>(type: "bigint", nullable: false),
                    expected_account_version = table.Column<long>(type: "bigint", nullable: false),
                    account_state = table.Column<string>(type: "text", nullable: false),
                    set_reference = table.Column<Guid>(type: "uuid", nullable: false),
                    expected_set_lifecycle_version = table.Column<long>(type: "bigint", nullable: false),
                    expected_usage_version = table.Column<long>(type: "bigint", nullable: true),
                    usage_admission = table.Column<string>(type: "text", nullable: false),
                    policy_version = table.Column<string>(type: "text", nullable: false),
                    policy_snapshot = table.Column<string>(type: "text", nullable: false),
                    hierarchy_snapshot = table.Column<string>(type: "text", nullable: false),
                    facts_digest = table.Column<string>(type: "text", nullable: false),
                    processing_reference = table.Column<string>(type: "text", nullable: false),
                    retention_reference = table.Column<string>(type: "text", nullable: false),
                    unresolved_effects_json = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    recovery_expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    state = table.Column<string>(type: "text", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    result_id = table.Column<Guid>(type: "uuid", nullable: true),
                    ended_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_recovery_cases", x => x.case_id);
                    table.CheckConstraint("ck_recovery_case_binding", "length(original_browser_digest)=64 AND length(network_digest)=64 AND length(context_digest)=64 AND length(facts_digest)=64 AND goal='ContainSelectedTunnerSession' AND session_effect='RevokeExactSelectedSession' AND length(policy_version)>0 AND length(processing_reference)>0 AND length(retention_reference)>0 AND jsonb_typeof(policy_snapshot::jsonb)='object' AND jsonb_typeof(hierarchy_snapshot::jsonb)='object' AND jsonb_typeof(unresolved_effects_json::jsonb)='array' AND jsonb_array_length(unresolved_effects_json::jsonb)>0");
                    table.CheckConstraint("ck_recovery_case_state", "expires_at>created_at AND recovery_expires_at>expires_at AND ((state='PENDING_CODE' AND version=1 AND result_id IS NULL AND ended_at IS NULL) OR (state='CONTAINED' AND version=2 AND result_id IS NOT NULL AND ended_at>=created_at AND ended_at<expires_at))");
                    table.CheckConstraint("ck_recovery_case_versions", "expected_session_version>=1 AND expected_account_version>=1 AND expected_set_lifecycle_version>=1 AND ((usage_admission='ExactUsageSnapshot' AND expected_usage_version IS NOT NULL AND expected_usage_version>=0) OR (usage_admission='IndependentUnusedEntry' AND expected_usage_version IS NULL))");
                    table.ForeignKey(
                        name: "FK_recovery_cases_accounts_account_id",
                        column: x => x.account_id,
                        principalSchema: "accounts",
                        principalTable: "accounts",
                        principalColumn: "account_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_recovery_cases_recovery_code_sets_account_id_set_reference",
                        columns: x => new { x.account_id, x.set_reference },
                        principalSchema: "identity",
                        principalTable: "recovery_code_sets",
                        principalColumns: AccountSetColumns,
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_recovery_cases_tunner_sessions_target_session_id",
                        column: x => x.target_session_id,
                        principalSchema: "identity",
                        principalTable: "tunner_sessions",
                        principalColumn: "session_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "recovery_results",
                schema: "identity",
                columns: table => new
                {
                    result_id = table.Column<Guid>(type: "uuid", nullable: false),
                    case_id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<string>(type: "text", nullable: false),
                    outcome = table.Column<string>(type: "text", nullable: false),
                    target_session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    prior_session_version = table.Column<long>(type: "bigint", nullable: false),
                    ended_session_version = table.Column<long>(type: "bigint", nullable: false),
                    context_digest = table.Column<string>(type: "text", nullable: false),
                    policy_version = table.Column<string>(type: "text", nullable: false),
                    policy_snapshot = table.Column<string>(type: "text", nullable: false),
                    hierarchy_snapshot = table.Column<string>(type: "text", nullable: false),
                    set_reference = table.Column<Guid>(type: "uuid", nullable: false),
                    set_lifecycle_version = table.Column<long>(type: "bigint", nullable: false),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    unresolved_effects_json = table.Column<string>(type: "text", nullable: false),
                    processing_reference = table.Column<string>(type: "text", nullable: false),
                    retention_reference = table.Column<string>(type: "text", nullable: false),
                    session_outbox_event_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_recovery_results", x => x.result_id);
                    table.CheckConstraint("ck_recovery_result_shape", "outcome='CONTAINED_PENDING_RESTORATION' AND prior_session_version>=1 AND ended_session_version=prior_session_version+1 AND set_lifecycle_version>=1 AND length(context_digest)=64 AND length(policy_version)>0 AND length(processing_reference)>0 AND length(retention_reference)>0 AND jsonb_typeof(policy_snapshot::jsonb)='object' AND jsonb_typeof(hierarchy_snapshot::jsonb)='object' AND jsonb_typeof(unresolved_effects_json::jsonb)='array' AND jsonb_array_length(unresolved_effects_json::jsonb)>0");
                    table.ForeignKey(
                        name: "FK_recovery_results_recovery_cases_case_id",
                        column: x => x.case_id,
                        principalSchema: "identity",
                        principalTable: "recovery_cases",
                        principalColumn: "case_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_recovery_results_tunner_sessions_target_session_id",
                        column: x => x.target_session_id,
                        principalSchema: "identity",
                        principalTable: "tunner_sessions",
                        principalColumn: "session_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_recovery_cases_account_id_original_browser_digest_request_id",
                schema: "identity",
                table: "recovery_cases",
                columns: OriginalRequestColumns,
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_recovery_cases_account_id_set_reference",
                schema: "identity",
                table: "recovery_cases",
                columns: AccountSetColumns);

            migrationBuilder.CreateIndex(
                name: "IX_recovery_cases_expires_at",
                schema: "identity",
                table: "recovery_cases",
                column: "expires_at");

            migrationBuilder.CreateIndex(
                name: "IX_recovery_cases_result_id",
                schema: "identity",
                table: "recovery_cases",
                column: "result_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_recovery_cases_target_session_id",
                schema: "identity",
                table: "recovery_cases",
                column: "target_session_id");

            migrationBuilder.CreateIndex(
                name: "IX_recovery_results_case_id",
                schema: "identity",
                table: "recovery_results",
                column: "case_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_recovery_results_target_session_id",
                schema: "identity",
                table: "recovery_results",
                column: "target_session_id");

            migrationBuilder.Sql(RecoveryCaseMigrationSql.Harden);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(RecoveryCaseMigrationSql.BlockPopulatedDowngrade);
            migrationBuilder.Sql(RecoveryCaseMigrationSql.RemoveGuards);

            migrationBuilder.DropTable(
                name: "recovery_results",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "recovery_cases",
                schema: "identity");
        }
    }
}
