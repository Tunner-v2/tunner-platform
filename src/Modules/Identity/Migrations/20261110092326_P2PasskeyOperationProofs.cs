using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tunner.Identity.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class P2PasskeyOperationProofs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_passkey_state",
                schema: "identity",
                table: "passkey_credentials");

            migrationBuilder.DropCheckConstraint(
                name: "ck_passkey_ceremony_state",
                schema: "identity",
                table: "passkey_ceremonies");

            migrationBuilder.AddColumn<string>(
                name: "passkey_assurance_key",
                schema: "identity",
                table: "step_up_proofs",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "passkey_credential_reference",
                schema: "identity",
                table: "step_up_proofs",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "passkey_policy_version",
                schema: "identity",
                table: "step_up_proofs",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "usage_version",
                schema: "identity",
                table: "passkey_credentials",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<string>(
                name: "bound_context",
                schema: "identity",
                table: "passkey_ceremonies",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "bound_context_digest",
                schema: "identity",
                table: "passkey_ceremonies",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "bound_correlation_id",
                schema: "identity",
                table: "passkey_ceremonies",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "bound_expected_version",
                schema: "identity",
                table: "passkey_ceremonies",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "bound_operation",
                schema: "identity",
                table: "passkey_ceremonies",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "bound_operation_id",
                schema: "identity",
                table: "passkey_ceremonies",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "bound_resource_id",
                schema: "identity",
                table: "passkey_ceremonies",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "step_up_request_id",
                schema: "identity",
                table: "passkey_ceremonies",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_step_up_proofs_passkey_credential_reference",
                schema: "identity",
                table: "step_up_proofs",
                column: "passkey_credential_reference");

            migrationBuilder.AddCheckConstraint(
                name: "ck_step_up_method",
                schema: "identity",
                table: "step_up_proofs",
                sql: "(authentication_method='pwd' AND passkey_credential_reference IS NULL AND passkey_policy_version IS NULL AND passkey_assurance_key IS NULL) OR (authentication_method LIKE 'passkey:%' AND passkey_credential_reference IS NOT NULL AND passkey_policy_version IS NOT NULL AND passkey_assurance_key IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_passkey_state",
                schema: "identity",
                table: "passkey_credentials",
                sql: "state IN ('ACTIVE','REVOKED') AND version >= 1 AND usage_version >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_passkey_ceremony_state",
                schema: "identity",
                table: "passkey_ceremonies",
                sql: "state IN ('PENDING','AUTHORIZED','COMPLETED','REJECTED','EXPIRED') AND kind IN ('ENROLL','SIGN_IN','REVOKE','STEP_UP')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_passkey_proof_binding",
                schema: "identity",
                table: "passkey_ceremonies",
                sql: "kind<>'STEP_UP' OR (step_up_request_id IS NOT NULL AND account_id IS NOT NULL AND session_id IS NOT NULL AND bound_operation_id IS NOT NULL AND bound_correlation_id IS NOT NULL AND bound_operation IS NOT NULL AND bound_context IS NOT NULL AND bound_resource_id IS NOT NULL AND bound_expected_version IS NOT NULL AND bound_expected_version >= 0 AND bound_context_digest IS NOT NULL)");

            migrationBuilder.AddForeignKey(
                name: "FK_step_up_proofs_passkey_credentials_passkey_credential_refer~",
                schema: "identity",
                table: "step_up_proofs",
                column: "passkey_credential_reference",
                principalSchema: "identity",
                principalTable: "passkey_credentials",
                principalColumn: "credential_reference",
                onDelete: ReferentialAction.Restrict);
            migrationBuilder.Sql(PasskeyOperationProofMigrationSql.Guards);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(IdentityMigrationSql.GuardDowngrade);
            migrationBuilder.Sql(PasskeyOperationProofMigrationSql.RestorePrimaryGuards);
            migrationBuilder.DropForeignKey(
                name: "FK_step_up_proofs_passkey_credentials_passkey_credential_refer~",
                schema: "identity",
                table: "step_up_proofs");

            migrationBuilder.DropIndex(
                name: "IX_step_up_proofs_passkey_credential_reference",
                schema: "identity",
                table: "step_up_proofs");

            migrationBuilder.DropCheckConstraint(
                name: "ck_step_up_method",
                schema: "identity",
                table: "step_up_proofs");

            migrationBuilder.DropCheckConstraint(
                name: "ck_passkey_state",
                schema: "identity",
                table: "passkey_credentials");

            migrationBuilder.DropCheckConstraint(
                name: "ck_passkey_ceremony_state",
                schema: "identity",
                table: "passkey_ceremonies");

            migrationBuilder.DropCheckConstraint(
                name: "ck_passkey_proof_binding",
                schema: "identity",
                table: "passkey_ceremonies");

            migrationBuilder.DropColumn(
                name: "passkey_assurance_key",
                schema: "identity",
                table: "step_up_proofs");

            migrationBuilder.DropColumn(
                name: "passkey_credential_reference",
                schema: "identity",
                table: "step_up_proofs");

            migrationBuilder.DropColumn(
                name: "passkey_policy_version",
                schema: "identity",
                table: "step_up_proofs");

            migrationBuilder.DropColumn(
                name: "usage_version",
                schema: "identity",
                table: "passkey_credentials");

            migrationBuilder.DropColumn(
                name: "bound_context",
                schema: "identity",
                table: "passkey_ceremonies");

            migrationBuilder.DropColumn(
                name: "bound_context_digest",
                schema: "identity",
                table: "passkey_ceremonies");

            migrationBuilder.DropColumn(
                name: "bound_correlation_id",
                schema: "identity",
                table: "passkey_ceremonies");

            migrationBuilder.DropColumn(
                name: "bound_expected_version",
                schema: "identity",
                table: "passkey_ceremonies");

            migrationBuilder.DropColumn(
                name: "bound_operation",
                schema: "identity",
                table: "passkey_ceremonies");

            migrationBuilder.DropColumn(
                name: "bound_operation_id",
                schema: "identity",
                table: "passkey_ceremonies");

            migrationBuilder.DropColumn(
                name: "bound_resource_id",
                schema: "identity",
                table: "passkey_ceremonies");

            migrationBuilder.DropColumn(
                name: "step_up_request_id",
                schema: "identity",
                table: "passkey_ceremonies");

            migrationBuilder.AddCheckConstraint(
                name: "ck_passkey_state",
                schema: "identity",
                table: "passkey_credentials",
                sql: "state IN ('ACTIVE','REVOKED') AND version >= 1");

            migrationBuilder.AddCheckConstraint(
                name: "ck_passkey_ceremony_state",
                schema: "identity",
                table: "passkey_ceremonies",
                sql: "state IN ('PENDING','AUTHORIZED','COMPLETED','REJECTED','EXPIRED') AND kind IN ('ENROLL','SIGN_IN','REVOKE')");
        }
    }
}
