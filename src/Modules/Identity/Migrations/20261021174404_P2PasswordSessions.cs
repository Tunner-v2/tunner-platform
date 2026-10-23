using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tunner.Identity.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class P2PasswordSessions : Migration
    {
        private static readonly string[] BrowserOperationColumns = ["browser_digest", "idempotency_key"];
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "authentication_abuse",
                schema: "identity",
                columns: table => new
                {
                    partition = table.Column<string>(type: "text", nullable: false),
                    policy_version = table.Column<string>(type: "text", nullable: false),
                    window = table.Column<long>(type: "bigint", nullable: false),
                    count = table.Column<int>(type: "integer", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_authentication_abuse", x => new { x.partition, x.policy_version, x.window });
                });

            migrationBuilder.CreateTable(
                name: "tunner_sessions",
                schema: "identity",
                columns: table => new
                {
                    session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    key_digest = table.Column<string>(type: "text", nullable: false),
                    protected_key = table.Column<string>(type: "text", nullable: false),
                    protected_ticket = table.Column<string>(type: "text", nullable: false),
                    browser_digest = table.Column<string>(type: "text", nullable: false),
                    idempotency_key = table.Column<Guid>(type: "uuid", nullable: false),
                    identifier_digest = table.Column<string>(type: "text", nullable: false),
                    remember_me = table.Column<bool>(type: "boolean", nullable: false),
                    account_id = table.Column<string>(type: "text", nullable: false),
                    security_stamp_digest = table.Column<string>(type: "text", nullable: false),
                    scheme = table.Column<string>(type: "text", nullable: false),
                    origin = table.Column<string>(type: "text", nullable: false),
                    authentication_methods = table.Column<string>(type: "text", nullable: false),
                    assurance = table.Column<string>(type: "text", nullable: false),
                    authenticated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    idle_ticks = table.Column<long>(type: "bigint", nullable: false),
                    state = table.Column<string>(type: "text", nullable: false),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    policy_version = table.Column<string>(type: "text", nullable: false),
                    access_policy_version = table.Column<string>(type: "text", nullable: false),
                    access_evidence_reference = table.Column<string>(type: "text", nullable: false),
                    processing_reference = table.Column<string>(type: "text", nullable: false),
                    retention_reference = table.Column<string>(type: "text", nullable: false),
                    operation_id = table.Column<string>(type: "text", nullable: false),
                    correlation_id = table.Column<string>(type: "text", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tunner_sessions", x => x.session_id);
                    table.CheckConstraint("ck_session_bounds", "expires_at > authenticated_at AND last_seen_at >= authenticated_at AND idle_ticks > 0 AND version >= 1");
                    table.CheckConstraint("ck_session_key", "length(key_digest) = 64 AND length(browser_digest) = 64");
                    table.CheckConstraint("ck_session_state", "state IN ('ACTIVE','REVOKED','EXPIRED')");
                    table.ForeignKey(
                        name: "FK_tunner_sessions_accounts_account_id",
                        column: x => x.account_id,
                        principalSchema: "accounts",
                        principalTable: "accounts",
                        principalColumn: "account_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_tunner_sessions_account_id",
                schema: "identity",
                table: "tunner_sessions",
                column: "account_id");

            migrationBuilder.CreateIndex(
                name: "IX_tunner_sessions_browser_digest_idempotency_key",
                schema: "identity",
                table: "tunner_sessions",
                columns: BrowserOperationColumns,
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_tunner_sessions_key_digest",
                schema: "identity",
                table: "tunner_sessions",
                column: "key_digest",
                unique: true);
            migrationBuilder.Sql(AuthenticationMigrationSql.Guard);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(IdentityMigrationSql.GuardDowngrade);
            migrationBuilder.Sql(AuthenticationMigrationSql.GuardDowngrade);
            migrationBuilder.DropTable(
                name: "authentication_abuse",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "tunner_sessions",
                schema: "identity");
        }
    }
}
