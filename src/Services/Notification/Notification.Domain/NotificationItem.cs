namespace Notification.Domain;

public sealed class NotificationItem
{
    private NotificationItem() { }

    private NotificationItem(Guid id, Guid userId, Guid eventId, string eventType, string aggregateId,
        string title, string message, DateTimeOffset createdAt)
    {
        Id = id;
        UserId = userId;
        EventId = eventId;
        EventType = Require(eventType, 100, nameof(eventType));
        AggregateId = Require(aggregateId, 150, nameof(aggregateId));
        Title = Require(title, 200, nameof(title));
        Message = Require(message, 2000, nameof(message));
        CreatedAt = createdAt.ToUniversalTime();
    }

    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public Guid EventId { get; private set; }
    public string EventType { get; private set; } = string.Empty;
    public string AggregateId { get; private set; } = string.Empty;
    public string Title { get; private set; } = string.Empty;
    public string Message { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? ReadAt { get; private set; }
    public bool IsRead => ReadAt.HasValue;

    public static NotificationItem Create(Guid userId, Guid eventId, string eventType, string aggregateId,
        string title, string message, DateTimeOffset occurredAt) =>
        new(Guid.NewGuid(), userId, eventId, eventType, aggregateId, title, message, occurredAt);

    public bool MarkRead(DateTimeOffset now)
    {
        if (ReadAt.HasValue) return false;
        ReadAt = now.ToUniversalTime();
        return true;
    }

    private static string Require(string value, int maxLength, string name)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("Value is required.", name);
        value = value.Trim();
        if (value.Length > maxLength) throw new ArgumentOutOfRangeException(name, $"Value exceeds {maxLength} characters.");
        return value;
    }
}
