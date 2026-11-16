using System.Net;
using Google.Protobuf.Reflection;
using Grpc.Core;
using Grpc.Health.V1;
using Grpc.Net.Client;
using Npgsql;
using Xunit;
using V1 = LabLend.Inventory.Contracts.V1;

namespace Inventory.IntegrationTests;

/// <summary>T23 contract tests and T09 timeout over a real HTTP/2 socket, the transport Borrowing uses in Docker.</summary>
public sealed class GrpcOverHttp2Tests(KestrelInventoryFactory factory) : IClassFixture<KestrelInventoryFactory>, IDisposable
{
    private readonly HttpVersionRecorder http = new();
    private GrpcChannel? channel;
    private GrpcChannel Channel => channel ??= GrpcChannel.ForAddress(factory.GrpcAddress, new GrpcChannelOptions { HttpHandler = http });
    private V1.InventoryAllocation.InventoryAllocationClient Client => new(Channel);

    [Fact] // T23: renaming, renumbering or retyping anything below breaks Borrowing's generated client -> must be a v2, not an edit
    public void Proto_v1_schema_is_unchanged()
    {
        var file = V1.InventoryReflection.Descriptor;
        var actual = string.Join("\n", new[] { $"package {file.Package};" }
            .Concat(file.Services.SelectMany(s => s.Methods.Select(m => $"{s.Name}.{m.Name}({m.InputType.Name}) returns ({m.OutputType.Name})")))
            .Concat(file.MessageTypes.Select(m => $"message {m.Name} {{ {string.Join(" ", m.Fields.InFieldNumberOrder().Select(f => $"{Type(f)} {f.Name} = {f.FieldNumber};"))} }}"))
            .Concat(file.EnumTypes.Select(e => $"enum {e.Name} {{ {string.Join(" ", e.Values.Select(v => $"{v.Name} = {v.Number};"))} }}")));
        const string expected = """
            package lablend.inventory.v1;
            InventoryAllocation.CheckAvailability(CheckAvailabilityRequest) returns (CheckAvailabilityReply)
            InventoryAllocation.ReserveAssets(ReserveAssetsRequest) returns (AllocationReply)
            InventoryAllocation.ConfirmHandover(LoanRequest) returns (AllocationReply)
            InventoryAllocation.ReturnAssets(ReturnAssetsRequest) returns (AllocationReply)
            InventoryAllocation.ReleaseReservations(LoanRequest) returns (AllocationReply)
            InventoryAllocation.GetOperationStatus(OperationStatusRequest) returns (OperationStatusReply)
            message CheckAvailabilityRequest { repeated string equipment_type_ids = 1; }
            message Availability { string equipment_type_id = 1; int32 available = 2; int32 total = 3; }
            message CheckAvailabilityReply { repeated Availability items = 1; }
            message ReserveItem { string loan_item_id = 1; string equipment_type_id = 2; }
            message ReserveAssetsRequest { string operation_id = 1; string loan_id = 2; repeated ReserveItem items = 3; }
            message LoanRequest { string loan_id = 1; }
            message ReturnItem { string loan_item_id = 1; ReturnCondition condition = 2; }
            message ReturnAssetsRequest { string loan_id = 1; repeated ReturnItem items = 2; }
            message Allocation { string loan_item_id = 1; string asset_unit_id = 2; string serial_number = 3; string reservation_status = 4; string asset_status = 5; }
            message AllocationReply { string loan_id = 1; repeated Allocation allocations = 2; }
            message OperationStatusRequest { string operation_id = 1; }
            message OperationStatusReply { bool found = 1; string loan_id = 2; repeated Allocation allocations = 3; }
            enum ReturnCondition { RETURN_CONDITION_UNSPECIFIED = 0; RETURN_CONDITION_NORMAL = 1; RETURN_CONDITION_DAMAGED = 2; RETURN_CONDITION_LOST = 3; }
            """;
        Assert.Equal(expected.ReplaceLineEndings("\n"), actual);
    }

    [Fact] // T23: a separate client reaches every RPC over a real HTTP/2 connection
    public async Task Every_rpc_works_between_separate_client_and_server_over_http2()
    {
        var health = await new Health.HealthClient(Channel).CheckAsync(new HealthCheckRequest()); // anonymous by design
        Assert.Equal(HealthCheckResponse.Types.ServingStatus.Serving, health.Status);
        var anonymous = await Assert.ThrowsAsync<RpcException>(() =>
            Client.CheckAvailabilityAsync(new V1.CheckAvailabilityRequest { EquipmentTypeIds = { Guid.NewGuid().ToString() } }).ResponseAsync);
        Assert.Equal(StatusCode.Unauthenticated, anonymous.StatusCode); // T27 holds on the real transport too

        var (typeId, _) = await factory.NewStockAsync(2);
        var auth = factory.ServiceAuth;
        var stock = await Client.CheckAvailabilityAsync(new V1.CheckAvailabilityRequest { EquipmentTypeIds = { typeId.ToString() } }, auth);
        Assert.Equal((2, 2), (stock.Items.Single().Available, stock.Items.Single().Total));

        var loan = Reserve(typeId);
        var held = await Client.ReserveAssetsAsync(loan, auth);
        Assert.Equal("HELD", held.Allocations.Single().ReservationStatus);
        Assert.True((await Client.GetOperationStatusAsync(new V1.OperationStatusRequest { OperationId = loan.OperationId }, auth)).Found);
        var onLoan = await Client.ConfirmHandoverAsync(new V1.LoanRequest { LoanId = loan.LoanId }, auth);
        Assert.Equal("ON_LOAN", onLoan.Allocations.Single().AssetStatus);
        var returned = await Client.ReturnAssetsAsync(new V1.ReturnAssetsRequest
        {
            LoanId = loan.LoanId,
            Items = { new V1.ReturnItem { LoanItemId = loan.Items[0].LoanItemId, Condition = V1.ReturnCondition.Normal } }
        }, auth);
        Assert.Equal(("RETURNED", "AVAILABLE"), (returned.Allocations.Single().ReservationStatus, returned.Allocations.Single().AssetStatus));

        var other = Reserve(typeId);
        await Client.ReserveAssetsAsync(other, auth);
        var released = await Client.ReleaseReservationsAsync(new V1.LoanRequest { LoanId = other.LoanId }, auth);
        Assert.Equal("RELEASED", released.Allocations.Single().ReservationStatus);

        Assert.NotEmpty(http.Versions);
        Assert.All(http.Versions, v => Assert.Equal(HttpVersion.Version20, v));
    }

    [Fact] // T09: Borrowing's deadline expires mid-reserve -> nothing half-done, and the same operation_id recovers
    public async Task Reserve_that_times_out_rolls_back_and_the_same_operation_recovers()
    {
        var (typeId, _) = await factory.NewStockAsync(1);
        var reserve = Reserve(typeId);
        var auth = factory.ServiceAuth;
        await using (var db = await factory.OpenDatabaseAsync())
        await using (var tx = await db.BeginTransactionAsync())
        {
            // Plain reads still pass; the server's INSERT of the operation row blocks until the deadline.
            await new NpgsqlCommand("""LOCK TABLE "ReserveOperations" IN EXCLUSIVE MODE""", db, tx).ExecuteNonQueryAsync();
            var timeout = await Assert.ThrowsAsync<RpcException>(() =>
                Client.ReserveAssetsAsync(reserve, auth, DateTime.UtcNow.AddMilliseconds(500)).ResponseAsync);
            Assert.Equal(StatusCode.DeadlineExceeded, timeout.StatusCode);
            // Release the lock only after the server cancelled its blocked statement (deadline -> CancellationToken -> PG cancel).
            await WaitUntilAsync(async () => (long)(await new NpgsqlCommand("SELECT count(*) FROM pg_locks WHERE NOT granted", db, tx).ExecuteScalarAsync())! == 0);
        }

        var status = await Client.GetOperationStatusAsync(new V1.OperationStatusRequest { OperationId = reserve.OperationId }, auth);
        Assert.False(status.Found); // unknown result resolves to "nothing reserved": Borrowing must not mark the loan APPROVED
        var stock = await Client.CheckAvailabilityAsync(new V1.CheckAvailabilityRequest { EquipmentTypeIds = { typeId.ToString() } }, auth);
        Assert.Equal(1, stock.Items.Single().Available);

        var retried = await Client.ReserveAssetsAsync(reserve, auth); // recovery = retry with the same operation_id
        Assert.Equal(("HELD", "RESERVED"), (retried.Allocations.Single().ReservationStatus, retried.Allocations.Single().AssetStatus));
    }

    public void Dispose() { channel?.Dispose(); http.Dispose(); }

    private static V1.ReserveAssetsRequest Reserve(Guid equipmentTypeId) => new()
    {
        OperationId = Guid.NewGuid().ToString(),
        LoanId = Guid.NewGuid().ToString(),
        Items = { new V1.ReserveItem { LoanItemId = Guid.NewGuid().ToString(), EquipmentTypeId = equipmentTypeId.ToString() } }
    };

    private static string Type(FieldDescriptor f) => (f.IsRepeated ? "repeated " : "") + f.FieldType switch
    {
        FieldType.Message => f.MessageType.Name,
        FieldType.Enum => f.EnumType.Name,
        var scalar => scalar.ToString().ToLowerInvariant()
    };

    private static async Task WaitUntilAsync(Func<Task<bool>> condition)
    {
        for (var deadline = DateTime.UtcNow.AddSeconds(10); !await condition(); await Task.Delay(50))
            if (DateTime.UtcNow > deadline) throw new TimeoutException("Condition not met within 10 s.");
    }
}
