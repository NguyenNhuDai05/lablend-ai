using Catalog.Application;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Catalog.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddCatalogInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connection = configuration.GetConnectionString("Database");
        if (string.IsNullOrWhiteSpace(connection))
            throw new InvalidOperationException("ConnectionStrings__Database is required for Catalog.");
        services.AddDbContext<CatalogDbContext>(options => options.UseNpgsql(connection));
        services.AddScoped<ICategoryRepository, CategoryRepository>();
        services.AddScoped<IEquipmentTypeRepository, EquipmentTypeRepository>();
        return services;
    }

    // ponytail: migrate on startup, fine for one Catalog instance; switch to a migration job if it ever scales out.
    public static void MigrateCatalogDatabase(this IServiceProvider services)
    {
        using var scope = services.CreateScope();
        scope.ServiceProvider.GetRequiredService<CatalogDbContext>().Database.Migrate();
    }
}
