namespace Inventory.Domain;

// Immutable receipt: replay never depends on the asset's later maintenance/return status.
public sealed class ReplacementOperation
{
    public Guid OperationId { get; set; }
    public string RequestHash { get; set; } = "";
    public string ResultJson { get; set; } = "{}";
    public DateTimeOffset CreatedAt { get; set; }
}
