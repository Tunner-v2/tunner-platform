using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tunner.PresentationContent.Migrations
{
    /// <inheritdoc />
    public partial class P2PresentationContentFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "presentation_content");

            migrationBuilder.CreateTable(
                name: "profiles",
                schema: "presentation_content",
                columns: table => new
                {
                    Scope = table.Column<string>(type: "text", nullable: false),
                    Version = table.Column<string>(type: "text", nullable: false),
                    Snapshot = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_profiles", x => new { x.Scope, x.Version });
                });

            migrationBuilder.CreateTable(
                name: "definitions",
                schema: "presentation_content",
                columns: table => new
                {
                    Scope = table.Column<string>(type: "text", nullable: false),
                    ProfileVersion = table.Column<string>(type: "text", nullable: false),
                    Key = table.Column<string>(type: "text", nullable: false),
                    Snapshot = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_definitions", x => new { x.Scope, x.ProfileVersion, x.Key });
                    table.ForeignKey(
                        name: "FK_definitions_profiles_Scope_ProfileVersion",
                        columns: x => new { x.Scope, x.ProfileVersion },
                        principalSchema: "presentation_content",
                        principalTable: "profiles",
                        principalColumns: new[] { "Scope", "Version" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "variants",
                schema: "presentation_content",
                columns: table => new
                {
                    Scope = table.Column<string>(type: "text", nullable: false),
                    ProfileVersion = table.Column<string>(type: "text", nullable: false),
                    Reference = table.Column<string>(type: "text", nullable: false),
                    Snapshot = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_variants", x => new { x.Scope, x.ProfileVersion, x.Reference });
                    table.ForeignKey(
                        name: "FK_variants_profiles_Scope_ProfileVersion",
                        columns: x => new { x.Scope, x.ProfileVersion },
                        principalSchema: "presentation_content",
                        principalTable: "profiles",
                        principalColumns: new[] { "Scope", "Version" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "proposals",
                schema: "presentation_content",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Scope = table.Column<string>(type: "text", nullable: false),
                    ProfileVersion = table.Column<string>(type: "text", nullable: false),
                    Key = table.Column<string>(type: "text", nullable: false),
                    VariantReference = table.Column<string>(type: "text", nullable: false),
                    Text = table.Column<string>(type: "text", nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    State = table.Column<string>(type: "text", nullable: false),
                    ContributorsJson = table.Column<string>(type: "text", nullable: false),
                    SourceEvidenceReference = table.Column<string>(type: "text", nullable: false),
                    ModelOrigin = table.Column<bool>(type: "boolean", nullable: false),
                    VersionReference = table.Column<Guid>(type: "uuid", nullable: true),
                    ApproverReference = table.Column<string>(type: "text", nullable: true),
                    ApprovalEvidenceReference = table.Column<string>(type: "text", nullable: true),
                    LastEventReference = table.Column<Guid>(type: "uuid", nullable: false),
                    LastTargetReference = table.Column<Guid>(type: "uuid", nullable: false),
                    LastOperation = table.Column<string>(type: "text", nullable: false),
                    LastActorReference = table.Column<string>(type: "text", nullable: false),
                    LastDigest = table.Column<string>(type: "text", nullable: false),
                    ApprovalEventReference = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_proposals", x => x.Id);
                    table.CheckConstraint("ck_content_proposal", "\"Revision\">=1 AND \"State\" IN ('DRAFT','VALIDATED','APPROVED','PUBLISHED')");
                    table.ForeignKey(
                        name: "FK_proposals_definitions_Scope_ProfileVersion_Key",
                        columns: x => new { x.Scope, x.ProfileVersion, x.Key },
                        principalSchema: "presentation_content",
                        principalTable: "definitions",
                        principalColumns: new[] { "Scope", "ProfileVersion", "Key" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_proposals_variants_Scope_ProfileVersion_VariantReference",
                        columns: x => new { x.Scope, x.ProfileVersion, x.VariantReference },
                        principalSchema: "presentation_content",
                        principalTable: "variants",
                        principalColumns: new[] { "Scope", "ProfileVersion", "Reference" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "versions",
                schema: "presentation_content",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProposalReference = table.Column<Guid>(type: "uuid", nullable: false),
                    Scope = table.Column<string>(type: "text", nullable: false),
                    ProfileVersion = table.Column<string>(type: "text", nullable: false),
                    Key = table.Column<string>(type: "text", nullable: false),
                    VariantReference = table.Column<string>(type: "text", nullable: false),
                    Locale = table.Column<string>(type: "text", nullable: false),
                    Text = table.Column<string>(type: "text", nullable: false),
                    TextDigest = table.Column<string>(type: "text", nullable: false),
                    ContributorsJson = table.Column<string>(type: "text", nullable: false),
                    SourceEvidenceReference = table.Column<string>(type: "text", nullable: false),
                    ModelOrigin = table.Column<bool>(type: "boolean", nullable: false),
                    ValidationEventReference = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_versions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_versions_proposals_ProposalReference",
                        column: x => x.ProposalReference,
                        principalSchema: "presentation_content",
                        principalTable: "proposals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "publications",
                schema: "presentation_content",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    VersionReference = table.Column<Guid>(type: "uuid", nullable: false),
                    Scope = table.Column<string>(type: "text", nullable: false),
                    ProfileVersion = table.Column<string>(type: "text", nullable: false),
                    Key = table.Column<string>(type: "text", nullable: false),
                    VariantReference = table.Column<string>(type: "text", nullable: false),
                    From = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    IncludesFrom = table.Column<bool>(type: "boolean", nullable: false),
                    Until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    IncludesUntil = table.Column<bool>(type: "boolean", nullable: false),
                    SupersedesPublicationReference = table.Column<Guid>(type: "uuid", nullable: true),
                    PublisherReference = table.Column<string>(type: "text", nullable: false),
                    ApproverReference = table.Column<string>(type: "text", nullable: true),
                    ApprovalEvidenceReference = table.Column<string>(type: "text", nullable: true),
                    PublisherEvidenceReference = table.Column<string>(type: "text", nullable: false),
                    PublisherAuthorizationVersion = table.Column<string>(type: "text", nullable: false),
                    PublicationEventReference = table.Column<Guid>(type: "uuid", nullable: false),
                    Operation = table.Column<string>(type: "text", nullable: false),
                    ApprovalEventReference = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_publications", x => x.Id);
                    table.CheckConstraint("ck_content_publication_interval", "\"Until\" IS NULL OR \"Until\">\"From\" OR (\"Until\"=\"From\" AND \"IncludesFrom\" AND \"IncludesUntil\")");
                    table.ForeignKey(
                        name: "FK_publications_publications_SupersedesPublicationReference",
                        column: x => x.SupersedesPublicationReference,
                        principalSchema: "presentation_content",
                        principalTable: "publications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_publications_versions_VersionReference",
                        column: x => x.VersionReference,
                        principalSchema: "presentation_content",
                        principalTable: "versions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_proposals_Scope_ProfileVersion_Key",
                schema: "presentation_content",
                table: "proposals",
                columns: new[] { "Scope", "ProfileVersion", "Key" });

            migrationBuilder.CreateIndex(
                name: "IX_proposals_Scope_ProfileVersion_VariantReference",
                schema: "presentation_content",
                table: "proposals",
                columns: new[] { "Scope", "ProfileVersion", "VariantReference" });

            migrationBuilder.CreateIndex(
                name: "IX_publications_Scope_ProfileVersion_Key_VariantReference",
                schema: "presentation_content",
                table: "publications",
                columns: new[] { "Scope", "ProfileVersion", "Key", "VariantReference" });

            migrationBuilder.CreateIndex(
                name: "IX_publications_SupersedesPublicationReference",
                schema: "presentation_content",
                table: "publications",
                column: "SupersedesPublicationReference",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_publications_VersionReference",
                schema: "presentation_content",
                table: "publications",
                column: "VersionReference");

            migrationBuilder.CreateIndex(
                name: "IX_versions_ProposalReference",
                schema: "presentation_content",
                table: "versions",
                column: "ProposalReference");

            migrationBuilder.CreateIndex(
                name: "IX_versions_Scope_ProfileVersion_Key_VariantReference",
                schema: "presentation_content",
                table: "versions",
                columns: new[] { "Scope", "ProfileVersion", "Key", "VariantReference" });
            migrationBuilder.Sql(ContentIntegritySql.Install);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                LOCK TABLE presentation_content.profiles, presentation_content.definitions,
                  presentation_content.variants, presentation_content.proposals,
                  presentation_content.versions, presentation_content.publications IN ACCESS EXCLUSIVE MODE;
                DO $guard$
                BEGIN
                  IF EXISTS (SELECT 1 FROM presentation_content.profiles)
                    OR EXISTS (SELECT 1 FROM presentation_content.definitions)
                    OR EXISTS (SELECT 1 FROM presentation_content.variants)
                    OR EXISTS (SELECT 1 FROM presentation_content.proposals)
                    OR EXISTS (SELECT 1 FROM presentation_content.versions)
                    OR EXISTS (SELECT 1 FROM presentation_content.publications)
                  THEN
                    RAISE EXCEPTION 'Committed presentation content history requires a governed forward migration'
                      USING ERRCODE='23514';
                  END IF;
                END $guard$;
                """);
            migrationBuilder.DropTable(
                name: "publications",
                schema: "presentation_content");

            migrationBuilder.DropTable(
                name: "versions",
                schema: "presentation_content");

            migrationBuilder.DropTable(
                name: "proposals",
                schema: "presentation_content");

            migrationBuilder.DropTable(
                name: "definitions",
                schema: "presentation_content");

            migrationBuilder.DropTable(
                name: "variants",
                schema: "presentation_content");

            migrationBuilder.DropTable(
                name: "profiles",
                schema: "presentation_content");
            migrationBuilder.Sql("""
                DROP FUNCTION IF EXISTS presentation_content.guard_causal_effect();
                DROP FUNCTION IF EXISTS presentation_content.guard_proposal_transition();
                DROP FUNCTION IF EXISTS presentation_content.valid_publication(uuid);
                DROP FUNCTION IF EXISTS presentation_content.valid_version(uuid);
                DROP FUNCTION IF EXISTS presentation_content.has_effect(uuid,text,text,text,text,text,text);
                DROP FUNCTION IF EXISTS presentation_content.reject_history_mutation();
                """);
        }
    }
}
