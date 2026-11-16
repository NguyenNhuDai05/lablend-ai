namespace Borrowing.Infrastructure;

public sealed class BorrowingWorkflowOptions
{
    public int BatchSize { get; set; } = 20;
    public int MaxAttempts { get; set; } = 8;
    public int PickupHours { get; set; } = 24;
    public int PollSeconds { get; set; } = 15;
}
