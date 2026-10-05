using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Tunner.Database;

namespace Tunner.Database.Migrations.Migrations;

/// <summary>Forward-only P1 migration that introduces the platform-owned transactional outbox record.</summary>
[DbContext(typeof(TunnerMigrationsDbContext))]
[Migration("20261001230000_P1TransactionalOutbox")]
public sealed class P1TransactionalOutbox : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(name: "platform");

        migrationBuilder.CreateTable(
            name: "outbox_messages",
            schema: "platform",
            columns: table => new
            {
                event_id = table.Column<Guid>(type: "uuid", nullable: false),
                envelope_version = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                event_type = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                event_schema_version = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                occurred_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                published_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                source = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                subject = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                resource_version = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                operation_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                correlation_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                causation_event_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                data_classification = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                payload = table.Column<string>(type: "jsonb", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_outbox_messages", x => x.event_id);
            });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
        => throw new NotSupportedException("P1 transactional-outbox migration is forward-only; use a governed fix-forward migration.");
}