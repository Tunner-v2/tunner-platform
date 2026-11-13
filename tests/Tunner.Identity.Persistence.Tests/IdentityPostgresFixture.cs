using DotNet.Testcontainers.Containers;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Tunner.Testing;
using Tunner.Testing.Recovery;
using Xunit;
using Xunit.Sdk;

namespace Tunner.Identity.Persistence.Tests;

public sealed class IdentityPostgresFixture : IAsyncLifetime
{
    private IContainer? container;
    private readonly RecoveryCodeOwnedPostgres? finiteOwner;
    private readonly bool releasePrivateDatabases;
    public IdentityPostgresFixture() { }
    internal IdentityPostgresFixture(RecoveryCodeOwnedPostgres finiteOwner, bool releasePrivateDatabases = false)
    { finiteOwner.RequireReady(); this.finiteOwner = finiteOwner; this.releasePrivateDatabases = releasePrivateDatabases; }

    public async Task InitializeAsync()
    {
        if (finiteOwner is not null) { finiteOwner.RequireReady(); return; }
        if (!TestHarnessProfiles.IsContainerExecutionEnabled()) return;
        container = TestcontainersBaseline.CreatePostgresContainer();
        await container.StartAsync();
    }

    public Task<IdentityTestDatabase> CreateDatabaseAsync(string? targetMigration = null) => CreateDatabaseCoreAsync(targetMigration, default);
    internal Task<IdentityTestDatabase> CreateFiniteDatabaseAsync(CancellationToken ct)
    {
        if (finiteOwner is null) throw new InvalidOperationException("RECOVERY_FINITE_DATABASE_OWNER_REQUIRED");
        return CreateDatabaseCoreAsync(null, ct);
    }
    private async Task<IdentityTestDatabase> CreateDatabaseCoreAsync(string? targetMigration, CancellationToken ct)
    {
        if (container is null && finiteOwner is null) throw SkipException.ForSkip("Set TUNNER_RUN_CONTAINER_TESTS=1 for disposable PostgreSQL Identity tests.");
        finiteOwner?.RequireReady();
        var adminConnection = finiteOwner is not null
            ? new NpgsqlConnectionStringBuilder(finiteOwner.Connection("postgres"))
            : new NpgsqlConnectionStringBuilder
            {
                Host = container!.Hostname, Port = container!.GetMappedPublicPort(5432), Database = "postgres", Username = "tunner"
            };
        var connectionString = releasePrivateDatabases
            ? await finiteOwner!.CreateIdentityDatabaseAsync(ct, targetMigration)
            : await RecoveryCodeIdentityDatabaseProvisioning.CreateAsync(adminConnection, targetMigration, ct);
        return new IdentityTestDatabase(connectionString, finiteOwner, releasePrivateDatabases);
    }

    public async Task DisposeAsync()
    {
        if (container is not null) await container.DisposeAsync();
    }
}

public sealed class IdentityTestDatabase : IAsyncDisposable
{
    private readonly bool releasePrivateDatabase;
    private int disposed;
    public IdentityTestDatabase(string connectionString) : this(connectionString, null) { }
    internal IdentityTestDatabase(string connectionString, RecoveryCodeOwnedPostgres? finiteOwner, bool releasePrivateDatabase = false)
    {
        finiteOwner?.RequireReady(); FiniteOwner = finiteOwner;
        if (releasePrivateDatabase && finiteOwner is null) throw new InvalidOperationException("RECOVERY_DATABASE_OWNER_REQUIRED");
        this.releasePrivateDatabase = releasePrivateDatabase;
        ConnectionString = connectionString;
        Services = new ServiceCollection().AddTunnerIdentityPersistence(connectionString).BuildServiceProvider();
    }

    internal RecoveryCodeOwnedPostgres? FiniteOwner { get; }
    public string ConnectionString { get; }
    public ServiceProvider Services { get; }
    public IdentityPersistenceDbContext Context()
    {
        return RecoveryCodeIdentityDatabaseProvisioning.Context(Services, ConnectionString);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        await Services.DisposeAsync();
        if (releasePrivateDatabase) await FiniteOwner!.ReleaseIdentityDatabaseAsync(ConnectionString);
    }
}
