namespace Catalog.Domain;

public sealed class Category
{
    private Category() { } // EF Core

    public Category(string name, string? description)
    {
        Id = Guid.NewGuid();
        CreatedAt = DateTimeOffset.UtcNow;
        Update(name, description, isActive: true);
    }

    public Guid Id { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public bool IsActive { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public void Update(string name, string? description, bool isActive)
    {
        Name = name.Trim();
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        IsActive = isActive;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    // Soft deactivate: other services keep logical references to catalog IDs.
    public void Deactivate() => Update(Name, Description, isActive: false);
}
