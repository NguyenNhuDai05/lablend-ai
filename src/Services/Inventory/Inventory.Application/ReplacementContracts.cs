namespace Inventory.Application;

public sealed record ReplaceAssetCommand(Guid OperationId, Guid IncidentId, Guid OriginalLoanId, Guid OriginalLoanItemId,
    Guid OriginalAssetUnitId, Guid EquipmentTypeId, Guid ReplacementLoanId, Guid ReplacementLoanItemId, Guid ActorId, bool AlreadyReturned, DateTimeOffset DueAt);
public sealed record ReplacementResult(bool Found, string RequestHash, Guid OriginalLoanId, Guid OriginalLoanItemId, Guid OriginalAssetUnitId,
    Guid ReplacementLoanId, Guid ReplacementLoanItemId, Guid ReplacementAssetUnitId, string SerialNumber, string? ErrorCode = null);
public interface IReplacementRepository
{
    Task<ReplacementResult> Replace(ReplaceAssetCommand command, CancellationToken ct);
    Task<ReplacementResult> Status(Guid operationId, CancellationToken ct);
}
