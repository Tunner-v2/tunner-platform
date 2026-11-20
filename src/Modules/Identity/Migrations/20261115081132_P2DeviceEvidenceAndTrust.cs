using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tunner.Identity.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class P2DeviceEvidenceAndTrust : Migration
    {
        private static readonly string[] DeviceOwnerColumns = ["account_id", "origin", "device_id"];
        private static readonly string[] TrustOwnerColumns = ["account_id", "origin", "device_id", "version"];
        private static readonly string[] OriginalRequestColumns = ["session_id", "request_id"];
        private static readonly string[] CorrelationColumns = ["account_id", "origin", "correlation_digest"];
        private static readonly string[] SessionTrustColumns = ["account_id", "origin", "device_id", "trust_version"];
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "device_records",
                schema: "identity",
                columns: table => new
                {
                    device_id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<string>(type: "text", nullable: false),
                    origin = table.Column<string>(type: "text", nullable: false),
                    correlation_digest = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_observed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    characteristics_json = table.Column<string>(type: "text", nullable: false),
                    policy_version = table.Column<string>(type: "text", nullable: false),
                    policy_snapshot = table.Column<string>(type: "text", nullable: false),
                    collection_reference = table.Column<string>(type: "text", nullable: false),
                    inventory_reference = table.Column<string>(type: "text", nullable: false),
                    processing_reference = table.Column<string>(type: "text", nullable: false),
                    retention_reference = table.Column<string>(type: "text", nullable: false),
                    classification = table.Column<string>(type: "text", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_device_records", x => x.device_id);
                    table.UniqueConstraint("AK_device_records_account_id_origin_device_id", x => new { x.account_id, x.origin, x.device_id });
                    table.CheckConstraint("ck_device_record", "version>=1 AND last_observed_at>=created_at AND length(correlation_digest)=64 AND length(origin)>0 AND length(policy_version)>0 AND length(collection_reference)>0 AND length(inventory_reference)>0 AND length(processing_reference)>0 AND length(retention_reference)>0 AND length(classification)>0 AND jsonb_typeof(characteristics_json::jsonb)='object' AND jsonb_typeof(policy_snapshot::jsonb)='object'");
                    table.ForeignKey(
                        name: "FK_device_records_accounts_account_id",
                        column: x => x.account_id,
                        principalSchema: "accounts",
                        principalTable: "accounts",
                        principalColumn: "account_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "device_operations",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<string>(type: "text", nullable: false),
                    session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    origin = table.Column<string>(type: "text", nullable: false),
                    device_id = table.Column<Guid>(type: "uuid", nullable: false),
                    expected_device_version = table.Column<long>(type: "bigint", nullable: false),
                    expected_trust_version = table.Column<long>(type: "bigint", nullable: false),
                    expected_account_version = table.Column<long>(type: "bigint", nullable: false),
                    security_stamp_digest = table.Column<string>(type: "text", nullable: false),
                    policy_version = table.Column<string>(type: "text", nullable: false),
                    policy_snapshot = table.Column<string>(type: "text", nullable: false),
                    assurance_policy_version = table.Column<string>(type: "text", nullable: false),
                    context_digest = table.Column<string>(type: "text", nullable: false),
                    correlation_id = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    recovery_expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    state = table.Column<string>(type: "text", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    proof_id = table.Column<Guid>(type: "uuid", nullable: true),
                    result_trust_version = table.Column<long>(type: "bigint", nullable: true),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    outbox_event_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_device_operations", x => x.id);
                    table.CheckConstraint("ck_device_operation", "expected_device_version>=1 AND expected_trust_version>=1 AND expected_account_version>=1 AND expires_at>created_at AND recovery_expires_at>expires_at AND length(context_digest)=64 AND length(security_stamp_digest)=64 AND length(policy_version)>0 AND length(assurance_policy_version)>0 AND jsonb_typeof(policy_snapshot::jsonb)='object' AND ((state='PENDING' AND version=1 AND proof_id IS NULL AND result_trust_version IS NULL AND completed_at IS NULL AND outbox_event_id IS NULL) OR (state='COMPLETED' AND version=2 AND proof_id IS NOT NULL AND result_trust_version=expected_trust_version+1 AND completed_at>=created_at AND completed_at<expires_at AND outbox_event_id IS NOT NULL))");
                    table.ForeignKey(
                        name: "FK_device_operations_device_records_account_id_origin_device_id",
                        columns: x => new { x.account_id, x.origin, x.device_id },
                        principalSchema: "identity",
                        principalTable: "device_records",
                        principalColumns: DeviceOwnerColumns,
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_device_operations_outbox_messages_outbox_event_id",
                        column: x => x.outbox_event_id,
                        principalSchema: "platform",
                        principalTable: "outbox_messages",
                        principalColumn: "event_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_device_operations_step_up_proofs_proof_id",
                        column: x => x.proof_id,
                        principalSchema: "identity",
                        principalTable: "step_up_proofs",
                        principalColumn: "proof_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_device_operations_tunner_sessions_session_id",
                        column: x => x.session_id,
                        principalSchema: "identity",
                        principalTable: "tunner_sessions",
                        principalColumn: "session_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "device_trust_versions",
                schema: "identity",
                columns: table => new
                {
                    device_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    account_id = table.Column<string>(type: "text", nullable: false),
                    origin = table.Column<string>(type: "text", nullable: false),
                    device_version = table.Column<long>(type: "bigint", nullable: false),
                    state = table.Column<string>(type: "text", nullable: false),
                    source_session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    removal_operation_id = table.Column<Guid>(type: "uuid", nullable: true),
                    eligibility_facts_json = table.Column<string>(type: "text", nullable: false),
                    eligibility_decision_json = table.Column<string>(type: "text", nullable: false),
                    policy_version = table.Column<string>(type: "text", nullable: false),
                    policy_snapshot = table.Column<string>(type: "text", nullable: false),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    operation_id = table.Column<string>(type: "text", nullable: false),
                    correlation_id = table.Column<string>(type: "text", nullable: false),
                    outbox_event_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_device_trust_versions", x => new { x.device_id, x.version });
                    table.UniqueConstraint("AK_device_trust_versions_account_id_origin_device_id_version", x => new { x.account_id, x.origin, x.device_id, x.version });
                    table.CheckConstraint("ck_device_trust", "version>=1 AND device_version>=1 AND length(policy_version)>0 AND length(operation_id)>0 AND length(correlation_id)>0 AND jsonb_typeof(policy_snapshot::jsonb)='object' AND ((state IN ('TRUSTED','UNTRUSTED') AND removal_operation_id IS NULL AND jsonb_typeof(eligibility_facts_json::jsonb)='object' AND jsonb_typeof(eligibility_decision_json::jsonb)='object') OR (state='REVOKED' AND removal_operation_id IS NOT NULL AND version>=2 AND eligibility_facts_json='' AND eligibility_decision_json=''))");
                    table.ForeignKey(
                        name: "FK_device_trust_versions_device_operations_removal_operation_id",
                        column: x => x.removal_operation_id,
                        principalSchema: "identity",
                        principalTable: "device_operations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_device_trust_versions_device_records_account_id_origin_devi~",
                        columns: x => new { x.account_id, x.origin, x.device_id },
                        principalSchema: "identity",
                        principalTable: "device_records",
                        principalColumns: DeviceOwnerColumns,
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_device_trust_versions_outbox_messages_outbox_event_id",
                        column: x => x.outbox_event_id,
                        principalSchema: "platform",
                        principalTable: "outbox_messages",
                        principalColumn: "event_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_device_trust_versions_tunner_sessions_source_session_id",
                        column: x => x.source_session_id,
                        principalSchema: "identity",
                        principalTable: "tunner_sessions",
                        principalColumn: "session_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "device_session_evidence",
                schema: "identity",
                columns: table => new
                {
                    session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<string>(type: "text", nullable: false),
                    origin = table.Column<string>(type: "text", nullable: false),
                    device_id = table.Column<Guid>(type: "uuid", nullable: false),
                    device_version = table.Column<long>(type: "bigint", nullable: false),
                    trust_version = table.Column<long>(type: "bigint", nullable: false),
                    observed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    policy_snapshot = table.Column<string>(type: "text", nullable: false),
                    characteristics_json = table.Column<string>(type: "text", nullable: false),
                    outbox_event_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_device_session_evidence", x => x.session_id);
                    table.CheckConstraint("ck_device_session_evidence", "device_version>=1 AND trust_version>=1 AND jsonb_typeof(policy_snapshot::jsonb)='object' AND jsonb_typeof(characteristics_json::jsonb)='object'");
                    table.ForeignKey(
                        name: "FK_device_session_evidence_device_trust_versions_account_id_or~",
                        columns: x => new { x.account_id, x.origin, x.device_id, x.trust_version },
                        principalSchema: "identity",
                        principalTable: "device_trust_versions",
                        principalColumns: TrustOwnerColumns,
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_device_session_evidence_outbox_messages_outbox_event_id",
                        column: x => x.outbox_event_id,
                        principalSchema: "platform",
                        principalTable: "outbox_messages",
                        principalColumn: "event_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_device_session_evidence_tunner_sessions_session_id",
                        column: x => x.session_id,
                        principalSchema: "identity",
                        principalTable: "tunner_sessions",
                        principalColumn: "session_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_device_operations_account_id_origin_device_id",
                schema: "identity",
                table: "device_operations",
                columns: DeviceOwnerColumns);

            migrationBuilder.CreateIndex(
                name: "IX_device_operations_outbox_event_id",
                schema: "identity",
                table: "device_operations",
                column: "outbox_event_id");

            migrationBuilder.CreateIndex(
                name: "IX_device_operations_proof_id",
                schema: "identity",
                table: "device_operations",
                column: "proof_id");

            migrationBuilder.CreateIndex(
                name: "IX_device_operations_session_id_request_id",
                schema: "identity",
                table: "device_operations",
                columns: OriginalRequestColumns,
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_device_records_account_id_origin_correlation_digest",
                schema: "identity",
                table: "device_records",
                columns: CorrelationColumns,
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_device_session_evidence_account_id_origin_device_id_trust_v~",
                schema: "identity",
                table: "device_session_evidence",
                columns: SessionTrustColumns);

            migrationBuilder.CreateIndex(
                name: "IX_device_session_evidence_outbox_event_id",
                schema: "identity",
                table: "device_session_evidence",
                column: "outbox_event_id");

            migrationBuilder.CreateIndex(
                name: "IX_device_trust_versions_outbox_event_id",
                schema: "identity",
                table: "device_trust_versions",
                column: "outbox_event_id");

            migrationBuilder.CreateIndex(
                name: "IX_device_trust_versions_removal_operation_id",
                schema: "identity",
                table: "device_trust_versions",
                column: "removal_operation_id");

            migrationBuilder.CreateIndex(
                name: "IX_device_trust_versions_source_session_id",
                schema: "identity",
                table: "device_trust_versions",
                column: "source_session_id");
            migrationBuilder.Sql(DeviceMigrationSql.Guards);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(DeviceMigrationSql.BlockPopulatedDowngrade);
            migrationBuilder.Sql(DeviceMigrationSql.RemoveGuards);
            migrationBuilder.DropTable(
                name: "device_session_evidence",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "device_trust_versions",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "device_operations",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "device_records",
                schema: "identity");
        }
    }
}
