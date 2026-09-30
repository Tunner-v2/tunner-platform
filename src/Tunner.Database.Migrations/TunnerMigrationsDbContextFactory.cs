using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Tunner.Database;

namespace Tunner.Database.Migrations;

/// <summary>Creates the P0 migration context without storing a connection string in source or configuration.</summary>
public sealed class TunnerMigrationsDbContextFactory : IDesignTimeDbContextFactory<TunnerMigrationsDbContext>
{
    public TunnerMigrationsDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("TUNNER_MIGRATION_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("TUNNER_MIGRATION_CONNECTION_STRING must be supplied by the operator for design-time migration commands.");
        }

        var options = new DbContextOptionsBuilder<TunnerMigrationsDbContext>()
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsAssembly(typeof(TunnerMigrationsDbContextFactory).Assembly.FullName))
            .Options;
        return new TunnerMigrationsDbContext(options);
    }
}