using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.DependencyInjection;

namespace Tunner.Identity.Persistence;

public sealed class IdentityPersistenceDbContextFactory : IDesignTimeDbContextFactory<IdentityPersistenceDbContext>
{
    // Options-only provider is process-scoped for design-time tools; it holds no database or secret.
    private static readonly ServiceProvider ModelServices = CreateModelServices();

    public IdentityPersistenceDbContext CreateDbContext(string[] args)
    {
        var connection = Environment.GetEnvironmentVariable("TUNNER_IDENTITY_MIGRATION_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(connection))
            throw new InvalidOperationException("TUNNER_IDENTITY_MIGRATION_CONNECTION_STRING must be supplied for Identity migration commands.");
        var builder = new DbContextOptionsBuilder<IdentityPersistenceDbContext>().UseApplicationServiceProvider(ModelServices);
        IdentityPersistenceRegistration.ConfigureDatabase(builder, connection);
        return new IdentityPersistenceDbContext(builder.Options);
    }

    private static ServiceProvider CreateModelServices()
    {
        var services = new ServiceCollection();
        services.Configure<IdentityOptions>(IdentityPersistenceRegistration.ConfigureStoreModel);
        return services.BuildServiceProvider();
    }
}
