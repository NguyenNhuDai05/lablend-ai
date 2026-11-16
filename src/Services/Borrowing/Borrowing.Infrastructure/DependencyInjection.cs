using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Borrowing.Application;

namespace Borrowing.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddBorrowingInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connection = configuration.GetConnectionString("Database");
        if (string.IsNullOrWhiteSpace(connection))
            throw new InvalidOperationException("ConnectionStrings__Database is required for Borrowing.");
        services.AddDbContext<BorrowingDbContext>(options => options.UseNpgsql(connection));
        services.AddScoped<ILoanRepository, LoanRepository>();
        services.AddScoped<LoanService>();
        services.AddScoped<ILoanLifecycle, LoanLifecycle>();
        services.AddScoped<LoanLifecycle>();
        services.AddScoped<LoanReconciliation>();
        services.AddOptions<BorrowingWorkflowOptions>().Bind(configuration.GetSection("BorrowingWorkflow"))
            .Validate(o => o.BatchSize is >= 1 and <= 100 && o.MaxAttempts is >= 1 and <= 20 && o.PickupHours is >= 1 and <= 168
                && o.PollSeconds is >= 1 and <= 3600, "Invalid BorrowingWorkflow configuration.").ValidateOnStart();
        services.AddOptions<InventoryOptions>().Bind(configuration.GetSection("Inventory"))
            .Validate(o => Uri.TryCreate(o.GrpcAddress, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https"
                && o.DeadlineSeconds is >= 1 and <= 30, "Invalid Inventory endpoint/deadline.");
        services.AddSingleton(sp => Grpc.Net.Client.GrpcChannel.ForAddress(sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<InventoryOptions>>().Value.GrpcAddress));
        services.AddScoped<IInventoryClient, InventoryGrpcClient>();
        services.AddSingleton(TimeProvider.System);
        services.AddHttpClient<ICatalogClient, CatalogClient>(client =>
        {
            client.BaseAddress = new Uri(configuration["Catalog:BaseAddress"] ?? "http://localhost:5102/");
            client.Timeout = TimeSpan.FromSeconds(5);
        });
        return services;
    }
}
