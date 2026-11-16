namespace Inventory.Application;

/// <summary>Use-case failure. Transports map StatusCode/Code to HTTP ProblemDetails or gRPC status.</summary>
public sealed class InventoryException(int statusCode, string code, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
    public string Code { get; } = code;

    public const string InsufficientStockCode = "INSUFFICIENT_STOCK";
    public const string OperationIdReusedCode = "OPERATION_ID_REUSED";
    public const string ConcurrencyConflictCode = "CONCURRENCY_CONFLICT";

    public static InventoryException NotFound(string message) => new(404, "NOT_FOUND", message);
    public static InventoryException Invalid(string message) => new(400, "VALIDATION_ERROR", message);
    public static InventoryException Conflict(string message) => new(409, "CONFLICT", message);
    public static InventoryException InsufficientStock(Guid equipmentTypeId) =>
        new(409, InsufficientStockCode, $"Not enough AVAILABLE units of equipment type '{equipmentTypeId}'.");
    public static InventoryException OperationIdReused(Guid operationId) =>
        new(409, OperationIdReusedCode, $"Operation '{operationId}' was already used with a different request.");
    public static InventoryException ConcurrencyConflict() =>
        new(409, ConcurrencyConflictCode, "The unit was changed by another request; reload and retry.");
}
