namespace Inventory.Domain;

/// <summary>
/// Append-only status history of one unit, written in the same transaction as the status change.
/// FromStatus null = first known status (unit created, or baseline row when the table was introduced).
/// </summary>
public sealed class StockMovement
{
    private StockMovement() { } // EF Core

    public StockMovement(Guid assetUnitId, string? fromStatus, string toStatus, Guid? loanId, Guid? changedBy)
    {
        AssetUnitId = assetUnitId;
        FromStatus = fromStatus;
        ToStatus = toStatus;
        LoanId = loanId;
        ChangedBy = changedBy;
        OccurredAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Identity column: also the exact order in which movements were written.</summary>
    public long Id { get; private set; }
    public Guid AssetUnitId { get; private set; }
    public string? FromStatus { get; private set; }
    public string ToStatus { get; private set; } = string.Empty;
    /// <summary>Borrowing loan behind reserve/handover/return/release (logical ID, no FK).</summary>
    public Guid? LoanId { get; private set; }
    /// <summary>Staff/Admin user (JWT sub) for REST changes; null for service-to-service gRPC calls.</summary>
    public Guid? ChangedBy { get; private set; }
    public DateTimeOffset OccurredAt { get; private set; }
}
