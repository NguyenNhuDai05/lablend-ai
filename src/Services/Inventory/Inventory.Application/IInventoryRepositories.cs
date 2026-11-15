using Inventory.Domain;

namespace Inventory.Application;

// Implemented in Inventory.Infrastructure. SaveChangesAsync maps unique/concurrency violations to InventoryException.
public interface IAssetRepository
{
    Task<(IReadOnlyList<AssetUnit> Items, long Total)> SearchAsync(AssetQuery query, CancellationToken ct);
    Task<AssetUnit?> FindAsync(Guid id, CancellationToken ct);
    Task<IReadOnlyList<AvailabilityResponse>> GetAvailabilityAsync(IReadOnlyCollection<Guid> equipmentTypeIds, CancellationToken ct);
    Task<IReadOnlyList<MaintenanceRecord>> ListMaintenanceAsync(Guid assetUnitId, CancellationToken ct);
    Task<MaintenanceRecord?> FindOpenMaintenanceAsync(Guid assetUnitId, CancellationToken ct);
    /// <summary>Status history of one unit, newest first.</summary>
    Task<IReadOnlyList<StockMovement>> ListMovementsAsync(Guid assetUnitId, CancellationToken ct);
    void Add(AssetUnit assetUnit);
    void Add(MaintenanceRecord record);
    Task SaveChangesAsync(CancellationToken ct);
}

public interface IAllocationRepository
{
    Task<ReserveOperation?> FindOperationAsync(Guid operationId, CancellationToken ct);
    Task<IReadOnlyList<Reservation>> GetByOperationAsync(Guid operationId, CancellationToken ct);
    /// <summary>Tracked reservations of a loan including their AssetUnit.</summary>
    Task<IReadOnlyList<Reservation>> GetByLoanAsync(Guid loanId, CancellationToken ct);

    /// <summary>
    /// In ONE local transaction: records the operation and locks one AVAILABLE unit per item
    /// (FOR UPDATE SKIP LOCKED). All items are reserved or none. Throws InsufficientStock on shortage.
    /// Returns null when the same operationId was committed concurrently (caller replays it).
    /// </summary>
    Task<IReadOnlyList<Reservation>?> TryReserveAsync(ReserveOperation operation, IReadOnlyList<ReserveItem> items, CancellationToken ct);

    void Add(MaintenanceRecord record);
    Task SaveChangesAsync(CancellationToken ct);
}
