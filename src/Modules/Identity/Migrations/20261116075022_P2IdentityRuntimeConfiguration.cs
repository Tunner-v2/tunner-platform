using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tunner.Identity.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class P2IdentityRuntimeConfiguration : Migration
    {
        private static readonly string[] GovernanceIdentityColumns = ["scope_key", "version"];
        private static readonly string[] GovernanceLookupColumns = ["scope_key", "governance_version"];
        private static readonly string[] PolicyVersionColumns = ["scope_key", "policy_version_reference"];
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "runtime_configuration_governance",
                schema: "identity",
                columns: table => new
                {
                    scope_key = table.Column<string>(type: "text", nullable: false),
                    version = table.Column<string>(type: "text", nullable: false),
                    snapshot = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_runtime_configuration_governance", x => new { x.scope_key, x.version });
                });

            migrationBuilder.CreateTable(
                name: "runtime_configuration_proposals",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    scope_key = table.Column<string>(type: "text", nullable: false),
                    governance_version = table.Column<string>(type: "text", nullable: false),
                    payload = table.Column<string>(type: "text", nullable: false),
                    digest = table.Column<string>(type: "text", nullable: false),
                    policy_version_reference = table.Column<string>(type: "text", nullable: false),
                    contributors_json = table.Column<string>(type: "text", nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    state = table.Column<string>(type: "text", nullable: false),
                    model_origin = table.Column<bool>(type: "boolean", nullable: false),
                    version_reference = table.Column<Guid>(type: "uuid", nullable: true),
                    approver_reference = table.Column<string>(type: "text", nullable: true),
                    approval_evidence_reference = table.Column<string>(type: "text", nullable: true),
                    approval_event_reference = table.Column<Guid>(type: "uuid", nullable: true),
                    last_event_reference = table.Column<Guid>(type: "uuid", nullable: false),
                    last_operation = table.Column<string>(type: "text", nullable: false),
                    last_target_reference = table.Column<Guid>(type: "uuid", nullable: false),
                    last_actor_reference = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_runtime_configuration_proposals", x => x.id);
                    table.CheckConstraint("ck_identity_configuration_proposal", "revision>=1 AND state IN ('DRAFT','VALIDATED','APPROVED','PUBLISHED')");
                    table.ForeignKey(
                        name: "FK_runtime_configuration_proposals_runtime_configuration_gover~",
                        columns: x => new { x.scope_key, x.governance_version },
                        principalSchema: "identity",
                        principalTable: "runtime_configuration_governance",
                        principalColumns: GovernanceIdentityColumns,
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "runtime_configuration_versions",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    proposal_reference = table.Column<Guid>(type: "uuid", nullable: false),
                    scope_key = table.Column<string>(type: "text", nullable: false),
                    governance_version = table.Column<string>(type: "text", nullable: false),
                    policy_version_reference = table.Column<string>(type: "text", nullable: false),
                    payload = table.Column<string>(type: "text", nullable: false),
                    digest = table.Column<string>(type: "text", nullable: false),
                    contributors_json = table.Column<string>(type: "text", nullable: false),
                    model_origin = table.Column<bool>(type: "boolean", nullable: false),
                    validation_event_reference = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_runtime_configuration_versions", x => x.id);
                    table.ForeignKey(
                        name: "FK_runtime_configuration_versions_runtime_configuration_propos~",
                        column: x => x.proposal_reference,
                        principalSchema: "identity",
                        principalTable: "runtime_configuration_proposals",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "runtime_configuration_publications",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    version_reference = table.Column<Guid>(type: "uuid", nullable: false),
                    scope_key = table.Column<string>(type: "text", nullable: false),
                    effective_from = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    includes_from = table.Column<bool>(type: "boolean", nullable: false),
                    effective_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    includes_until = table.Column<bool>(type: "boolean", nullable: false),
                    supersedes_publication_reference = table.Column<Guid>(type: "uuid", nullable: true),
                    approver_reference = table.Column<string>(type: "text", nullable: false),
                    approval_evidence_reference = table.Column<string>(type: "text", nullable: false),
                    approval_event_reference = table.Column<Guid>(type: "uuid", nullable: false),
                    publisher_reference = table.Column<string>(type: "text", nullable: false),
                    publisher_evidence_reference = table.Column<string>(type: "text", nullable: false),
                    publisher_authorization_version = table.Column<string>(type: "text", nullable: false),
                    publication_event_reference = table.Column<Guid>(type: "uuid", nullable: false),
                    operation = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_runtime_configuration_publications", x => x.id);
                    table.CheckConstraint("ck_identity_configuration_interval", "effective_until IS NULL OR effective_until>effective_from OR (effective_until=effective_from AND includes_from AND includes_until)");
                    table.ForeignKey(
                        name: "FK_runtime_configuration_publications_runtime_configuration_pu~",
                        column: x => x.supersedes_publication_reference,
                        principalSchema: "identity",
                        principalTable: "runtime_configuration_publications",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_runtime_configuration_publications_runtime_configuration_ve~",
                        column: x => x.version_reference,
                        principalSchema: "identity",
                        principalTable: "runtime_configuration_versions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_runtime_configuration_proposals_scope_key_governance_version",
                schema: "identity",
                table: "runtime_configuration_proposals",
                columns: GovernanceLookupColumns);

            migrationBuilder.CreateIndex(
                name: "IX_runtime_configuration_publications_scope_key",
                schema: "identity",
                table: "runtime_configuration_publications",
                column: "scope_key");

            migrationBuilder.CreateIndex(
                name: "IX_runtime_configuration_publications_supersedes_publication_r~",
                schema: "identity",
                table: "runtime_configuration_publications",
                column: "supersedes_publication_reference",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_runtime_configuration_publications_version_reference",
                schema: "identity",
                table: "runtime_configuration_publications",
                column: "version_reference");

            migrationBuilder.CreateIndex(
                name: "IX_runtime_configuration_versions_proposal_reference",
                schema: "identity",
                table: "runtime_configuration_versions",
                column: "proposal_reference");

            migrationBuilder.CreateIndex(
                name: "IX_runtime_configuration_versions_scope_key_policy_version_ref~",
                schema: "identity",
                table: "runtime_configuration_versions",
                columns: PolicyVersionColumns,
                unique: true);
            migrationBuilder.Sql(IdentityRuntimeConfigurationIntegritySql.Install);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                LOCK TABLE identity.runtime_configuration_governance, identity.runtime_configuration_proposals,
                  identity.runtime_configuration_versions, identity.runtime_configuration_publications IN ACCESS EXCLUSIVE MODE;
                DO $guard$
                BEGIN
                  IF EXISTS (SELECT 1 FROM identity.runtime_configuration_governance)
                    OR EXISTS (SELECT 1 FROM identity.runtime_configuration_proposals)
                    OR EXISTS (SELECT 1 FROM identity.runtime_configuration_versions)
                    OR EXISTS (SELECT 1 FROM identity.runtime_configuration_publications)
                  THEN
                    RAISE EXCEPTION 'Committed Identity configuration history requires a governed forward migration'
                      USING ERRCODE='23514';
                  END IF;
                END $guard$;
                """);
            migrationBuilder.DropTable(
                name: "runtime_configuration_publications",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "runtime_configuration_versions",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "runtime_configuration_proposals",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "runtime_configuration_governance",
                schema: "identity");
            migrationBuilder.Sql("""
                DROP FUNCTION IF EXISTS identity.runtime_configuration_effect_guard();
                DROP FUNCTION IF EXISTS identity.runtime_configuration_transition();
                DROP FUNCTION IF EXISTS identity.runtime_configuration_valid_publication(uuid);
                DROP FUNCTION IF EXISTS identity.runtime_configuration_valid_version(uuid);
                DROP FUNCTION IF EXISTS identity.runtime_configuration_has_effect(uuid,text,text,text,text,text,text);
                DROP FUNCTION IF EXISTS identity.runtime_configuration_immutable();
                """);
        }
    }
}
