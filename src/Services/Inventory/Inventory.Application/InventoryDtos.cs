using System.ComponentModel.DataAnnotations;

namespace Inventory.Application;

public sealed record AssetResponse(Guid Id, Guid EquipmentTypeId, string SerialNumber, string Location, string? Notes,
    string Status, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

public sealed record CreateAssetRequest(
    Guid EquipmentTypeId,
    [Required, StringLength(64, MinimumLength = 2)] string SerialNumber,
    [Required, StringLength(150)] string Location,
    [StringLength(1000)] string? Notes);

public sealed record UpdateAssetRequest(
    Guid EquipmentTypeId,
    [Required, StringLength(64, MinimumLength = 2)] string SerialNumber,
    [Required, StringLength(150)] string Location,
    [StringLength(1000)] string? Notes);

public sealed record StartMaintenanceRequest([Required, StringLength(1000)] string Reason);

public sealed record CompleteMaintenanceRequest([StringLength(1000)] string? Notes);

public sealed record MaintenanceResponse(Guid Id, Guid AssetUnitId, string Reason, Guid? OpenedBy, DateTimeOffset OpenedAt,
    DateTimeOffset? CompletedAt, Guid? CompletedBy, string? CompletionNotes);

/// <summary>Available = units that can be reserved now; Total excludes RETIRED and LOST units.</summary>
public sealed record AvailabilityResponse(Guid EquipmentTypeId, int Available, int Total);

/// <summary>GET /api/inventory/assets query. sortBy/direction/status are whitelisted, pageSize is capped.</summary>
public sealed class AssetQuery
{
    public Guid? EquipmentTypeId { get; init; }
    [RegularExpression("AVAILABLE|RESERVED|ON_LOAN|MAINTENANCE|LOST|RETIRED")] public string? Status { get; init; }
    /// <summary>Matches serial number or location.</summary>
    [StringLength(100)] public string? Keyword { get; init; }
    [RegularExpression("(?i)serialNumber|createdAt|status")] public string SortBy { get; init; } = "serialNumber";
    [RegularExpression("(?i)asc|desc")] public string Direction { get; init; } = "asc";
    [Range(1, 100_000)] public int Page { get; init; } = 1;
    [Range(1, 100)] public int PageSize { get; init; } = 20;
}

// Allocation use cases (driven by the gRPC transport; proto lives in proto/inventory/v1).
public sealed record ReserveItem(Guid LoanItemId, Guid EquipmentTypeId);
public sealed record ReserveCommand(Guid OperationId, Guid LoanId, IReadOnlyList<ReserveItem> Items);
public sealed record ReturnItem(Guid LoanItemId, string Condition);
public sealed record AllocationResult(Guid LoanItemId, Guid AssetUnitId, string SerialNumber, string ReservationStatus, string AssetStatus);
public sealed record OperationStatus(bool Found, Guid? LoanId, IReadOnlyList<AllocationResult> Allocations);
