using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Recommendation.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddRecommendationInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connection = configuration.GetConnectionString("Database");
        if (string.IsNullOrWhiteSpace(connection))
            throw new InvalidOperationException("ConnectionStrings__Database is required for Recommendation.");
        services.AddDbContext<RecommendationDbContext>(options => options.UseNpgsql(connection));
        return services;
    }
}
