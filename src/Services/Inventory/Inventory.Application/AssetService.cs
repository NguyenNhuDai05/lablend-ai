using Inventory.Domain;
using LabLend.Contracts;

namespace Inventory.Application;

public sealed class AssetService(IAssetRepository assets)
{
    public async Task<PagedResponse<AssetResponse>> SearchAsync(AssetQuery query, CancellationToken ct)
    {
        var (items, total) = await assets.SearchAsync(query, ct);
        return new(items.Select(ToResponse).ToList(), total, query.Page, query.PageSize);
    }

    public async Task<AssetResponse> GetAsync(Guid id, CancellationToken ct) => ToResponse(await RequireAsync(id, ct));

    public async Task<AssetResponse> CreateAsync(CreateAssetRequest request, Guid? userId, CancellationToken ct)
    {
        RequireEquipmentType(request.EquipmentTypeId);
        var asset = new AssetUnit(request.EquipmentTypeId, request.SerialNumber, request.Location, request.Notes, userId);
        assets.Add(asset);
        await assets.SaveChangesAsync(ct);
        return ToResponse(asset);
    }

    public async Task<AssetResponse> UpdateAsync(Guid id, UpdateAssetRequest request, CancellationToken ct)
    {
        RequireEquipmentType(request.EquipmentTypeId);
        var asset = await RequireAsync(id, ct);
        asset.UpdateDetails(request.EquipmentTypeId, request.SerialNumber, request.Location, request.Notes);
        await assets.SaveChangesAsync(ct);
        return ToResponse(asset);
    }

    public async Task RetireAsync(Guid id, Guid? userId, CancellationToken ct)
    {
        var asset = await RequireAsync(id, ct);
        if (asset.Status == AssetStatus.Retired) return; // DELETE is idempotent
        asset.Retire(userId);
        // Beyond repair: retiring a unit under maintenance closes its open record instead of leaving it open forever.
        (await assets.FindOpenMaintenanceAsync(id, ct))?.Complete("Closed: unit retired.", userId);
        await assets.SaveChangesAsync(ct);
    }

    public async Task<MaintenanceResponse> StartMaintenanceAsync(Guid id, StartMaintenanceRequest request, Guid? userId, CancellationToken ct)
    {
        var asset = await RequireAsync(id, ct);
        asset.StartMaintenance(userId);
        var record = new MaintenanceRecord(asset.Id, request.Reason, userId);
        assets.Add(record);
        await assets.SaveChangesAsync(ct);
        return ToResponse(record);
    }

    public async Task<AssetResponse> CompleteMaintenanceAsync(Guid id, CompleteMaintenanceRequest request, Guid? userId, CancellationToken ct)
    {
        var asset = await RequireAsync(id, ct);
        asset.CompleteMaintenance(userId);
        (await assets.FindOpenMaintenanceAsync(id, ct))?.Complete(request.Notes, userId);
        await assets.SaveChangesAsync(ct);
        return ToResponse(asset);
    }

    /// <summary>Newest first.</summary>
    public async Task<IReadOnlyList<StockMovementResponse>> ListMovementsAsync(Guid id, CancellationToken ct)
    {
        await RequireAsync(id, ct);
        return (await assets.ListMovementsAsync(id, ct))
            .Select(m => new StockMovementResponse(m.Id, m.AssetUnitId, m.FromStatus, m.ToStatus, m.LoanId, m.ChangedBy, m.OccurredAt))
            .ToList();
    }

    public async Task<IReadOnlyList<MaintenanceResponse>> ListMaintenanceAsync(Guid id, CancellationToken ct)
    {
        await RequireAsync(id, ct);
        return (await assets.ListMaintenanceAsync(id, ct)).Select(ToResponse).ToList();
    }

    public Task<IReadOnlyList<AvailabilityResponse>> GetAvailabilityAsync(IReadOnlyCollection<Guid> equipmentTypeIds, CancellationToken ct)
    {
        if (equipmentTypeIds.Count is 0 or > 100)
            throw InventoryException.Invalid("Provide between 1 and 100 equipmentTypeIds.");
        return assets.GetAvailabilityAsync(equipmentTypeIds, ct);
    }

    // ponytail: equipment type IDs are logical Catalog references, not verified over the network.
    private static void RequireEquipmentType(Guid equipmentTypeId)
    {
        if (equipmentTypeId == Guid.Empty)
            throw InventoryException.Invalid("equipmentTypeId is required.");
    }

    private async Task<AssetUnit> RequireAsync(Guid id, CancellationToken ct) =>
        await assets.FindAsync(id, ct) ?? throw InventoryException.NotFound($"Asset unit '{id}' was not found.");

    private static AssetResponse ToResponse(AssetUnit a) =>
        new(a.Id, a.EquipmentTypeId, a.SerialNumber, a.Location, a.Notes, a.Status, a.CreatedAt, a.UpdatedAt);

    private static MaintenanceResponse ToResponse(MaintenanceRecord m) =>
        new(m.Id, m.AssetUnitId, m.Reason, m.OpenedBy, m.OpenedAt, m.CompletedAt, m.CompletedBy, m.CompletionNotes);
}
