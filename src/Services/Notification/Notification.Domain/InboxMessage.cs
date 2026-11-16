namespace Notification.Domain;

public enum InboxStatus { Processed }

public sealed class InboxMessage
{
    private InboxMessage() { }
    private InboxMessage(Guid eventId, string streamMessageId, string eventType, DateTimeOffset processedAt)
    {
        EventId = eventId;
        StreamMessageId = streamMessageId;
        EventType = eventType;
        ProcessedAt = processedAt.ToUniversalTime();
        Status = InboxStatus.Processed;
    }

    public Guid EventId { get; private set; }
    public string StreamMessageId { get; private set; } = string.Empty;
    public string EventType { get; private set; } = string.Empty;
    public InboxStatus Status { get; private set; }
    public DateTimeOffset ProcessedAt { get; private set; }

    public static InboxMessage Processed(Guid eventId, string streamMessageId, string eventType, DateTimeOffset now)
    {
        if (eventId == Guid.Empty) throw new ArgumentException("Event id is required.", nameof(eventId));
        if (string.IsNullOrWhiteSpace(streamMessageId)) throw new ArgumentException("Stream message id is required.", nameof(streamMessageId));
        if (string.IsNullOrWhiteSpace(eventType)) throw new ArgumentException("Event type is required.", nameof(eventType));
        return new(eventId, streamMessageId.Trim(), eventType.Trim(), now);
    }
}
