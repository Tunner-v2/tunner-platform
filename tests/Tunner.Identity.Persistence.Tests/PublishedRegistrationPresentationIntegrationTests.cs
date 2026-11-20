using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Tunner.PresentationContent;
using Xunit;

namespace Tunner.Identity.Persistence.Tests;

public sealed class PublishedRegistrationPresentationIntegrationTests(SessionListingPostgresFixture fixture)
    : IClassFixture<SessionListingPostgresFixture>
{
    [Fact]
    public async Task WholePublishedRegistrationBundleAppearsInHttpsOptionsAndReplacementRetainsHistoryWithoutAccountOrConsentWrites()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        var inputs = Inputs(database); var environment = inputs.Environment; var notices = new Notices(); var age = new FixtureAccountAgePolicies();
        await environment.InitializeAsync(); var publications = new Dictionary<string, ContentHistoryEntry>(StringComparer.Ordinal);
        foreach (var (key, text) in inputs.Original.Content)
            publications.Add(key, await environment.PublishTextAsync(key, text.Text));
        var bindings = new Bindings(inputs.Policy.Values, environment, inputs.Original);
        await using var host = await AuthenticationTestHost.Start(database, services => Compose(services, database, inputs, bindings, age, notices));
        using (var response = await host.Get("/registration/options"))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode); using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var value = body.RootElement; Assert.Equal(inputs.Policy.Values.Version, value.GetProperty("policyVersion").GetString());
            Assert.Equal(inputs.Policy.Values.TermsVersion, value.GetProperty("termsVersion").GetString()); Assert.Equal(inputs.Policy.Values.PrivacyVersion, value.GetProperty("privacyVersion").GetString());
            Assert.Equal(inputs.Original.TermsUri.AbsoluteUri, value.GetProperty("termsUri").GetString()); Assert.Equal(inputs.Original.PrivacyUri.AbsoluteUri, value.GetProperty("privacyUri").GetString());
            await using var owner = environment.Open(); var resolved = owner.Application.Resolve(new(environment.Scope, environment.Variant, environment.Clock.GetUtcNow()));
            Assert.Equal(inputs.Original.Content.Count, value.GetProperty("content").EnumerateObject().Count());
            foreach (var expected in resolved.Content.Values) AssertProjection(value, expected);
            Assert.True(value.TryGetProperty("csrfToken", out _)); Assert.True(value.TryGetProperty("registrationKey", out _));
        }
        ((AuthenticationTestHost.TestClock)environment.Clock).Advance(TimeSpan.FromSeconds(1));
        var replacement = await environment.PublishTextAsync("registration.heading", "Synthetic reviewed Registration heading",
            supersedes: publications["registration.heading"].PublicationReference);
        using (var response = await host.Get("/registration/options"))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode); using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            await using var owner = environment.Open(); var current = owner.Application.Resolve(new(environment.Scope, environment.Variant, environment.Clock.GetUtcNow())).Content["registration.heading"];
            Assert.Equal("Synthetic reviewed Registration heading", current.Text); AssertProjection(body.RootElement, current);
            Assert.Equal(replacement.PublicationReference.ToString("N"), current.PublicationReference);
        }
        await using (var owner = environment.Open())
        {
            var history = await owner.Application.HistoryAsync(environment.Scope, "registration.heading", environment.Variant, "OP-fixture-registration-history", "fixture-history-correlation");
            Assert.Contains(history, x => x.PublicationReference == publications["registration.heading"].PublicationReference);
            Assert.Contains(history, x => x.PublicationReference == replacement.PublicationReference);
            Assert.NotEqual(publications["registration.heading"].VersionReference, replacement.VersionReference);
        }
        await AssertNoAccountOrConsentWrites(database, age, notices);
    }

    [Theory]
    [InlineData("missing-publication")]
    [InlineData("wrong-binding")]
    [InlineData("expired-binding")]
    [InlineData("storage-outage")]
    public async Task UnavailableRegistrationPresentationReturnsSafe503BeforeBrowserBootstrap(string failure)
    {
        await using var database = await fixture.CreateDatabaseAsync(); var inputs = Inputs(database); var environment = inputs.Environment;
        await environment.InitializeAsync(); var age = new FixtureAccountAgePolicies(); var notices = new Notices();
        if (failure != "storage-outage")
            foreach (var (key, value) in inputs.Original.Content)
                if (failure != "missing-publication" || key != "personal.birth.label") _ = await environment.PublishTextAsync(key, value.Text);
        var bindings = new Bindings(inputs.Policy.Values, environment, inputs.Original);
        if (failure == "expired-binding") bindings.Value = bindings.Value with
        { EffectiveInterval = new(environment.Clock.GetUtcNow().AddMinutes(-1), true, environment.Clock.GetUtcNow(), false) };
        var contentConnection = failure == "storage-outage" ? new NpgsqlConnectionStringBuilder(database.ConnectionString)
        { Database = "fixture_absent_registration_content_" + Guid.NewGuid().ToString("N") }.ConnectionString : null;
        await using var host = await AuthenticationTestHost.Start(database, services => Compose(services, database, inputs, bindings, age, notices, contentConnection));
        var valid = bindings.Value;
        if (failure == "wrong-binding")
        {
            // All content is actually published: each isolated policy/notice mismatch must itself deny the HTTP bootstrap.
            foreach (var invalid in new[] { valid with { RegistrationPolicyVersion = "fixture-other-policy" }, valid with { TermsVersion = "fixture-other-terms" }, valid with { PrivacyVersion = "fixture-other-privacy" } })
            { bindings.Value = invalid; await AssertUnavailable(host, inputs.Original.Content["registration.heading"].Text); }
        }
        else await AssertUnavailable(host, inputs.Original.Content["registration.heading"].Text);
        await AssertNoAccountOrConsentWrites(database, age, notices);
    }

    private static async Task AssertUnavailable(AuthenticationTestHost host, string originalHeading)
    {
        using var response = await host.Get("/registration/options"); Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var text = await response.Content.ReadAsStringAsync(); Assert.Contains("DEPENDENCY_UNAVAILABLE", text, StringComparison.Ordinal);
        Assert.DoesNotContain(originalHeading, text, StringComparison.Ordinal); Assert.DoesNotContain("fixture-registration-binding", text, StringComparison.Ordinal);
        Assert.DoesNotContain("fixture_absent_registration_content_", text, StringComparison.Ordinal); Assert.DoesNotContain("Npgsql", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Password=", text, StringComparison.OrdinalIgnoreCase); Assert.False(response.Headers.Contains("Set-Cookie"));
        using var body = JsonDocument.Parse(text); Assert.False(body.RootElement.TryGetProperty("registrationKey", out _));
        Assert.False(body.RootElement.TryGetProperty("csrfToken", out _)); Assert.False(body.RootElement.TryGetProperty("content", out _));
    }
    private static async Task AssertNoAccountOrConsentWrites(IdentityTestDatabase database, FixtureAccountAgePolicies age, Notices notices)
    {
        await using var context = database.Context(); Assert.Empty(await context.Accounts.ToArrayAsync()); Assert.Empty(await context.Users.ToArrayAsync());
        Assert.Empty(await context.VerifiedContacts.ToArrayAsync()); Assert.Empty(await context.Set<RegistrationOperationRow>().ToArrayAsync());
        Assert.Empty(await context.Set<AccountPersonalInformationRow>().ToArrayAsync()); Assert.Empty(await context.Set<AccountPersonalInformationVersionRow>().ToArrayAsync());
        Assert.Empty(age.Evidence); Assert.Equal(0, notices.Calls);
    }
    private static void AssertProjection(JsonElement response, PublishedContent expected)
    {
        var actual = response.GetProperty("content").GetProperty(expected.Key);
        Assert.Equal(expected.Key, actual.GetProperty("key").GetString()); Assert.Equal(expected.Text, actual.GetProperty("text").GetString());
        Assert.Equal(expected.Locale, actual.GetProperty("locale").GetString()); Assert.Equal(expected.VariantReference, actual.GetProperty("variantRef").GetString());
        Assert.Equal(expected.VersionReference, actual.GetProperty("versionRef").GetString()); Assert.Equal(expected.PublicationReference, actual.GetProperty("publicationRef").GetString());
        Assert.Equal("PUBLISHED", actual.GetProperty("state").GetString());
    }
    private sealed record TestInputs(RegistrationPolicyVersion Policy, RegistrationPresentation Original, PresentationContentTestEnvironment Environment);
    private static TestInputs Inputs(IdentityTestDatabase database)
    {
        var policy = IdentityRuntimeAdditionalConfigurationTests.Registration(new RuntimeConfigurationTestEnvironment(database), "fixture-registration-v2");
        var original = new RegistrationBffTests.Presentation().RequirePublished(policy.Values);
        var environment = new PresentationContentTestEnvironment(database, new AuthenticationTestHost.TestClock(),
            original.Content.ToDictionary(x => x.Key, x => x.Value.Text, StringComparer.Ordinal));
        return new(policy, original, environment);
    }
    private static void Compose(IServiceCollection services, IdentityTestDatabase database, TestInputs inputs, Bindings bindings,
        FixtureAccountAgePolicies age, Notices notices, string? contentConnection = null)
    {
        // Explicit synthetic test composition only. No production provider, policy, locale or age/consent value is selected.
        services.AddSingleton<TimeProvider>(inputs.Environment.Clock);
        services.AddSingleton<IPresentationContentProfileSource>(inputs.Environment); services.AddSingleton<IPresentationContentAuthorizationSource>(inputs.Environment.Authorization);
        services.AddTunnerPresentationContent(contentConnection ?? database.ConnectionString); services.AddSingleton<IRegistrationContentBindingSource>(bindings);
        services.AddTunnerPublishedRegistrationPresentation(); services.AddSingleton<IRegistrationPolicySource>(new RegistrationPolicyCatalog([inputs.Policy]));
        services.AddSingleton<IRegistrationPasswordBlocklist, Blocklist>(); services.AddSingleton<IRegistrationNotificationDispatcher>(notices);
        services.AddSingleton<IAccountAgePolicySource>(age); services.AddSingleton<IAccountConsentEvidenceSource>(age);
        services.AddSingleton(_ => new RegistrationNetworkPartitionKey(RandomNumberGenerator.GetBytes(32)));
    }
    private sealed class Bindings : IRegistrationContentBindingSource
    {
        internal RegistrationContentBinding Value;
        internal Bindings(RegistrationPolicy policy, PresentationContentTestEnvironment environment, RegistrationPresentation original)
        { Value = new(policy.Version, policy.TermsVersion, policy.PrivacyVersion, "fixture-registration-binding", environment.Profile.EvidenceReference,
            environment.Scope, environment.Variant, environment.Interval, original.TermsUri, original.PrivacyUri, original.SupportUri); }
        public RegistrationContentBinding RequireQualified(RegistrationPolicy policy, DateTimeOffset effectiveAt) => Value;
    }
    private sealed class Blocklist : IRegistrationPasswordBlocklist
    {
        public ValueTask<bool> IsBlockedAsync(string password, CancellationToken cancellationToken)
        { cancellationToken.ThrowIfCancellationRequested(); return ValueTask.FromResult(false); }
    }
    private sealed class Notices : IRegistrationNotificationDispatcher
    {
        internal int Calls;
        public Task QueueAsync(RegistrationNotification notification, CancellationToken cancellationToken)
        { cancellationToken.ThrowIfCancellationRequested(); Calls++; return Task.CompletedTask; }
    }
}
