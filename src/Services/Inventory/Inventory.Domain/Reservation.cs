namespace Inventory.Domain;

public static class ReservationStatus
{
    public const string Held = "HELD";
    public const string HandedOver = "HANDED_OVER";
    public const string Returned = "RETURNED";
    public const string Released = "RELEASED";
}

public static class ReturnCondition
{
    public const string Normal = "NORMAL";
    public const string Damaged = "DAMAGED";
    public const string Lost = "LOST";
}

/// <summary>Binds one Borrowing loan item (logical IDs) to one physical unit.</summary>
public sealed class Reservation
{
    private Reservation() { } // EF Core

    public Reservation(Guid operationId, Guid loanId, Guid loanItemId, Guid assetUnitId)
    {
        Id = Guid.NewGuid();
        OperationId = operationId;
        LoanId = loanId;
        LoanItemId = loanItemId;
        AssetUnitId = assetUnitId;
        Status = ReservationStatus.Held;
        CreatedAt = UpdatedAt = DateTimeOffset.UtcNow;
    }

    public Guid Id { get; private set; }
    public Guid OperationId { get; private set; }
    public Guid LoanId { get; private set; }
    public Guid LoanItemId { get; private set; }
    public Guid AssetUnitId { get; private set; }
    public AssetUnit? AssetUnit { get; private set; }
    public string Status { get; private set; } = ReservationStatus.Held;
    public string? ReturnCondition { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    // Each transition is idempotent: repeating it on an already-moved reservation is a no-op.
    public void HandOver(Guid? actor = null)
    {
        if (Status == ReservationStatus.HandedOver) return;
        Require(ReservationStatus.Held);
        AssetUnit!.HandOver(LoanId, actor);
        SetStatus(ReservationStatus.HandedOver);
    }

    public void Return(string condition, Guid? actor = null)
    {
        if (Status == ReservationStatus.Returned) return;
        Require(ReservationStatus.HandedOver);
        AssetUnit!.Return(condition, LoanId, actor);
        ReturnCondition = condition;
        SetStatus(ReservationStatus.Returned);
    }

    public void Release()
    {
        if (Status == ReservationStatus.Released) return;
        Require(ReservationStatus.Held);
        AssetUnit!.Release(LoanId);
        SetStatus(ReservationStatus.Released);
    }

    private void Require(string status)
    {
        if (Status != status)
            throw new DomainRuleException($"Reservation for loan item {LoanItemId} is {Status}, expected {status}.");
    }

    private void SetStatus(string status)
    {
        Status = status;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}

/// <summary>Idempotency record for ReserveAssets: one row per successful operationId.</summary>
public sealed class ReserveOperation
{
    private ReserveOperation() { } // EF Core

    public ReserveOperation(Guid operationId, Guid loanId, string requestHash)
    {
        OperationId = operationId;
        LoanId = loanId;
        RequestHash = requestHash;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public Guid OperationId { get; private set; }
    public Guid LoanId { get; private set; }
    public string RequestHash { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }
}
