namespace Catalog.Application;

/// <summary>Business rule failure; Catalog.Api maps StatusCode to ProblemDetails.</summary>
public sealed class CatalogException(int statusCode, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;

    public static CatalogException NotFound(string resource, Guid id) => new(404, $"{resource} '{id}' was not found.");
    public static CatalogException Conflict(string message) => new(409, message);
    public static CatalogException Invalid(string message) => new(400, message);
}
