using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Recommendation.Infrastructure;

public sealed class RecommendationDesignTimeFactory : IDesignTimeDbContextFactory<RecommendationDbContext>
{
    public RecommendationDbContext CreateDbContext(string[] args) => new(new DbContextOptionsBuilder<RecommendationDbContext>()
        .UseNpgsql(Environment.GetEnvironmentVariable("ConnectionStrings__Database")
            ?? "Host=localhost;Database=recommendation_db;Username=recommendation_user").Options);
}
