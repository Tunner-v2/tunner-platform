using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Tunner.PresentationContent;
using Xunit;

namespace Tunner.Identity.Persistence.Tests;

public sealed class PublishedSessionListingPresentationIntegrationTests(SessionListingPostgresFixture fixture) : IClassFixture<SessionListingPostgresFixture>
{
    [Fact]
    public async Task WholePublishedBundleReplacementAndHistoryReachExistingAuthenticatedOptionsWithoutSessionRevocationOrProofMutation()
    {
        await using var database = await fixture.CreateDatabaseAsync(); var original = PublishedSessionListingPresentationTests.ExistingBundle();
        PresentationContentTestEnvironment? captured = null; Bindings? binding = null;
        await using var host = await AuthenticationTestHost.Start(database, services =>
        {
            captured = Environment(database, services, original); binding = new(PublishedSessionListingPresentationTests.TestPolicy(), captured);
            Compose(services, database, captured, binding);
        });
        var environment = captured!; Assert.Same(host.Clock, environment.Clock); await environment.InitializeAsync();
        var publications = new Dictionary<string, ContentHistoryEntry>(StringComparer.Ordinal);
        foreach (var (key, value) in original.Content) publications.Add(key, await environment.PublishTextAsync(key, value.Text));
        await host.SeedAsync(database); var cookies = await TotpTests.Login(host);
        Assert.Equal(binding!.Value.AuthenticationPolicyVersion, host.Services.GetRequiredService<IAuthenticationPolicySource>().RequireEffective(host.Clock.GetUtcNow()).Version);
        using (var response = await host.Get("/account/sessions/options", cookies))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode); using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            await using var owner = environment.Open(); var resolved = owner.Application.Resolve(new(environment.Scope, environment.Variant, host.Clock.GetUtcNow()));
            Assert.Equal(resolved.Content.Count, body.RootElement.GetProperty("content").EnumerateObject().Count());
            foreach (var item in resolved.Content.Values) AssertProjection(body.RootElement, item);
            Assert.False(body.RootElement.TryGetProperty("csrfToken", out _)); Assert.False(body.RootElement.TryGetProperty("requestId", out _));
            Assert.False(body.RootElement.TryGetProperty("ceremonyId", out _)); Assert.False(body.RootElement.TryGetProperty("challenge", out _));
        }
        host.Clock.Advance(TimeSpan.FromSeconds(1));
        var replacement = await environment.PublishTextAsync("account.sessions.title", "Synthetic reviewed Sessions heading",
            supersedes: publications["account.sessions.title"].PublicationReference);
        using (var response = await host.Get("/account/sessions/options", cookies))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode); using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            await using var owner = environment.Open(); var current = owner.Application.Resolve(new(environment.Scope, environment.Variant, host.Clock.GetUtcNow())).Content["account.sessions.title"];
            Assert.Equal("Synthetic reviewed Sessions heading", current.Text); Assert.Equal(replacement.PublicationReference.ToString("N"), current.PublicationReference); AssertProjection(body.RootElement, current);
            var history = await owner.Application.HistoryAsync(environment.Scope, current.Key, environment.Variant, "OP-fixture-passkey-history", "fixture-history");
            Assert.Contains(history, x => x.PublicationReference == publications[current.Key].PublicationReference);
            Assert.Contains(history, x => x.PublicationReference == replacement.PublicationReference);
        }
        await AssertNoRevocationOrProofMutation(database);
    }

    [Theory]
    [InlineData("missing-publication")]
    [InlineData("wrong-binding")]
    [InlineData("expired-binding")]
    [InlineData("storage-outage")]
    public async Task UnavailablePublishedContentReturnsSafe503WithoutSessionRevocationOrProofWrites(string failure)
    {
        await using var database = await fixture.CreateDatabaseAsync(); var original = PublishedSessionListingPresentationTests.ExistingBundle();
        PresentationContentTestEnvironment? captured = null; Bindings? binding = null;
        var connection = failure == "storage-outage" ? new NpgsqlConnectionStringBuilder(database.ConnectionString)
        { Database = "fixture_absent_session_content_" + Guid.NewGuid().ToString("N") }.ConnectionString : null;
        await using var host = await AuthenticationTestHost.Start(database, services =>
        {
            captured = Environment(database, services, original); binding = new(PublishedSessionListingPresentationTests.TestPolicy(), captured);
            Compose(services, database, captured, binding, connection);
        });
        var environment = captured!; var bindings = binding!; Assert.Same(host.Clock, environment.Clock); await environment.InitializeAsync();
        if (failure != "storage-outage")
            foreach (var (key, item) in original.Content)
                if (failure != "missing-publication" || key != "account.sessions.details") _ = await environment.PublishTextAsync(key, item.Text);
        if (failure == "wrong-binding") bindings.Value = bindings.Value with { AuthenticationPolicyVersion = "fixture-other-authentication" };
        if (failure == "expired-binding") bindings.Value = bindings.Value with { EffectiveInterval = new(host.Clock.GetUtcNow().AddMinutes(-1), true, host.Clock.GetUtcNow(), false) };
        await host.SeedAsync(database); var cookies = await TotpTests.Login(host);
        using var response = await host.Get("/account/sessions/options", cookies); Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var text = await response.Content.ReadAsStringAsync(); Assert.Contains("DEPENDENCY_UNAVAILABLE", text, StringComparison.Ordinal);
        foreach (var forbidden in new[] { "fixture-session-binding", "fixture_absent_session_content_", "Npgsql", "Password=" }) Assert.DoesNotContain(forbidden, text, StringComparison.OrdinalIgnoreCase);
        using var body = JsonDocument.Parse(text);
        foreach (var absent in new[] { "content", "csrfToken", "requestId", "ceremonyId", "challenge", "credentialId", "publicKey" }) Assert.False(body.RootElement.TryGetProperty(absent, out _));
        await AssertNoRevocationOrProofMutation(database);
    }

    private static PresentationContentTestEnvironment Environment(IdentityTestDatabase database, IServiceCollection services, SessionListingPresentation original)
    {
        // Use the host's exact existing clock object, including any synchronization performed by its native fixture.
        var clock = Assert.IsType<AuthenticationTestHost.TestClock>(services.Last(x => x.ServiceType == typeof(TimeProvider)).ImplementationInstance);
        return new(database, clock, original.Content.ToDictionary(x => x.Key, x => x.Value.Text, StringComparer.Ordinal));
    }
    private static void Compose(IServiceCollection services, IdentityTestDatabase database, PresentationContentTestEnvironment environment, Bindings binding, string? contentConnection = null)
    {
        services.AddSingleton<IPresentationContentProfileSource>(environment); services.AddSingleton<IPresentationContentAuthorizationSource>(environment.Authorization);
        services.AddTunnerPresentationContent(contentConnection ?? database.ConnectionString); services.AddSingleton<ISessionListingContentBindingSource>(binding);
        // This isolated test supplies an explicit read budget; presentation registration alone does not activate the optional routes.
        services.AddSingleton<ISessionListingHttpBudgetSource>(new SessionListingHttpBudgetCatalog([
            new(new("fixture-session-read-budget", "ACTIVE", SessionListingHttpBudget.Scope, environment.Interval, null, ["SYNTHETIC-ONLY"]), 2, 4096)]));
        services.AddTunnerPublishedSessionListingPresentation();
    }
    private static async Task AssertNoRevocationOrProofMutation(IdentityTestDatabase database)
    {
        await using var context = database.Context(); Assert.Empty(await context.Set<PasskeyCredentialRow>().ToArrayAsync());
        Assert.Empty(await context.Set<PasskeyCeremonyRow>().ToArrayAsync()); Assert.Empty(await context.Set<StepUpProofRow>().ToArrayAsync());
        Assert.All(await context.Set<AuthenticationSessionRow>().ToArrayAsync(), session => Assert.Equal("ACTIVE", session.State));
    }
    private static void AssertProjection(JsonElement root, PublishedContent expected)
    {
        var actual = root.GetProperty("content").GetProperty(expected.Key); Assert.Equal(expected.Key, actual.GetProperty("key").GetString());
        Assert.Equal(expected.Text, actual.GetProperty("text").GetString()); Assert.Equal(expected.Locale, actual.GetProperty("locale").GetString());
        Assert.Equal(expected.VariantReference, actual.GetProperty("variantRef").GetString()); Assert.Equal(expected.VersionReference, actual.GetProperty("versionRef").GetString());
        Assert.Equal(expected.PublicationReference, actual.GetProperty("publicationRef").GetString()); Assert.Equal("PUBLISHED", actual.GetProperty("state").GetString());
    }
    private sealed class Bindings(AuthenticationPolicy admittedPolicy, PresentationContentTestEnvironment environment) : ISessionListingContentBindingSource
    {
        internal SessionListingContentBinding Value = new(admittedPolicy.Version, "fixture-session-binding", environment.Profile.EvidenceReference, environment.Scope, environment.Variant, environment.Interval);
        public SessionListingContentBinding RequireQualified(AuthenticationPolicy policy, DateTimeOffset effectiveAt) => Value;
    }
}
