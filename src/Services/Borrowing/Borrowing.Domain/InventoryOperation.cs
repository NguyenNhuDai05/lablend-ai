namespace Borrowing.Domain;

// Immutable command intent survives the network call and a process failure before local commit.
public sealed class InventoryOperation
{
    public Guid OperationId { get; set; } = Guid.NewGuid();
    public Guid LoanId { get; set; }
    public string OperationType { get; set; } = "";
    public string State { get; set; } = "PENDING";
    public Guid ActorId { get; set; }
    public string CorrelationId { get; set; } = "";
    public string RequestJson { get; set; } = "{}";
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public int RetryCount { get; set; }
    public string? LastError { get; set; }
    public DateTimeOffset NextRetryAt { get; set; }
    public uint Version { get; set; }
}
