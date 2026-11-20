using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tunner.Identity.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class P2FederatedRegistration : Migration
    {
        private static readonly string[] RegistrationLinkOwnerColumns = ["AccountId", "FederationLinkId"];
        private static readonly string[] ExternalLinkOwnerColumns = ["account_id", "link_id"];
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_registration_operations_AccountId",
                schema: "identity",
                table: "registration_operations");

            migrationBuilder.DropCheckConstraint(
                name: "ck_federation_result",
                schema: "identity",
                table: "federation_attempts");

            migrationBuilder.DropCheckConstraint(
                name: "ck_federation_verified",
                schema: "identity",
                table: "federation_attempts");

            migrationBuilder.AddColumn<Guid>(
                name: "FederationAttemptId",
                schema: "identity",
                table: "registration_operations",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FederationContactMappingEvidenceReference",
                schema: "identity",
                table: "registration_operations",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "FederationContactVerifiedAt",
                schema: "identity",
                table: "registration_operations",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "FederationContactVersion",
                schema: "identity",
                table: "registration_operations",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "FederationLinkId",
                schema: "identity",
                table: "registration_operations",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FederationOrigin",
                schema: "identity",
                table: "registration_operations",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FederationProfileSnapshot",
                schema: "identity",
                table: "registration_operations",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FederationProvider",
                schema: "identity",
                table: "registration_operations",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FederationVerifiedContactDigest",
                schema: "identity",
                table: "registration_operations",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Mode",
                schema: "identity",
                table: "registration_operations",
                type: "text",
                nullable: false,
                defaultValue: "PASSWORD");

            migrationBuilder.AddColumn<string>(
                name: "protected_registration_evidence",
                schema: "identity",
                table: "federation_attempts",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "registration_browser_digest",
                schema: "identity",
                table: "federation_attempts",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "registration_browser_expires_at",
                schema: "identity",
                table: "federation_attempts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "registration_idempotency_key",
                schema: "identity",
                table: "federation_attempts",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "resolution",
                schema: "identity",
                table: "federation_attempts",
                type: "text",
                nullable: false,
                defaultValue: "UNRESOLVED");

            migrationBuilder.AddColumn<Guid>(
                name: "result_registration_operation_id",
                schema: "identity",
                table: "federation_attempts",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_registration_operations_AccountId_FederationLinkId",
                schema: "identity",
                table: "registration_operations",
                columns: RegistrationLinkOwnerColumns);

            migrationBuilder.CreateIndex(
                name: "IX_registration_operations_FederationAttemptId",
                schema: "identity",
                table: "registration_operations",
                column: "FederationAttemptId",
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_registration_mode",
                schema: "identity",
                table: "registration_operations",
                sql: "(\"Mode\"='PASSWORD' AND \"FederationAttemptId\" IS NULL AND \"FederationLinkId\" IS NULL AND \"FederationProvider\" IS NULL\r\n  AND \"FederationOrigin\" IS NULL AND \"FederationProfileSnapshot\" IS NULL AND \"FederationContactMappingEvidenceReference\" IS NULL\r\n  AND \"FederationVerifiedContactDigest\" IS NULL AND \"FederationContactVerifiedAt\" IS NULL AND \"FederationContactVersion\" IS NULL)\r\nOR (\"Mode\"='FEDERATED' AND \"FederationAttemptId\" IS NOT NULL AND \"FederationLinkId\" IS NOT NULL\r\n  AND \"AccountId\" IS NOT NULL AND \"ContactId\" IS NOT NULL AND \"ProtectedCredential\" IS NULL\r\n  AND \"FederationProvider\" IN ('Google','Microsoft') AND \"FederationOrigin\" IS NOT NULL\r\n  AND \"FederationProfileSnapshot\" IS NOT NULL AND jsonb_typeof(\"FederationProfileSnapshot\"::jsonb)='object'\r\n  AND \"FederationContactMappingEvidenceReference\" IS NOT NULL AND length(\"FederationContactMappingEvidenceReference\")>0\r\n  AND \"FederationVerifiedContactDigest\" IS NOT NULL AND length(\"FederationVerifiedContactDigest\")=64\r\n  AND \"FederationContactVerifiedAt\" IS NOT NULL AND \"FederationContactVerifiedAt\"<=\"CreatedAt\"\r\n  AND \"FederationContactVersion\" IS NOT NULL AND \"FederationContactVersion\">=1\r\n  AND \"Stage\" IN ('PERSONAL_INFORMATION','CONSENT_REQUIRED','COMPLETE'))");

            migrationBuilder.CreateIndex(
                name: "IX_federation_attempts_result_registration_operation_id",
                schema: "identity",
                table: "federation_attempts",
                column: "result_registration_operation_id");

            migrationBuilder.AddCheckConstraint(
                name: "ck_federation_registration",
                schema: "identity",
                table: "federation_attempts",
                sql: "resolution IN ('UNRESOLVED','LINKED_SIGN_IN','REGISTRATION_INTAKE')\r\nAND (resolution<>'LINKED_SIGN_IN' OR purpose='SignIn')\r\nAND ((registration_browser_digest IS NULL AND registration_idempotency_key IS NULL AND registration_browser_expires_at IS NULL)\r\n  OR (purpose='SignIn' AND provider='Google' AND registration_browser_digest IS NOT NULL AND length(registration_browser_digest)=64\r\n    AND registration_idempotency_key IS NOT NULL AND registration_browser_expires_at IS NOT NULL AND registration_browser_expires_at>created_at))\r\nAND (resolution<>'REGISTRATION_INTAKE' OR (purpose='SignIn' AND provider='Google' AND registration_browser_digest IS NOT NULL\r\n  AND link_id IS NULL AND link_version IS NULL AND link_qualification_evidence_reference IS NULL AND link_qualification_profile_version IS NULL))\r\nAND ((protected_registration_evidence IS NULL AND NOT (resolution='REGISTRATION_INTAKE' AND state='VERIFIED'))\r\n  OR (protected_registration_evidence IS NOT NULL AND length(protected_registration_evidence)>0 AND resolution='REGISTRATION_INTAKE' AND state='VERIFIED'))\r\nAND ((result_registration_operation_id IS NULL AND NOT (resolution='REGISTRATION_INTAKE' AND state='COMPLETED'))\r\n  OR (result_registration_operation_id IS NOT NULL AND resolution='REGISTRATION_INTAKE' AND state='COMPLETED'))");

            migrationBuilder.AddCheckConstraint(
                name: "ck_federation_result",
                schema: "identity",
                table: "federation_attempts",
                sql: "((state IN ('PREPARED','CHALLENGE_ADMITTED','VERIFIED') AND ended_at IS NULL AND result_expires_at IS NULL\n  AND result_session_id IS NULL AND pending_mfa_id IS NULL AND result_proof_id IS NULL AND error_code IS NULL)\nOR (state IN ('COMPLETED','REJECTED','EXPIRED') AND ended_at IS NOT NULL AND result_expires_at IS NOT NULL\n  AND ended_at>=created_at AND result_expires_at>ended_at AND ((state='COMPLETED' AND verified_at IS NOT NULL AND error_code IS NULL\n    AND ((purpose='SignIn' AND result_proof_id IS NULL AND ((result_session_id IS NOT NULL AND pending_mfa_id IS NULL) OR (result_session_id IS NULL AND pending_mfa_id IS NOT NULL)))\n      OR (purpose='Link' AND result_session_id IS NULL AND pending_mfa_id IS NULL AND result_proof_id IS NULL)\n      OR (purpose='OperationProof' AND result_session_id IS NULL AND pending_mfa_id IS NULL AND result_proof_id IS NOT NULL)))\n    OR (state IN ('REJECTED','EXPIRED') AND error_code IS NOT NULL AND result_session_id IS NULL AND pending_mfa_id IS NULL AND result_proof_id IS NULL)))) OR (purpose='SignIn' AND resolution='REGISTRATION_INTAKE' AND state='COMPLETED' AND verified_at IS NOT NULL AND error_code IS NULL\r\nAND result_registration_operation_id IS NOT NULL AND result_session_id IS NULL AND pending_mfa_id IS NULL AND result_proof_id IS NULL\r\nAND ended_at IS NOT NULL AND ended_at>=created_at AND result_expires_at IS NOT NULL AND result_expires_at>ended_at)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_federation_verified",
                schema: "identity",
                table: "federation_attempts",
                sql: "((verified_at IS NULL AND verification_expires_at IS NULL AND validated_issuer IS NULL AND link_id IS NULL AND link_version IS NULL\n  AND link_qualification_evidence_reference IS NULL AND link_qualification_profile_version IS NULL AND protected_link_subject IS NULL\n  AND state NOT IN ('VERIFIED','COMPLETED'))\nOR (verified_at IS NOT NULL AND verification_expires_at IS NOT NULL AND validated_issuer IS NOT NULL\n  AND verified_at>=created_at AND verified_at<expires_at AND verification_expires_at>verified_at AND verification_expires_at<=expires_at\n  AND length(validated_issuer)>0 AND ((purpose IN ('SignIn','OperationProof') AND link_id IS NOT NULL AND link_version IS NOT NULL AND link_version>=1\n    AND link_qualification_evidence_reference IS NOT NULL AND length(link_qualification_evidence_reference)>0\n    AND link_qualification_profile_version IS NOT NULL AND length(link_qualification_profile_version)>0 AND protected_link_subject IS NULL)\n  OR (purpose='Link' AND protected_link_subject IS NOT NULL AND length(protected_link_subject)>0\n    AND link_id IS NULL AND link_version IS NULL AND link_qualification_evidence_reference IS NULL AND link_qualification_profile_version IS NULL)))) OR (purpose='SignIn' AND provider='Google' AND resolution='REGISTRATION_INTAKE' AND state IN ('VERIFIED','COMPLETED','REJECTED','EXPIRED')\r\nAND verified_at IS NOT NULL AND verification_expires_at IS NOT NULL AND validated_issuer IS NOT NULL AND length(validated_issuer)>0\r\nAND verified_at>=created_at AND verified_at<expires_at AND verification_expires_at>verified_at AND verification_expires_at<=expires_at\r\nAND link_id IS NULL AND link_version IS NULL AND link_qualification_evidence_reference IS NULL AND link_qualification_profile_version IS NULL\r\nAND protected_link_subject IS NULL)");

            migrationBuilder.AddForeignKey(
                name: "FK_federation_attempts_registration_operations_result_registra~",
                schema: "identity",
                table: "federation_attempts",
                column: "result_registration_operation_id",
                principalSchema: "identity",
                principalTable: "registration_operations",
                principalColumn: "OperationId",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_registration_operations_external_identity_links_AccountId_F~",
                schema: "identity",
                table: "registration_operations",
                columns: RegistrationLinkOwnerColumns,
                principalSchema: "identity",
                principalTable: "external_identity_links",
                principalColumns: ExternalLinkOwnerColumns,
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_registration_operations_federation_attempts_FederationAttem~",
                schema: "identity",
                table: "registration_operations",
                column: "FederationAttemptId",
                principalSchema: "identity",
                principalTable: "federation_attempts",
                principalColumn: "attempt_id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.Sql(RegistrationFederationMigrationSql.Harden);
            migrationBuilder.Sql(RegistrationFederationMigrationSql.UpgradeAttemptGuard);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(IdentityMigrationSql.GuardDowngrade);
            migrationBuilder.Sql("DROP TRIGGER registration_federation_source_guard ON identity.registration_operations; DROP FUNCTION identity.guard_registration_federation_source();");
            migrationBuilder.Sql(FederationLinkManagementMigrationSql.UpgradeAttemptGuard);

            migrationBuilder.DropForeignKey(
                name: "FK_federation_attempts_registration_operations_result_registra~",
                schema: "identity",
                table: "federation_attempts");

            migrationBuilder.DropForeignKey(
                name: "FK_registration_operations_external_identity_links_AccountId_F~",
                schema: "identity",
                table: "registration_operations");

            migrationBuilder.DropForeignKey(
                name: "FK_registration_operations_federation_attempts_FederationAttem~",
                schema: "identity",
                table: "registration_operations");

            migrationBuilder.DropIndex(
                name: "IX_registration_operations_AccountId_FederationLinkId",
                schema: "identity",
                table: "registration_operations");

            migrationBuilder.DropIndex(
                name: "IX_registration_operations_FederationAttemptId",
                schema: "identity",
                table: "registration_operations");

            migrationBuilder.DropCheckConstraint(
                name: "ck_registration_mode",
                schema: "identity",
                table: "registration_operations");

            migrationBuilder.DropIndex(
                name: "IX_federation_attempts_result_registration_operation_id",
                schema: "identity",
                table: "federation_attempts");

            migrationBuilder.DropCheckConstraint(
                name: "ck_federation_registration",
                schema: "identity",
                table: "federation_attempts");

            migrationBuilder.DropCheckConstraint(
                name: "ck_federation_result",
                schema: "identity",
                table: "federation_attempts");

            migrationBuilder.DropCheckConstraint(
                name: "ck_federation_verified",
                schema: "identity",
                table: "federation_attempts");

            migrationBuilder.DropColumn(
                name: "FederationAttemptId",
                schema: "identity",
                table: "registration_operations");

            migrationBuilder.DropColumn(
                name: "FederationContactMappingEvidenceReference",
                schema: "identity",
                table: "registration_operations");

            migrationBuilder.DropColumn(
                name: "FederationContactVerifiedAt",
                schema: "identity",
                table: "registration_operations");

            migrationBuilder.DropColumn(
                name: "FederationContactVersion",
                schema: "identity",
                table: "registration_operations");

            migrationBuilder.DropColumn(
                name: "FederationLinkId",
                schema: "identity",
                table: "registration_operations");

            migrationBuilder.DropColumn(
                name: "FederationOrigin",
                schema: "identity",
                table: "registration_operations");

            migrationBuilder.DropColumn(
                name: "FederationProfileSnapshot",
                schema: "identity",
                table: "registration_operations");

            migrationBuilder.DropColumn(
                name: "FederationProvider",
                schema: "identity",
                table: "registration_operations");

            migrationBuilder.DropColumn(
                name: "FederationVerifiedContactDigest",
                schema: "identity",
                table: "registration_operations");

            migrationBuilder.DropColumn(
                name: "Mode",
                schema: "identity",
                table: "registration_operations");

            migrationBuilder.DropColumn(
                name: "protected_registration_evidence",
                schema: "identity",
                table: "federation_attempts");

            migrationBuilder.DropColumn(
                name: "registration_browser_digest",
                schema: "identity",
                table: "federation_attempts");

            migrationBuilder.DropColumn(
                name: "registration_browser_expires_at",
                schema: "identity",
                table: "federation_attempts");

            migrationBuilder.DropColumn(
                name: "registration_idempotency_key",
                schema: "identity",
                table: "federation_attempts");

            migrationBuilder.DropColumn(
                name: "resolution",
                schema: "identity",
                table: "federation_attempts");

            migrationBuilder.DropColumn(
                name: "result_registration_operation_id",
                schema: "identity",
                table: "federation_attempts");

            migrationBuilder.CreateIndex(
                name: "IX_registration_operations_AccountId",
                schema: "identity",
                table: "registration_operations",
                column: "AccountId");

            migrationBuilder.AddCheckConstraint(
                name: "ck_federation_result",
                schema: "identity",
                table: "federation_attempts",
                sql: "(state IN ('PREPARED','CHALLENGE_ADMITTED','VERIFIED') AND ended_at IS NULL AND result_expires_at IS NULL\n  AND result_session_id IS NULL AND pending_mfa_id IS NULL AND result_proof_id IS NULL AND error_code IS NULL)\nOR (state IN ('COMPLETED','REJECTED','EXPIRED') AND ended_at IS NOT NULL AND result_expires_at IS NOT NULL\n  AND ended_at>=created_at AND result_expires_at>ended_at AND ((state='COMPLETED' AND verified_at IS NOT NULL AND error_code IS NULL\n    AND ((purpose='SignIn' AND result_proof_id IS NULL AND ((result_session_id IS NOT NULL AND pending_mfa_id IS NULL) OR (result_session_id IS NULL AND pending_mfa_id IS NOT NULL)))\n      OR (purpose='Link' AND result_session_id IS NULL AND pending_mfa_id IS NULL AND result_proof_id IS NULL)\n      OR (purpose='OperationProof' AND result_session_id IS NULL AND pending_mfa_id IS NULL AND result_proof_id IS NOT NULL)))\n    OR (state IN ('REJECTED','EXPIRED') AND error_code IS NOT NULL AND result_session_id IS NULL AND pending_mfa_id IS NULL AND result_proof_id IS NULL)))");

            migrationBuilder.AddCheckConstraint(
                name: "ck_federation_verified",
                schema: "identity",
                table: "federation_attempts",
                sql: "(verified_at IS NULL AND verification_expires_at IS NULL AND validated_issuer IS NULL AND link_id IS NULL AND link_version IS NULL\n  AND link_qualification_evidence_reference IS NULL AND link_qualification_profile_version IS NULL AND protected_link_subject IS NULL\n  AND state NOT IN ('VERIFIED','COMPLETED'))\nOR (verified_at IS NOT NULL AND verification_expires_at IS NOT NULL AND validated_issuer IS NOT NULL\n  AND verified_at>=created_at AND verified_at<expires_at AND verification_expires_at>verified_at AND verification_expires_at<=expires_at\n  AND length(validated_issuer)>0 AND ((purpose IN ('SignIn','OperationProof') AND link_id IS NOT NULL AND link_version IS NOT NULL AND link_version>=1\n    AND link_qualification_evidence_reference IS NOT NULL AND length(link_qualification_evidence_reference)>0\n    AND link_qualification_profile_version IS NOT NULL AND length(link_qualification_profile_version)>0 AND protected_link_subject IS NULL)\n  OR (purpose='Link' AND protected_link_subject IS NOT NULL AND length(protected_link_subject)>0\n    AND link_id IS NULL AND link_version IS NULL AND link_qualification_evidence_reference IS NULL AND link_qualification_profile_version IS NULL)))");
        }
    }
}
