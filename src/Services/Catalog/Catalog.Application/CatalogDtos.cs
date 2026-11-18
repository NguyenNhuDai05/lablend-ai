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
    [ImageUrl, StringLength(500)] string? ImageUrl);

public sealed record UpdateEquipmentTypeRequest(
    Guid CategoryId,
    [Required, StringLength(150, MinimumLength = 2)] string Name,
    [StringLength(2000)] string? Description,
    [StringLength(4000)] string? Specifications,
    [MaxLength(20)] string[]? Tags,
    [ImageUrl, StringLength(500)] string? ImageUrl,
    bool IsActive);

/// <summary>
/// An absolute http(s) URL, or an image bundled with the web app under /equipment/
/// (apps/web/public/equipment), e.g. "/equipment/may-hien-song.webp".
/// </summary>
public sealed partial class ImageUrlAttribute() : ValidationAttribute("The {0} field must be an http(s) URL or /equipment/<name>.webp|png|jpg.")
{
    public override bool IsValid(object? value) => value switch
    {
        null => true,
        string s => BundledPath().IsMatch(s)
            || Uri.TryCreate(s, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp),
        _ => false,
    };

    [System.Text.RegularExpressions.GeneratedRegex("^/equipment/[a-z0-9][a-z0-9-]{0,99}\\.(webp|png|jpe?g)$")]
    private static partial System.Text.RegularExpressions.Regex BundledPath();
}

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
