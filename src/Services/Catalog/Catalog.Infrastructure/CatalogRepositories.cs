using Catalog.Application;
using Catalog.Domain;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Catalog.Infrastructure;

internal sealed class CategoryRepository(CatalogDbContext db) : ICategoryRepository
{
    public async Task<IReadOnlyList<Category>> ListAsync(bool includeInactive, CancellationToken ct) =>
        await db.Categories.AsNoTracking()
            .Where(c => includeInactive || c.IsActive)
            .OrderBy(c => c.Name)
            .ToListAsync(ct);

    public Task<Category?> FindAsync(Guid id, CancellationToken ct) => db.Categories.FirstOrDefaultAsync(c => c.Id == id, ct);

    public Task<bool> HasActiveEquipmentTypesAsync(Guid categoryId, CancellationToken ct) =>
        db.EquipmentTypes.AnyAsync(t => t.CategoryId == categoryId && t.IsActive, ct);

    public void Add(Category category) => db.Categories.Add(category);

    public Task SaveChangesAsync(CancellationToken ct) => db.SaveWithConflictMappingAsync(ct);
}

internal sealed class EquipmentTypeRepository(CatalogDbContext db) : IEquipmentTypeRepository
{
    public async Task<(IReadOnlyList<EquipmentType> Items, long Total)> SearchAsync(EquipmentTypeQuery query,
        bool includeInactive, CancellationToken ct)
    {
        var types = db.EquipmentTypes.AsNoTracking().Include(t => t.Category).AsQueryable();
        if (!includeInactive)
            types = types.Where(t => t.IsActive);
        if (query.CategoryId is { } categoryId)
            types = types.Where(t => t.CategoryId == categoryId);
        if (!string.IsNullOrWhiteSpace(query.Keyword))
        {
            // Parameterized ILIKE; % and _ typed by the user act as wildcards, which is harmless here.
            var pattern = $"%{query.Keyword.Trim()}%";
            types = types.Where(t => EF.Functions.ILike(t.Name, pattern) || EF.Functions.ILike(t.Description!, pattern));
        }
        if (!string.IsNullOrWhiteSpace(query.Tag))
        {
            var tag = query.Tag.Trim().ToLowerInvariant();
            types = types.Where(t => t.Tags.Contains(tag));
        }

        var total = await types.LongCountAsync(ct);
        var descending = query.Direction.Equals("desc", StringComparison.OrdinalIgnoreCase);
        var ordered = query.SortBy.Equals("createdAt", StringComparison.OrdinalIgnoreCase)
            ? descending ? types.OrderByDescending(t => t.CreatedAt) : types.OrderBy(t => t.CreatedAt)
            : descending ? types.OrderByDescending(t => t.Name) : types.OrderBy(t => t.Name);
        var items = await ordered.ThenBy(t => t.Id) // stable paging
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToListAsync(ct);
        return (items, total);
    }

    public Task<EquipmentType?> FindAsync(Guid id, CancellationToken ct) =>
        db.EquipmentTypes.Include(t => t.Category).FirstOrDefaultAsync(t => t.Id == id, ct);

    public void Add(EquipmentType equipmentType) => db.EquipmentTypes.Add(equipmentType);

    public Task SaveChangesAsync(CancellationToken ct) => db.SaveWithConflictMappingAsync(ct);
}

internal static class CatalogDbContextExtensions
{
    // The unique indexes are the source of truth for duplicate names, including concurrent requests.
    public static async Task SaveWithConflictMappingAsync(this CatalogDbContext db, CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw CatalogException.Conflict("A resource with the same name already exists.");
        }
    }
}
