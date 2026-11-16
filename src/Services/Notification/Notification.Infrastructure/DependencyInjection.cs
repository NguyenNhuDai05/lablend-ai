using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Notification.Application;
using StackExchange.Redis;

namespace Notification.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddNotificationInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connection = configuration.GetConnectionString("Database");
        if (string.IsNullOrWhiteSpace(connection))
            throw new InvalidOperationException("ConnectionStrings__Database is required for Notification.");
        services.AddDbContext<NotificationDbContext>(options => options.UseNpgsql(connection));
        services.AddScoped<INotificationRepository, NotificationRepository>();
        services.AddScoped<INotificationInbox, EfNotificationInbox>();
        services.AddOptions<NotificationStreamOptions>().BindConfiguration(NotificationStreamOptions.Section)
            .Validate(options => options.BatchSize is > 0 and <= 100 && options.MaxAttempts is > 0 and <= 20
                && options.PendingIdleMilliseconds >= 1000 && !string.IsNullOrWhiteSpace(options.Stream)
                && !string.IsNullOrWhiteSpace(options.Group) && !string.IsNullOrWhiteSpace(options.ParkingStream),
                "NotificationStream configuration is invalid.").ValidateOnStart();
        var redisConnection = configuration.GetConnectionString("Redis");
        if (string.IsNullOrWhiteSpace(redisConnection))
            throw new InvalidOperationException("ConnectionStrings__Redis is required for Notification.");
        services.AddSingleton<IConnectionMultiplexer>(_ =>
        {
            var redisOptions = ConfigurationOptions.Parse(redisConnection);
            redisOptions.AbortOnConnectFail = false;
            redisOptions.ConnectRetry = 3;
            return ConnectionMultiplexer.Connect(redisOptions);
        });
        services.AddHostedService<NotificationStreamConsumer>();
        return services;
    }
}
