namespace Borrowing.Domain;

// One student draft maps to one aggregate. Persisted with the aggregate/history/outbox transaction.
public sealed class LoanSubmission
{
    public Guid StudentId { get; set; }
    public Guid Key { get; set; }
    public string RequestHash { get; set; } = string.Empty;
    public Guid LoanId { get; set; }
}
