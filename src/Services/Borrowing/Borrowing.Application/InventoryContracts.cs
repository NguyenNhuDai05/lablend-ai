using System.ComponentModel.DataAnnotations;

namespace Borrowing.Application;

public sealed record ReturnLoanItem(Guid LoanItemId, [Required] string Condition);
public sealed record ReturnLoanRequest([Required, MinLength(1), MaxLength(20)] IReadOnlyList<ReturnLoanItem> Items);
public sealed record InventoryAllocation(Guid LoanItemId, Guid AssetUnitId, string ReservationStatus, string AssetStatus, string SerialNumber = "");
public sealed record InventorySnapshot(bool Found, Guid LoanId, IReadOnlyList<InventoryAllocation> Allocations);
public sealed class InventoryFailure(int status, string code, bool uncertain) : Exception(code)
{
    public int Status { get; } = status;
    public bool Uncertain { get; } = uncertain;
}
public interface IInventoryClient
{
    Task<InventorySnapshot> Reserve(Guid operationId, Guid loanId, IReadOnlyDictionary<Guid, Guid> items, string correlationId, CancellationToken ct);
    Task<InventorySnapshot> Handover(Guid loanId, string correlationId, CancellationToken ct);
    Task<InventorySnapshot> Return(Guid loanId, IReadOnlyDictionary<Guid, string> items, string correlationId, CancellationToken ct);
    Task<InventorySnapshot> Release(Guid loanId, string correlationId, CancellationToken ct);
    Task<InventorySnapshot> Status(Guid operationId, string correlationId, CancellationToken ct);
    Task<InventorySnapshot> LoanStatus(Guid loanId, Guid operationId, string correlationId, CancellationToken ct) => Status(operationId, correlationId, ct);
}
public interface ILoanLifecycle
{
    Task Execute(Guid loanId, string type, Guid actor, string correlationId, ReturnLoanRequest? request, CancellationToken ct);
}
