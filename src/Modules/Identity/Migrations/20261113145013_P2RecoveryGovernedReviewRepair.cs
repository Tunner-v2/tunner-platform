using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tunner.Identity.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class P2RecoveryGovernedReviewRepair : Migration
    {
        private static readonly string[] GovernedDispositionColumns = ["governed_disposition_reference", "governed_disposition_version"];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_recovery_result_shape",
                schema: "identity",
                table: "recovery_results");

            migrationBuilder.DropCheckConstraint(
                name: "ck_recovery_case_state",
                schema: "identity",
                table: "recovery_cases");

            migrationBuilder.DropCheckConstraint(
                name: "ck_recovery_case_versions",
                schema: "identity",
                table: "recovery_cases");

            migrationBuilder.AddColumn<string>(
                name: "governed_disposition_reference",
                schema: "identity",
                table: "recovery_results",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "governed_disposition_snapshot",
                schema: "identity",
                table: "recovery_results",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "governed_disposition_version",
                schema: "identity",
                table: "recovery_results",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "governed_contacts_digest",
                schema: "identity",
                table: "recovery_cases",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_recovery_results_governed_disposition_reference_governed_di~",
                schema: "identity",
                table: "recovery_results",
                columns: GovernedDispositionColumns,
                unique: true,
                filter: "proof_kind='GOVERNED_REVIEW'");

            migrationBuilder.AddCheckConstraint(
                name: "ck_recovery_result_shape",
                schema: "identity",
                table: "recovery_results",
                sql: "(((((proof_kind='CODE' AND set_reference IS NOT NULL AND set_lifecycle_version IS NOT NULL AND passkey_credential_reference IS NULL AND passkey_version IS NULL AND passkey_usage_version IS NULL AND passkey_ceremony_id IS NULL AND passkey_method IS NULL AND passkey_assurance IS NULL AND native_before_digest IS NULL AND native_after_digest IS NULL AND (set_lifecycle_version>=1 AND length(context_digest)=64 AND length(policy_version)>0 AND length(processing_reference)>0 AND length(retention_reference)>0\nAND jsonb_typeof(policy_snapshot::jsonb)='object' AND jsonb_typeof(hierarchy_snapshot::jsonb)='object' AND jsonb_typeof(unresolved_effects_json::jsonb)='array'\nAND ((outcome='CONTAINED_PENDING_RESTORATION' AND target_session_id IS NOT NULL AND prior_session_version IS NOT NULL AND prior_session_version>=1\n  AND ended_session_version IS NOT NULL AND ended_session_version=prior_session_version+1 AND session_outbox_event_id IS NOT NULL\n  AND repair_outbox_event_id IS NULL AND prior_password_digest IS NULL AND repaired_password_digest IS NULL\n  AND prior_security_stamp_digest IS NULL AND repaired_security_stamp_digest IS NULL AND session_effect IS NULL AND jsonb_array_length(unresolved_effects_json::jsonb)>0)\n  OR (outcome='PASSWORD_REPAIRED_NO_SESSION' AND target_session_id IS NULL AND prior_session_version IS NULL AND ended_session_version IS NULL\n    AND session_outbox_event_id IS NULL AND repair_outbox_event_id IS NOT NULL AND prior_password_digest IS NOT NULL AND length(prior_password_digest)=64\n    AND repaired_password_digest IS NOT NULL AND length(repaired_password_digest)=64 AND prior_security_stamp_digest IS NOT NULL AND length(prior_security_stamp_digest)=64\n    AND repaired_security_stamp_digest IS NOT NULL AND length(repaired_security_stamp_digest)=64 AND repaired_security_stamp_digest<>prior_security_stamp_digest\n    AND session_effect='InvalidateExistingTunnerSessions')))) OR (proof_kind='PASSKEY' AND set_reference IS NULL AND set_lifecycle_version IS NULL AND passkey_credential_reference IS NOT NULL\r\nAND passkey_version IS NOT NULL AND passkey_version>=1 AND passkey_usage_version IS NOT NULL AND passkey_usage_version>=1\r\nAND passkey_ceremony_id IS NOT NULL AND passkey_method IS NOT NULL AND length(passkey_method)>0\r\nAND passkey_assurance IS NOT NULL AND length(passkey_assurance)>0 AND lower(passkey_assurance)<>'aal3'\r\nAND native_before_digest IS NOT NULL AND length(native_before_digest)=64 AND native_after_digest IS NOT NULL AND length(native_after_digest)=64\r\nAND outcome='PASSWORD_REPAIRED_NO_SESSION' AND target_session_id IS NULL AND prior_session_version IS NULL AND ended_session_version IS NULL\r\nAND session_outbox_event_id IS NULL AND repair_outbox_event_id IS NOT NULL AND prior_password_digest IS NOT NULL AND length(prior_password_digest)=64\r\nAND repaired_password_digest IS NOT NULL AND length(repaired_password_digest)=64 AND prior_security_stamp_digest IS NOT NULL AND length(prior_security_stamp_digest)=64\r\nAND repaired_security_stamp_digest IS NOT NULL AND length(repaired_security_stamp_digest)=64 AND repaired_security_stamp_digest<>prior_security_stamp_digest\r\nAND session_effect IS NOT NULL AND session_effect='InvalidateExistingTunnerSessions' AND length(context_digest)=64 AND length(policy_version)>0\r\nAND length(processing_reference)>0 AND length(retention_reference)>0 AND jsonb_typeof(policy_snapshot::jsonb)='object'\r\nAND jsonb_typeof(hierarchy_snapshot::jsonb)='object' AND jsonb_typeof(unresolved_effects_json::jsonb)='array')) AND contact_reference IS NULL AND contact_version IS NULL AND contact_challenge_id IS NULL AND contact_destination_digest IS NULL AND contact_risk_snapshot IS NULL AND contact_notification_delivery_id IS NULL) OR (proof_kind='CONTACT_RISK' AND outcome='PASSWORD_REPAIRED_NO_SESSION' AND set_reference IS NULL AND set_lifecycle_version IS NULL\r\nAND target_session_id IS NULL AND prior_session_version IS NULL AND ended_session_version IS NULL AND session_outbox_event_id IS NULL\r\nAND passkey_credential_reference IS NULL AND passkey_version IS NULL AND passkey_usage_version IS NULL AND passkey_ceremony_id IS NULL\r\nAND passkey_method IS NULL AND passkey_assurance IS NULL AND native_before_digest IS NULL AND native_after_digest IS NULL\r\nAND contact_reference IS NOT NULL AND contact_version IS NOT NULL AND contact_version>=1 AND contact_challenge_id IS NOT NULL\r\nAND contact_destination_digest IS NOT NULL AND length(contact_destination_digest)=64 AND contact_risk_snapshot IS NOT NULL AND jsonb_typeof(contact_risk_snapshot::jsonb)='object'\r\nAND contact_notification_delivery_id IS NOT NULL AND repair_outbox_event_id IS NOT NULL\r\nAND prior_password_digest IS NOT NULL AND length(prior_password_digest)=64 AND repaired_password_digest IS NOT NULL AND length(repaired_password_digest)=64\r\nAND prior_security_stamp_digest IS NOT NULL AND length(prior_security_stamp_digest)=64 AND repaired_security_stamp_digest IS NOT NULL AND length(repaired_security_stamp_digest)=64\r\nAND repaired_security_stamp_digest<>prior_security_stamp_digest AND session_effect IS NOT NULL AND session_effect='InvalidateExistingTunnerSessions'\r\nAND length(context_digest)=64 AND length(policy_version)>0 AND length(processing_reference)>0 AND length(retention_reference)>0\r\nAND jsonb_typeof(policy_snapshot::jsonb)='object' AND jsonb_typeof(hierarchy_snapshot::jsonb)='object' AND jsonb_typeof(unresolved_effects_json::jsonb)='array')) AND governed_disposition_reference IS NULL AND governed_disposition_version IS NULL AND governed_disposition_snapshot IS NULL) OR (proof_kind='GOVERNED_REVIEW' AND outcome='PASSWORD_REPAIRED_NO_SESSION' AND set_reference IS NULL AND set_lifecycle_version IS NULL\r\nAND target_session_id IS NULL AND prior_session_version IS NULL AND ended_session_version IS NULL AND session_outbox_event_id IS NULL\r\nAND passkey_credential_reference IS NULL AND passkey_version IS NULL AND passkey_usage_version IS NULL AND passkey_ceremony_id IS NULL\r\nAND passkey_method IS NULL AND passkey_assurance IS NULL AND native_before_digest IS NULL AND native_after_digest IS NULL\r\nAND contact_reference IS NULL AND contact_version IS NULL AND contact_challenge_id IS NULL AND contact_destination_digest IS NULL AND contact_risk_snapshot IS NULL AND contact_notification_delivery_id IS NULL\r\nAND governed_disposition_reference IS NOT NULL AND length(governed_disposition_reference)>0 AND governed_disposition_version IS NOT NULL AND governed_disposition_version>=1\r\nAND governed_disposition_snapshot IS NOT NULL AND jsonb_typeof(governed_disposition_snapshot::jsonb)='object' AND repair_outbox_event_id IS NOT NULL\r\nAND prior_password_digest IS NOT NULL AND length(prior_password_digest)=64 AND repaired_password_digest IS NOT NULL AND length(repaired_password_digest)=64\r\nAND prior_security_stamp_digest IS NOT NULL AND length(prior_security_stamp_digest)=64 AND repaired_security_stamp_digest IS NOT NULL AND length(repaired_security_stamp_digest)=64\r\nAND repaired_security_stamp_digest<>prior_security_stamp_digest AND session_effect IS NOT NULL AND session_effect='InvalidateExistingTunnerSessions'\r\nAND length(context_digest)=64 AND length(policy_version)>0 AND length(processing_reference)>0 AND length(retention_reference)>0\r\nAND jsonb_typeof(policy_snapshot::jsonb)='object' AND jsonb_typeof(hierarchy_snapshot::jsonb)='object' AND jsonb_typeof(unresolved_effects_json::jsonb)='array')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_recovery_case_state",
                schema: "identity",
                table: "recovery_cases",
                sql: "expires_at>created_at AND recovery_expires_at>expires_at AND (((state='PENDING_CODE' AND proof_kind='CODE')\r\n  OR (state='PENDING_PASSKEY' AND proof_kind='PASSKEY') OR (state='PENDING_CONTACT_RISK' AND proof_kind='CONTACT_RISK')\r\n  OR (state='PENDING_GOVERNED_REVIEW' AND proof_kind='GOVERNED_REVIEW')) AND version=1 AND result_id IS NULL AND ended_at IS NULL\r\n  OR (version=2 AND result_id IS NOT NULL AND ended_at>=created_at AND ended_at<expires_at\r\n    AND ((state='CONTAINED' AND goal='ContainSelectedTunnerSession' AND proof_kind='CODE') OR (state='REPAIRED' AND goal='RepairPassword'))))");

            migrationBuilder.AddCheckConstraint(
                name: "ck_recovery_case_versions",
                schema: "identity",
                table: "recovery_cases",
                sql: "((((expected_account_version>=1 AND ((proof_kind='CODE' AND set_reference IS NOT NULL AND expected_set_lifecycle_version IS NOT NULL\r\n  AND expected_set_lifecycle_version>=1 AND usage_admission IS NOT NULL AND passkey_credential_reference IS NULL AND expected_passkey_version IS NULL\r\n  AND expected_passkey_usage_version IS NULL AND passkey_ceremony_id IS NULL AND ((usage_admission='ExactUsageSnapshot'\r\n    AND expected_usage_version IS NOT NULL AND expected_usage_version>=0) OR (usage_admission='IndependentUnusedEntry' AND expected_usage_version IS NULL)))\r\n  OR (proof_kind='PASSKEY' AND goal='RepairPassword' AND set_reference IS NULL AND expected_set_lifecycle_version IS NULL\r\n    AND expected_usage_version IS NULL AND usage_admission IS NULL AND passkey_credential_reference IS NOT NULL\r\n    AND expected_passkey_version IS NOT NULL AND expected_passkey_version>=1 AND expected_passkey_usage_version IS NOT NULL\r\n    AND expected_passkey_usage_version>=0 AND passkey_ceremony_id IS NOT NULL))) AND contact_reference IS NULL AND expected_contact_version IS NULL AND contact_destination_digest IS NULL AND contact_challenge_id IS NULL AND initial_contact_risk_snapshot IS NULL) OR (proof_kind='CONTACT_RISK' AND goal='RepairPassword' AND expected_account_version>=1 AND set_reference IS NULL\r\nAND expected_set_lifecycle_version IS NULL AND expected_usage_version IS NULL AND usage_admission IS NULL\r\nAND passkey_credential_reference IS NULL AND expected_passkey_version IS NULL AND expected_passkey_usage_version IS NULL AND passkey_ceremony_id IS NULL\r\nAND contact_reference IS NOT NULL AND expected_contact_version IS NOT NULL AND expected_contact_version>=1\r\nAND contact_destination_digest IS NOT NULL AND length(contact_destination_digest)=64 AND contact_challenge_id IS NOT NULL\r\nAND initial_contact_risk_snapshot IS NOT NULL AND jsonb_typeof(initial_contact_risk_snapshot::jsonb)='object')) AND governed_contacts_digest IS NULL) OR (proof_kind='GOVERNED_REVIEW' AND goal='RepairPassword' AND expected_account_version>=1 AND set_reference IS NULL\r\nAND expected_set_lifecycle_version IS NULL AND expected_usage_version IS NULL AND usage_admission IS NULL\r\nAND passkey_credential_reference IS NULL AND expected_passkey_version IS NULL AND expected_passkey_usage_version IS NULL AND passkey_ceremony_id IS NULL\r\nAND contact_reference IS NULL AND expected_contact_version IS NULL AND contact_destination_digest IS NULL AND contact_challenge_id IS NULL AND initial_contact_risk_snapshot IS NULL\r\nAND governed_contacts_digest IS NOT NULL AND length(governed_contacts_digest)=64)");
            migrationBuilder.Sql(RecoveryGovernedMigrationSql.Harden);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(RecoveryGovernedMigrationSql.BlockPopulatedDowngrade);
            migrationBuilder.Sql(RecoveryGovernedMigrationSql.RemoveGuards);
            migrationBuilder.DropIndex(
                name: "IX_recovery_results_governed_disposition_reference_governed_di~",
                schema: "identity",
                table: "recovery_results");

            migrationBuilder.DropCheckConstraint(
                name: "ck_recovery_result_shape",
                schema: "identity",
                table: "recovery_results");

            migrationBuilder.DropCheckConstraint(
                name: "ck_recovery_case_state",
                schema: "identity",
                table: "recovery_cases");

            migrationBuilder.DropCheckConstraint(
                name: "ck_recovery_case_versions",
                schema: "identity",
                table: "recovery_cases");

            migrationBuilder.DropColumn(
                name: "governed_disposition_reference",
                schema: "identity",
                table: "recovery_results");

            migrationBuilder.DropColumn(
                name: "governed_disposition_snapshot",
                schema: "identity",
                table: "recovery_results");

            migrationBuilder.DropColumn(
                name: "governed_disposition_version",
                schema: "identity",
                table: "recovery_results");

            migrationBuilder.DropColumn(
                name: "governed_contacts_digest",
                schema: "identity",
                table: "recovery_cases");

            migrationBuilder.AddCheckConstraint(
                name: "ck_recovery_result_shape",
                schema: "identity",
                table: "recovery_results",
                sql: "(((proof_kind='CODE' AND set_reference IS NOT NULL AND set_lifecycle_version IS NOT NULL AND passkey_credential_reference IS NULL AND passkey_version IS NULL AND passkey_usage_version IS NULL AND passkey_ceremony_id IS NULL AND passkey_method IS NULL AND passkey_assurance IS NULL AND native_before_digest IS NULL AND native_after_digest IS NULL AND (set_lifecycle_version>=1 AND length(context_digest)=64 AND length(policy_version)>0 AND length(processing_reference)>0 AND length(retention_reference)>0\nAND jsonb_typeof(policy_snapshot::jsonb)='object' AND jsonb_typeof(hierarchy_snapshot::jsonb)='object' AND jsonb_typeof(unresolved_effects_json::jsonb)='array'\nAND ((outcome='CONTAINED_PENDING_RESTORATION' AND target_session_id IS NOT NULL AND prior_session_version IS NOT NULL AND prior_session_version>=1\n  AND ended_session_version IS NOT NULL AND ended_session_version=prior_session_version+1 AND session_outbox_event_id IS NOT NULL\n  AND repair_outbox_event_id IS NULL AND prior_password_digest IS NULL AND repaired_password_digest IS NULL\n  AND prior_security_stamp_digest IS NULL AND repaired_security_stamp_digest IS NULL AND session_effect IS NULL AND jsonb_array_length(unresolved_effects_json::jsonb)>0)\n  OR (outcome='PASSWORD_REPAIRED_NO_SESSION' AND target_session_id IS NULL AND prior_session_version IS NULL AND ended_session_version IS NULL\n    AND session_outbox_event_id IS NULL AND repair_outbox_event_id IS NOT NULL AND prior_password_digest IS NOT NULL AND length(prior_password_digest)=64\n    AND repaired_password_digest IS NOT NULL AND length(repaired_password_digest)=64 AND prior_security_stamp_digest IS NOT NULL AND length(prior_security_stamp_digest)=64\n    AND repaired_security_stamp_digest IS NOT NULL AND length(repaired_security_stamp_digest)=64 AND repaired_security_stamp_digest<>prior_security_stamp_digest\n    AND session_effect='InvalidateExistingTunnerSessions')))) OR (proof_kind='PASSKEY' AND set_reference IS NULL AND set_lifecycle_version IS NULL AND passkey_credential_reference IS NOT NULL\r\nAND passkey_version IS NOT NULL AND passkey_version>=1 AND passkey_usage_version IS NOT NULL AND passkey_usage_version>=1\r\nAND passkey_ceremony_id IS NOT NULL AND passkey_method IS NOT NULL AND length(passkey_method)>0\r\nAND passkey_assurance IS NOT NULL AND length(passkey_assurance)>0 AND lower(passkey_assurance)<>'aal3'\r\nAND native_before_digest IS NOT NULL AND length(native_before_digest)=64 AND native_after_digest IS NOT NULL AND length(native_after_digest)=64\r\nAND outcome='PASSWORD_REPAIRED_NO_SESSION' AND target_session_id IS NULL AND prior_session_version IS NULL AND ended_session_version IS NULL\r\nAND session_outbox_event_id IS NULL AND repair_outbox_event_id IS NOT NULL AND prior_password_digest IS NOT NULL AND length(prior_password_digest)=64\r\nAND repaired_password_digest IS NOT NULL AND length(repaired_password_digest)=64 AND prior_security_stamp_digest IS NOT NULL AND length(prior_security_stamp_digest)=64\r\nAND repaired_security_stamp_digest IS NOT NULL AND length(repaired_security_stamp_digest)=64 AND repaired_security_stamp_digest<>prior_security_stamp_digest\r\nAND session_effect IS NOT NULL AND session_effect='InvalidateExistingTunnerSessions' AND length(context_digest)=64 AND length(policy_version)>0\r\nAND length(processing_reference)>0 AND length(retention_reference)>0 AND jsonb_typeof(policy_snapshot::jsonb)='object'\r\nAND jsonb_typeof(hierarchy_snapshot::jsonb)='object' AND jsonb_typeof(unresolved_effects_json::jsonb)='array')) AND contact_reference IS NULL AND contact_version IS NULL AND contact_challenge_id IS NULL AND contact_destination_digest IS NULL AND contact_risk_snapshot IS NULL AND contact_notification_delivery_id IS NULL) OR (proof_kind='CONTACT_RISK' AND outcome='PASSWORD_REPAIRED_NO_SESSION' AND set_reference IS NULL AND set_lifecycle_version IS NULL\r\nAND target_session_id IS NULL AND prior_session_version IS NULL AND ended_session_version IS NULL AND session_outbox_event_id IS NULL\r\nAND passkey_credential_reference IS NULL AND passkey_version IS NULL AND passkey_usage_version IS NULL AND passkey_ceremony_id IS NULL\r\nAND passkey_method IS NULL AND passkey_assurance IS NULL AND native_before_digest IS NULL AND native_after_digest IS NULL\r\nAND contact_reference IS NOT NULL AND contact_version IS NOT NULL AND contact_version>=1 AND contact_challenge_id IS NOT NULL\r\nAND contact_destination_digest IS NOT NULL AND length(contact_destination_digest)=64 AND contact_risk_snapshot IS NOT NULL AND jsonb_typeof(contact_risk_snapshot::jsonb)='object'\r\nAND contact_notification_delivery_id IS NOT NULL AND repair_outbox_event_id IS NOT NULL\r\nAND prior_password_digest IS NOT NULL AND length(prior_password_digest)=64 AND repaired_password_digest IS NOT NULL AND length(repaired_password_digest)=64\r\nAND prior_security_stamp_digest IS NOT NULL AND length(prior_security_stamp_digest)=64 AND repaired_security_stamp_digest IS NOT NULL AND length(repaired_security_stamp_digest)=64\r\nAND repaired_security_stamp_digest<>prior_security_stamp_digest AND session_effect IS NOT NULL AND session_effect='InvalidateExistingTunnerSessions'\r\nAND length(context_digest)=64 AND length(policy_version)>0 AND length(processing_reference)>0 AND length(retention_reference)>0\r\nAND jsonb_typeof(policy_snapshot::jsonb)='object' AND jsonb_typeof(hierarchy_snapshot::jsonb)='object' AND jsonb_typeof(unresolved_effects_json::jsonb)='array')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_recovery_case_state",
                schema: "identity",
                table: "recovery_cases",
                sql: "expires_at>created_at AND recovery_expires_at>expires_at AND (((state='PENDING_CODE' AND proof_kind='CODE')\r\n  OR (state='PENDING_PASSKEY' AND proof_kind='PASSKEY') OR (state='PENDING_CONTACT_RISK' AND proof_kind='CONTACT_RISK'))\r\n  AND version=1 AND result_id IS NULL AND ended_at IS NULL OR (version=2 AND result_id IS NOT NULL AND ended_at>=created_at AND ended_at<expires_at\r\n    AND ((state='CONTAINED' AND goal='ContainSelectedTunnerSession' AND proof_kind='CODE') OR (state='REPAIRED' AND goal='RepairPassword'))))");

            migrationBuilder.AddCheckConstraint(
                name: "ck_recovery_case_versions",
                schema: "identity",
                table: "recovery_cases",
                sql: "((expected_account_version>=1 AND ((proof_kind='CODE' AND set_reference IS NOT NULL AND expected_set_lifecycle_version IS NOT NULL\r\n  AND expected_set_lifecycle_version>=1 AND usage_admission IS NOT NULL AND passkey_credential_reference IS NULL AND expected_passkey_version IS NULL\r\n  AND expected_passkey_usage_version IS NULL AND passkey_ceremony_id IS NULL AND ((usage_admission='ExactUsageSnapshot'\r\n    AND expected_usage_version IS NOT NULL AND expected_usage_version>=0) OR (usage_admission='IndependentUnusedEntry' AND expected_usage_version IS NULL)))\r\n  OR (proof_kind='PASSKEY' AND goal='RepairPassword' AND set_reference IS NULL AND expected_set_lifecycle_version IS NULL\r\n    AND expected_usage_version IS NULL AND usage_admission IS NULL AND passkey_credential_reference IS NOT NULL\r\n    AND expected_passkey_version IS NOT NULL AND expected_passkey_version>=1 AND expected_passkey_usage_version IS NOT NULL\r\n    AND expected_passkey_usage_version>=0 AND passkey_ceremony_id IS NOT NULL))) AND contact_reference IS NULL AND expected_contact_version IS NULL AND contact_destination_digest IS NULL AND contact_challenge_id IS NULL AND initial_contact_risk_snapshot IS NULL) OR (proof_kind='CONTACT_RISK' AND goal='RepairPassword' AND expected_account_version>=1 AND set_reference IS NULL\r\nAND expected_set_lifecycle_version IS NULL AND expected_usage_version IS NULL AND usage_admission IS NULL\r\nAND passkey_credential_reference IS NULL AND expected_passkey_version IS NULL AND expected_passkey_usage_version IS NULL AND passkey_ceremony_id IS NULL\r\nAND contact_reference IS NOT NULL AND expected_contact_version IS NOT NULL AND expected_contact_version>=1\r\nAND contact_destination_digest IS NOT NULL AND length(contact_destination_digest)=64 AND contact_challenge_id IS NOT NULL\r\nAND initial_contact_risk_snapshot IS NOT NULL AND jsonb_typeof(initial_contact_risk_snapshot::jsonb)='object')");
        }
    }
}
