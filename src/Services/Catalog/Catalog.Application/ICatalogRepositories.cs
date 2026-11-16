using Catalog.Domain;

namespace Catalog.Application;

// Implemented in Catalog.Infrastructure. SaveChangesAsync throws CatalogException(409) on unique violations.
public interface ICategoryRepository
{
    Task<IReadOnlyList<Category>> ListAsync(bool includeInactive, CancellationToken ct);
    Task<Category?> FindAsync(Guid id, CancellationToken ct);
    Task<bool> HasActiveEquipmentTypesAsync(Guid categoryId, CancellationToken ct);
    void Add(Category category);
    Task SaveChangesAsync(CancellationToken ct);
}

public interface IEquipmentTypeRepository
{
    Task<(IReadOnlyList<EquipmentType> Items, long Total)> SearchAsync(EquipmentTypeQuery query, bool includeInactive, CancellationToken ct);
    Task<EquipmentType?> FindAsync(Guid id, CancellationToken ct);
    void Add(EquipmentType equipmentType);
    Task SaveChangesAsync(CancellationToken ct);
}
