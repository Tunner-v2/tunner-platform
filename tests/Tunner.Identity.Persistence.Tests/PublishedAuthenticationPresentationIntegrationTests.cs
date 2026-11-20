using System.Net;
using System.Data.Common;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using Tunner.PresentationContent;
using Xunit;

namespace Tunner.Identity.Persistence.Tests;

public sealed class PublishedAuthenticationPresentationIntegrationTests(SessionListingPostgresFixture fixture)
    : IClassFixture<SessionListingPostgresFixture>
{
    [Fact]
    public async Task CommittedPublicationAppearsInHttpsLoginAndReplacementPreservesHistory()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        var clock = new AuthenticationTestHost.TestClock();
        var policy = new AuthenticationTestHost.TestPolicies().Policy;
        var original = new AuthenticationTestHost.TestPresentation().RequirePublished(policy);
        var values = original.Content.ToDictionary(item => item.Key, item => item.Value.Text, StringComparer.Ordinal);
        var environment = new PresentationContentTestEnvironment(database, clock, values);
        await environment.InitializeAsync();
        var publications = new Dictionary<string, ContentHistoryEntry>(StringComparer.Ordinal);
        foreach (var (key, text) in values)
            publications.Add(key, await environment.PublishTextAsync(key, text));
        var bindings = new Bindings(policy, environment, original);
        await using var host = await AuthenticationTestHost.Start(database, services =>
            Compose(services, database, environment, bindings));

        using (var first = await host.Get("/authentication/options"))
        {
            Assert.Equal(HttpStatusCode.OK, first.StatusCode);
            using var body = JsonDocument.Parse(await first.Content.ReadAsStringAsync());
            await using var scope = environment.Open();
            var resolved = scope.Application.Resolve(new(environment.Scope, environment.Variant, clock.GetUtcNow()));
            AssertProjection(body.RootElement, resolved.Content["login.heading"]);
        }

        clock.Advance(TimeSpan.FromSeconds(1));
        var replacement = await environment.PublishTextAsync("login.heading", "Fixture reviewed replacement heading",
            supersedes: publications["login.heading"].PublicationReference);
        using (var current = await host.Get("/authentication/options"))
        {
            Assert.Equal(HttpStatusCode.OK, current.StatusCode);
            using var body = JsonDocument.Parse(await current.Content.ReadAsStringAsync());
            await using var scope = environment.Open();
            var resolved = scope.Application.Resolve(new(environment.Scope, environment.Variant, clock.GetUtcNow()));
            AssertProjection(body.RootElement, resolved.Content["login.heading"]);
            Assert.Equal("Fixture reviewed replacement heading", resolved.Content["login.heading"].Text);
        }

        await using var historyScope = environment.Open();
        var history = await historyScope.Application.HistoryAsync(environment.Scope, "login.heading", environment.Variant,
            "OP-fixture-history", "fixture-history-correlation", default);
        Assert.Contains(history, item => item.PublicationReference == publications["login.heading"].PublicationReference);
        Assert.Contains(history, item => item.PublicationReference == replacement.PublicationReference);
        Assert.NotEqual(publications["login.heading"].VersionReference, replacement.VersionReference);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingPublicationOrUnqualifiedBindingReturns503WithoutSyntheticFallback(bool mismatchedBinding)
    {
        await using var database = await fixture.CreateDatabaseAsync();
        var clock = new AuthenticationTestHost.TestClock();
        var policy = new AuthenticationTestHost.TestPolicies().Policy;
        var original = new AuthenticationTestHost.TestPresentation().RequirePublished(policy);
        var values = original.Content.ToDictionary(item => item.Key, item => item.Value.Text, StringComparer.Ordinal);
        var environment = new PresentationContentTestEnvironment(database, clock, values);
        await environment.InitializeAsync();
        var bindings = new Bindings(policy, environment, original) { MismatchedPolicy = mismatchedBinding };
        await using var host = await AuthenticationTestHost.Start(database, services =>
            Compose(services, database, environment, bindings));
        using var response = await host.Get("/authentication/options");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("DEPENDENCY_UNAVAILABLE", body, StringComparison.Ordinal);
        Assert.DoesNotContain(values["login.heading"], body, StringComparison.Ordinal);
        Assert.DoesNotContain("fixture-only-content-configuration", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ContentStorageOutageReturns503WithoutStorageDetails()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        var clock = new AuthenticationTestHost.TestClock();
        var policy = new AuthenticationTestHost.TestPolicies().Policy;
        var original = new AuthenticationTestHost.TestPresentation().RequirePublished(policy);
        var environment = new PresentationContentTestEnvironment(database, clock,
            original.Content.ToDictionary(item => item.Key, item => item.Value.Text, StringComparer.Ordinal));
        await environment.InitializeAsync();
        var binding = new Bindings(policy, environment, original);
        // Only this isolated test's content connection points at an absent database on its own admitted container.
        var absent = new NpgsqlConnectionStringBuilder(database.ConnectionString)
        {
            Database = "fixture_absent_content_" + Guid.NewGuid().ToString("N")
        }.ConnectionString;
        await using var host = await AuthenticationTestHost.Start(database, services =>
            Compose(services, database, environment, binding, absent));
        using var response = await host.Get("/authentication/options");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("DEPENDENCY_UNAVAILABLE", body, StringComparison.Ordinal);
        Assert.DoesNotContain("fixture_absent_content_", body, StringComparison.Ordinal);
        Assert.DoesNotContain("Npgsql", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DestructiveOwnerMigrationDowngradePreservesCommittedContentHistory()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        var environment = new PresentationContentTestEnvironment(database, new AuthenticationTestHost.TestClock());
        await environment.InitializeAsync();
        var publication = await environment.PublishTextAsync("login.heading", "Fixture immutable content history");
        await using (var context = environment.Context())
        {
            var migrator = context.GetService<IMigrator>();
            var error = await Assert.ThrowsAsync<PostgresException>(() => migrator.MigrateAsync("0"));
            Assert.Equal("23514", error.SqlState);
        }
        await using var verification = environment.Context();
        Assert.NotEmpty(await verification.Database.GetAppliedMigrationsAsync());
        await using var scope = environment.Open();
        var history = await scope.Application.HistoryAsync(environment.Scope, "login.heading", environment.Variant,
            "OP-fixture-preserved-history", "fixture-history-correlation", default);
        Assert.Contains(history, item => item.PublicationReference == publication.PublicationReference);
    }

    [Fact]
    public async Task EmptyOwnerMigrationCanDowngradeAndReapply()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        var environment = new PresentationContentTestEnvironment(database, new AuthenticationTestHost.TestClock());
        await environment.InitializeAsync();
        var lockProbe = new OwnerDowngradeLockProbe();
        environment.Interceptors = [lockProbe];
        await using (var context = environment.Context())
        {
            var ownerTables = context.Model.GetEntityTypes().Where(item => item.GetSchema() == "presentation_content")
                .Select(item => item.GetTableName()!).Distinct(StringComparer.Ordinal).ToArray();
            await context.GetService<IMigrator>().MigrateAsync("0");
            Assert.NotEmpty(ownerTables);
            Assert.All(ownerTables, table => Assert.Contains(table, lockProbe.HeldTables));
        }
        environment.Interceptors = [];
        await environment.InitializeAsync();
        var publication = await environment.PublishTextAsync("login.heading", "Fixture after empty schema round trip");
        await using var scope = environment.Open();
        var resolved = scope.Application.Resolve(new(environment.Scope, environment.Variant, environment.Clock.GetUtcNow()));
        Assert.Equal(publication.PublicationReference.ToString("N"), resolved.Content["login.heading"].PublicationReference);
    }

    private static void Compose(IServiceCollection services, IdentityTestDatabase database,
        PresentationContentTestEnvironment environment, Bindings bindings, string? contentConnection = null)
    {
        // Test-only composition supplies synthetic qualified inputs; production has no such fallback.
        services.AddSingleton<TimeProvider>(environment.Clock);
        services.AddSingleton<IPresentationContentProfileSource>(environment);
        services.AddSingleton<IPresentationContentAuthorizationSource>(environment.Authorization);
        services.AddTunnerPresentationContent(contentConnection ?? database.ConnectionString);
        services.AddSingleton<IAuthenticationContentBindingSource>(bindings);
        services.RemoveAll<IAuthenticationPresentationSource>();
        services.AddTunnerPublishedAuthenticationPresentation();
    }

    private static void AssertProjection(JsonElement response, PublishedContent expected)
    {
        var actual = response.GetProperty("content").GetProperty(expected.Key);
        Assert.Equal(expected.Text, actual.GetProperty("text").GetString());
        Assert.Equal(expected.Locale, actual.GetProperty("locale").GetString());
        Assert.Equal(expected.VersionReference, actual.GetProperty("versionRef").GetString());
        Assert.Equal(expected.PublicationReference, actual.GetProperty("publicationRef").GetString());
        Assert.Equal(expected.VariantReference, actual.GetProperty("variantRef").GetString());
        Assert.Equal("PUBLISHED", actual.GetProperty("state").GetString());
    }

    private sealed class OwnerDowngradeLockProbe : DbCommandInterceptor
    {
        internal readonly HashSet<string> HeldTables = new(StringComparer.Ordinal);
        public override async ValueTask<int> NonQueryExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
            int result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("DO $guard$", StringComparison.Ordinal))
            {
                await using var probe = command.Connection!.CreateCommand();
                probe.Transaction = command.Transaction;
                probe.CommandText = """
                    SELECT DISTINCT c.relname FROM pg_locks l
                    JOIN pg_class c ON c.oid=l.relation JOIN pg_namespace n ON n.oid=c.relnamespace
                    WHERE l.pid=pg_backend_pid() AND l.mode='AccessExclusiveLock'
                      AND n.nspname='presentation_content' AND c.relkind='r'
                    """;
                await using var reader = await probe.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                    HeldTables.Add(reader.GetString(0));
            }
            return result;
        }
    }

    private sealed class Bindings(AuthenticationPolicy policy, PresentationContentTestEnvironment environment,
        AuthenticationPresentation original) : IAuthenticationContentBindingSource
    {
        internal bool MismatchedPolicy;
        public AuthenticationContentBinding RequireQualified(AuthenticationPolicy requested, DateTimeOffset effectiveAt) =>
            new(MismatchedPolicy ? "fixture-unrelated-authentication-policy" : policy.Version,
                "fixture-only-content-configuration", environment.Profile.EvidenceReference,
                environment.Scope, environment.Variant, environment.Interval,
                original.TermsUri, original.PrivacyUri, original.SupportUri);
    }
}
