using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tunner.Identity.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class P2RecoveryPasskeyGuardDispatch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(RecoveryPasskeyMigrationSql.DispatchLegacyCeremonyGuard);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(RecoveryPasskeyMigrationSql.BlockPopulatedDowngrade);
            migrationBuilder.Sql(RecoveryPasskeyMigrationSql.RestoreLegacyCeremonyGuard);
        }
    }
}
