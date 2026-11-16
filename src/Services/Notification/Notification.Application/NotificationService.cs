namespace Notification.Application;

public sealed class NotificationService(INotificationRepository repository, TimeProvider timeProvider)
{
    public async Task<NotificationPage> ListAsync(Guid userId, int page, int pageSize, CancellationToken cancellationToken)
    {
        if (page < 1) throw new ArgumentOutOfRangeException(nameof(page));
        if (pageSize is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(pageSize));
        var result = await repository.ListAsync(userId, page, pageSize, cancellationToken);
        return new NotificationPage(result.Items.Select(ToDto).ToArray(), result.Total, page, pageSize);
    }

    public async Task<NotificationDto?> MarkReadAsync(Guid notificationId, Guid userId, CancellationToken cancellationToken)
    {
        var notification = await repository.FindOwnedAsync(notificationId, userId, cancellationToken);
        if (notification is null) return null;
        if (notification.MarkRead(timeProvider.GetUtcNow())) await repository.SaveChangesAsync(cancellationToken);
        return ToDto(notification);
    }

    public Task<long> UnreadCountAsync(Guid userId, CancellationToken cancellationToken) =>
        repository.UnreadCountAsync(userId, cancellationToken);

    private static NotificationDto ToDto(Notification.Domain.NotificationItem item) =>
        new(item.Id, item.EventType, item.AggregateId, item.Title, item.Message, item.CreatedAt, item.ReadAt);
}
