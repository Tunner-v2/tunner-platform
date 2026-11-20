using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tunner.PresentationContent.Migrations
{
    /// <inheritdoc />
    public partial class P2ContentChangeReason : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ChangeReason",
                schema: "presentation_content",
                table: "versions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ChangeReason",
                schema: "presentation_content",
                table: "proposals",
                type: "text",
                nullable: true);
            migrationBuilder.Sql("""
                CREATE FUNCTION presentation_content.guard_change_reason() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                  IF TG_TABLE_NAME='proposals' THEN
                    IF NEW."LastOperation"<>'UpdateDraft' AND NEW."ChangeReason" IS DISTINCT FROM OLD."ChangeReason"
                    THEN RAISE EXCEPTION 'A reason change requires a new content draft revision' USING ERRCODE='23514'; END IF;
                  ELSE
                    IF NOT EXISTS (SELECT 1 FROM presentation_content.proposals p
                      WHERE p."Id"=NEW."ProposalReference" AND p."ChangeReason" IS NOT DISTINCT FROM NEW."ChangeReason")
                    THEN RAISE EXCEPTION 'Content version reason must match its validated draft' USING ERRCODE='23514'; END IF;
                  END IF;
                  RETURN NEW;
                END $$;
                CREATE TRIGGER content_change_reason_transition BEFORE UPDATE ON presentation_content.proposals
                  FOR EACH ROW EXECUTE FUNCTION presentation_content.guard_change_reason();
                CREATE CONSTRAINT TRIGGER content_version_reason AFTER INSERT ON presentation_content.versions
                  DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION presentation_content.guard_change_reason();
                """);
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
                  THEN RAISE EXCEPTION 'Committed content reasons require a governed forward migration' USING ERRCODE='23514'; END IF;
                END $guard$;
                DROP TRIGGER content_change_reason_transition ON presentation_content.proposals;
                DROP TRIGGER content_version_reason ON presentation_content.versions;
                DROP FUNCTION presentation_content.guard_change_reason();
                """);
            migrationBuilder.DropColumn(
                name: "ChangeReason",
                schema: "presentation_content",
                table: "versions");

            migrationBuilder.DropColumn(
                name: "ChangeReason",
                schema: "presentation_content",
                table: "proposals");
        }
    }
}
