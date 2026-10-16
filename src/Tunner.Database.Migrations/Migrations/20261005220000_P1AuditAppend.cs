using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Tunner.Database;

namespace Tunner.Database.Migrations.Migrations;

/// <summary>Forward-only audit append storage; owner retention disposition remains separately governed.</summary>
[DbContext(typeof(TunnerMigrationsDbContext))]
[Migration("20261005220000_P1AuditAppend")]
public sealed class P1AuditAppend : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(name: "audit");
        migrationBuilder.CreateTable(
            name: "events", schema: "audit",
            columns: table => new
            {
                event_id = table.Column<Guid>(type: "uuid", nullable: false),
                occurred_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                operation_id = table.Column<string>(type: "text", nullable: false),
                correlation_id = table.Column<string>(type: "text", nullable: false),
                data_classification = table.Column<string>(type: "text", nullable: false),
                retention_class_ref = table.Column<string>(type: "text", nullable: false),
                envelope = table.Column<string>(type: "text", nullable: false),
                integrity_format = table.Column<string>(type: "text", nullable: false),
                envelope_sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_events", x => x.event_id);
                table.CheckConstraint("ck_audit_events_integrity_format", "integrity_format = 'sha256-envelope-v1'");
                table.CheckConstraint("ck_audit_events_digest", "envelope_sha256 ~ '^[0-9a-f]{64}$'");
            });
        migrationBuilder.CreateIndex(name: "IX_audit_events_operation", schema: "audit", table: "events", column: "operation_id");
        migrationBuilder.CreateIndex(name: "IX_audit_events_correlation", schema: "audit", table: "events", column: "correlation_id");
        migrationBuilder.Sql("""
            CREATE FUNCTION audit.reject_event_mutation() RETURNS trigger
            LANGUAGE plpgsql AS $$
            BEGIN
                RAISE EXCEPTION 'Audit events are append-only' USING ERRCODE = '55000';
            END;
            $$;
            CREATE TRIGGER audit_events_immutable_rows
                BEFORE UPDATE OR DELETE ON audit.events
                FOR EACH ROW EXECUTE FUNCTION audit.reject_event_mutation();
            CREATE TRIGGER audit_events_immutable_truncate
                BEFORE TRUNCATE ON audit.events
                FOR EACH STATEMENT EXECUTE FUNCTION audit.reject_event_mutation();
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException("Audit history is forward-only; use a governed fix-forward migration.");
}
