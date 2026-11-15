using Inventory.Application;
using LabLend.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Inventory.Host.Controllers;

/// <summary>Staff/Admin asset management. Allocation (reserve/handover/return) is gRPC-only for Borrowing.</summary>
[ApiController]
[Route("api/inventory/assets")]
[Authorize(Policy = "StaffOrAdmin")]
public sealed class AssetsController(AssetService service) : ControllerBase
{
    /// <summary>Filter by equipmentTypeId/status/keyword (serial or location), sort serialNumber|createdAt|status, paginate.</summary>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public Task<PagedResponse<AssetResponse>> Search([FromQuery] AssetQuery query, CancellationToken ct) => service.SearchAsync(query, ct);

    [HttpGet("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<AssetResponse> Get(Guid id, CancellationToken ct) => service.GetAsync(id, ct);

    [HttpPost]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AssetResponse>> Create(CreateAssetRequest request, CancellationToken ct)
    {
        var created = await service.CreateAsync(request, UserId, ct);
        return Created($"/api/inventory/assets/{created.Id}", created);
    }

    [HttpPut("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public Task<AssetResponse> Update(Guid id, UpdateAssetRequest request, CancellationToken ct) => service.UpdateAsync(id, request, ct);

    /// <summary>Soft delete: status RETIRED. 409 while the unit is RESERVED or ON_LOAN.</summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Retire(Guid id, CancellationToken ct)
    {
        await service.RetireAsync(id, UserId, ct);
        return NoContent();
    }

    /// <summary>Status history (StockMovements), newest first: every change from creation, staff actions and Borrowing allocations.</summary>
    [HttpGet("{id:guid}/movements")]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<IReadOnlyList<StockMovementResponse>> ListMovements(Guid id, CancellationToken ct) => service.ListMovementsAsync(id, ct);

    [HttpGet("{id:guid}/maintenance")]
    public Task<IReadOnlyList<MaintenanceResponse>> ListMaintenance(Guid id, CancellationToken ct) => service.ListMaintenanceAsync(id, ct);

    /// <summary>AVAILABLE → MAINTENANCE with a reason; the unit cannot be reserved until completed.</summary>
    [HttpPost("{id:guid}/maintenance")]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<MaintenanceResponse>> StartMaintenance(Guid id, StartMaintenanceRequest request, CancellationToken ct)
    {
        var record = await service.StartMaintenanceAsync(id, request, UserId, ct);
        return Created($"/api/inventory/assets/{id}/maintenance", record);
    }

    /// <summary>MAINTENANCE → AVAILABLE and closes the open maintenance record.</summary>
    [HttpPost("{id:guid}/maintenance/complete")]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public Task<AssetResponse> CompleteMaintenance(Guid id, CompleteMaintenanceRequest request, CancellationToken ct) =>
        service.CompleteMaintenanceAsync(id, request, UserId, ct);

    private Guid? UserId => Guid.TryParse(User.FindFirst("sub")?.Value, out var id) ? id : null;
}
