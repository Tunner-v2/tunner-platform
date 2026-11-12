using System.Text.Json;
using Xunit;

namespace Tunner.Identity.Persistence.Tests;

public sealed class FederationIdentityBindingTests
{
    // Explicit synthetic-only admitted inputs. No production profile, migration or protocol verification.
    private static FederationProviderRegistration Registration(string issuer = "https://issuer.example.test",
        string client = "synthetic-client-a", string ns = "synthetic-stable-registration-a",
        IEnumerable<string>? aliases = null, string version = "synthetic-profile-v1", int subjectBytes = 128,
        FederationProviderKind provider = FederationProviderKind.Google) =>
        new(provider, ns, issuer, client, aliases ?? [], version, "synthetic-explicit-issuer-map", 2048, subjectBytes);

    [Fact]
    public void StableRegisteredNamespaceIsNotDerivedFromIssuerClientOrPolicyVersion()
    {
        var first = Registration().BindExternalIdentity("https://issuer.example.test", "synthetic-client-a", "opaque-subject");
        var updatedPolicy = Registration(version: "synthetic-profile-v2").BindExternalIdentity("https://issuer.example.test", "synthetic-client-a", "opaque-subject");
        var otherRegistration = Registration(client: "synthetic-client-b", ns: "synthetic-stable-registration-b")
            .BindExternalIdentity("https://issuer.example.test", "synthetic-client-b", "opaque-subject");
        Assert.Equal("synthetic-stable-registration-a", first.ProviderNamespace);
        Assert.Equal(first.ProviderNamespace, updatedPolicy.ProviderNamespace);
        Assert.NotEqual(first.RegistrationProfileVersion, updatedPolicy.RegistrationProfileVersion);
        Assert.NotEqual(first.ProviderNamespace, otherRegistration.ProviderNamespace);
    }

    [Fact]
    public void ExplicitlyAdmittedIssuerAliasCannotSplitStableIdentity()
    {
        var profile = Registration(aliases: ["https://alias.example.test"]);
        var canonical = profile.BindExternalIdentity("https://issuer.example.test", "synthetic-client-a", "opaque-subject");
        var alias = profile.BindExternalIdentity("https://alias.example.test", "synthetic-client-a", "opaque-subject");
        Assert.Equal(canonical.ProviderNamespace, alias.ProviderNamespace); Assert.Equal(canonical.Subject, alias.Subject);
        Assert.Equal(canonical.RegistrationProfileVersion, alias.RegistrationProfileVersion);
        Assert.Equal("synthetic-explicit-issuer-map", alias.IssuerMappingEvidenceReference);
        Assert.Throws<ArgumentException>(() => Registration().BindExternalIdentity("https://alias.example.test", "synthetic-client-a", "opaque-subject"));
    }

    [Fact]
    public void ExactGoogleLegacyClaimIssuerNeedsExplicitGoogleProfileAdmission()
    {
        // Official issuer spellings only; synthetic client/subject/namespace, no live provider execution.
        var admitted = Registration(issuer: "https://accounts.google.com", aliases: ["accounts.google.com"]);
        var canonical = admitted.BindExternalIdentity("https://accounts.google.com", "synthetic-client-a", "opaque-subject");
        var legacy = admitted.BindExternalIdentity("accounts.google.com", "synthetic-client-a", "opaque-subject");
        Assert.Equal(canonical.ProviderNamespace, legacy.ProviderNamespace); Assert.Equal(canonical.Subject, legacy.Subject);
        Assert.Throws<ArgumentException>(() => Registration(issuer: "https://accounts.google.com")
            .BindExternalIdentity("accounts.google.com", "synthetic-client-a", "opaque-subject"));
        Assert.Throws<ArgumentException>(() => Registration(provider: FederationProviderKind.Microsoft, aliases: ["accounts.google.com"]));
        Assert.Throws<ArgumentException>(() => Registration(provider: FederationProviderKind.Microsoft)
            .BindExternalIdentity("accounts.google.com", "synthetic-client-a", "opaque-subject"));
        Assert.Throws<ArgumentException>(() => Registration(aliases: ["arbitrary-bare.example.test"]));
        Assert.Throws<ArgumentException>(() => Registration(aliases: ["http://accounts.google.com"]));
        Assert.Throws<ArgumentException>(() => Registration(aliases: ["https://*.google.com"]));
    }

    [Theory]
    [InlineData("https://wrong.example.test", "synthetic-client-a")]
    [InlineData("https://issuer.example.test/", "synthetic-client-a")]
    [InlineData("https://ISSUER.example.test", "synthetic-client-a")]
    [InlineData("https://issuer.example.test", "synthetic-client-b")]
    public void UnmappedSpellingOrChangedClientRequiresQualifiedProfileOrMigration(string issuer, string client)
        => Assert.Throws<ArgumentException>(() => Registration().BindExternalIdentity(issuer, client, "opaque-subject"));

    [Theory]
    [InlineData("Subject", "subject")]
    [InlineData("subject", " subject ")]
    [InlineData("é", "e\u0301")]
    public void SubjectRemainsAnExactOpaqueIdentity(string first, string second)
    {
        var registration = Registration();
        var one = registration.BindExternalIdentity("https://issuer.example.test", "synthetic-client-a", first);
        var two = registration.BindExternalIdentity("https://issuer.example.test", "synthetic-client-a", second);
        Assert.Equal(first, one.Subject); Assert.Equal(second, two.Subject); Assert.NotEqual(one.Subject, two.Subject);
    }

    [Fact]
    public void InvalidEncodingNulEmptyAndOversizedSubjectFailWithoutReturningInput()
    {
        var registration = Registration(subjectBytes: 4);
        foreach (var subject in new[] { "", "x\0y", "\ud800", "12345", "ééé" })
        {
            var failure = Assert.Throws<ArgumentException>(() => registration.BindExternalIdentity("https://issuer.example.test", "synthetic-client-a", subject));
            Assert.DoesNotContain("synthetic-client-a", failure.Message, StringComparison.Ordinal);
        }
        Assert.Equal("éé", registration.BindExternalIdentity("https://issuer.example.test", "synthetic-client-a", "éé").Subject);
    }

    [Fact]
    public void PreparedRowRetainsOwnedIdentityAndContainsNoEmailOrAuthenticationGrant()
    {
        var registration = Registration();
        var key = registration.BindExternalIdentity("https://issuer.example.test", "synthetic-client-a", "opaque-subject");
        var id = Guid.NewGuid(); var row = key.PrepareOwnedLink(id, "ACC-synthetic", "synthetic-evidence");
        Assert.Equal(id, row.LinkId); Assert.Equal("ACC-synthetic", row.AccountId); Assert.Equal(1, row.Version);
        Assert.Equal(key.ProviderNamespace, row.Provider); Assert.Equal(key.Subject, row.Subject);
        Assert.DoesNotContain(typeof(FederationIdentityKey).GetProperties(), property => property.Name.Contains("email", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("{}", JsonSerializer.Serialize(key)); Assert.Equal("{}", JsonSerializer.Serialize(registration));
        Assert.DoesNotContain("opaque-subject", key.ToString(), StringComparison.Ordinal);
        Assert.Throws<ArgumentException>(() => key.PrepareOwnedLink(Guid.Empty, "ACC-synthetic", "synthetic-evidence"));
    }

    [Fact]
    public void RegistrationRequiresExplicitSupportedShapeBoundsAndMappingProvenance()
    {
        Assert.Throws<ArgumentException>(() => Registration(issuer: ""));
        Assert.Throws<ArgumentException>(() => Registration(issuer: "invalid\0issuer"));
        Assert.Throws<ArgumentException>(() => Registration(ns: ""));
        Assert.Throws<ArgumentException>(() => Registration(subjectBytes: 0));
        Assert.Throws<ArgumentException>(() => Registration(version: ""));
        Assert.Throws<ArgumentException>(() => Registration(aliases: [""]));
        Assert.Throws<ArgumentOutOfRangeException>(() => Registration(provider: (FederationProviderKind)99));
    }
}
