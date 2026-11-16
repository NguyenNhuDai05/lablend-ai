namespace Notification.Infrastructure;

public sealed class NotificationStreamOptions
{
    public const string Section = "NotificationStream";
    public string Stream { get; init; } = "lablend.events.v1";
    public string Group { get; init; } = "notification-consumers";
    public string ParkingStream { get; init; } = "lablend.events.v1.notification.parking";
    public string EventField { get; init; } = "event";
    public int BatchSize { get; init; } = 20;
    public int MaxAttempts { get; init; } = 5;
    public int PendingIdleMilliseconds { get; init; } = 30_000;
    public int PollDelayMilliseconds { get; init; } = 500;
}
