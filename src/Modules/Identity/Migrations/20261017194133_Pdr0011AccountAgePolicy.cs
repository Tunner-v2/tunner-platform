using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tunner.Identity.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Pdr0011AccountAgePolicy : Migration
    {
        private static readonly string[] ProfileIdempotencyColumns = ["OnboardingOperationId", "IdempotencyKey"];
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_active_age",
                schema: "accounts",
                table: "accounts");

            migrationBuilder.CreateTable(
                name: "personal_information",
                schema: "accounts",
                columns: table => new
                {
                    AccountId = table.Column<string>(type: "text", nullable: false),
                    ProtectedData = table.Column<string>(type: "text", nullable: true),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    RecordedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    PurgeAfter = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ProcessingReference = table.Column<string>(type: "text", nullable: false),
                    RetentionReference = table.Column<string>(type: "text", nullable: false),
                    EvaluationJson = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_personal_information", x => x.AccountId);
                    table.CheckConstraint("ck_personal_processing", "length(\"ProcessingReference\") > 0 AND length(\"RetentionReference\") > 0");
                    table.CheckConstraint("ck_personal_retention", "\"PurgeAfter\" > \"RecordedAt\"");
                    table.CheckConstraint("ck_personal_version", "\"Version\" >= 1");
                    table.ForeignKey(
                        name: "FK_personal_information_accounts_AccountId",
                        column: x => x.AccountId,
                        principalSchema: "accounts",
                        principalTable: "accounts",
                        principalColumn: "account_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "personal_information_versions",
                schema: "accounts",
                columns: table => new
                {
                    AccountId = table.Column<string>(type: "text", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    IdempotencyKey = table.Column<Guid>(type: "uuid", nullable: false),
                    OnboardingOperationId = table.Column<Guid>(type: "uuid", nullable: false),
                    RecordedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EvaluationJson = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_personal_information_versions", x => new { x.AccountId, x.Version });
                    table.ForeignKey(
                        name: "FK_personal_information_versions_accounts_AccountId",
                        column: x => x.AccountId,
                        principalSchema: "accounts",
                        principalTable: "accounts",
                        principalColumn: "account_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_personal_information_PurgeAfter",
                schema: "accounts",
                table: "personal_information",
                column: "PurgeAfter");

            migrationBuilder.CreateIndex(
                name: "IX_personal_information_versions_OnboardingOperationId_Idempot~",
                schema: "accounts",
                table: "personal_information_versions",
                columns: ProfileIdempotencyColumns,
                unique: true);
            migrationBuilder.Sql("CREATE TRIGGER immutable_personal_versions BEFORE UPDATE OR DELETE ON accounts.personal_information_versions FOR EACH ROW EXECUTE FUNCTION accounts.reject_identity_history_mutation(); CREATE TRIGGER immutable_personal_versions_truncate BEFORE TRUNCATE ON accounts.personal_information_versions FOR EACH STATEMENT EXECUTE FUNCTION accounts.reject_identity_history_mutation();");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(IdentityMigrationSql.GuardDowngrade);
            migrationBuilder.DropTable(
                name: "personal_information",
                schema: "accounts");

            migrationBuilder.DropTable(
                name: "personal_information_versions",
                schema: "accounts");

            migrationBuilder.AddCheckConstraint(
                name: "ck_active_age",
                schema: "accounts",
                table: "accounts",
                sql: "state <> 'ACTIVE' OR (age_eligibility_status IS NOT NULL AND age_eligibility_status = 'ELIGIBLE')");
        }
    }
}
