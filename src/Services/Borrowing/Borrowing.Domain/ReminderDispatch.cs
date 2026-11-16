namespace Borrowing.Domain;

public sealed class ReminderDispatch
{
    public Guid EventId { get; set; } = Guid.NewGuid();
    public Guid LoanId { get; set; }
    public string EventType { get; set; } = "";
    public DateTimeOffset DueAt { get; set; }
    public int ThresholdMinutes { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
