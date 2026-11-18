using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text;

namespace Borrowing.Application;

public sealed record ReportIncidentRequest(Guid LoanId, Guid LoanItemId, [Required] string ProblemCategory,
    [Required, StringLength(2000, MinimumLength = 10)] string Description, bool ReplacementRequested);
public sealed record InspectIncidentRequest([Required] string Outcome, [Required, StringLength(2000, MinimumLength = 10)] string Notes,
    string? CorrectiveAction, bool PhysicallyReceived, string? VerifiedSerialNumber);
public sealed record ReplaceIncidentRequest(bool ConfirmHandover);
public sealed record ResolveIncidentRequest([Required, StringLength(2000, MinimumLength = 10)] string Notes);
public sealed record IncidentQuery(string? Status = null, DateTimeOffset? From = null, DateTimeOffset? To = null,
    Guid? EquipmentTypeId = null, Guid? StaffId = null, string? Search = null, int Page = 1, int PageSize = 10);
public sealed record IncidentHistoryDto(Guid Id, string Action, string? FromStatus, string ToStatus, Guid ActorUserId, string? Notes, DateTimeOffset OccurredAt);
public sealed record IncidentInspectionDto(string Outcome, string Notes, string? CorrectiveAction, Guid InspectedBy, DateTimeOffset InspectedAt, DateTimeOffset? PhysicallyReceivedAt);
public sealed record ReplacementDto(Guid OperationId, Guid ReplacementLoanId, Guid ReplacementLoanItemId, Guid? ReplacementAssetUnitId,
    string? SerialNumber, Guid ReplacedBy, DateTimeOffset? ReplacedAt);
public sealed record IncidentEvidenceDto(Guid Id, string ContentType, int Size, DateTimeOffset CreatedAt);
public sealed record IncidentDto(Guid Id, string Code, Guid LoanId, Guid LoanItemId, Guid StudentId, Guid OriginalAssetUnitId,
    Guid EquipmentTypeId, string SerialNumber, string ProblemCategory, string Description, bool ReplacementRequested, bool WaitingConsented,
    string Status, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, string? Resolution, Guid? ResolvedBy, DateTimeOffset? ResolvedAt,
    Guid? PendingOperationId, string? OperationState, string? OperationError, IncidentInspectionDto? Inspection,
    IReadOnlyList<IncidentHistoryDto> History, IReadOnlyList<ReplacementDto> Replacements, IReadOnlyList<IncidentEvidenceDto> Evidence);
public sealed record IncidentStatistics(long Total, long AwaitingInspection, long WaitingReplacement, long SuccessfullyReplaced,
    long ClosedWithoutReplacement, double? AverageResolutionHours, IReadOnlyList<VerifiedFailureCount> VerifiedFailures);
public sealed record VerifiedFailureCount(Guid EquipmentTypeId, long Count);

public sealed record ReplacementCommand(Guid OperationId, Guid IncidentId, Guid OriginalLoanId, Guid OriginalLoanItemId,
    Guid OriginalAssetUnitId, Guid EquipmentTypeId, Guid ReplacementLoanId, Guid ReplacementLoanItemId, Guid ActorId, bool AlreadyReturned, DateTimeOffset DueAt)
{
    public string Hash() => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
        $"{IncidentId}|{OriginalLoanId}|{OriginalLoanItemId}|{OriginalAssetUnitId}|{EquipmentTypeId}|{ReplacementLoanId}|{ReplacementLoanItemId}|{ActorId}|{AlreadyReturned}|{DueAt.ToUniversalTime():O}")));
}
public sealed record ReplacementSnapshot(bool Found, string RequestHash, Guid OriginalLoanId, Guid OriginalLoanItemId, Guid OriginalAssetUnitId,
    Guid ReplacementLoanId, Guid ReplacementLoanItemId, Guid ReplacementAssetUnitId, string SerialNumber);
public interface IInventoryIncidentClient
{
    Task<InventorySnapshot> LoanAssets(Guid loanId, string correlationId, CancellationToken ct);
    Task<ReplacementSnapshot> Replace(ReplacementCommand command, string correlationId, CancellationToken ct);
    Task<ReplacementSnapshot> ReplacementStatus(Guid operationId, string correlationId, CancellationToken ct);
}
