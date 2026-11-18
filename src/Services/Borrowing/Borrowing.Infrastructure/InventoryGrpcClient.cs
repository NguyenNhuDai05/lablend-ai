using Borrowing.Application;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.Extensions.Options;
using V1 = LabLend.Inventory.Contracts.V1;

namespace Borrowing.Infrastructure;

public sealed class InventoryOptions
{
    public string GrpcAddress { get; set; } = "http://localhost:5181";
    public string ServiceToken { get; set; } = "";
    public int DeadlineSeconds { get; set; } = 5;
}

public sealed class InventoryGrpcClient(GrpcChannel channel, IOptions<InventoryOptions> options) : IInventoryClient, IInventoryIncidentClient
{
    private readonly V1.InventoryAllocation.InventoryAllocationClient client = new(channel);
    private CallOptions Call(string correlation, CancellationToken ct)
    {
        if (options.Value.ServiceToken.Length < 32) throw new InventoryFailure(503, "INVENTORY_CREDENTIAL_NOT_CONFIGURED", true);
        return new(new Metadata { { "authorization", "Bearer " + options.Value.ServiceToken }, { "x-correlation-id", correlation } },
            DateTime.UtcNow.AddSeconds(options.Value.DeadlineSeconds), ct);
    }
    public Task<InventorySnapshot> Reserve(Guid operationId, Guid loanId, IReadOnlyDictionary<Guid, Guid> items, string correlationId, CancellationToken ct) => Send(async () =>
    {
        var request = new V1.ReserveAssetsRequest { OperationId = operationId.ToString(), LoanId = loanId.ToString() };
        request.Items.AddRange(items.Select(i => new V1.ReserveItem { LoanItemId = i.Key.ToString(), EquipmentTypeId = i.Value.ToString() }));
        return Map(await client.ReserveAssetsAsync(request, Call(correlationId, ct)));
    });
    public Task<InventorySnapshot> Handover(Guid loanId, string correlationId, CancellationToken ct) => Send(async () =>
        Map(await client.ConfirmHandoverAsync(new V1.LoanRequest { LoanId = loanId.ToString() }, Call(correlationId, ct))));
    public Task<InventorySnapshot> Release(Guid loanId, string correlationId, CancellationToken ct) => Send(async () =>
        Map(await client.ReleaseReservationsAsync(new V1.LoanRequest { LoanId = loanId.ToString() }, Call(correlationId, ct))));
    public Task<InventorySnapshot> Return(Guid loanId, IReadOnlyDictionary<Guid, string> items, string correlationId, CancellationToken ct) => Send(async () =>
    {
        var request = new V1.ReturnAssetsRequest { LoanId = loanId.ToString() };
        request.Items.AddRange(items.Select(i => new V1.ReturnItem { LoanItemId = i.Key.ToString(), Condition = i.Value switch
        { "NORMAL" => V1.ReturnCondition.Normal, "DAMAGED" => V1.ReturnCondition.Damaged, "LOST" => V1.ReturnCondition.Lost, _ => throw new LoanFailure(400, "Invalid return condition.") } }));
        return Map(await client.ReturnAssetsAsync(request, Call(correlationId, ct)));
    });
    public Task<InventorySnapshot> Status(Guid operationId, string correlationId, CancellationToken ct) => Send(async () =>
    {
        var result = await client.GetOperationStatusAsync(new V1.OperationStatusRequest { OperationId = operationId.ToString() }, Call(correlationId, ct));
        return result.Found ? new(true, Parse(result.LoanId), result.Allocations.Select(Map).ToArray()) : new(false, Guid.Empty, []);
    });
    private static InventorySnapshot Map(V1.AllocationReply result) => new(true, Parse(result.LoanId), result.Allocations.Select(Map).ToArray());
    private static InventoryAllocation Map(V1.Allocation item) => new(Parse(item.LoanItemId), Parse(item.AssetUnitId), item.ReservationStatus, item.AssetStatus, item.SerialNumber);
    private static Guid Parse(string value) => Guid.TryParse(value, out var id) && id != Guid.Empty ? id : throw new InventoryFailure(502, "INVALID_INVENTORY_REPLY", true);
    public Task<InventorySnapshot> LoanStatus(Guid loanId, Guid operationId, string correlationId, CancellationToken ct) => LoanAssets(loanId, correlationId, ct);
    public Task<InventorySnapshot> LoanAssets(Guid loanId, string correlationId, CancellationToken ct) => Send(async () =>
        Map(await client.GetLoanAllocationsAsync(new V1.LoanRequest { LoanId = loanId.ToString() }, Call(correlationId, ct))));
    public Task<ReplacementSnapshot> ReplacementStatus(Guid operationId, string correlationId, CancellationToken ct) => SendCall<ReplacementSnapshot>(async () =>
        MapReplacement(await client.GetReplacementOperationStatusAsync(new V1.OperationStatusRequest { OperationId = operationId.ToString() }, Call(correlationId, ct))));
    public Task<ReplacementSnapshot> Replace(ReplacementCommand c, string correlationId, CancellationToken ct) => SendCall<ReplacementSnapshot>(async () =>
        MapReplacement(await client.ReplaceLoanAssetAsync(new V1.ReplaceLoanAssetRequest { OperationId = c.OperationId.ToString(), IncidentId = c.IncidentId.ToString(),
            OriginalLoanId = c.OriginalLoanId.ToString(), OriginalLoanItemId = c.OriginalLoanItemId.ToString(), OriginalAssetUnitId = c.OriginalAssetUnitId.ToString(),
            EquipmentTypeId = c.EquipmentTypeId.ToString(), ReplacementLoanId = c.ReplacementLoanId.ToString(), ReplacementLoanItemId = c.ReplacementLoanItemId.ToString(),
            ActorId = c.ActorId.ToString(), AlreadyReturned = c.AlreadyReturned, DueAt = c.DueAt.ToUniversalTime().ToString("O") }, Call(correlationId, ct))));
    private static ReplacementSnapshot MapReplacement(V1.ReplacementOperationReply r)
    {
        if (r.Found && !string.IsNullOrEmpty(r.ErrorCode)) throw new InventoryFailure(409, r.ErrorCode, false);
        return r.Found ? new(true, r.RequestHash, Parse(r.OriginalLoanId), Parse(r.OriginalLoanItemId), Parse(r.OriginalAssetUnitId), Parse(r.ReplacementLoanId), Parse(r.ReplacementLoanItemId), Parse(r.ReplacementAssetUnitId), r.SerialNumber)
            : new(false, "", default, default, default, default, default, default, "");
    }
    private static Task<InventorySnapshot> Send(Func<Task<InventorySnapshot>> call) => SendCall(call);
    private static async Task<T> SendCall<T>(Func<Task<T>> call)
    {
        try { return await call(); }
        catch (RpcException ex)
        {
            var code = ex.Trailers.FirstOrDefault(t => t.Key == "lablend-code")?.Value ?? ex.StatusCode.ToString();
            // Unknown outcomes remain durable. Never replace a reservation operation ID on retry.
            var uncertain = ex.StatusCode is not (StatusCode.InvalidArgument or StatusCode.NotFound or StatusCode.FailedPrecondition or StatusCode.AlreadyExists);
            var status = ex.StatusCode switch { StatusCode.InvalidArgument => 400, StatusCode.NotFound or StatusCode.FailedPrecondition or StatusCode.AlreadyExists => 409, _ => 503 };
            throw new InventoryFailure(status, code, uncertain);
        }
    }
}
