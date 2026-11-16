using Inventory.Application;
using Microsoft.AspNetCore.Mvc;

namespace Inventory.Host.Controllers;

[ApiController]
[Route("api/inventory/availability")]
public sealed class AvailabilityController(AssetService service) : ControllerBase
{
    /// <summary>Public snapshot per equipment type: ?equipmentTypeIds=a&amp;equipmentTypeIds=b (1-100). Not a reservation.</summary>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public Task<IReadOnlyList<AvailabilityResponse>> Get([FromQuery] Guid[] equipmentTypeIds, CancellationToken ct) =>
        service.GetAvailabilityAsync(equipmentTypeIds, ct);
}
