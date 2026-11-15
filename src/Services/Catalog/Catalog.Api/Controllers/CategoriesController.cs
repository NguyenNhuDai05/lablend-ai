using Catalog.Application;
using LabLend.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Catalog.Api.Controllers;

[ApiController]
[Route("api/categories")]
public sealed class CategoriesController(CategoryService service) : ControllerBase
{
    /// <summary>List categories ordered by name. includeInactive is only honored for LAB_STAFF/ADMIN.</summary>
    [HttpGet]
    public Task<IReadOnlyList<CategoryResponse>> List([FromQuery] bool includeInactive, CancellationToken ct) =>
        service.ListAsync(includeInactive && IsStaff, ct);

    [HttpGet("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<CategoryResponse> Get(Guid id, CancellationToken ct) => service.GetAsync(id, ct);

    [HttpPost]
    [Authorize(Policy = "StaffOrAdmin")]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CategoryResponse>> Create(CreateCategoryRequest request, CancellationToken ct)
    {
        var created = await service.CreateAsync(request, ct);
        return Created($"/api/categories/{created.Id}", created);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = "StaffOrAdmin")]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public Task<CategoryResponse> Update(Guid id, UpdateCategoryRequest request, CancellationToken ct) =>
        service.UpdateAsync(id, request, ct);

    /// <summary>Soft deactivate. 409 while the category still has active equipment types.</summary>
    [HttpDelete("{id:guid}")]
    [Authorize(Policy = "StaffOrAdmin")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await service.DeactivateAsync(id, ct);
        return NoContent();
    }

    private bool IsStaff => User.IsInRole(Roles.LabStaff) || User.IsInRole(Roles.Admin);
}
