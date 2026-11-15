using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Grpc.Core;
using Inventory.Application;
using LabLend.Contracts;
using Xunit;
using V1 = LabLend.Inventory.Contracts.V1;

namespace Inventory.IntegrationTests;

public sealed class InventoryTests(InventoryApiFactory factory) : IClassFixture<InventoryApiFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly V1.InventoryAllocation.InventoryAllocationClient grpc = factory.Grpc();

    [Fact]
    public async Task Rest_asset_crud_maintenance_and_public_availability()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.ClientAs(null).GetAsync("/api/inventory/assets")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await factory.ClientAs("STUDENT").GetAsync("/api/inventory/assets")).StatusCode);
        var seeded = await AvailabilityAsync(Guid.Parse("e0000000-0000-0000-0000-000000000004"));
        Assert.Equal((1, 1), (seeded.Available, seeded.Total)); // single microscope for the race demo

        var staff = factory.ClientAs("LAB_STAFF");
        var (typeId, units) = await factory.NewStockAsync(2);
        var unit = units[0];
        var duplicate = await staff.PostAsJsonAsync("/api/inventory/assets", new CreateAssetRequest(typeId, unit.SerialNumber.ToLowerInvariant(), "x", null));
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode); // serials are normalized upper-case + unique
        var page = await staff.GetFromJsonAsync<PagedResponse<AssetResponse>>($"/api/inventory/assets?equipmentTypeId={typeId}&sortBy=serialNumber&direction=desc&pageSize=1", Json);
        Assert.Equal((2, 1), (page!.Total, page.Items.Count));
        Assert.Equal(HttpStatusCode.BadRequest, (await staff.GetAsync("/api/inventory/assets?status=BROKEN")).StatusCode);

        var updated = await (await staff.PutAsJsonAsync($"/api/inventory/assets/{unit.Id}", new UpdateAssetRequest(typeId, unit.SerialNumber, "Kho B", "đã hiệu chuẩn")))
            .Content.ReadFromJsonAsync<AssetResponse>(Json);
        Assert.Equal("Kho B", updated!.Location);

        Assert.Equal(HttpStatusCode.Created, (await staff.PostAsJsonAsync($"/api/inventory/assets/{unit.Id}/maintenance", new StartMaintenanceRequest("Vỡ đầu dò"))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await staff.PostAsJsonAsync($"/api/inventory/assets/{unit.Id}/maintenance", new StartMaintenanceRequest("lần 2"))).StatusCode);
        Assert.Equal((1, 2), Tuple(await AvailabilityAsync(typeId)));
        var completed = await (await staff.PostAsJsonAsync($"/api/inventory/assets/{unit.Id}/maintenance/complete", new CompleteMaintenanceRequest("OK")))
            .Content.ReadFromJsonAsync<AssetResponse>(Json);
        Assert.Equal("AVAILABLE", completed!.Status);
        var history = await staff.GetFromJsonAsync<List<MaintenanceResponse>>($"/api/inventory/assets/{unit.Id}/maintenance", Json);
        Assert.NotNull(history!.Single().CompletedAt);

        Assert.Equal(HttpStatusCode.NoContent, (await staff.DeleteAsync($"/api/inventory/assets/{unit.Id}")).StatusCode);
        Assert.Equal((1, 1), Tuple(await AvailabilityAsync(typeId))); // retired units leave the total

        // Beyond repair: retiring a unit under maintenance closes its open maintenance record.
        var broken = units[1];
        Assert.Equal(HttpStatusCode.Created, (await staff.PostAsJsonAsync($"/api/inventory/assets/{broken.Id}/maintenance", new StartMaintenanceRequest("Hỏng mạch nguồn"))).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await staff.DeleteAsync($"/api/inventory/assets/{broken.Id}")).StatusCode);
        var closed = (await staff.GetFromJsonAsync<List<MaintenanceResponse>>($"/api/inventory/assets/{broken.Id}/maintenance", Json))!.Single();
        Assert.NotNull(closed.CompletedAt);
        Assert.NotNull(closed.CompletedBy);
    }

    [Fact] // T27
    public async Task Grpc_business_calls_require_the_service_token()
    {
        var request = new V1.CheckAvailabilityRequest { EquipmentTypeIds = { Guid.NewGuid().ToString() } };
        var missing = await Assert.ThrowsAsync<RpcException>(() => grpc.CheckAvailabilityAsync(request).ResponseAsync);
        Assert.Equal(StatusCode.Unauthenticated, missing.StatusCode);
        var wrong = await Assert.ThrowsAsync<RpcException>(() =>
            grpc.CheckAvailabilityAsync(request, new Metadata { { "authorization", "Bearer not-the-token" } }).ResponseAsync);
        Assert.Equal(StatusCode.Unauthenticated, wrong.StatusCode);

        var (typeId, _) = await factory.NewStockAsync(1);
        await Assert.ThrowsAsync<RpcException>(() => grpc.ReserveAssetsAsync(Reserve(Guid.NewGuid(), typeId)).ResponseAsync);
        Assert.Equal(1, (await AvailabilityAsync(typeId)).Available); // nothing reserved without credentials
    }

    [Fact] // T07
    public async Task Concurrent_reservations_of_the_last_unit_have_exactly_one_winner()
    {
        var (typeId, _) = await factory.NewStockAsync(1);
        var attempts = Enumerable.Range(0, 8).Select(async _ =>
        {
            try { await grpc.ReserveAssetsAsync(Reserve(Guid.NewGuid(), typeId), factory.ServiceAuth); return "OK"; }
            catch (RpcException ex) { return ex.Trailers.GetValue("lablend-code") ?? ex.StatusCode.ToString(); }
        });
        var outcomes = await Task.WhenAll(attempts);
        Assert.Equal(1, outcomes.Count(o => o == "OK"));
        Assert.All(outcomes.Where(o => o != "OK"), o => Assert.Equal(InventoryException.InsufficientStockCode, o));
        Assert.Equal((0, 1), Tuple(await AvailabilityAsync(typeId)));
    }

    [Fact] // T08
    public async Task Multi_item_reservation_is_all_or_nothing()
    {
        var (a, aUnits) = await factory.NewStockAsync(1);
        var (b, _) = await factory.NewStockAsync(1);
        var tooMany = Reserve(Guid.NewGuid(), a, b, b);
        var ex = await Assert.ThrowsAsync<RpcException>(() => grpc.ReserveAssetsAsync(tooMany, factory.ServiceAuth).ResponseAsync);
        Assert.Equal(StatusCode.FailedPrecondition, ex.StatusCode);
        Assert.Equal(1, (await AvailabilityAsync(a)).Available); // the unit locked for item 1 was rolled back
        Assert.Equal(["-→AVAILABLE"], await MovementsAsync(aUnits[0].Id)); // ...and so was its movement row
        Assert.Equal(1, (await AvailabilityAsync(b)).Available);
        var status = await grpc.GetOperationStatusAsync(new V1.OperationStatusRequest { OperationId = tooMany.OperationId }, factory.ServiceAuth);
        Assert.False(status.Found);
    }

    [Fact] // T09: Inventory committed but the reply was lost -> status and replay return the same unit, never a second one
    public async Task Lost_reply_is_resolved_by_status_and_replay_without_double_allocation()
    {
        var (typeId, _) = await factory.NewStockAsync(2);
        var reserve = Reserve(Guid.NewGuid(), typeId);
        await grpc.ReserveAssetsAsync(reserve, factory.ServiceAuth); // pretend this reply never reached Borrowing

        var status = await grpc.GetOperationStatusAsync(new V1.OperationStatusRequest { OperationId = reserve.OperationId }, factory.ServiceAuth);
        Assert.True(status.Found);
        Assert.Equal(reserve.LoanId, status.LoanId);
        var replay = await grpc.ReserveAssetsAsync(reserve, factory.ServiceAuth);
        Assert.Equal(status.Allocations.Single().AssetUnitId, replay.Allocations.Single().AssetUnitId);
        Assert.Equal((1, 2), Tuple(await AvailabilityAsync(typeId))); // still exactly one of the two units held
    }

    [Fact] // T06 server side, T10, T11, idempotent replay
    public async Task Reserve_handover_return_lifecycle_is_idempotent()
    {
        var (typeId, _) = await factory.NewStockAsync(2);
        var reserve = Reserve(Guid.NewGuid(), typeId, typeId);
        var first = await grpc.ReserveAssetsAsync(reserve, factory.ServiceAuth);
        Assert.All(first.Allocations, x => Assert.Equal(("HELD", "RESERVED"), (x.ReservationStatus, x.AssetStatus)));
        var replay = await grpc.ReserveAssetsAsync(reserve, factory.ServiceAuth);
        Assert.Equal(first.Allocations.Select(x => x.AssetUnitId).Order(), replay.Allocations.Select(x => x.AssetUnitId).Order());
        var reused = reserve.Clone();
        reused.Items.RemoveAt(1);
        Assert.Equal(StatusCode.AlreadyExists, (await Assert.ThrowsAsync<RpcException>(() => grpc.ReserveAssetsAsync(reused, factory.ServiceAuth).ResponseAsync)).StatusCode);
        Assert.True((await grpc.GetOperationStatusAsync(new V1.OperationStatusRequest { OperationId = reserve.OperationId }, factory.ServiceAuth)).Found);

        var loan = new V1.LoanRequest { LoanId = reserve.LoanId };
        await grpc.ConfirmHandoverAsync(loan, factory.ServiceAuth);
        var handedOver = await grpc.ConfirmHandoverAsync(loan, factory.ServiceAuth); // replay
        Assert.All(handedOver.Allocations, x => Assert.Equal(("HANDED_OVER", "ON_LOAN"), (x.ReservationStatus, x.AssetStatus)));

        var items = reserve.Items.Select(x => x.LoanItemId).ToList();
        var returnRequest = new V1.ReturnAssetsRequest
        {
            LoanId = reserve.LoanId,
            Items =
            {
                new V1.ReturnItem { LoanItemId = items[0], Condition = V1.ReturnCondition.Normal },
                new V1.ReturnItem { LoanItemId = items[1], Condition = V1.ReturnCondition.Damaged }
            }
        };
        await grpc.ReturnAssetsAsync(returnRequest, factory.ServiceAuth);
        var returned = await grpc.ReturnAssetsAsync(returnRequest, factory.ServiceAuth); // replay
        var byItem = returned.Allocations.ToDictionary(x => x.LoanItemId, x => x.AssetStatus);
        Assert.Equal(("AVAILABLE", "MAINTENANCE"), (byItem[items[0]], byItem[items[1]])); // T11
        var damagedUnit = returned.Allocations.Single(x => x.LoanItemId == items[1]).AssetUnitId;
        var history = await factory.ClientAs("ADMIN").GetFromJsonAsync<List<MaintenanceResponse>>($"/api/inventory/assets/{damagedUnit}/maintenance", Json);
        Assert.Single(history!);
        Assert.Equal(1, (await AvailabilityAsync(typeId)).Available); // the damaged unit cannot be reserved
    }

    [Fact] // StockMovements: every status change, with who (staff) or which loan (gRPC) caused it
    public async Task Every_status_change_is_recorded_as_a_stock_movement()
    {
        var (typeId, units) = await factory.NewStockAsync(1);
        var unitId = units[0].Id;
        var reserve = Reserve(Guid.NewGuid(), typeId);
        var loan = new V1.LoanRequest { LoanId = reserve.LoanId };
        var returnDamaged = new V1.ReturnAssetsRequest
        {
            LoanId = reserve.LoanId,
            Items = { new V1.ReturnItem { LoanItemId = reserve.Items[0].LoanItemId, Condition = V1.ReturnCondition.Damaged } }
        };
        // Each call twice: replays must not write duplicate movements.
        for (var i = 0; i < 2; i++) await grpc.ReserveAssetsAsync(reserve, factory.ServiceAuth);
        for (var i = 0; i < 2; i++) await grpc.ConfirmHandoverAsync(loan, factory.ServiceAuth);
        for (var i = 0; i < 2; i++) await grpc.ReturnAssetsAsync(returnDamaged, factory.ServiceAuth);
        var staff = factory.ClientAs("LAB_STAFF");
        (await staff.PostAsJsonAsync($"/api/inventory/assets/{unitId}/maintenance/complete", new CompleteMaintenanceRequest(null))).EnsureSuccessStatusCode();
        (await staff.DeleteAsync($"/api/inventory/assets/{unitId}")).EnsureSuccessStatusCode();
        (await staff.DeleteAsync($"/api/inventory/assets/{unitId}")).EnsureSuccessStatusCode(); // idempotent retire

        var movements = (await staff.GetFromJsonAsync<List<StockMovementResponse>>($"/api/inventory/assets/{unitId}/movements", Json))!;
        movements.Reverse(); // API is newest first
        Assert.Equal(["-→AVAILABLE", "AVAILABLE→RESERVED", "RESERVED→ON_LOAN", "ON_LOAN→MAINTENANCE", "MAINTENANCE→AVAILABLE", "AVAILABLE→RETIRED"],
            movements.Select(Arrow));
        var loanId = Guid.Parse(reserve.LoanId);
        Assert.All(movements[1..4], m => Assert.Equal((loanId, (Guid?)null), (m.LoanId, m.ChangedBy))); // gRPC: loan, no user
        Assert.All(movements.Where((_, i) => i is 0 or 4 or 5), m => Assert.True(m.LoanId is null && m.ChangedBy is not null)); // REST: staff user
        Assert.Equal(HttpStatusCode.NotFound, (await staff.GetAsync($"/api/inventory/assets/{Guid.NewGuid()}/movements")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await factory.ClientAs("STUDENT").GetAsync($"/api/inventory/assets/{unitId}/movements")).StatusCode);
    }

    [Fact] // compensation used by Borrowing reconciliation (T28 server side)
    public async Task Release_frees_held_units_and_blocks_replaying_the_released_operation()
    {
        var (typeId, units) = await factory.NewStockAsync(1);
        var reserve = Reserve(Guid.NewGuid(), typeId);
        await grpc.ReserveAssetsAsync(reserve, factory.ServiceAuth);
        var loan = new V1.LoanRequest { LoanId = reserve.LoanId };
        Assert.Equal("AVAILABLE", (await grpc.ReleaseReservationsAsync(loan, factory.ServiceAuth)).Allocations.Single().AssetStatus);
        Assert.Empty((await grpc.ReleaseReservationsAsync(loan, factory.ServiceAuth)).Allocations); // idempotent
        Assert.Equal("RESERVED→AVAILABLE", (await MovementsAsync(units[0].Id))[0]);
        Assert.Equal(StatusCode.FailedPrecondition,
            (await Assert.ThrowsAsync<RpcException>(() => grpc.ReserveAssetsAsync(reserve, factory.ServiceAuth).ResponseAsync)).StatusCode);
        Assert.Equal(StatusCode.NotFound,
            (await Assert.ThrowsAsync<RpcException>(() => grpc.ConfirmHandoverAsync(loan, factory.ServiceAuth).ResponseAsync)).StatusCode);
        Assert.Equal(StatusCode.InvalidArgument,
            (await Assert.ThrowsAsync<RpcException>(() => grpc.ConfirmHandoverAsync(new V1.LoanRequest { LoanId = "nope" }, factory.ServiceAuth).ResponseAsync)).StatusCode);
    }

    private static V1.ReserveAssetsRequest Reserve(Guid loanId, params Guid[] equipmentTypeIds) => new()
    {
        OperationId = Guid.NewGuid().ToString(),
        LoanId = loanId.ToString(),
        Items = { equipmentTypeIds.Select(t => new V1.ReserveItem { LoanItemId = Guid.NewGuid().ToString(), EquipmentTypeId = t.ToString() }) }
    };

    private async Task<AvailabilityResponse> AvailabilityAsync(Guid typeId) =>
        (await factory.ClientAs(null).GetFromJsonAsync<List<AvailabilityResponse>>($"/api/inventory/availability?equipmentTypeIds={typeId}", Json))!.Single();

    private static (int, int) Tuple(AvailabilityResponse a) => (a.Available, a.Total);

    private static string Arrow(StockMovementResponse m) => $"{m.FromStatus ?? "-"}→{m.ToStatus}";

    /// <summary>Movements as "FROM→TO", newest first.</summary>
    private async Task<List<string>> MovementsAsync(Guid unitId) =>
        (await factory.ClientAs("ADMIN").GetFromJsonAsync<List<StockMovementResponse>>($"/api/inventory/assets/{unitId}/movements", Json))!
            .Select(Arrow).ToList();
}
