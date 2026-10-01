using Microsoft.EntityFrameworkCore;

namespace Tunner.Database;

/// <summary>Empty P0 migration model. Product/domain entities are added only by later governed work.</summary>
public sealed class TunnerMigrationsDbContext(DbContextOptions<TunnerMigrationsDbContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
    }
}