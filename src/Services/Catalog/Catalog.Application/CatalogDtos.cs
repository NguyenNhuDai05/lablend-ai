using System.ComponentModel.DataAnnotations;

namespace Catalog.Application;

public sealed record CategoryResponse(Guid Id, string Name, string? Description, bool IsActive,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

public sealed record EquipmentTypeResponse(Guid Id, Guid CategoryId, string CategoryName, string Name,
    string? Description, string? Specifications, IReadOnlyList<string> Tags, string? ImageUrl, bool IsActive,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

public sealed record CreateCategoryRequest(
    [Required, StringLength(150, MinimumLength = 2)] string Name,
    [StringLength(1000)] string? Description);

public sealed record UpdateCategoryRequest(
    [Required, StringLength(150, MinimumLength = 2)] string Name,
    [StringLength(1000)] string? Description,
    bool IsActive);

public sealed record CreateEquipmentTypeRequest(
    Guid CategoryId,
    [Required, StringLength(150, MinimumLength = 2)] string Name,
    [StringLength(2000)] string? Description,
    [StringLength(4000)] string? Specifications,
    [MaxLength(20)] string[]? Tags,
    [Url, StringLength(500)] string? ImageUrl);

public sealed record UpdateEquipmentTypeRequest(
    Guid CategoryId,
    [Required, StringLength(150, MinimumLength = 2)] string Name,
    [StringLength(2000)] string? Description,
    [StringLength(4000)] string? Specifications,
    [MaxLength(20)] string[]? Tags,
    [Url, StringLength(500)] string? ImageUrl,
    bool IsActive);

/// <summary>GET /api/equipment-types query. sortBy/direction are whitelisted, pageSize is capped.</summary>
public sealed class EquipmentTypeQuery
{
    [StringLength(100)] public string? Keyword { get; init; }
    public Guid? CategoryId { get; init; }
    [StringLength(50)] public string? Tag { get; init; }
    /// <summary>Only honored for LAB_STAFF/ADMIN.</summary>
    public bool IncludeInactive { get; init; }
    [RegularExpression("(?i)name|createdAt")] public string SortBy { get; init; } = "name";
    [RegularExpression("(?i)asc|desc")] public string Direction { get; init; } = "asc";
    [Range(1, 100_000)] public int Page { get; init; } = 1;
    [Range(1, 100)] public int PageSize { get; init; } = 10;
}
