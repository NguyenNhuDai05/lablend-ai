using Grpc.Core;
using Inventory.Application;
using V1 = LabLend.Inventory.Contracts.V1;

namespace Inventory.Host.Grpc;

/// <summary>Transport only: proto ↔ application DTOs. Business rules live in AllocationService.</summary>
internal sealed class InventoryAllocationGrpcService(AllocationService allocation, IAllocationRepository allocations, IReplacementRepository replacements) : V1.InventoryAllocation.InventoryAllocationBase
{
    public override async Task<V1.CheckAvailabilityReply> CheckAvailability(V1.CheckAvailabilityRequest request, ServerCallContext context)
    {
        var ids = request.EquipmentTypeIds.Select(id => Id(id, "equipment_type_id")).ToList();
        var items = await allocation.CheckAvailabilityAsync(ids, context.CancellationToken);
        return new V1.CheckAvailabilityReply
        {
            Items = { items.Select(a => new V1.Availability { EquipmentTypeId = a.EquipmentTypeId.ToString(), Available = a.Available, Total = a.Total }) }
        };
    }

    public override async Task<V1.AllocationReply> ReserveAssets(V1.ReserveAssetsRequest request, ServerCallContext context)
    {
        var command = new ReserveCommand(Id(request.OperationId, "operation_id"), Id(request.LoanId, "loan_id"),
            request.Items.Select(i => new ReserveItem(Id(i.LoanItemId, "loan_item_id"), Id(i.EquipmentTypeId, "equipment_type_id"))).ToList());
        return Reply(request.LoanId, await allocation.ReserveAsync(command, context.CancellationToken));
    }

    public override async Task<V1.AllocationReply> ConfirmHandover(V1.LoanRequest request, ServerCallContext context) =>
        Reply(request.LoanId, await allocation.HandOverAsync(Id(request.LoanId, "loan_id"), context.CancellationToken));

    public override async Task<V1.AllocationReply> ReturnAssets(V1.ReturnAssetsRequest request, ServerCallContext context)
    {
        var items = request.Items.Select(i => new ReturnItem(Id(i.LoanItemId, "loan_item_id"), i.Condition switch
        {
            V1.ReturnCondition.Normal => Domain.ReturnCondition.Normal,
            V1.ReturnCondition.Damaged => Domain.ReturnCondition.Damaged,
            V1.ReturnCondition.Lost => Domain.ReturnCondition.Lost,
            _ => i.Condition.ToString()
        })).ToList();
        return Reply(request.LoanId, await allocation.ReturnAsync(Id(request.LoanId, "loan_id"), items, context.CancellationToken));
    }

    public override async Task<V1.AllocationReply> ReleaseReservations(V1.LoanRequest request, ServerCallContext context) =>
        Reply(request.LoanId, await allocation.ReleaseAsync(Id(request.LoanId, "loan_id"), context.CancellationToken));

    public override async Task<V1.OperationStatusReply> GetOperationStatus(V1.OperationStatusRequest request, ServerCallContext context)
    {
        var status = await allocation.GetOperationAsync(Id(request.OperationId, "operation_id"), context.CancellationToken);
        return new V1.OperationStatusReply
        {
            Found = status.Found,
            LoanId = status.LoanId?.ToString() ?? "",
            Allocations = { status.Allocations.Select(Map) }
        };
    }

    private static V1.AllocationReply Reply(string loanId, IEnumerable<AllocationResult> allocations) =>
        new() { LoanId = loanId, Allocations = { allocations.Select(Map) } };

    public override async Task<V1.AllocationReply> GetLoanAllocations(V1.LoanRequest request, ServerCallContext context)
    {
        var rows = await allocations.GetByLoanAsync(Id(request.LoanId, "loan_id"), context.CancellationToken);
        return Reply(request.LoanId, rows.Where(r => r.Status != Domain.ReservationStatus.Released).Select(r =>
            new AllocationResult(r.LoanItemId, r.AssetUnitId, r.AssetUnit!.SerialNumber, r.Status, r.AssetUnit.Status)));
    }
    public override async Task<V1.ReplacementOperationReply> ReplaceLoanAsset(V1.ReplaceLoanAssetRequest r, ServerCallContext context) =>
        ReplacementReply(await replacements.Replace(new(Id(r.OperationId, "operation_id"), Id(r.IncidentId, "incident_id"),
            Id(r.OriginalLoanId, "original_loan_id"), Id(r.OriginalLoanItemId, "original_loan_item_id"), Id(r.OriginalAssetUnitId, "original_asset_unit_id"),
            Id(r.EquipmentTypeId, "equipment_type_id"), Id(r.ReplacementLoanId, "replacement_loan_id"), Id(r.ReplacementLoanItemId, "replacement_loan_item_id"),
            Id(r.ActorId, "actor_id"), r.AlreadyReturned, DateTimeOffset.TryParse(r.DueAt, out var due) ? due.ToUniversalTime() : throw InventoryException.Invalid("due_at is required.")), context.CancellationToken));
    public override async Task<V1.ReplacementOperationReply> GetReplacementOperationStatus(V1.OperationStatusRequest r, ServerCallContext context) =>
        ReplacementReply(await replacements.Status(Id(r.OperationId, "operation_id"), context.CancellationToken));
    private static V1.ReplacementOperationReply ReplacementReply(ReplacementResult r) => new()
    {
        Found = r.Found, RequestHash = r.RequestHash, OriginalLoanId = r.OriginalLoanId.ToString(), OriginalLoanItemId = r.OriginalLoanItemId.ToString(),
        OriginalAssetUnitId = r.OriginalAssetUnitId.ToString(), ReplacementLoanId = r.ReplacementLoanId.ToString(), ReplacementLoanItemId = r.ReplacementLoanItemId.ToString(),
        ReplacementAssetUnitId = r.ReplacementAssetUnitId.ToString(), SerialNumber = r.SerialNumber, ErrorCode = r.ErrorCode ?? ""
    };

    private static V1.Allocation Map(AllocationResult a) => new()
    {
        LoanItemId = a.LoanItemId.ToString(), AssetUnitId = a.AssetUnitId.ToString(), SerialNumber = a.SerialNumber,
        ReservationStatus = a.ReservationStatus, AssetStatus = a.AssetStatus
    };

    private static Guid Id(string value, string field) => Guid.TryParse(value, out var id)
        ? id
        : throw new RpcException(new Status(StatusCode.InvalidArgument, $"{field} must be a UUID."));
}
