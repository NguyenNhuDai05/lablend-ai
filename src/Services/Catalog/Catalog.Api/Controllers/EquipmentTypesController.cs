using Catalog.Application;
using LabLend.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Catalog.Api.Controllers;

[ApiController]
[Route("api/equipment-types")]
public sealed class EquipmentTypesController(EquipmentTypeService service) : ControllerBase
{
    /// <summary>Search, filter (keyword, categoryId, tag), sort (name|createdAt, asc|desc) and paginate (pageSize ≤ 100).</summary>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public Task<PagedResponse<EquipmentTypeResponse>> Search([FromQuery] EquipmentTypeQuery query, CancellationToken ct) =>
        service.SearchAsync(query, query.IncludeInactive && IsStaff, ct);

    [HttpGet("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<EquipmentTypeResponse> Get(Guid id, CancellationToken ct) => service.GetAsync(id, ct);

    [HttpPost]
    [Authorize(Policy = "StaffOrAdmin")]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<EquipmentTypeResponse>> Create(CreateEquipmentTypeRequest request, CancellationToken ct)
    {
        var created = await service.CreateAsync(request, ct);
        return Created($"/api/equipment-types/{created.Id}", created);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = "StaffOrAdmin")]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public Task<EquipmentTypeResponse> Update(Guid id, UpdateEquipmentTypeRequest request, CancellationToken ct) =>
        service.UpdateAsync(id, request, ct);

    /// <summary>Soft deactivate; the ID stays valid for loans that already reference it.</summary>
    [HttpDelete("{id:guid}")]
    [Authorize(Policy = "StaffOrAdmin")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await service.DeactivateAsync(id, ct);
        return NoContent();
    }

    private bool IsStaff => User.IsInRole(Roles.LabStaff) || User.IsInRole(Roles.Admin);
}
