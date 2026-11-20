using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tunner.PresentationContent.Migrations
{
    /// <inheritdoc />
    public partial class P2ContentIntegritySnapshotRead : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(ContentIntegritySnapshotReadSql.Upgrade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(ContentIntegritySnapshotReadSql.Restore);
        }
    }
}
