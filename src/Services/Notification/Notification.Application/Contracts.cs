using System.Text.Json;
using Notification.Domain;

namespace Notification.Application;

public sealed record NotificationDto(Guid Id, string EventType, string AggregateId, string Title, string Message,
    DateTimeOffset CreatedAt, DateTimeOffset? ReadAt);
public sealed record NotificationPage(IReadOnlyList<NotificationDto> Items, long Total, int Page, int PageSize);
public sealed record IncomingNotification(Guid EventId, string EventType, string AggregateId, Guid RecipientUserId,
    string Title, string Message, DateTimeOffset OccurredAt, string StreamMessageId);
public sealed record LoanEventEnvelope(Guid EventId, string EventType, int EventVersion, string AggregateId,
    string? CorrelationId, DateTimeOffset OccurredAt, JsonElement Payload);
public enum ConsumeResult { Created, Duplicate }

public interface INotificationRepository
{
    Task<(IReadOnlyList<NotificationItem> Items, long Total)> ListAsync(Guid userId, int page, int pageSize, CancellationToken cancellationToken, string? aggregateId = null);
    Task<NotificationItem?> FindOwnedAsync(Guid notificationId, Guid userId, CancellationToken cancellationToken);
    Task<long> UnreadCountAsync(Guid userId, CancellationToken cancellationToken);
    Task SaveChangesAsync(CancellationToken cancellationToken);
}

public interface INotificationInbox
{
    Task<ConsumeResult> PersistAsync(IncomingNotification notification, CancellationToken cancellationToken);
}

public interface ILoanEventMapper
{
    IncomingNotification Map(string streamMessageId, string json);
}
