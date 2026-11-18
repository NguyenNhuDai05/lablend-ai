using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Recommendation.Application;
using Grpc.Net.Client;
using V1 = LabLend.Inventory.Contracts.V1;

namespace Recommendation.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddRecommendationInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connection = configuration.GetConnectionString("Database");
        if (string.IsNullOrWhiteSpace(connection))
            throw new InvalidOperationException("ConnectionStrings__Database is required for Recommendation.");
        services.AddDbContext<RecommendationDbContext>(options => options.UseNpgsql(connection));
        services.AddOptions<GeminiOptions>().Bind(configuration.GetSection("Gemini"));
        var policy = new RecommendationPolicy(); configuration.GetSection("Recommendation").Bind(policy);
        if (policy.DailyLimit is < 1 or > 100 || policy.MaxCandidates is < 1 or > 1000 || policy.MaxContextBytes is < 1000 or > 200_000)
            throw new InvalidOperationException("Invalid Recommendation limits.");
        services.AddSingleton(policy); services.AddSingleton(TimeProvider.System);
        services.AddScoped<IRecommendationRepository, RecommendationRepository>();
        services.AddScoped<IRecommendationService, RecommendationService>();
        services.AddOptions<InventoryOptions>().Bind(configuration.GetSection("Inventory"))
            .Validate(o => Uri.TryCreate(o.GrpcAddress, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https"
                && o.DeadlineSeconds is >= 1 and <= 30, "Invalid Inventory configuration.");
        services.AddSingleton(sp => GrpcChannel.ForAddress(sp.GetRequiredService<IOptions<InventoryOptions>>().Value.GrpcAddress));
        services.AddSingleton(sp => new V1.InventoryAllocation.InventoryAllocationClient(sp.GetRequiredService<GrpcChannel>()));
        services.AddScoped<IInventoryAvailabilityClient, InventoryAvailabilityClient>();
        services.AddHttpClient<IGeminiClient, GeminiClient>(c => c.Timeout = TimeSpan.FromSeconds(65))
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false })
            .RemoveAllLoggers();
        services.AddHttpClient<ICatalogClient, CatalogClient>(c => {
            c.BaseAddress = new Uri(configuration["Catalog:BaseAddress"] ?? "http://localhost:5102/");
            c.Timeout = TimeSpan.FromSeconds(5);
        });
        return services;
    }
}
