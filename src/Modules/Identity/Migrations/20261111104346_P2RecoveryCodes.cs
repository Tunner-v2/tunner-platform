using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tunner.Identity.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class P2RecoveryCodes : Migration
    {
        private static readonly string[] SessionOwnerColumns = ["session_id", "account_id"];
        private static readonly string[] IssuedSetOwnerColumns = ["account_id", "operation_id", "set_reference"];
        private static readonly string[] OperationOwnerColumns = ["account_id", "operation_id"];
        private static readonly string[] SetOwnerColumns = ["account_id", "set_reference"];
        private static readonly string[] ConsumedOwnerOperationColumns = ["account_id", "consumed_by_operation_id"];
        private static readonly string[] SessionRequestColumns = ["session_id", "request_id"];
        private static readonly string[] IssuanceOwnerSetColumns = ["account_id", "issuance_operation_id", "set_reference"];
        private static readonly string[] RetirementOwnerColumns = ["account_id", "retired_by_operation_id"];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "recovery_code_operations",
                schema: "identity",
                columns: table => new
                {
                    operation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<string>(type: "text", nullable: false),
                    session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    origin = table.Column<string>(type: "text", nullable: false),
                    kind = table.Column<string>(type: "text", nullable: false),
                    state = table.Column<string>(type: "text", nullable: false),
                    set_reference = table.Column<Guid>(type: "uuid", nullable: false),
                    expected_version = table.Column<long>(type: "bigint", nullable: false),
                    context_digest = table.Column<string>(type: "text", nullable: false),
                    prior_set_plan_digest = table.Column<string>(type: "text", nullable: false),
                    impact_facts_digest = table.Column<string>(type: "text", nullable: false),
                    policy_version = table.Column<string>(type: "text", nullable: false),
                    policy_digest = table.Column<string>(type: "text", nullable: false),
                    authentication_policy_version = table.Column<string>(type: "text", nullable: false),
                    security_stamp_digest = table.Column<string>(type: "text", nullable: false),
                    crypto_profile_version = table.Column<string>(type: "text", nullable: false),
                    format_profile_version = table.Column<string>(type: "text", nullable: false),
                    delivery_profile_version = table.Column<string>(type: "text", nullable: false),
                    codes_per_set = table.Column<int>(type: "integer", nullable: false),
                    maximum_codes_per_set = table.Column<int>(type: "integer", nullable: false),
                    retirement_plan_json = table.Column<string>(type: "text", nullable: false),
                    prior_set_count = table.Column<int>(type: "integer", nullable: false),
                    prior_remaining_code_count = table.Column<int>(type: "integer", nullable: false),
                    published_impact_key = table.Column<string>(type: "text", nullable: false),
                    publication_digest = table.Column<string>(type: "text", nullable: false),
                    publication_version = table.Column<string>(type: "text", nullable: false),
                    publication_from = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    publication_includes_from = table.Column<bool>(type: "boolean", nullable: false),
                    publication_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    publication_includes_until = table.Column<bool>(type: "boolean", nullable: false),
                    admission_policy_version = table.Column<string>(type: "text", nullable: false),
                    admission_evidence_reference = table.Column<string>(type: "text", nullable: false),
                    admission_from = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    admission_includes_from = table.Column<bool>(type: "boolean", nullable: false),
                    admission_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    admission_includes_until = table.Column<bool>(type: "boolean", nullable: false),
                    assurance_policy_version = table.Column<string>(type: "text", nullable: false),
                    proof_reference = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    result_recovery_expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    first_response_committed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    processing_reference = table.Column<string>(type: "text", nullable: false),
                    retention_reference = table.Column<string>(type: "text", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_recovery_code_operations", x => x.operation_id);
                    table.UniqueConstraint("AK_recovery_code_operations_account_id_operation_id", x => new { x.account_id, x.operation_id });
                    table.UniqueConstraint("AK_recovery_code_operations_account_id_operation_id_set_refere~", x => new { x.account_id, x.operation_id, x.set_reference });
                    table.CheckConstraint("ck_recovery_first_response", "(state='COMPLETED' AND completed_at IS NOT NULL AND first_response_committed_at IS NOT NULL AND first_response_committed_at=completed_at AND proof_reference IS NOT NULL) OR (state<>'COMPLETED' AND completed_at IS NULL AND first_response_committed_at IS NULL AND proof_reference IS NULL)");
                    table.CheckConstraint("ck_recovery_operation", "kind IN ('GENERATE','REPLACE') AND state IN ('PENDING_PROOF','COMPLETED','REJECTED','EXPIRED') AND version>=1 AND expected_version>0 AND expected_version<=9007199254740991 AND expires_at>created_at AND result_recovery_expires_at>expires_at AND codes_per_set>0 AND maximum_codes_per_set>=codes_per_set AND prior_set_count>=0 AND prior_remaining_code_count>=0");
                    table.CheckConstraint("ck_recovery_operation_binding", "length(account_id)>0 AND length(origin)>0 AND length(context_digest)=64 AND length(prior_set_plan_digest)=64 AND length(impact_facts_digest)=64 AND length(policy_version)>0 AND length(assurance_policy_version)>0 AND length(processing_reference)>0 AND length(retention_reference)>0");
                    table.CheckConstraint("ck_recovery_operation_profile", "length(policy_digest)=64 AND length(security_stamp_digest)=64 AND length(authentication_policy_version)>0 AND length(crypto_profile_version)>0 AND format_profile_version='NATIVE_HEX32_V1' AND length(delivery_profile_version)>0 AND length(retirement_plan_json)>0 AND length(published_impact_key)>0 AND length(publication_digest)=64 AND length(publication_version)>0 AND length(admission_policy_version)>0 AND length(admission_evidence_reference)>0");
                    table.ForeignKey(
                        name: "FK_recovery_code_operations_tunner_sessions_session_id_account~",
                        columns: x => new { x.session_id, x.account_id },
                        principalSchema: "identity",
                        principalTable: "tunner_sessions",
                        principalColumns: SessionOwnerColumns,
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "recovery_code_sets",
                schema: "identity",
                columns: table => new
                {
                    set_reference = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<string>(type: "text", nullable: false),
                    issuance_operation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    state = table.Column<string>(type: "text", nullable: false),
                    lifecycle_version = table.Column<long>(type: "bigint", nullable: false),
                    usage_version = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    retired_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    retired_by_operation_id = table.Column<Guid>(type: "uuid", nullable: true),
                    policy_version = table.Column<string>(type: "text", nullable: false),
                    crypto_profile_version = table.Column<string>(type: "text", nullable: false),
                    processing_reference = table.Column<string>(type: "text", nullable: false),
                    retention_reference = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_recovery_code_sets", x => x.set_reference);
                    table.UniqueConstraint("AK_recovery_code_sets_account_id_set_reference", x => new { x.account_id, x.set_reference });
                    table.CheckConstraint("ck_recovery_set", "state IN ('ACTIVE','REPLACED') AND lifecycle_version>=1 AND usage_version>=0");
                    table.CheckConstraint("ck_recovery_set_binding", "length(account_id)>0 AND length(policy_version)>0 AND length(crypto_profile_version)>0 AND length(processing_reference)>0 AND length(retention_reference)>0");
                    table.CheckConstraint("ck_recovery_set_retirement", "(state='ACTIVE' AND retired_at IS NULL AND retired_by_operation_id IS NULL) OR (state='REPLACED' AND retired_at IS NOT NULL AND retired_at>=created_at AND retired_by_operation_id IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_recovery_code_sets_accounts_account_id",
                        column: x => x.account_id,
                        principalSchema: "accounts",
                        principalTable: "accounts",
                        principalColumn: "account_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_recovery_code_sets_recovery_code_operations_account_id_issu~",
                        columns: x => new { x.account_id, x.issuance_operation_id, x.set_reference },
                        principalSchema: "identity",
                        principalTable: "recovery_code_operations",
                        principalColumns: IssuedSetOwnerColumns,
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_recovery_code_sets_recovery_code_operations_account_id_reti~",
                        columns: x => new { x.account_id, x.retired_by_operation_id },
                        principalSchema: "identity",
                        principalTable: "recovery_code_operations",
                        principalColumns: OperationOwnerColumns,
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "recovery_code_entries",
                schema: "identity",
                columns: table => new
                {
                    entry_reference = table.Column<Guid>(type: "uuid", nullable: false),
                    set_reference = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<string>(type: "text", nullable: false),
                    verifier = table.Column<string>(type: "text", nullable: false),
                    crypto_profile_version = table.Column<string>(type: "text", nullable: false),
                    consumed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    consumed_by_operation_id = table.Column<string>(type: "text", nullable: true),
                    consumption_correlation_id = table.Column<string>(type: "text", nullable: true),
                    consumption_context_digest = table.Column<string>(type: "text", nullable: true),
                    consumption_policy_version = table.Column<string>(type: "text", nullable: true),
                    consumption_owner_policy_version = table.Column<string>(type: "text", nullable: true),
                    consumption_outcome_reference = table.Column<string>(type: "text", nullable: true),
                    consumption_set_lifecycle_version = table.Column<long>(type: "bigint", nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_recovery_code_entries", x => x.entry_reference);
                    table.CheckConstraint("ck_recovery_consumption", "(consumed_at IS NULL AND consumed_by_operation_id IS NULL AND consumption_correlation_id IS NULL AND consumption_context_digest IS NULL AND consumption_policy_version IS NULL AND consumption_owner_policy_version IS NULL AND consumption_outcome_reference IS NULL AND consumption_set_lifecycle_version IS NULL AND version=1) OR (consumed_at IS NOT NULL AND consumed_by_operation_id IS NOT NULL AND consumption_correlation_id IS NOT NULL AND consumption_context_digest IS NOT NULL AND consumption_policy_version IS NOT NULL AND consumption_owner_policy_version IS NOT NULL AND consumption_outcome_reference IS NOT NULL AND consumption_set_lifecycle_version IS NOT NULL AND consumption_set_lifecycle_version>=1 AND length(consumed_by_operation_id)>0 AND length(consumption_correlation_id)>0 AND length(consumption_context_digest)=64 AND length(consumption_policy_version)>0 AND length(consumption_owner_policy_version)>0 AND length(consumption_outcome_reference)>0 AND version=2)");
                    table.CheckConstraint("ck_recovery_verifier", "length(verifier)=84 AND length(crypto_profile_version)>0");
                    table.ForeignKey(
                        name: "FK_recovery_code_entries_recovery_code_sets_account_id_set_ref~",
                        columns: x => new { x.account_id, x.set_reference },
                        principalSchema: "identity",
                        principalTable: "recovery_code_sets",
                        principalColumns: SetOwnerColumns,
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_recovery_code_entries_account_id_consumed_by_operation_id",
                schema: "identity",
                table: "recovery_code_entries",
                columns: ConsumedOwnerOperationColumns,
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_recovery_code_entries_account_id_set_reference",
                schema: "identity",
                table: "recovery_code_entries",
                columns: SetOwnerColumns);

            migrationBuilder.CreateIndex(
                name: "IX_recovery_code_operations_session_id_account_id",
                schema: "identity",
                table: "recovery_code_operations",
                columns: SessionOwnerColumns);

            migrationBuilder.CreateIndex(
                name: "IX_recovery_code_operations_session_id_request_id",
                schema: "identity",
                table: "recovery_code_operations",
                columns: SessionRequestColumns,
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_recovery_code_sets_account_id_issuance_operation_id_set_ref~",
                schema: "identity",
                table: "recovery_code_sets",
                columns: IssuanceOwnerSetColumns);

            migrationBuilder.CreateIndex(
                name: "IX_recovery_code_sets_account_id_retired_by_operation_id",
                schema: "identity",
                table: "recovery_code_sets",
                columns: RetirementOwnerColumns);

            migrationBuilder.CreateIndex(
                name: "IX_recovery_code_sets_issuance_operation_id",
                schema: "identity",
                table: "recovery_code_sets",
                column: "issuance_operation_id",
                unique: true);

            migrationBuilder.Sql(RecoveryCodeMigrationSql.Guards);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(IdentityMigrationSql.GuardDowngrade);
            migrationBuilder.Sql(RecoveryCodeMigrationSql.RemoveGuards);
            migrationBuilder.DropTable(
                name: "recovery_code_entries",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "recovery_code_sets",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "recovery_code_operations",
                schema: "identity");
        }
    }
}
