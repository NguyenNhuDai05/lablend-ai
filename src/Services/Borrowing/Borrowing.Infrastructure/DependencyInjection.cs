using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Borrowing.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddBorrowingInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connection = configuration.GetConnectionString("Database");
        if (string.IsNullOrWhiteSpace(connection))
            throw new InvalidOperationException("ConnectionStrings__Database is required for Borrowing.");
        services.AddDbContext<BorrowingDbContext>(options => options.UseNpgsql(connection));
        return services;
    }
}
