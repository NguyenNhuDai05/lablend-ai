using Inventory.Application;
using Inventory.Domain;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Inventory.Infrastructure;

internal sealed class AssetRepository(InventoryDbContext db) : IAssetRepository
{
    public async Task<(IReadOnlyList<AssetUnit> Items, long Total)> SearchAsync(AssetQuery query, CancellationToken ct)
    {
        var assets = db.AssetUnits.AsNoTracking();
        if (query.EquipmentTypeId is { } typeId)
            assets = assets.Where(a => a.EquipmentTypeId == typeId);
        if (!string.IsNullOrEmpty(query.Status))
            assets = assets.Where(a => a.Status == query.Status);
        if (!string.IsNullOrWhiteSpace(query.Keyword))
        {
            var pattern = $"%{query.Keyword.Trim()}%";
            assets = assets.Where(a => EF.Functions.ILike(a.SerialNumber, pattern) || EF.Functions.ILike(a.Location, pattern));
        }

        var total = await assets.LongCountAsync(ct);
        var descending = query.Direction.Equals("desc", StringComparison.OrdinalIgnoreCase);
        var ordered = query.SortBy.ToLowerInvariant() switch
        {
            "createdat" => descending ? assets.OrderByDescending(a => a.CreatedAt) : assets.OrderBy(a => a.CreatedAt),
            "status" => descending ? assets.OrderByDescending(a => a.Status) : assets.OrderBy(a => a.Status),
            _ => descending ? assets.OrderByDescending(a => a.SerialNumber) : assets.OrderBy(a => a.SerialNumber)
        };
        var items = await ordered.ThenBy(a => a.Id)
            .Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToListAsync(ct);
        return (items, total);
    }

    public Task<AssetUnit?> FindAsync(Guid id, CancellationToken ct) => db.AssetUnits.FirstOrDefaultAsync(a => a.Id == id, ct);

    public async Task<IReadOnlyList<AvailabilityResponse>> GetAvailabilityAsync(IReadOnlyCollection<Guid> equipmentTypeIds, CancellationToken ct)
    {
        var ids = equipmentTypeIds.Distinct().ToArray();
        var counts = await db.AssetUnits.AsNoTracking()
            .Where(a => ids.Contains(a.EquipmentTypeId) && a.Status != AssetStatus.Retired && a.Status != AssetStatus.Lost)
            .GroupBy(a => a.EquipmentTypeId)
            .Select(g => new { TypeId = g.Key, Available = g.Count(a => a.Status == AssetStatus.Available), Total = g.Count() })
            .ToDictionaryAsync(c => c.TypeId, ct);
        return ids.Select(id => counts.TryGetValue(id, out var c)
            ? new AvailabilityResponse(id, c.Available, c.Total)
            : new AvailabilityResponse(id, 0, 0)).ToList();
    }

    public async Task<IReadOnlyList<MaintenanceRecord>> ListMaintenanceAsync(Guid assetUnitId, CancellationToken ct) =>
        await db.MaintenanceRecords.AsNoTracking().Where(m => m.AssetUnitId == assetUnitId)
            .OrderByDescending(m => m.OpenedAt).ToListAsync(ct);

    public Task<MaintenanceRecord?> FindOpenMaintenanceAsync(Guid assetUnitId, CancellationToken ct) =>
        db.MaintenanceRecords.FirstOrDefaultAsync(m => m.AssetUnitId == assetUnitId && m.CompletedAt == null, ct);

    // ponytail: whole history, unpaged; a unit changes status a few times per loan. Page it if an audit view needs years of data.
    public async Task<IReadOnlyList<StockMovement>> ListMovementsAsync(Guid assetUnitId, CancellationToken ct) =>
        await db.StockMovements.AsNoTracking().Where(m => m.AssetUnitId == assetUnitId)
            .OrderByDescending(m => m.Id).ToListAsync(ct);

    public void Add(AssetUnit assetUnit) => db.AssetUnits.Add(assetUnit);
    public void Add(MaintenanceRecord record) => db.MaintenanceRecords.Add(record);
    public Task SaveChangesAsync(CancellationToken ct) => db.SaveMappedAsync(ct);
}

internal sealed class AllocationRepository(InventoryDbContext db) : IAllocationRepository
{
    public Task<ReserveOperation?> FindOperationAsync(Guid operationId, CancellationToken ct) =>
        db.ReserveOperations.AsNoTracking().FirstOrDefaultAsync(o => o.OperationId == operationId, ct);

    public async Task<IReadOnlyList<Reservation>> GetByOperationAsync(Guid operationId, CancellationToken ct) =>
        await db.Reservations.Include(r => r.AssetUnit).Where(r => r.OperationId == operationId)
            .OrderBy(r => r.CreatedAt).ToListAsync(ct);

    public async Task<IReadOnlyList<Reservation>> GetByLoanAsync(Guid loanId, CancellationToken ct) =>
        await db.Reservations.Include(r => r.AssetUnit).Where(r => r.LoanId == loanId)
            .OrderBy(r => r.CreatedAt).ToListAsync(ct);

    public async Task<IReadOnlyList<Reservation>?> TryReserveAsync(ReserveOperation operation, IReadOnlyList<ReserveItem> items, CancellationToken ct)
    {
        // Disposing without commit rolls everything back, including on InsufficientStock.
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        db.ReserveOperations.Add(operation);
        try
        {
            await db.SaveChangesAsync(ct); // a concurrent duplicate blocks here until the first one commits
        }
        catch (DbUpdateException ex) when (ex.IsUniqueViolation())
        {
            db.ChangeTracker.Clear();
            return null;
        }

        var reservations = new List<Reservation>(items.Count);
        foreach (var item in items)
        {
            // SKIP LOCKED: two approvals racing for the last unit never both get it; the loser sees no row.
            var unit = await db.Database.SqlQuery<Guid>($"""
                UPDATE "AssetUnits" SET "Status" = 'RESERVED', "UpdatedAt" = now()
                WHERE "Id" = (SELECT "Id" FROM "AssetUnits"
                              WHERE "EquipmentTypeId" = {item.EquipmentTypeId} AND "Status" = 'AVAILABLE'
                              ORDER BY "SerialNumber" LIMIT 1
                              FOR UPDATE SKIP LOCKED)
                RETURNING "Id" AS "Value"
                """).ToListAsync(ct);
            if (unit.Count == 0)
            {
                db.ChangeTracker.Clear();
                throw InventoryException.InsufficientStock(item.EquipmentTypeId);
            }
            reservations.Add(new Reservation(operation.OperationId, operation.LoanId, item.LoanItemId, unit[0]));
            // The SQL update bypasses AssetUnit.SetStatus, so record its movement here (same transaction).
            db.StockMovements.Add(new StockMovement(unit[0], AssetStatus.Available, AssetStatus.Reserved, operation.LoanId, null));
        }
        db.Reservations.AddRange(reservations);
        await db.SaveMappedAsync(ct);
        await transaction.CommitAsync(ct);
        return await GetByOperationAsync(operation.OperationId, ct);
    }

    public void Add(MaintenanceRecord record) => db.MaintenanceRecords.Add(record);
    public Task SaveChangesAsync(CancellationToken ct) => db.SaveMappedAsync(ct);
}

internal static class InventoryDbContextExtensions
{
    private static readonly Dictionary<string, string> UniqueMessages = new()
    {
        ["IX_AssetUnits_SerialNumber"] = "Serial number already exists.",
        ["UX_Reservations_ActiveLoanItem"] = "Loan item already holds a unit.",
        ["UX_MaintenanceRecords_Open"] = "Unit already has an open maintenance record."
    };

    public static async Task SaveMappedAsync(this InventoryDbContext db, CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw InventoryException.ConcurrencyConflict();
        }
        catch (DbUpdateException ex) when (ex.IsUniqueViolation())
        {
            var constraint = (ex.InnerException as PostgresException)?.ConstraintName ?? "";
            throw InventoryException.Conflict(UniqueMessages.GetValueOrDefault(constraint, "Duplicate resource."));
        }
    }

    public static bool IsUniqueViolation(this DbUpdateException ex) =>
        ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };
}
