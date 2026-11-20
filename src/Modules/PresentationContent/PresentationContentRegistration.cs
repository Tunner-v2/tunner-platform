using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.DependencyInjection;

namespace Tunner.PresentationContent;

public static class PresentationContentRegistration
{
    public static IServiceCollection AddTunnerPresentationContent(this IServiceCollection services, string connectionString)
    {
        ArgumentNullException.ThrowIfNull(services); ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        services.AddDbContext<PresentationContentDbContext>(options => options.UseNpgsql(connectionString,
            postgres => postgres.MigrationsHistoryTable("__EFMigrationsHistory", "presentation_content")));
        services.AddScoped<PresentationContentApplication>();
        services.AddScoped<IPresentationContentResolver>(provider => provider.GetRequiredService<PresentationContentApplication>());
        return services;
    }
}
public sealed class PresentationContentDbContextFactory : IDesignTimeDbContextFactory<PresentationContentDbContext>
{
    public PresentationContentDbContext CreateDbContext(string[] args)
    {
        var connection = Environment.GetEnvironmentVariable("TUNNER_PRESENTATION_CONTENT_MIGRATION_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(connection)) throw new InvalidOperationException("An explicit PresentationContent migration connection is required.");
        return new(new DbContextOptionsBuilder<PresentationContentDbContext>().UseNpgsql(connection,
            postgres => postgres.MigrationsHistoryTable("__EFMigrationsHistory", "presentation_content")).Options);
    }
}
