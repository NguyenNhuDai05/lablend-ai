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
        services.AddHttpClient<ICatalogClient, CatalogClient>(client =>
        {
            client.BaseAddress = new Uri(configuration["Catalog:BaseAddress"] ?? "http://localhost:5102/");
            client.Timeout = TimeSpan.FromSeconds(5);
        });
        return services;
    }
}
