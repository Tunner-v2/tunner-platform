using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Xunit;

namespace Tunner.Identity.Persistence.Tests;

public sealed class IdentityRuntimeConfigurationMigrationTests(SessionListingPostgresFixture fixture) : IClassFixture<SessionListingPostgresFixture>
{
    private const string PriorMigration = "20261115081132_P2DeviceEvidenceAndTrust";

    [Fact]
    public async Task EmptyConfigurationSchemaCanDowngradeAndReapply()
    {
        await using var database = await fixture.CreateDatabaseAsync(); var environment = new RuntimeConfigurationTestEnvironment(database);
        await using (var context = environment.Context())
        {
            await context.GetService<IMigrator>().MigrateAsync(PriorMigration);
            await context.Database.MigrateAsync();
        }
        await environment.PublishAsync(IdentityConfigurationFamily.Authentication, "fixture-reapplied");
        Assert.Equal("fixture-reapplied", environment.Source.RequireEffective(environment.Clock.GetUtcNow()).Version);
    }

    [Fact]
    public async Task ConfigurationHistoryCannotBeDroppedByDowngrade()
    {
        await using var database = await fixture.CreateDatabaseAsync(); var environment = new RuntimeConfigurationTestEnvironment(database);
        var publication = await environment.PublishAsync(IdentityConfigurationFamily.Authentication, "fixture-preserved");
        await using var context = environment.Context();
        var error = await Assert.ThrowsAsync<PostgresException>(() => context.GetService<IMigrator>().MigrateAsync(PriorMigration));
        Assert.Equal("23514", error.SqlState);
        Assert.Equal("fixture-preserved", environment.Source.RequireEffective(environment.Clock.GetUtcNow()).Version);
        await using var scope = environment.Open();
        var history = await scope.Application.HistoryAsync(new(IdentityConfigurationFamily.Authentication), "OP-preserved-history", "fixture-correlation");
        Assert.Contains(history, x => x.PublicationReference == publication.PublicationReference);
    }
}
