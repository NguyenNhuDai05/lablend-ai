using Inventory.Application;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Inventory.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInventoryInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connection = configuration.GetConnectionString("Database");
        if (string.IsNullOrWhiteSpace(connection))
            throw new InvalidOperationException("ConnectionStrings__Database is required for Inventory.");
        services.AddDbContext<InventoryDbContext>(options => options.UseNpgsql(connection));
        services.AddScoped<IAssetRepository, AssetRepository>();
        services.AddScoped<IAllocationRepository, AllocationRepository>();
        services.AddScoped<IReplacementRepository, ReplacementRepository>();
        return services;
    }

    // ponytail: migrate on startup, fine for one Inventory instance; switch to a migration job if it ever scales out.
    public static void MigrateInventoryDatabase(this IServiceProvider services)
    {
        using var scope = services.CreateScope();
        scope.ServiceProvider.GetRequiredService<InventoryDbContext>().Database.Migrate();
    }
}
