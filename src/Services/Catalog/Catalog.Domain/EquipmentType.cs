namespace Catalog.Domain;

public sealed class EquipmentType
{
    private EquipmentType() { } // EF Core

    public EquipmentType(Category category, string name, string? description, string? specifications,
        IEnumerable<string>? tags, string? imageUrl)
    {
        Id = Guid.NewGuid();
        CreatedAt = DateTimeOffset.UtcNow;
        Update(category, name, description, specifications, tags, imageUrl, isActive: true);
    }

    public Guid Id { get; private set; }
    public Guid CategoryId { get; private set; }
    public Category? Category { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public string? Specifications { get; private set; }
    public string[] Tags { get; private set; } = [];
    public string? ImageUrl { get; private set; }
    public bool IsActive { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public void Update(Category category, string name, string? description, string? specifications,
        IEnumerable<string>? tags, string? imageUrl, bool isActive)
    {
        Category = category;
        CategoryId = category.Id;
        Name = name.Trim();
        Description = Clean(description);
        Specifications = Clean(specifications);
        Tags = NormalizeTags(tags);
        ImageUrl = Clean(imageUrl);
        IsActive = isActive;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void Deactivate() => Update(Category!, Name, Description, Specifications, Tags, ImageUrl, isActive: false);

    // Lowercase + distinct so tag filters are exact and case-insensitive.
    public static string[] NormalizeTags(IEnumerable<string>? tags) => (tags ?? [])
        .Select(tag => tag.Trim().ToLowerInvariant())
        .Where(tag => tag.Length > 0)
        .Distinct()
        .ToArray();

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
