using Catalog.Domain;

namespace Catalog.Application;

public sealed class CategoryService(ICategoryRepository categories)
{
    public async Task<IReadOnlyList<CategoryResponse>> ListAsync(bool includeInactive, CancellationToken ct) =>
        (await categories.ListAsync(includeInactive, ct)).Select(ToResponse).ToList();

    public async Task<CategoryResponse> GetAsync(Guid id, CancellationToken ct) => ToResponse(await RequireAsync(id, ct));

    public async Task<CategoryResponse> CreateAsync(CreateCategoryRequest request, CancellationToken ct)
    {
        var category = new Category(request.Name, request.Description);
        categories.Add(category);
        await categories.SaveChangesAsync(ct);
        return ToResponse(category);
    }

    public async Task<CategoryResponse> UpdateAsync(Guid id, UpdateCategoryRequest request, CancellationToken ct)
    {
        var category = await RequireAsync(id, ct);
        if (category.IsActive && !request.IsActive)
            await EnsureNoActiveEquipmentTypesAsync(id, ct);
        category.Update(request.Name, request.Description, request.IsActive);
        await categories.SaveChangesAsync(ct);
        return ToResponse(category);
    }

    public async Task DeactivateAsync(Guid id, CancellationToken ct)
    {
        var category = await RequireAsync(id, ct);
        if (!category.IsActive) return; // DELETE is idempotent
        await EnsureNoActiveEquipmentTypesAsync(id, ct);
        category.Deactivate();
        await categories.SaveChangesAsync(ct);
    }

    private async Task EnsureNoActiveEquipmentTypesAsync(Guid id, CancellationToken ct)
    {
        if (await categories.HasActiveEquipmentTypesAsync(id, ct))
            throw CatalogException.Conflict("Category still has active equipment types; deactivate them first.");
    }

    private async Task<Category> RequireAsync(Guid id, CancellationToken ct) =>
        await categories.FindAsync(id, ct) ?? throw CatalogException.NotFound("Category", id);

    private static CategoryResponse ToResponse(Category c) =>
        new(c.Id, c.Name, c.Description, c.IsActive, c.CreatedAt, c.UpdatedAt);
}
