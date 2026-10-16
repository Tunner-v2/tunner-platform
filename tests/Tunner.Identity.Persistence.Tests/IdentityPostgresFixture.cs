using DotNet.Testcontainers.Containers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Tunner.Database;
using Tunner.Database.Migrations;
using Tunner.Testing;
using Xunit;
using Xunit.Sdk;

namespace Tunner.Identity.Persistence.Tests;

public sealed class IdentityPostgresFixture : IAsyncLifetime
{
    private IContainer? container;

    public async Task InitializeAsync()
    {
        if (!TestHarnessProfiles.IsContainerExecutionEnabled()) return;
        container = TestcontainersBaseline.CreatePostgresContainer();
        await container.StartAsync();
    }

    public async Task<IdentityTestDatabase> CreateDatabaseAsync()
    {
        if (container is null) throw SkipException.ForSkip("Set TUNNER_RUN_CONTAINER_TESTS=1 for disposable PostgreSQL Identity tests.");
        var name = "identity_" + Guid.NewGuid().ToString("N");
        var adminConnection = new NpgsqlConnectionStringBuilder
        {
            Host = container.Hostname, Port = container.GetMappedPublicPort(5432), Database = "postgres", Username = "tunner"
        };
        await using (var connection = new NpgsqlConnection(adminConnection.ConnectionString))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "CREATE DATABASE " + name;
            await command.ExecuteNonQueryAsync();
        }
        adminConnection.Database = name;
        await using (var platform = new TunnerMigrationsDbContext(new DbContextOptionsBuilder<TunnerMigrationsDbContext>()
            .UseNpgsql(adminConnection.ConnectionString, x => x.MigrationsAssembly(typeof(TunnerMigrationsDbContextFactory).Assembly.FullName)).Options))
            await platform.Database.MigrateAsync();
        var database = new IdentityTestDatabase(adminConnection.ConnectionString);
        await using var identity = database.Context();
        await identity.Database.MigrateAsync();
        return database;
    }

    public async Task DisposeAsync()
    {
        if (container is not null) await container.DisposeAsync();
    }
}

public sealed class IdentityTestDatabase : IAsyncDisposable
{
    public IdentityTestDatabase(string connectionString)
    {
        ConnectionString = connectionString;
        Services = new ServiceCollection().AddTunnerIdentityPersistence(connectionString).BuildServiceProvider();
    }

    public string ConnectionString { get; }
    public ServiceProvider Services { get; }
    public IdentityPersistenceDbContext Context()
    {
        var builder = new DbContextOptionsBuilder<IdentityPersistenceDbContext>().UseApplicationServiceProvider(Services);
        IdentityPersistenceRegistration.ConfigureDatabase(builder, ConnectionString);
        return new IdentityPersistenceDbContext(builder.Options);
    }

    public ValueTask DisposeAsync() => Services.DisposeAsync();
}
