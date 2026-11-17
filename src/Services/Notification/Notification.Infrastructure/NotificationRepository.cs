using Microsoft.EntityFrameworkCore;
using Notification.Application;
using Notification.Domain;

namespace Notification.Infrastructure;

public sealed class NotificationRepository(NotificationDbContext dbContext) : INotificationRepository
{
    public async Task<(IReadOnlyList<NotificationItem> Items, long Total)> ListAsync(Guid userId, int page, int pageSize, CancellationToken cancellationToken, string? aggregateId = null)
    {
        var query = dbContext.Notifications.AsNoTracking().Where(x => x.UserId == userId);
        if (aggregateId != null) query = query.Where(x => x.AggregateId == aggregateId);
        var total = await query.LongCountAsync(cancellationToken);
        var items = await query.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);
        return (items, total);
    }

    public Task<NotificationItem?> FindOwnedAsync(Guid notificationId, Guid userId, CancellationToken cancellationToken) =>
        dbContext.Notifications.SingleOrDefaultAsync(x => x.Id == notificationId && x.UserId == userId, cancellationToken);

    public Task<long> UnreadCountAsync(Guid userId, CancellationToken cancellationToken) =>
        dbContext.Notifications.LongCountAsync(x => x.UserId == userId && x.ReadAt == null, cancellationToken);

    public async Task SaveChangesAsync(CancellationToken cancellationToken) =>
        await dbContext.SaveChangesAsync(cancellationToken);
}
