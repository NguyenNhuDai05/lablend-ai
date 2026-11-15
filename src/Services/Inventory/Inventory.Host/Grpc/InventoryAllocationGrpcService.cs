using Grpc.Core;
using Inventory.Application;
using V1 = LabLend.Inventory.Contracts.V1;

namespace Inventory.Host.Grpc;

/// <summary>Transport only: proto ↔ application DTOs. Business rules live in AllocationService.</summary>
internal sealed class InventoryAllocationGrpcService(AllocationService allocation) : V1.InventoryAllocation.InventoryAllocationBase
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

    private static V1.Allocation Map(AllocationResult a) => new()
    {
        LoanItemId = a.LoanItemId.ToString(), AssetUnitId = a.AssetUnitId.ToString(), SerialNumber = a.SerialNumber,
        ReservationStatus = a.ReservationStatus, AssetStatus = a.AssetStatus
    };

    private static Guid Id(string value, string field) => Guid.TryParse(value, out var id)
        ? id
        : throw new RpcException(new Status(StatusCode.InvalidArgument, $"{field} must be a UUID."));
}
