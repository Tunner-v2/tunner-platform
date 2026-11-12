using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Xunit;

namespace Tunner.Identity.Persistence.Tests;

// Public synthetic provenance and EF/SQL declarations only. No database connection, provider or session is opened.
public sealed class FederationSessionModelTests
{
    private static readonly string[] LinkForeignKey = ["AccountId", "FederationLinkId"];
    private static readonly string[] LinkPrincipalKey = ["AccountId", "LinkId"];
    private static readonly string[] AttemptForeignKey = ["FederationAttemptId"];
    private static readonly string[] AttemptPrincipalKey = ["AttemptId"];

    private static IdentityPersistenceDbContext Context() => new(new DbContextOptionsBuilder<IdentityPersistenceDbContext>()
        .UseNpgsql("Host=localhost;Database=not_opened;Username=synthetic").Options);

    private static FederationAuthenticationSource Source(string profile = "synthetic-profile-v1", string sessionUse = "synthetic-session-use-v1",
        string qualification = "synthetic-qualification-evidence-v1", string ns = "synthetic-stable-namespace",
        string subject = "PUBLIC-NONFUNCTIONAL-OPAQUE-SUBJECT", string client = "synthetic-client") => new(
        Guid.Parse("11111111-1111-1111-1111-111111111111"), FederationProviderKind.Google,
        Guid.Parse("22222222-2222-2222-2222-222222222222"), 3, ns, subject, client,
        "https://issuer.example.test", profile, "synthetic-issuer-map-v1", "synthetic-historical-qualification-profile",
        qualification, "synthetic-admission-v1", "synthetic-assurance-map-v1", sessionUse, "SYNTHETIC_PRIMARY_ASSURANCE",
        new DateTimeOffset(2030, 1, 1, 0, 5, 0, TimeSpan.Zero));

    private static ExternalIdentityLinkRow Link(FederationAuthenticationSource source) => new()
    {
        LinkId = source.LinkId, AccountId = "ACC-public-nonfunctional", Provider = source.Namespace, Subject = source.Subject,
        Version = source.LinkVersion, LinkState = "ACTIVE", QualifiedClientId = source.ClientId,
        QualificationProfileVersion = source.QualificationProfileVersion, IssuerMappingEvidenceReference = source.IssuerMappingEvidenceReference,
        QualificationEvidenceReference = source.QualificationEvidenceReference, EvidenceReference = "synthetic-link-history"
    };

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SourceRelationshipsBindTheSameAccountLinkAndOriginalAttemptWithoutCascade(bool pending)
    {
        using var db = Context();
        var model = db.GetService<IDesignTimeModel>().Model;
        var entity = model.FindEntityType(pending ? typeof(TotpPendingAuthenticationRow) : typeof(AuthenticationSessionRow))!;
        var owner = Assert.Single(entity.GetForeignKeys(), fk => fk.PrincipalEntityType.ClrType == typeof(ExternalIdentityLinkRow));
        Assert.True(owner.Properties.Select(p => p.Name).SequenceEqual(LinkForeignKey));
        Assert.True(owner.PrincipalKey.Properties.Select(p => p.Name).SequenceEqual(LinkPrincipalKey));
        Assert.Equal(DeleteBehavior.Restrict, owner.DeleteBehavior);
        Assert.False(owner.IsRequired); // A password/passkey row does not invent a federation owner.
        var attempt = Assert.Single(entity.GetForeignKeys(), fk => fk.PrincipalEntityType.ClrType == typeof(FederationAttemptRow));
        Assert.True(attempt.Properties.Select(p => p.Name).SequenceEqual(AttemptForeignKey));
        Assert.True(attempt.PrincipalKey.Properties.Select(p => p.Name).SequenceEqual(AttemptPrincipalKey));
        Assert.Equal(DeleteBehavior.Restrict, attempt.DeleteBehavior);
        Assert.False(attempt.IsRequired);
        Assert.True(entity.FindProperty("Version")!.IsConcurrencyToken);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ModelDeclaresAnAbsentLegacyBundleOrCompleteQualifiedFederationBundle(bool pending)
    {
        using var db = Context();
        var entity = db.GetService<IDesignTimeModel>().Model.FindEntityType(
            pending ? typeof(TotpPendingAuthenticationRow) : typeof(AuthenticationSessionRow))!;
        var sourceProperties = entity.GetProperties().Where(p => p.Name.StartsWith("Federation", StringComparison.Ordinal)).ToArray();
        Assert.Equal(pending ? 14 : 15, sourceProperties.Length);
        var store = StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema());
        var sql = entity.GetCheckConstraints().Single(c => c.Name == (pending ? "ck_totp_pending_federation" : "ck_session_federation")).Sql;
        foreach (var property in sourceProperties)
        {
            Assert.True(property.IsNullable);
            Assert.Null(property.GetDefaultValue());
            var column = property.GetColumnName(store)!;
            Assert.Contains(column + " IS NULL", sql);
            Assert.Contains(column + " IS NOT NULL", sql);
        }
        Assert.Contains("federation_link_version>=1", sql);
        Assert.Contains("federation_provider IN ('Google','Microsoft')", sql);
        Assert.Contains(pending ? "primary_method='federation'" : "authentication_methods IN ('federation','federation+totp')", sql);
        if (pending)
        {
            Assert.Contains("passkey_credential_reference IS NULL", sql);
            Assert.Contains("passkey_credential_version IS NULL", sql);
            var result = entity.GetCheckConstraints().Single(c => c.Name == "ck_totp_pending_result").Sql;
            Assert.Contains("accepted_authenticator_reference IS NOT NULL", result);
            Assert.Contains("accepted_generation IS NOT NULL", result);
        }
        else
        {
            var method = entity.GetCheckConstraints().Single(c => c.Name == "ck_session_method").Sql;
            Assert.Contains("'pwd'", method); Assert.Contains("'passkey'", method);
            Assert.Contains("'pwd+totp'", method); Assert.Contains("'passkey+totp'", method);
            Assert.Contains("'federation+totp'", method);
            Assert.Contains("totp_authenticator_reference IS NOT NULL", method);
            Assert.Contains("mfa_requirement_policy_version IS NOT NULL", method);
        }
        // These assertions qualify the registered declarations, not PostgreSQL enforcement or sign-in authority.
    }

    [Theory]
    [InlineData("pwd")]
    [InlineData("passkey")]
    [InlineData("pwd+totp")]
    [InlineData("passkey+totp")]
    public void LegacyMethodRowsDoNotAcquireFederationSourceDefaults(string method)
    {
        var session = new AuthenticationSessionRow { AuthenticationMethods = method };
        var pending = new TotpPendingAuthenticationRow { PrimaryMethod = method.Split('+')[0] };
        foreach (var property in typeof(AuthenticationSessionRow).GetProperties().Where(p => p.Name.StartsWith("Federation", StringComparison.Ordinal)))
            Assert.Null(property.GetValue(session));
        foreach (var property in typeof(TotpPendingAuthenticationRow).GetProperties().Where(p => p.Name.StartsWith("Federation", StringComparison.Ordinal)))
            Assert.Null(property.GetValue(pending));
        var link = Link(Source());
        Assert.Null(FederationAuthenticationSource.Read(session, link));
        Assert.Null(FederationAuthenticationSource.Read(pending, link));
    }

    [Fact]
    public void SessionAndPendingProjectionsRetainTheSameExactQualificationWithoutPromotingAssurance()
    {
        var source = Source(); var link = Link(source);
        var session = new AuthenticationSessionRow { AccountId = link.AccountId, AuthenticationMethods = "federation+totp", Assurance = "SYNTHETIC_COMBINED_ASSURANCE" };
        var pending = new TotpPendingAuthenticationRow { AccountId = link.AccountId, PrimaryMethod = "federation", PrimaryAssurance = source.PrimaryAssurance,
            ExpiresAt = source.VerificationExpiresAt!.Value };
        source.Store(session); source.Store(pending);
        var restoredSession = FederationAuthenticationSource.Read(session, link);
        var restoredPending = FederationAuthenticationSource.Read(pending, link);
        Assert.NotNull(restoredSession); Assert.NotNull(restoredPending);
        Assert.Equal(source.Digest, restoredSession!.Digest);
        Assert.Equal(source.Digest, restoredPending!.Digest);
        Assert.Equal(source.Identifier, restoredSession.Identifier);
        Assert.Equal(source.Identifier, restoredPending.Identifier);
        Assert.Equal(source.PrimaryAssurance, session.FederationPrimaryAssurance);
        Assert.Equal("SYNTHETIC_COMBINED_ASSURANCE", session.Assurance);
        Assert.Equal("federation+totp", session.AuthenticationMethods);
        Assert.Null(session.PasskeyCredentialReference); Assert.Null(pending.PasskeyCredentialReference);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("google")]
    [InlineData("GOOGLE")]
    [InlineData("GitHub")]
    public void StoredSourceCannotRelabelAnUnsupportedOrNumericProvider(string provider)
    {
        var source = Source(); var link = Link(source);
        var session = new AuthenticationSessionRow(); source.Store(session);
        var pending = new TotpPendingAuthenticationRow { PrimaryAssurance = source.PrimaryAssurance }; source.Store(pending);
        session.FederationProvider = provider; pending.FederationProvider = provider;
        Assert.Null(FederationAuthenticationSource.Read(session, link));
        Assert.Null(FederationAuthenticationSource.Read(pending, link));
    }

    [Fact]
    public void PolicyAndQualificationChangesKeepStableIdentityButCannotReuseAnOriginalPendingBinding()
    {
        var original = Source();
        var rotatedProfile = Source(profile: "synthetic-profile-v2");
        var changedUsePolicy = Source(sessionUse: "synthetic-session-use-v2");
        var changedQualification = Source(qualification: "synthetic-qualification-evidence-v2");
        Assert.Equal(original.Identifier, rotatedProfile.Identifier);
        Assert.Equal(original.Identifier, changedUsePolicy.Identifier);
        Assert.Equal(original.Identifier, changedQualification.Identifier);
        Assert.NotEqual(original.Digest, rotatedProfile.Digest);
        Assert.NotEqual(original.Digest, changedUsePolicy.Digest);
        Assert.NotEqual(original.Digest, changedQualification.Digest);
        // Provenance binding only: this does not authorize a session, retire one, or establish provider auth_time.
    }

    [Theory]
    [InlineData("namespace")]
    [InlineData("subject")]
    [InlineData("client")]
    public void DifferentRegisteredIdentityCannotReuseThePrimaryIdentifier(string change)
    {
        var original = Source();
        var other = change switch
        {
            "namespace" => Source(ns: "synthetic-other-namespace"),
            "subject" => Source(subject: "public-nonfunctional-opaque-subject"),
            _ => Source(client: "synthetic-other-client")
        };
        Assert.NotEqual(original.Identifier, other.Identifier);
        Assert.NotEqual(original.Digest, other.Digest);
    }

    [Fact]
    public void SourceDiagnosticsDoNotSerializeOpaqueIdentityOrQualificationValues()
    {
        var source = Source();
        Assert.Equal(nameof(FederationAuthenticationSource), source.ToString());
        Assert.Equal("{}", JsonSerializer.Serialize(source));
    }

    [Fact]
    public void SessionGuardDeclarationFreezesEveryMappedFederationProvenanceColumn()
    {
        using var db = Context();
        var entity = db.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(AuthenticationSessionRow))!;
        var store = StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema());
        var guard = FederationSessionMigrationSql.Harden;
        foreach (var property in entity.GetProperties().Where(p => p.Name.StartsWith("Federation", StringComparison.Ordinal)))
        {
            var column = property.GetColumnName(store)!;
            Assert.Contains("NEW." + column, guard);
            Assert.Contains("OLD." + column, guard);
        }
        Assert.Contains("IS DISTINCT FROM", guard);
        Assert.Contains("ERRCODE='23514'", guard);
        Assert.Contains("BEFORE UPDATE ON identity.tunner_sessions", guard);
        // Source declaration coverage only. Actual trigger rejection requires the separately owned finite PostgreSQL run.
    }
}
