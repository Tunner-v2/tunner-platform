using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tunner.Identity.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class P2Registration : Migration
    {
        private static readonly string[] RegistrationIdempotencyColumns = ["BrowserDigest", "IdempotencyKey"];
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "registration_abuse_buckets",
                schema: "identity",
                columns: table => new
                {
                    Partition = table.Column<string>(type: "text", nullable: false),
                    Kind = table.Column<string>(type: "text", nullable: false),
                    Window = table.Column<long>(type: "bigint", nullable: false),
                    Count = table.Column<int>(type: "integer", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_registration_abuse_buckets", x => new { x.Partition, x.Kind, x.Window });
                });

            migrationBuilder.CreateTable(
                name: "registration_operations",
                schema: "identity",
                columns: table => new
                {
                    OperationId = table.Column<Guid>(type: "uuid", nullable: false),
                    BrowserDigest = table.Column<string>(type: "text", nullable: false),
                    IdempotencyKey = table.Column<Guid>(type: "uuid", nullable: false),
                    CorrelationId = table.Column<string>(type: "text", nullable: false),
                    AccountId = table.Column<string>(type: "text", nullable: true),
                    ContactId = table.Column<Guid>(type: "uuid", nullable: true),
                    NormalizedEmail = table.Column<string>(type: "text", nullable: false),
                    ProtectedAddress = table.Column<string>(type: "text", nullable: true),
                    ProtectedCredential = table.Column<string>(type: "text", nullable: true),
                    ProtectedCode = table.Column<string>(type: "text", nullable: true),
                    ProtectedDigest = table.Column<string>(type: "text", nullable: true),
                    PolicyVersion = table.Column<string>(type: "text", nullable: false),
                    AgePolicyVersion = table.Column<string>(type: "text", nullable: false),
                    TermsVersion = table.Column<string>(type: "text", nullable: false),
                    PrivacyVersion = table.Column<string>(type: "text", nullable: false),
                    ProcessingReference = table.Column<string>(type: "text", nullable: false),
                    RetentionReference = table.Column<string>(type: "text", nullable: false),
                    AgeEvaluatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    AgeEvidenceReference = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ResendAfter = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    PurgeAfter = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    Resends = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    ErrorCode = table.Column<string>(type: "text", nullable: true),
                    DeliveryId = table.Column<Guid>(type: "uuid", nullable: true),
                    TemplateVersion = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_registration_operations", x => x.OperationId);
                    table.CheckConstraint("ck_registration_status", "\"Status\" IN ('REQUIRES_ACTION','SUCCEEDED','FAILED')");
                    table.CheckConstraint("ck_registration_version", "\"Version\" >= 1 AND \"Attempts\" >= 0 AND \"Resends\" >= 0");
                    table.ForeignKey(
                        name: "FK_registration_operations_accounts_AccountId",
                        column: x => x.AccountId,
                        principalSchema: "accounts",
                        principalTable: "accounts",
                        principalColumn: "account_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "registration_mutations",
                schema: "identity",
                columns: table => new
                {
                    OperationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "text", nullable: false),
                    IdempotencyKey = table.Column<Guid>(type: "uuid", nullable: false),
                    ResultJson = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_registration_mutations", x => new { x.OperationId, x.Kind, x.IdempotencyKey });
                    table.ForeignKey(
                        name: "FK_registration_mutations_registration_operations_OperationId",
                        column: x => x.OperationId,
                        principalSchema: "identity",
                        principalTable: "registration_operations",
                        principalColumn: "OperationId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_registration_operations_AccountId",
                schema: "identity",
                table: "registration_operations",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_registration_operations_BrowserDigest_IdempotencyKey",
                schema: "identity",
                table: "registration_operations",
                columns: RegistrationIdempotencyColumns,
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_registration_operations_PurgeAfter",
                schema: "identity",
                table: "registration_operations",
                column: "PurgeAfter");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DO $$ BEGIN IF EXISTS (SELECT 1 FROM identity.registration_operations) THEN RAISE EXCEPTION 'Registration evidence exists; use a governed forward migration' USING ERRCODE='23514'; END IF; END $$;");
            migrationBuilder.DropTable(
                name: "registration_abuse_buckets",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "registration_mutations",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "registration_operations",
                schema: "identity");
        }
    }
}
