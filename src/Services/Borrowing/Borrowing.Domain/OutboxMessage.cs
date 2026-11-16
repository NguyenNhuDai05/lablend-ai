namespace Borrowing.Domain;

public sealed class OutboxMessage
{
    public Guid EventId { get; set; }
    public string EventType { get; set; } = "";
    public int EventVersion { get; set; } = 1;
    public Guid AggregateId { get; set; }
    public string CorrelationId { get; set; } = "";
    public string PayloadJson { get; set; } = "{}";
    public DateTimeOffset OccurredAt { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }
    public int Attempts { get; set; }
    public DateTimeOffset NextAttemptAt { get; set; }
    public string? LastError { get; set; }
}
