using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Notification.Infrastructure;

public sealed class NotificationDbContextFactory : IDesignTimeDbContextFactory<NotificationDbContext>
{
    public NotificationDbContext CreateDbContext(string[] args)
    {
        var connection = Environment.GetEnvironmentVariable("ConnectionStrings__Database")
            ?? "Host=localhost;Database=notification_db;Username=notification_user;Password=design-time-only";
        var options = new DbContextOptionsBuilder<NotificationDbContext>().UseNpgsql(connection).Options;
        return new NotificationDbContext(options);
    }
}
