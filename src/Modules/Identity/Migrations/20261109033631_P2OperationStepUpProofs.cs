using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tunner.Identity.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class P2OperationStepUpProofs : Migration
    {
        private static readonly string[] SessionAccountColumns = ["session_id", "account_id"];
        private static readonly string[] SessionOperationColumns = ["session_id", "operation_id"];
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddUniqueConstraint(
                name: "AK_tunner_sessions_session_id_account_id",
                schema: "identity",
                table: "tunner_sessions",
                columns: SessionAccountColumns);

            migrationBuilder.CreateTable(
                name: "step_up_proofs",
                schema: "identity",
                columns: table => new
                {
                    proof_id = table.Column<Guid>(type: "uuid", nullable: false),
                    session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<string>(type: "text", nullable: false),
                    key_digest = table.Column<string>(type: "text", nullable: false),
                    protected_key = table.Column<string>(type: "text", nullable: false),
                    origin = table.Column<string>(type: "text", nullable: false),
                    scheme = table.Column<string>(type: "text", nullable: false),
                    security_stamp_digest = table.Column<string>(type: "text", nullable: false),
                    operation_id = table.Column<string>(type: "text", nullable: false),
                    correlation_id = table.Column<string>(type: "text", nullable: false),
                    operation = table.Column<string>(type: "text", nullable: false),
                    context = table.Column<string>(type: "text", nullable: false),
                    resource_id = table.Column<string>(type: "text", nullable: false),
                    expected_version = table.Column<long>(type: "bigint", nullable: false),
                    context_digest = table.Column<string>(type: "text", nullable: false),
                    policy_version = table.Column<string>(type: "text", nullable: false),
                    authentication_policy_version = table.Column<string>(type: "text", nullable: false),
                    authentication_method = table.Column<string>(type: "text", nullable: false),
                    assurance = table.Column<string>(type: "text", nullable: false),
                    processing_reference = table.Column<string>(type: "text", nullable: false),
                    retention_reference = table.Column<string>(type: "text", nullable: false),
                    authenticated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    state = table.Column<string>(type: "text", nullable: false),
                    ended_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_step_up_proofs", x => x.proof_id);
                    table.CheckConstraint("ck_step_up_bounds", "expires_at > authenticated_at AND expected_version >= 0 AND version >= 1");
                    table.CheckConstraint("ck_step_up_consumed", "state<>'CONSUMED' OR ended_at < expires_at");
                    table.CheckConstraint("ck_step_up_digests", "key_digest ~ '^[0-9A-F]{64}$' AND context_digest ~ '^[0-9A-F]{64}$'");
                    table.CheckConstraint("ck_step_up_end", "(state='ACTIVE' AND ended_at IS NULL AND length(protected_key)>0) OR (state<>'ACTIVE' AND ended_at IS NOT NULL AND ended_at >= authenticated_at AND protected_key='')");
                    table.CheckConstraint("ck_step_up_state", "state IN ('ACTIVE','CONSUMED','EXPIRED')");
                    table.ForeignKey(
                        name: "FK_step_up_proofs_tunner_sessions_session_id_account_id",
                        columns: x => new { x.session_id, x.account_id },
                        principalSchema: "identity",
                        principalTable: "tunner_sessions",
                        principalColumns: SessionAccountColumns,
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_step_up_proofs_key_digest",
                schema: "identity",
                table: "step_up_proofs",
                column: "key_digest",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_step_up_proofs_session_id_account_id",
                schema: "identity",
                table: "step_up_proofs",
                columns: SessionAccountColumns);

            migrationBuilder.CreateIndex(
                name: "IX_step_up_proofs_session_id_operation_id",
                schema: "identity",
                table: "step_up_proofs",
                columns: SessionOperationColumns,
                unique: true);
            migrationBuilder.Sql(StepUpModelConfiguration.Guard);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(IdentityMigrationSql.GuardDowngrade);
            migrationBuilder.Sql(StepUpModelConfiguration.GuardDowngrade);
            migrationBuilder.DropTable(
                name: "step_up_proofs",
                schema: "identity");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_tunner_sessions_session_id_account_id",
                schema: "identity",
                table: "tunner_sessions");
        }
    }
}
