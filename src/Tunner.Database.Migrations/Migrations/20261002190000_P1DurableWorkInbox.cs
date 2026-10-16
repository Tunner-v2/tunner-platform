using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Tunner.Database;

namespace Tunner.Database.Migrations.Migrations;

/// <summary>Forward-only P1 migration for Platform-owned durable-work leasing and leased consumer inbox records.</summary>
[DbContext(typeof(TunnerMigrationsDbContext))]
[Migration("20261002190000_P1DurableWorkInbox")]
public sealed class P1DurableWorkInbox : Migration
{
    private static readonly string[] DurableWorkClaimIndexColumns = ["completed_at_utc", "retry_eligible_at_utc", "lease_expires_at_utc"];
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(name: "platform");
        migrationBuilder.CreateTable(
            name: "durable_work_items", schema: "platform",
            columns: table => new
            {
                work_id = table.Column<Guid>(type: "uuid", nullable: false),
                work_kind = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                payload = table.Column<string>(type: "jsonb", nullable: false),
                retry_eligible_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                lease_token = table.Column<Guid>(type: "uuid", nullable: true),
                lease_acquired_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                lease_expires_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                completed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table => table.PrimaryKey("PK_durable_work_items", x => x.work_id));
        migrationBuilder.CreateIndex(
            name: "IX_durable_work_items_claim", schema: "platform", table: "durable_work_items",
            columns: DurableWorkClaimIndexColumns);
        migrationBuilder.CreateTable(
            name: "consumer_inbox", schema: "platform",
            columns: table => new
            {
                consumer_key = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                message_id = table.Column<Guid>(type: "uuid", nullable: false),
                lease_token = table.Column<Guid>(type: "uuid", nullable: true),
                reserved_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                lease_expires_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                completed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table => table.PrimaryKey("PK_consumer_inbox", x => new { x.consumer_key, x.message_id }));
    }

    protected override void Down(MigrationBuilder migrationBuilder)
        => throw new NotSupportedException("P1 durable-work/inbox migration is forward-only; use a governed fix-forward migration.");
}