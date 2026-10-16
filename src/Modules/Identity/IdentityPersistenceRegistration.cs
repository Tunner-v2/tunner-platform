using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Tunner.Identity.Persistence;

public static class IdentityPersistenceRegistration
{
    public static IServiceCollection AddTunnerIdentityPersistence(this IServiceCollection services, string connectionString)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        services.Configure<IdentityOptions>(ConfigureStoreModel);
        services.AddDbContext<IdentityPersistenceDbContext>(options => ConfigureDatabase(options, connectionString));
        services.AddScoped<IUserStore<IdentityUserAdapter>, UserOnlyStore<IdentityUserAdapter, IdentityPersistenceDbContext>>();
        services.AddOpenIddict().AddCore(options => options.UseEntityFrameworkCore().UseDbContext<IdentityPersistenceDbContext>());
        return services;
    }

    internal static void ConfigureStoreModel(IdentityOptions options)
    {
        options.Stores.SchemaVersion = IdentitySchemaVersions.Version3;
        options.Stores.MaxLengthForKeys = 128;
    }

    internal static DbContextOptionsBuilder ConfigureDatabase(DbContextOptionsBuilder options, string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        return options.UseNpgsql(connectionString, database => database.MigrationsHistoryTable("__EFMigrationsHistory", "identity"))
            .UseOpenIddict();
    }
}
