using Catalog.Domain;
using LabLend.Contracts;

namespace Catalog.Application;

public sealed class EquipmentTypeService(IEquipmentTypeRepository equipmentTypes, ICategoryRepository categories)
{
    public async Task<PagedResponse<EquipmentTypeResponse>> SearchAsync(EquipmentTypeQuery query, bool includeInactive, CancellationToken ct)
    {
        var (items, total) = await equipmentTypes.SearchAsync(query, includeInactive, ct);
        return new(items.Select(ToResponse).ToList(), total, query.Page, query.PageSize);
    }

    public async Task<EquipmentTypeResponse> GetAsync(Guid id, CancellationToken ct) => ToResponse(await RequireAsync(id, ct));

    public async Task<EquipmentTypeResponse> CreateAsync(CreateEquipmentTypeRequest request, CancellationToken ct)
    {
        var category = await RequireCategoryAsync(request.CategoryId, mustBeActive: true, ct);
        var equipmentType = new EquipmentType(category, request.Name, request.Description, request.Specifications,
            request.Tags, request.ImageUrl);
        equipmentTypes.Add(equipmentType);
        await equipmentTypes.SaveChangesAsync(ct);
        return ToResponse(equipmentType);
    }

    public async Task<EquipmentTypeResponse> UpdateAsync(Guid id, UpdateEquipmentTypeRequest request, CancellationToken ct)
    {
        var equipmentType = await RequireAsync(id, ct);
        var category = await RequireCategoryAsync(request.CategoryId, mustBeActive: request.IsActive, ct);
        equipmentType.Update(category, request.Name, request.Description, request.Specifications, request.Tags,
            request.ImageUrl, request.IsActive);
        await equipmentTypes.SaveChangesAsync(ct);
        return ToResponse(equipmentType);
    }

    public async Task DeactivateAsync(Guid id, CancellationToken ct)
    {
        var equipmentType = await RequireAsync(id, ct);
        if (!equipmentType.IsActive) return; // DELETE is idempotent
        equipmentType.Deactivate();
        await equipmentTypes.SaveChangesAsync(ct);
    }

    private async Task<EquipmentType> RequireAsync(Guid id, CancellationToken ct) =>
        await equipmentTypes.FindAsync(id, ct) ?? throw CatalogException.NotFound("Equipment type", id);

    private async Task<Category> RequireCategoryAsync(Guid categoryId, bool mustBeActive, CancellationToken ct)
    {
        var category = await categories.FindAsync(categoryId, ct)
            ?? throw CatalogException.Invalid($"Category '{categoryId}' does not exist.");
        if (mustBeActive && !category.IsActive)
            throw CatalogException.Invalid($"Category '{categoryId}' is inactive.");
        return category;
    }

    private static EquipmentTypeResponse ToResponse(EquipmentType t) =>
        new(t.Id, t.CategoryId, t.Category!.Name, t.Name, t.Description, t.Specifications, t.Tags, t.ImageUrl,
            t.IsActive, t.CreatedAt, t.UpdatedAt);
}
