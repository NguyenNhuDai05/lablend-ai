using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Identity.Application;
using Identity.Domain;
using Microsoft.AspNetCore.Identity;

namespace Identity.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddIdentityInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connection = configuration.GetConnectionString("Database");
        if (string.IsNullOrWhiteSpace(connection))
            throw new InvalidOperationException("ConnectionStrings__Database is required for Identity.");
        services.AddDbContext<IdentityDbContext>(options => options.UseNpgsql(connection));
        services.AddOptions<SessionSettings>().BindConfiguration("Session")
            .Validate(x => x.AccessMinutes is >= 1 and <= 30 && x.RefreshDays is >= 1 and <= 30
                && x.MaxFailedAttempts is >= 3 and <= 10 && x.LockoutMinutes is >= 1 and <= 60, "Invalid session settings.").ValidateOnStart();
        var session = configuration.GetSection("Session").Get<SessionSettings>() ?? new();
        services.AddIdentityCore<ApplicationUser>(options =>
        {
            options.User.RequireUniqueEmail = true;
            options.Password.RequiredLength = 12;
            options.Password.RequireDigit = true;
            options.Password.RequireLowercase = true;
            options.Password.RequireUppercase = true;
            options.Password.RequireNonAlphanumeric = true;
            options.Lockout.AllowedForNewUsers = true;
            options.Lockout.MaxFailedAccessAttempts = session.MaxFailedAttempts;
            options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(session.LockoutMinutes);
        }).AddRoles<ApplicationRole>().AddEntityFrameworkStores<IdentityDbContext>();
        services.AddScoped<IIdentityRepository, IdentityRepository>();
        services.AddScoped<IAccessTokenIssuer, JwtIssuer>();
        services.AddScoped<IdentityService>();
        return services;
    }
}
