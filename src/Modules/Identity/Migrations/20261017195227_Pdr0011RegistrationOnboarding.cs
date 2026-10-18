using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tunner.Identity.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Pdr0011RegistrationOnboarding : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "AgePolicyVersion",
                schema: "identity",
                table: "registration_operations",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "AgeEvidenceReference",
                schema: "identity",
                table: "registration_operations",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "AgeEvaluatedAt",
                schema: "identity",
                table: "registration_operations",
                type: "timestamp with time zone",
                nullable: true,
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone");

            migrationBuilder.AddColumn<string>(
                name: "Stage",
                schema: "identity",
                table: "registration_operations",
                type: "text",
                nullable: false,
                defaultValue: "LEGACY");

            migrationBuilder.AddCheckConstraint(
                name: "ck_registration_stage",
                schema: "identity",
                table: "registration_operations",
                sql: "\"Stage\" IN ('LEGACY','VERIFY_CONTACT','PERSONAL_INFORMATION','CONSENT_REQUIRED','COMPLETE')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(IdentityMigrationSql.GuardDowngrade);
            migrationBuilder.DropCheckConstraint(
                name: "ck_registration_stage",
                schema: "identity",
                table: "registration_operations");

            migrationBuilder.DropColumn(
                name: "Stage",
                schema: "identity",
                table: "registration_operations");

            migrationBuilder.AlterColumn<string>(
                name: "AgePolicyVersion",
                schema: "identity",
                table: "registration_operations",
                type: "text",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "AgeEvidenceReference",
                schema: "identity",
                table: "registration_operations",
                type: "text",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "AgeEvaluatedAt",
                schema: "identity",
                table: "registration_operations",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)),
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone",
                oldNullable: true);
        }
    }
}
