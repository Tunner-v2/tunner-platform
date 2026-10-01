using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Tunner.Database;

namespace Tunner.Database.Migrations.Migrations;

/// <summary>Empty, forward-only P0 rehearsal migration; it introduces no Product or domain schema.</summary>
[DbContext(typeof(TunnerMigrationsDbContext))]
[Migration("20260930173000_P0MigrationFoundation")]
public sealed class P0MigrationFoundation : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
    }

    protected override void Down(MigrationBuilder migrationBuilder)
        => throw new NotSupportedException("P0 migration rehearsal is forward-only; use a governed fix-forward migration.");
}