using Grpc.Core;
using Grpc.Net.Client;
using LabLend.Inventory.Contracts.V1;

// Demo only, started by scripts/Demo-Inventory.ps1 inside the Docker backend network: plays the Borrowing side of
// Inventory gRPC v1 against the seeded microscope (one unit) and leaves it AVAILABLE again, so the demo can be repeated.
const string Microscope = "e0000000-0000-0000-0000-000000000004";
var token = Environment.GetEnvironmentVariable("INVENTORY_SERVICE_TOKEN")
    ?? throw new InvalidOperationException("Set INVENTORY_SERVICE_TOKEN.");
using var channel = GrpcChannel.ForAddress(Environment.GetEnvironmentVariable("INVENTORY_GRPC") ?? "http://inventory:8081");
var inventory = new InventoryAllocation.InventoryAllocationClient(channel);
var serviceAuth = new Metadata { { "authorization", $"Bearer {token}" } };

Step("1. Gọi không có service token");
try { await inventory.CheckAvailabilityAsync(AvailabilityRequest(), Call(new Metadata())); }
catch (RpcException ex) { Say($"→ {ex.StatusCode}: chỉ service có token (Borrowing) được gọi nghiệp vụ"); }

Step("2. CheckAvailability: kính hiển vi");
if (await StockAsync() is not { Available: 1 } stock)
    return Fail("Cần đúng 1 kính hiển vi AVAILABLE như seed. Trả/giải phóng nó rồi chạy lại.");
Say($"→ available {stock.Available} / total {stock.Total}");

Step("3. ReserveAssets 2 kính hiển vi trong 1 phiếu (chỉ có 1)");
try { await inventory.ReserveAssetsAsync(Reserve(2), Call()); return Fail("Không được giữ chỗ khi thiếu hàng."); }
catch (RpcException ex) { Say($"→ {ex.StatusCode} {ex.Trailers.GetValue("lablend-code")}: {ex.Status.Detail}"); }
Say($"→ rollback toàn bộ, tồn kho vẫn {(await StockAsync()).Available}/1");

Step("4. 8 phiếu cùng lúc giành chiếc kính hiển vi cuối cùng");
var requests = Enumerable.Range(0, 8).Select(_ => Reserve(1)).ToList();
var outcomes = await Task.WhenAll(requests.Select(async request =>
{
    try { return (request, Reply: (AllocationReply?)await inventory.ReserveAssetsAsync(request, Call()), Code: "OK"); }
    catch (RpcException ex) { return (request, Reply: (AllocationReply?)null, Code: ex.Trailers.GetValue("lablend-code") ?? ex.StatusCode.ToString()); }
}));
for (var i = 0; i < outcomes.Length; i++)
    Say($"  phiếu {i + 1}: {(outcomes[i].Reply is { } r ? $"OK → giữ {r.Allocations[0].SerialNumber} ({r.Allocations[0].AssetStatus})" : outcomes[i].Code)}");
var winners = outcomes.Where(o => o.Reply is not null).ToList();
if (winners.Count != 1) return Fail($"Có {winners.Count} phiếu thắng, phải đúng 1.");
var won = winners[0].request;
Say("→ đúng 1 phiếu thắng (FOR UPDATE SKIP LOCKED), 7 phiếu nhận INSUFFICIENT_STOCK");

Step("5. Mất phản hồi: gửi lại cùng operation_id");
var replay = await inventory.ReserveAssetsAsync(won, Call());
Say($"→ trả lại đúng unit cũ {replay.Allocations[0].SerialNumber}, không giữ thêm (tồn kho {(await StockAsync()).Available}/1)");
var status = await inventory.GetOperationStatusAsync(new OperationStatusRequest { OperationId = won.OperationId }, Call());
Say($"→ GetOperationStatus: found={status.Found}, loan {status.LoanId[..8]}…");

Step("6. ConfirmHandover rồi ReturnAssets NORMAL");
var loan = new LoanRequest { LoanId = won.LoanId };
Say($"→ bàn giao: {(await inventory.ConfirmHandoverAsync(loan, Call())).Allocations[0].AssetStatus}");
var returned = await inventory.ReturnAssetsAsync(new ReturnAssetsRequest
{
    LoanId = won.LoanId,
    Items = { new ReturnItem { LoanItemId = won.Items[0].LoanItemId, Condition = ReturnCondition.Normal } }
}, Call());
Say($"→ trả: {returned.Allocations[0].ReservationStatus}, unit {returned.Allocations[0].AssetStatus}; tồn kho {(await StockAsync()).Available}/1");
return 0;

CallOptions Call(Metadata? headers = null) => new(headers ?? serviceAuth, DateTime.UtcNow.AddSeconds(5));
static CheckAvailabilityRequest AvailabilityRequest() => new() { EquipmentTypeIds = { Microscope } };
async Task<Availability> StockAsync() => (await inventory.CheckAvailabilityAsync(AvailabilityRequest(), Call())).Items.Single();
static ReserveAssetsRequest Reserve(int items) => new()
{
    OperationId = Guid.NewGuid().ToString(),
    LoanId = Guid.NewGuid().ToString(),
    Items = { Enumerable.Range(0, items).Select(_ => new ReserveItem { LoanItemId = Guid.NewGuid().ToString(), EquipmentTypeId = Microscope }) }
};
static void Step(string title) => Console.WriteLine($"\n  [gRPC] {title}");
static void Say(string line) => Console.WriteLine($"    {line}");
static int Fail(string reason) { Console.Error.WriteLine($"    LỖI: {reason}"); return 1; }
