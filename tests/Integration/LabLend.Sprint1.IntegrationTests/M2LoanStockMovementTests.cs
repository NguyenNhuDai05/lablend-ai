using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Xunit;
using Xunit.Abstractions;

namespace LabLend.Sprint1.IntegrationTests;

/// <summary>
/// M2-03: Inventory's own record of a real Borrowing loan. Inventory tests drive gRPC directly with made-up loan IDs and
/// Gate A checks asset status only; this checks StockMovements, availability and timing through the real REST → gRPC path.
/// </summary>
[Collection("postgres")]
public sealed class M2LoanStockMovementTests(PostgresFixture postgres, ITestOutputHelper output)
{
    [PostgresFact]
    public async Task Real_loan_writes_one_movement_per_transition_inside_its_loan_step_and_availability_follows()
    {
        await using var stack = new Sprint2Stack(postgres);
        await stack.Start();
        var (type, units) = await stack.Stock(2);
        Assert.Equal((2, 2), await Availability());

        var id = (await stack.Submit(type))["id"]!.GetValue<Guid>();
        await stack.Command(id, "approve");
        var item = (await stack.Loan(id))["items"]![0]!;
        var (itemId, unit) = (item["id"]!.GetValue<Guid>(), item["assetUnitId"]!.GetValue<Guid>());
        var spare = units.Single(u => u != unit);
        Assert.Equal((1, 2), await Availability());

        await stack.Command(id, "handover"); await stack.Command(id, "handover"); // replay
        // Staff cannot take a unit away from a running loan, and refused attempts leave no movement.
        Assert.Equal(HttpStatusCode.Conflict, (await stack.InventoryStaff.DeleteAsync($"api/inventory/assets/{unit}")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await stack.InventoryStaff.PostAsJsonAsync($"api/inventory/assets/{unit}/maintenance", new { reason = "M2-03" })).StatusCode);
        Assert.Equal((1, 2), await Availability());

        await stack.Return(id, (itemId, "DAMAGED")); await stack.Return(id, (itemId, "DAMAGED")); // replay
        Assert.Equal((1, 2), await Availability()); // MAINTENANCE still counts in total, not available
        await Sprint2Stack.Body(await stack.InventoryStaff.PostAsJsonAsync($"api/inventory/assets/{unit}/maintenance/complete", new { notes = "M2-03 repaired" }));
        Assert.Equal((2, 2), await Availability());
        Assert.Equal(HttpStatusCode.NoContent, (await stack.InventoryStaff.DeleteAsync($"api/inventory/assets/{spare}")).StatusCode);
        Assert.Equal((1, 1), await Availability()); // RETIRED leaves the total

        var movements = (await Sprint2Stack.Body(await stack.InventoryStaff.GetAsync($"api/inventory/assets/{unit}/movements"))).AsArray().Reverse().ToArray();
        Assert.Equal(["-→AVAILABLE", "AVAILABLE→RESERVED", "RESERVED→ON_LOAN", "ON_LOAN→MAINTENANCE", "MAINTENANCE→AVAILABLE"],
            movements.Select(m => $"{(string?)m!["fromStatus"] ?? "-"}→{(string?)m["toStatus"]}"));
        Assert.All(movements[1..4], m => Assert.Equal((id, (string?)null), (m!["loanId"]!.GetValue<Guid>(), (string?)m["changedBy"]))); // Borrowing via gRPC
        Assert.All(new[] { movements[0], movements[4] }, m => Assert.Null(m!["loanId"])); // staff via REST
        Assert.Equal((string?)movements[0]!["changedBy"], (string?)movements[4]!["changedBy"]);
        Assert.NotNull((string?)movements[0]!["changedBy"]);
        Assert.Equal(["-→AVAILABLE", "AVAILABLE→RETIRED"], (await Sprint2Stack.Body(await stack.InventoryStaff.GetAsync($"api/inventory/assets/{spare}/movements")))
            .AsArray().Reverse().Select(m => $"{(string?)m!["fromStatus"] ?? "-"}→{(string?)m["toStatus"]}"));

        // Each Inventory change happens inside the Borrowing step that caused it: after the step starts, before Borrowing records it.
        var loan = await stack.Loan(id);
        DateTimeOffset History(string action) => loan["history"]!.AsArray().First(h => (string?)h!["action"] == action)!["occurredAt"]!.GetValue<DateTimeOffset>();
        DateTimeOffset At(int i) => movements[i]!["occurredAt"]!.GetValue<DateTimeOffset>();
        var returnedAt = loan["items"]![0]!["returnedAt"]!.GetValue<DateTimeOffset>();
        var timeline = new[] { At(0), History("APPROVING"), At(1), History("APPROVED"), At(2), History("HANDED_OVER"), At(3), returnedAt, At(4) };
        Assert.Equal(timeline.Order(), timeline);

        var maintenance = (await Sprint2Stack.Body(await stack.InventoryStaff.GetAsync($"api/inventory/assets/{unit}/maintenance"))).AsArray();
        var record = Assert.Single(maintenance)!;
        Assert.Contains(id.ToString(), (string?)record["reason"]);
        Assert.Null(record["openedBy"]);
        Assert.Equal((string?)movements[4]!["changedBy"], (string?)record["completedBy"]);
        output.WriteLine($"PASS M2-03: loan {id}, unit {unit}: " + string.Join(" | ", timeline.Select(t => t.ToString("HH:mm:ss.ffffff"))));

        async Task<(int, int)> Availability()
        {
            var row = (await Sprint2Stack.Body(await stack.InventoryStaff.GetAsync($"api/inventory/availability?equipmentTypeIds={type}")))[0]!;
            return (row["available"]!.GetValue<int>(), row["total"]!.GetValue<int>());
        }
    }
}
