using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Tunner.PresentationContent;
using Xunit;

namespace Tunner.Identity.Persistence.Tests;

public sealed class PublishedTotpPresentationIntegrationTests(SessionListingPostgresFixture fixture) : IClassFixture<SessionListingPostgresFixture>
{
    [Fact]
    public async Task PublishedBundleAndReplacementReachAuthenticatedHttpsOptionsWithoutFactorOrSeedMutation()
    {
        await using var database = await fixture.CreateDatabaseAsync(); var profile = new TotpTests.Profile();
        var environment = Environment(database, profile); await environment.InitializeAsync();
        var publications = new Dictionary<string, ContentHistoryEntry>(StringComparer.Ordinal);
        foreach (var (key, text) in profile.RequirePublished(profile.Policy).Content)
            publications.Add(key, await environment.PublishTextAsync(key, text.Text));
        var bindings = new Bindings(profile.Policy, environment);
        await using var host = await AuthenticationTestHost.Start(database, services => Compose(services, database, profile, environment, bindings));
        await host.SeedAsync(database); var cookies = await TotpTests.Login(host);
        using (var response = await host.Get("/account/totp/options", cookies))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode); using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            await using var owner = environment.Open(); var bundle = owner.Application.Resolve(new(environment.Scope, environment.Variant, environment.Clock.GetUtcNow()));
            Assert.Equal(bundle.Content.Count, json.RootElement.GetProperty("content").EnumerateObject().Count());
            foreach (var text in bundle.Content.Values) AssertProjection(json.RootElement, text);
            Assert.True(json.RootElement.TryGetProperty("csrfToken", out _));
        }
        ((AuthenticationTestHost.TestClock)environment.Clock).Advance(TimeSpan.FromSeconds(1));
        var replacement = await environment.PublishTextAsync("totp.setup.heading", "Synthetic reviewed authenticator heading",
            supersedes: publications["totp.setup.heading"].PublicationReference);
        using (var response = await host.Get("/account/totp/options", cookies))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode); using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            await using var owner = environment.Open(); var text = owner.Application.Resolve(new(environment.Scope, environment.Variant, environment.Clock.GetUtcNow())).Content["totp.setup.heading"];
            Assert.Equal("Synthetic reviewed authenticator heading", text.Text); Assert.Equal(replacement.PublicationReference.ToString("N"), text.PublicationReference);
            AssertProjection(json.RootElement, text);
            var history = await owner.Application.HistoryAsync(environment.Scope, text.Key, environment.Variant, "OP-fixture-totp-history", "fixture-history");
            Assert.Contains(history, x => x.PublicationReference == publications[text.Key].PublicationReference);
            Assert.Contains(history, x => x.PublicationReference == replacement.PublicationReference);
        }
        await AssertNoFactorMutation(database, profile);
    }

    [Theory]
    [InlineData("missing-publication")]
    [InlineData("wrong-binding")]
    [InlineData("expired-binding")]
    [InlineData("storage-outage")]
    public async Task UnavailableContentFailsClosedWithoutSetupMaterialOrFactorWrites(string failure)
    {
        await using var database = await fixture.CreateDatabaseAsync(); var profile = new TotpTests.Profile();
        var environment = Environment(database, profile); await environment.InitializeAsync();
        if (failure != "storage-outage")
            foreach (var (key, text) in profile.RequirePublished(profile.Policy).Content)
                if (failure != "missing-publication" || key != "totp.setup.heading") _ = await environment.PublishTextAsync(key, text.Text);
        var bindings = new Bindings(profile.Policy, environment);
        if (failure == "wrong-binding") bindings.Value = bindings.Value with { TotpPolicyVersion = "fixture-other-totp" };
        if (failure == "expired-binding") bindings.Value = bindings.Value with
        { EffectiveInterval = new(environment.Clock.GetUtcNow().AddMinutes(-1), true, environment.Clock.GetUtcNow(), false) };
        var connection = failure == "storage-outage" ? new NpgsqlConnectionStringBuilder(database.ConnectionString)
        { Database = "fixture_absent_totp_content_" + Guid.NewGuid().ToString("N") }.ConnectionString : null;
        await using var host = await AuthenticationTestHost.Start(database, services => Compose(services, database, profile, environment, bindings, connection));
        await host.SeedAsync(database); var cookies = await TotpTests.Login(host);
        using var response = await host.Get("/account/totp/options", cookies); Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var textBody = await response.Content.ReadAsStringAsync(); Assert.Contains("DEPENDENCY_UNAVAILABLE", textBody, StringComparison.Ordinal);
        foreach (var forbidden in new[] { "fixture-totp-binding", "fixture_absent_totp_content_", "Npgsql", "Password=", "manualKey", "otpAuthUri", "csrfToken", "content" })
            Assert.DoesNotContain(forbidden, textBody, StringComparison.OrdinalIgnoreCase);
        await AssertNoFactorMutation(database, profile);
    }

    private static PresentationContentTestEnvironment Environment(IdentityTestDatabase database, TotpTests.Profile profile) => new(database,
        new AuthenticationTestHost.TestClock(), profile.RequirePublished(profile.Policy).Content.ToDictionary(x => x.Key, x => x.Value.Text, StringComparer.Ordinal));
    private static void Compose(IServiceCollection services, IdentityTestDatabase database, TotpTests.Profile profile,
        PresentationContentTestEnvironment environment, Bindings bindings, string? contentConnection = null)
    {
        services.AddSingleton<TimeProvider>(environment.Clock); services.AddSingleton<IPresentationContentProfileSource>(environment);
        services.AddSingleton<IPresentationContentAuthorizationSource>(environment.Authorization); services.AddTunnerPresentationContent(contentConnection ?? database.ConnectionString);
        services.AddSingleton<ITotpContentBindingSource>(bindings); services.AddTunnerPublishedTotpPresentation();
        services.AddSingleton<ITotpPolicySource>(profile); services.AddSingleton<ITotpSeedStore>(profile); services.AddSingleton<ITotpMutationEligibilitySource>(profile);
        services.AddSingleton<ITotpMfaRequirementSource>(profile); services.AddSingleton<IAssurancePolicySource>(profile); services.AddSingleton<IStepUpPresentationSource>(profile);
    }
    private static async Task AssertNoFactorMutation(IdentityTestDatabase database, TotpTests.Profile profile)
    {
        Assert.Equal(0, profile.Creates); await using var context = database.Context();
        Assert.Empty(await context.Set<TotpAuthenticatorRow>().ToArrayAsync()); Assert.Empty(await context.Set<TotpOperationRow>().ToArrayAsync());
        Assert.Empty(await context.Set<TotpUsedCodeRow>().ToArrayAsync()); Assert.Empty(await context.Set<TotpPendingAuthenticationRow>().ToArrayAsync());
    }
    private static void AssertProjection(JsonElement root, PublishedContent expected)
    {
        var actual = root.GetProperty("content").GetProperty(expected.Key); Assert.Equal(expected.Key, actual.GetProperty("key").GetString());
        Assert.Equal(expected.Text, actual.GetProperty("text").GetString()); Assert.Equal(expected.Locale, actual.GetProperty("locale").GetString());
        Assert.Equal(expected.VariantReference, actual.GetProperty("variantRef").GetString()); Assert.Equal(expected.VersionReference, actual.GetProperty("versionRef").GetString());
        Assert.Equal(expected.PublicationReference, actual.GetProperty("publicationRef").GetString()); Assert.Equal("PUBLISHED", actual.GetProperty("state").GetString());
    }
    private sealed class Bindings(TotpPolicy policy, PresentationContentTestEnvironment environment) : ITotpContentBindingSource
    {
        internal TotpContentBinding Value = new(policy.Version, "fixture-totp-binding", environment.Profile.EvidenceReference,
            environment.Scope, environment.Variant, environment.Interval);
        public TotpContentBinding RequireQualified(TotpPolicy current, DateTimeOffset effectiveAt) => Value;
    }
}
