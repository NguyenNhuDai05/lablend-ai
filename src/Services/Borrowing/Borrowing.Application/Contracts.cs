using System.ComponentModel.DataAnnotations;
using Borrowing.Domain;
using LabLend.Contracts;

namespace Borrowing.Application;

public sealed record CreateLoanItem(Guid EquipmentTypeId);
public sealed record CreateLoanRequest([Required, StringLength(2000, MinimumLength = 10)] string Purpose,
    DateTimeOffset DueAt, [Required, MinLength(1), MaxLength(20)] IReadOnlyList<CreateLoanItem> Items);
public sealed record RejectLoanRequest([Required, StringLength(1000, MinimumLength = 3)] string RejectionReason);
public sealed record LoanItemDto(Guid Id, Guid EquipmentTypeId, Guid? AssetUnitId, Guid? ReservationId, string ItemStatus, string? ReturnCondition = null, DateTimeOffset? ReturnedAt = null);
public sealed record LoanHistoryDto(Guid Id, Guid ActorUserId, string Action, string? FromStatus, string ToStatus, DateTimeOffset OccurredAt, string? Note);
public sealed record LoanDto(Guid Id, Guid StudentId, string Purpose, string Status, bool IsOverdue, DateTimeOffset RequestedAt,
    DateTimeOffset DueAt, Guid? ReviewedBy, DateTimeOffset? ReviewedAt, string? RejectionReason, DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt, IReadOnlyList<LoanItemDto> Items, IReadOnlyList<LoanHistoryDto> History,
    Guid? HandedOverBy = null, DateTimeOffset? HandedOverAt = null);
public sealed class LoanFailure(int status, string message) : Exception(message) { public int Status { get; } = status; }
public interface ICatalogClient { Task ValidateEquipment(IReadOnlyCollection<Guid> ids, string correlationId, CancellationToken ct); }
public interface ILoanRepository
{
    Task Add(LoanRequest loan, CancellationToken ct, string? correlationId = null);
    Task<LoanRequest?> FindSubmission(Guid studentId, Guid key, string hash, CancellationToken ct);
    Task<LoanRequest> AddSubmission(LoanRequest loan, Guid key, string hash, CancellationToken ct, string correlationId);
    Task<LoanRequest?> Get(Guid id, CancellationToken ct);
    Task<PagedResponse<LoanRequest>> List(Guid? studentId, LoanStatus? status, int page, int size, CancellationToken ct);
    Task<LoanRequest> Mutate(Guid id, Action<LoanRequest> mutation, CancellationToken ct, string? correlationId = null);
}
