using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Tunner.PresentationContent;
using Xunit;

namespace Tunner.Identity.Persistence.Tests;

public sealed class PublishedStepUpPresentationIntegrationTests(SessionListingPostgresFixture fixture) : IClassFixture<SessionListingPostgresFixture>
{
    [Fact]
    public async Task WholePublishedBundleReplacementAndHistoryReachExistingStepUpOptionsWithoutIssuingProof()
    {
        await using var db = await fixture.CreateDatabaseAsync(); await InitializeTargetAsync(db);
        PresentationContentTestEnvironment? captured = null;
        await using var host = await AuthenticationTestHost.Start(db, services =>
        {
            var clock = (AuthenticationTestHost.TestClock)services.Last(x => x.ServiceType == typeof(TimeProvider)).ImplementationInstance!;
            captured = Environment(db, clock); var policy = PublishedStepUpPresentationTests.Policy(clock);
            AddContent(services, db, captured); services.AddSingleton<IAssurancePolicySource>(new AssurancePolicyCatalog([policy]));
            services.AddSingleton<IUserStepUpOperationSource>(new StepUpBrowserTests.Owner(db)); services.AddSingleton<IStepUpContentBindingSource>(new Bindings(policy, captured));
            services.AddTunnerPublishedStepUpPresentation();
        });
        var env = captured!; Assert.Same(host.Clock, env.Clock); await env.InitializeAsync();
        var history = new Dictionary<string, ContentHistoryEntry>(StringComparer.Ordinal);
        foreach (var (key, item) in PublishedStepUpPresentationTests.ExistingBundle(host.Clock).Content) history.Add(key, await env.PublishTextAsync(key, item.Text));
        await host.SeedAsync(db); var cookies = await TotpTests.Login(host);
        using (var options = await host.Get("/authentication/step-up/options", cookies))
        {
            Assert.Equal(HttpStatusCode.OK, options.StatusCode); using var body = JsonDocument.Parse(await options.Content.ReadAsStringAsync());
            Assert.Equal("PENDING", body.RootElement.GetProperty("status").GetString()); Assert.True(body.RootElement.GetProperty("passwordAvailable").GetBoolean());
            Assert.False(body.RootElement.GetProperty("totpAvailable").GetBoolean()); Assert.False(body.RootElement.GetProperty("passkeyAvailable").GetBoolean());
            await using var content = env.Open(); var bundle = content.Application.Resolve(new(env.Scope, env.Variant, host.Clock.GetUtcNow()));
            Assert.Equal(bundle.Content.Count, body.RootElement.GetProperty("content").EnumerateObject().Count());
            foreach (var item in bundle.Content.Values) AssertProjection(body.RootElement, item);
        }
        host.Clock.Advance(TimeSpan.FromSeconds(1)); var replacement = await env.PublishTextAsync("stepUp.heading", "Synthetic reviewed operation proof heading", supersedes: history["stepUp.heading"].PublicationReference);
        using (var options = await host.Get("/authentication/step-up/options", cookies))
        {
            Assert.Equal(HttpStatusCode.OK, options.StatusCode); using var body = JsonDocument.Parse(await options.Content.ReadAsStringAsync());
            await using var content = env.Open(); var item = content.Application.Resolve(new(env.Scope, env.Variant, host.Clock.GetUtcNow())).Content["stepUp.heading"];
            Assert.Equal("Synthetic reviewed operation proof heading", item.Text); Assert.Equal(replacement.PublicationReference.ToString("N"), item.PublicationReference); AssertProjection(body.RootElement, item);
            var retained = await content.Application.HistoryAsync(env.Scope, item.Key, env.Variant, "OP-stepup-content-history", "fixture-correlation");
            Assert.Contains(retained, x => x.PublicationReference == history[item.Key].PublicationReference); Assert.Contains(retained, x => x.PublicationReference == replacement.PublicationReference);
        }
        await AssertNoProofAsync(db);
    }

    [Theory]
    [InlineData("missing-publication")]
    [InlineData("wrong-binding")]
    [InlineData("expired-binding")]
    [InlineData("storage-outage")]
    public async Task UnavailablePublishedContentReturnsSafe503WithoutProofOrOwnerMutation(string failure)
    {
        await using var db = await fixture.CreateDatabaseAsync(); await InitializeTargetAsync(db); PresentationContentTestEnvironment? captured = null; Bindings? bindings = null;
        var connection = failure == "storage-outage" ? new NpgsqlConnectionStringBuilder(db.ConnectionString) { Database = "fixture_absent_stepup_content_" + Guid.NewGuid().ToString("N") }.ConnectionString : null;
        await using var host = await AuthenticationTestHost.Start(db, services =>
        {
            var clock = (AuthenticationTestHost.TestClock)services.Last(x => x.ServiceType == typeof(TimeProvider)).ImplementationInstance!;
            captured = Environment(db, clock); var policy = PublishedStepUpPresentationTests.Policy(clock); bindings = new(policy, captured);
            AddContent(services, db, captured, connection); services.AddSingleton<IAssurancePolicySource>(new AssurancePolicyCatalog([policy]));
            services.AddSingleton<IUserStepUpOperationSource>(new StepUpBrowserTests.Owner(db)); services.AddSingleton<IStepUpContentBindingSource>(bindings); services.AddTunnerPublishedStepUpPresentation();
        });
        var env = captured!; Assert.Same(host.Clock, env.Clock); await env.InitializeAsync();
        if (failure != "storage-outage") foreach (var (key, item) in PublishedStepUpPresentationTests.ExistingBundle(host.Clock).Content)
            if (failure != "missing-publication" || key != "stepUp.heading") _ = await env.PublishTextAsync(key, item.Text);
        if (failure == "wrong-binding") bindings!.Value = bindings.Value with { AssuranceScope = new(bindings.Value.AssuranceScope.Operation, "other-context") };
        if (failure == "expired-binding") bindings!.Value = bindings.Value with { EffectiveInterval = new(host.Clock.GetUtcNow().AddSeconds(-1), true, host.Clock.GetUtcNow(), false) };
        await host.SeedAsync(db); var cookies = await TotpTests.Login(host);
        using var response = await host.Get("/authentication/step-up/options", cookies); Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var text = await response.Content.ReadAsStringAsync(); Assert.Contains("DEPENDENCY_UNAVAILABLE", text, StringComparison.Ordinal);
        foreach (var forbidden in new[] { "fixture-stepup-binding", "fixture_absent_stepup_content_", "Npgsql", "Password=" }) Assert.DoesNotContain(forbidden, text, StringComparison.OrdinalIgnoreCase);
        using var body = JsonDocument.Parse(text); foreach (var absent in new[] { "content", "csrfToken", "requestId", "proof", "challenge" }) Assert.False(body.RootElement.TryGetProperty(absent, out _));
        await AssertNoProofAsync(db);
    }

    internal static async Task InitializeTargetAsync(IdentityTestDatabase db)
    {
        await using var context = db.Context();
        await context.Database.ExecuteSqlRawAsync("CREATE TABLE identity.step_up_browser_target (id integer PRIMARY KEY, version bigint NOT NULL, value integer NOT NULL, session_id uuid NULL, operation_id text NULL); INSERT INTO identity.step_up_browser_target VALUES(1,1,0,NULL,NULL);");
    }
    internal static void AddContent(IServiceCollection services, IdentityTestDatabase db, PresentationContentTestEnvironment content, string? connection = null)
    {
        services.AddSingleton<IPresentationContentProfileSource>(content); services.AddSingleton<IPresentationContentAuthorizationSource>(content.Authorization); services.AddTunnerPresentationContent(connection ?? db.ConnectionString);
    }
    private static PresentationContentTestEnvironment Environment(IdentityTestDatabase db, TimeProvider clock) => new(db, clock,
        PublishedStepUpPresentationTests.ExistingBundle(clock).Content.ToDictionary(x => x.Key, x => x.Value.Text, StringComparer.Ordinal));
    internal static async Task AssertNoProofAsync(IdentityTestDatabase db)
    {
        await using var context = db.Context(); Assert.Empty(await context.Set<StepUpProofRow>().ToArrayAsync());
        Assert.Equal(0, await context.Database.SqlQueryRaw<int>("SELECT value AS \"Value\" FROM identity.step_up_browser_target WHERE id=1").SingleAsync());
        Assert.Empty(await context.Set<PasskeyCredentialRow>().ToArrayAsync()); Assert.Empty(await context.Set<PasskeyCeremonyRow>().ToArrayAsync()); Assert.Empty(await context.Set<TotpAuthenticatorRow>().ToArrayAsync());
    }
    private static void AssertProjection(JsonElement body, PublishedContent item)
    {
        var actual = body.GetProperty("content").GetProperty(item.Key); Assert.Equal(item.Text, actual.GetProperty("text").GetString()); Assert.Equal(item.Locale, actual.GetProperty("locale").GetString());
        Assert.Equal(item.VariantReference, actual.GetProperty("variantRef").GetString()); Assert.Equal(item.VersionReference, actual.GetProperty("versionRef").GetString()); Assert.Equal(item.PublicationReference, actual.GetProperty("publicationRef").GetString()); Assert.Equal("PUBLISHED", actual.GetProperty("state").GetString());
    }
    private sealed class Bindings(AssurancePolicyVersion admittedPolicy, PresentationContentTestEnvironment content) : IStepUpContentBindingSource
    {
        internal StepUpContentBinding Value = new(admittedPolicy.Metadata.Scope, admittedPolicy.Metadata.VersionId, "fixture-stepup-binding", content.Profile.EvidenceReference, content.Scope, content.Variant, content.Interval);
        public StepUpContentBinding RequireQualified(AssurancePolicyVersion policy, DateTimeOffset effectiveAt) => Value;
    }
}
