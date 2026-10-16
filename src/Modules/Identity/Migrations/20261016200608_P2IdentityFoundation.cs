using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Tunner.Identity.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class P2IdentityFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "accounts");

            migrationBuilder.EnsureSchema(
                name: "identity");

            migrationBuilder.CreateTable(
                name: "accounts",
                schema: "accounts",
                columns: table => new
                {
                    account_id = table.Column<string>(type: "text", nullable: false),
                    state = table.Column<string>(type: "text", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    recorded_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    operation_id = table.Column<string>(type: "text", nullable: false),
                    correlation_id = table.Column<string>(type: "text", nullable: false),
                    evidence_reference = table.Column<string>(type: "text", nullable: false),
                    age_eligibility_status = table.Column<string>(type: "text", nullable: true),
                    age_evaluated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    age_policy_version = table.Column<string>(type: "text", nullable: true),
                    age_evidence_reference = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_accounts", x => x.account_id);
                    table.CheckConstraint("ck_account_age", "(age_eligibility_status IS NULL AND age_evaluated_at_utc IS NULL AND age_policy_version IS NULL AND age_evidence_reference IS NULL) OR (age_eligibility_status IN ('ELIGIBLE','INELIGIBLE') AND age_evaluated_at_utc IS NOT NULL AND age_policy_version IS NOT NULL AND age_evidence_reference IS NOT NULL AND length(age_policy_version) > 0 AND length(age_evidence_reference) > 0)");
                    table.CheckConstraint("ck_account_id", "left(account_id, 4) = 'ACC-' AND length(account_id) > 4");
                    table.CheckConstraint("ck_account_provenance", "length(operation_id) > 0 AND length(correlation_id) > 0 AND length(evidence_reference) > 0");
                    table.CheckConstraint("ck_account_state", "state IN ('VERIFICATION_PENDING','ACTIVE','RESTRICTED','CLOSING','CLOSED')");
                    table.CheckConstraint("ck_account_version", "version >= 1");
                    table.CheckConstraint("ck_active_age", "state <> 'ACTIVE' OR (age_eligibility_status IS NOT NULL AND age_eligibility_status = 'ELIGIBLE')");
                });

            migrationBuilder.CreateTable(
                name: "OpenIddictApplications",
                schema: "identity",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    ApplicationType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    ClientId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ClientSecret = table.Column<string>(type: "text", nullable: true),
                    ClientType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    ConcurrencyToken = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    ConsentType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    DisplayName = table.Column<string>(type: "text", nullable: true),
                    DisplayNames = table.Column<string>(type: "text", nullable: true),
                    JsonWebKeySet = table.Column<string>(type: "text", nullable: true),
                    Permissions = table.Column<string>(type: "text", nullable: true),
                    PostLogoutRedirectUris = table.Column<string>(type: "text", nullable: true),
                    Properties = table.Column<string>(type: "text", nullable: true),
                    RedirectUris = table.Column<string>(type: "text", nullable: true),
                    Requirements = table.Column<string>(type: "text", nullable: true),
                    Settings = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OpenIddictApplications", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OpenIddictScopes",
                schema: "identity",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    ConcurrencyToken = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Description = table.Column<string>(type: "text", nullable: true),
                    Descriptions = table.Column<string>(type: "text", nullable: true),
                    DisplayName = table.Column<string>(type: "text", nullable: true),
                    DisplayNames = table.Column<string>(type: "text", nullable: true),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Properties = table.Column<string>(type: "text", nullable: true),
                    Resources = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OpenIddictScopes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "account_state_versions",
                schema: "accounts",
                columns: table => new
                {
                    account_id = table.Column<string>(type: "text", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    state = table.Column<string>(type: "text", nullable: false),
                    recorded_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    operation_id = table.Column<string>(type: "text", nullable: false),
                    correlation_id = table.Column<string>(type: "text", nullable: false),
                    evidence_reference = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_account_state_versions", x => new { x.account_id, x.version });
                    table.ForeignKey(
                        name: "FK_account_state_versions_accounts_account_id",
                        column: x => x.account_id,
                        principalSchema: "accounts",
                        principalTable: "accounts",
                        principalColumn: "account_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUsers",
                schema: "identity",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    AccountId = table.Column<string>(type: "text", nullable: false),
                    UserName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    NormalizedUserName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    NormalizedEmail = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    EmailConfirmed = table.Column<bool>(type: "boolean", nullable: false),
                    PasswordHash = table.Column<string>(type: "text", nullable: true),
                    SecurityStamp = table.Column<string>(type: "text", nullable: true),
                    ConcurrencyStamp = table.Column<string>(type: "text", nullable: true),
                    PhoneNumber = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    PhoneNumberConfirmed = table.Column<bool>(type: "boolean", nullable: false),
                    TwoFactorEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    LockoutEnd = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LockoutEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    AccessFailedCount = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUsers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AspNetUsers_accounts_AccountId",
                        column: x => x.AccountId,
                        principalSchema: "accounts",
                        principalTable: "accounts",
                        principalColumn: "account_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "external_identity_links",
                schema: "identity",
                columns: table => new
                {
                    link_id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<string>(type: "text", nullable: false),
                    provider = table.Column<string>(type: "text", nullable: false),
                    subject = table.Column<string>(type: "text", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    evidence_reference = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_external_identity_links", x => x.link_id);
                    table.CheckConstraint("ck_external_identity_key", "length(provider) > 0 AND length(subject) > 0");
                    table.CheckConstraint("ck_external_version", "version >= 1");
                    table.ForeignKey(
                        name: "FK_external_identity_links_accounts_account_id",
                        column: x => x.account_id,
                        principalSchema: "accounts",
                        principalTable: "accounts",
                        principalColumn: "account_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "verified_contacts",
                schema: "accounts",
                columns: table => new
                {
                    contact_id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<string>(type: "text", nullable: false),
                    normalized_email = table.Column<string>(type: "text", nullable: true),
                    state = table.Column<string>(type: "text", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    anonymized_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    evidence_reference = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_verified_contacts", x => x.contact_id);
                    table.CheckConstraint("ck_contact_anonymization", "(anonymized_at_utc IS NULL AND normalized_email IS NOT NULL AND length(normalized_email) > 0) OR (anonymized_at_utc IS NOT NULL AND normalized_email IS NULL)");
                    table.CheckConstraint("ck_contact_state", "state IN ('UNVERIFIED','VERIFIED','SUPERSEDED','REVOKED')");
                    table.CheckConstraint("ck_contact_version", "version >= 1");
                    table.ForeignKey(
                        name: "FK_verified_contacts_accounts_account_id",
                        column: x => x.account_id,
                        principalSchema: "accounts",
                        principalTable: "accounts",
                        principalColumn: "account_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "OpenIddictAuthorizations",
                schema: "identity",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    ApplicationId = table.Column<string>(type: "text", nullable: true),
                    ConcurrencyToken = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    CreationDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Properties = table.Column<string>(type: "text", nullable: true),
                    Scopes = table.Column<string>(type: "text", nullable: true),
                    Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Subject = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    Type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OpenIddictAuthorizations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OpenIddictAuthorizations_OpenIddictApplications_Application~",
                        column: x => x.ApplicationId,
                        principalSchema: "identity",
                        principalTable: "OpenIddictApplications",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserClaims",
                schema: "identity",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<string>(type: "text", nullable: false),
                    ClaimType = table.Column<string>(type: "text", nullable: true),
                    ClaimValue = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserClaims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AspNetUserClaims_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalSchema: "identity",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserLogins",
                schema: "identity",
                columns: table => new
                {
                    LoginProvider = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ProviderKey = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ProviderDisplayName = table.Column<string>(type: "text", nullable: true),
                    UserId = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserLogins", x => new { x.LoginProvider, x.ProviderKey });
                    table.ForeignKey(
                        name: "FK_AspNetUserLogins_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalSchema: "identity",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserPasskeys",
                schema: "identity",
                columns: table => new
                {
                    CredentialId = table.Column<byte[]>(type: "bytea", maxLength: 1024, nullable: false),
                    UserId = table.Column<string>(type: "text", nullable: false),
                    Data = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserPasskeys", x => x.CredentialId);
                    table.ForeignKey(
                        name: "FK_AspNetUserPasskeys_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalSchema: "identity",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserTokens",
                schema: "identity",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "text", nullable: false),
                    LoginProvider = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Value = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserTokens", x => new { x.UserId, x.LoginProvider, x.Name });
                    table.ForeignKey(
                        name: "FK_AspNetUserTokens_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalSchema: "identity",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "OpenIddictTokens",
                schema: "identity",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    ApplicationId = table.Column<string>(type: "text", nullable: true),
                    AuthorizationId = table.Column<string>(type: "text", nullable: true),
                    ConcurrencyToken = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    CreationDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ExpirationDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Payload = table.Column<string>(type: "text", nullable: true),
                    Properties = table.Column<string>(type: "text", nullable: true),
                    RedemptionDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ReferenceId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Subject = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    Type = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OpenIddictTokens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OpenIddictTokens_OpenIddictApplications_ApplicationId",
                        column: x => x.ApplicationId,
                        principalSchema: "identity",
                        principalTable: "OpenIddictApplications",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_OpenIddictTokens_OpenIddictAuthorizations_AuthorizationId",
                        column: x => x.AuthorizationId,
                        principalSchema: "identity",
                        principalTable: "OpenIddictAuthorizations",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserClaims_UserId",
                schema: "identity",
                table: "AspNetUserClaims",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserLogins_UserId",
                schema: "identity",
                table: "AspNetUserLogins",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserPasskeys_UserId",
                schema: "identity",
                table: "AspNetUserPasskeys",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "EmailIndex",
                schema: "identity",
                table: "AspNetUsers",
                column: "NormalizedEmail");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUsers_AccountId",
                schema: "identity",
                table: "AspNetUsers",
                column: "AccountId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UserNameIndex",
                schema: "identity",
                table: "AspNetUsers",
                column: "NormalizedUserName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_external_identity_links_account_id",
                schema: "identity",
                table: "external_identity_links",
                column: "account_id");

            migrationBuilder.CreateIndex(
                name: "IX_external_identity_links_provider_subject",
                schema: "identity",
                table: "external_identity_links",
                columns: new[] { "provider", "subject" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OpenIddictApplications_ClientId",
                schema: "identity",
                table: "OpenIddictApplications",
                column: "ClientId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OpenIddictAuthorizations_ApplicationId_Status_Subject_Type",
                schema: "identity",
                table: "OpenIddictAuthorizations",
                columns: new[] { "ApplicationId", "Status", "Subject", "Type" });

            migrationBuilder.CreateIndex(
                name: "IX_OpenIddictScopes_Name",
                schema: "identity",
                table: "OpenIddictScopes",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OpenIddictTokens_ApplicationId_Status_Subject_Type",
                schema: "identity",
                table: "OpenIddictTokens",
                columns: new[] { "ApplicationId", "Status", "Subject", "Type" });

            migrationBuilder.CreateIndex(
                name: "IX_OpenIddictTokens_AuthorizationId",
                schema: "identity",
                table: "OpenIddictTokens",
                column: "AuthorizationId");

            migrationBuilder.CreateIndex(
                name: "IX_OpenIddictTokens_ReferenceId",
                schema: "identity",
                table: "OpenIddictTokens",
                column: "ReferenceId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_verified_contacts_account_id",
                schema: "accounts",
                table: "verified_contacts",
                column: "account_id");

            migrationBuilder.CreateIndex(
                name: "IX_verified_contacts_normalized_email",
                schema: "accounts",
                table: "verified_contacts",
                column: "normalized_email",
                unique: true,
                filter: "state = 'VERIFIED' AND normalized_email IS NOT NULL");
            migrationBuilder.Sql(IdentityMigrationSql.Harden);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(IdentityMigrationSql.GuardDowngrade);
            migrationBuilder.DropTable(
                name: "account_state_versions",
                schema: "accounts");

            migrationBuilder.DropTable(
                name: "AspNetUserClaims",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "AspNetUserLogins",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "AspNetUserPasskeys",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "AspNetUserTokens",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "external_identity_links",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "OpenIddictScopes",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "OpenIddictTokens",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "verified_contacts",
                schema: "accounts");

            migrationBuilder.DropTable(
                name: "AspNetUsers",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "OpenIddictAuthorizations",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "accounts",
                schema: "accounts");

            migrationBuilder.DropTable(
                name: "OpenIddictApplications",
                schema: "identity");
            migrationBuilder.Sql(IdentityMigrationSql.DropFunctions);
        }
    }
}
