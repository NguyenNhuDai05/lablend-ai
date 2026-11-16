using System.Security.Cryptography;
using System.Text;
using Inventory.Domain;

namespace Inventory.Application;

/// <summary>Borrowing-facing allocation use cases. Every operation is safe to retry.</summary>
public sealed class AllocationService(IAllocationRepository allocations, AssetService assets)
{
    private const int MaxItems = 50;
    private static readonly string[] Conditions = [ReturnCondition.Normal, ReturnCondition.Damaged, ReturnCondition.Lost];

    public Task<IReadOnlyList<AvailabilityResponse>> CheckAvailabilityAsync(IReadOnlyCollection<Guid> equipmentTypeIds, CancellationToken ct) =>
        assets.GetAvailabilityAsync(equipmentTypeIds, ct);

    /// <summary>All-or-nothing; replaying the same operationId + payload returns the original allocation.</summary>
    public async Task<IReadOnlyList<AllocationResult>> ReserveAsync(ReserveCommand command, CancellationToken ct)
    {
        Validate(command);
        var hash = Hash(command);
        var existing = await allocations.FindOperationAsync(command.OperationId, ct);
        if (existing is null)
        {
            var reserved = await allocations.TryReserveAsync(new ReserveOperation(command.OperationId, command.LoanId, hash), command.Items, ct);
            if (reserved is not null)
                return reserved.Select(Map).ToList();
            existing = await allocations.FindOperationAsync(command.OperationId, ct) ?? throw InventoryException.ConcurrencyConflict();
        }
        if (existing.RequestHash != hash)
            throw InventoryException.OperationIdReused(command.OperationId);
        var replay = await allocations.GetByOperationAsync(command.OperationId, ct);
        if (replay.Any(r => r.Status == ReservationStatus.Released))
            throw InventoryException.Conflict($"Operation '{command.OperationId}' was released; reserve again with a new operationId.");
        return replay.Select(Map).ToList();
    }

    public async Task<IReadOnlyList<AllocationResult>> HandOverAsync(Guid loanId, CancellationToken ct)
    {
        var active = (await allocations.GetByLoanAsync(RequireId(loanId, "loanId"), ct))
            .Where(r => r.Status is ReservationStatus.Held or ReservationStatus.HandedOver).ToList();
        if (active.Count == 0)
            throw InventoryException.NotFound($"Loan '{loanId}' has no reserved units to hand over.");
        active.ForEach(r => r.HandOver());
        await allocations.SaveChangesAsync(ct);
        return active.Select(Map).ToList();
    }

    public async Task<IReadOnlyList<AllocationResult>> ReturnAsync(Guid loanId, IReadOnlyList<ReturnItem> items, CancellationToken ct)
    {
        if (items.Count is 0 or > MaxItems || items.Select(i => i.LoanItemId).Distinct().Count() != items.Count)
            throw InventoryException.Invalid($"Provide 1-{MaxItems} distinct loan items to return.");
        if (items.FirstOrDefault(i => !Conditions.Contains(i.Condition)) is { } bad)
            throw InventoryException.Invalid($"Unknown return condition '{bad.Condition}' for loan item {bad.LoanItemId}.");

        var reservations = await allocations.GetByLoanAsync(RequireId(loanId, "loanId"), ct);
        foreach (var item in items)
        {
            var reservation = reservations.FirstOrDefault(r => r.LoanItemId == item.LoanItemId
                    && r.Status is ReservationStatus.HandedOver or ReservationStatus.Returned)
                ?? throw InventoryException.NotFound($"Loan item '{item.LoanItemId}' is not on loan.");
            if (reservation.Status == ReservationStatus.Returned) continue; // replay
            reservation.Return(item.Condition);
            if (item.Condition == ReturnCondition.Damaged)
                allocations.Add(new MaintenanceRecord(reservation.AssetUnitId, $"Returned DAMAGED from loan {loanId}.", null));
        }
        await allocations.SaveChangesAsync(ct);
        return reservations.Where(r => r.Status != ReservationStatus.Released).Select(Map).ToList();
    }

    /// <summary>Compensation/expiry: frees every HELD unit of the loan. Unknown loan = nothing to release.</summary>
    public async Task<IReadOnlyList<AllocationResult>> ReleaseAsync(Guid loanId, CancellationToken ct)
    {
        var held = (await allocations.GetByLoanAsync(RequireId(loanId, "loanId"), ct))
            .Where(r => r.Status == ReservationStatus.Held).ToList();
        held.ForEach(r => r.Release());
        await allocations.SaveChangesAsync(ct);
        return held.Select(Map).ToList();
    }

    public async Task<OperationStatus> GetOperationAsync(Guid operationId, CancellationToken ct)
    {
        var operation = await allocations.FindOperationAsync(RequireId(operationId, "operationId"), ct);
        if (operation is null)
            return new(false, null, []); // never applied: nothing is held for it
        var reservations = await allocations.GetByOperationAsync(operationId, ct);
        return new(true, operation.LoanId, reservations.Select(Map).ToList());
    }

    private static void Validate(ReserveCommand c)
    {
        RequireId(c.OperationId, "operationId");
        RequireId(c.LoanId, "loanId");
        if (c.Items.Count is 0 or > MaxItems)
            throw InventoryException.Invalid($"A reservation needs 1-{MaxItems} items.");
        if (c.Items.Any(i => i.LoanItemId == Guid.Empty || i.EquipmentTypeId == Guid.Empty))
            throw InventoryException.Invalid("Every item needs loanItemId and equipmentTypeId.");
        if (c.Items.Select(i => i.LoanItemId).Distinct().Count() != c.Items.Count)
            throw InventoryException.Invalid("Duplicate loanItemId in reservation.");
    }

    private static Guid RequireId(Guid id, string name) =>
        id != Guid.Empty ? id : throw InventoryException.Invalid($"{name} is required.");

    private static string Hash(ReserveCommand c)
    {
        var canonical = $"{c.LoanId}|" + string.Join(",", c.Items.OrderBy(i => i.LoanItemId).Select(i => $"{i.LoanItemId}:{i.EquipmentTypeId}"));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    private static AllocationResult Map(Reservation r) =>
        new(r.LoanItemId, r.AssetUnitId, r.AssetUnit!.SerialNumber, r.Status, r.AssetUnit.Status);
}
