using Borrowing.Domain;
using LabLend.Contracts;
using System.Security.Cryptography;
using System.Text.Json;

namespace Borrowing.Application;

public sealed class LoanService(ILoanRepository repository, ICatalogClient catalog)
{
    public async Task<LoanDto> Create(Guid studentId, CreateLoanRequest request, string correlationId, CancellationToken ct, Guid? submissionKey = null)
    {
        // Validate everything before calling the dependency. No database transaction spans an HTTP call.
        LoanRequest loan;
        if (request.Items == null || request.Items.Any(i => i == null)) throw new LoanFailure(400, "Items must not be null.");
        var hash = SubmissionHash(request);
        // Replay is resolved before time-sensitive validation or Catalog reads: a committed request stays retrievable.
        if (submissionKey is { } key && await repository.FindSubmission(studentId, key, hash, ct) is { } previous) return Dto(previous);
        try { loan = LoanRequest.Create(studentId, request.Purpose, request.DueAt, request.Items.Select(i => i.EquipmentTypeId), DateTimeOffset.UtcNow); }
        catch (ArgumentException) { throw new LoanFailure(400, "Purpose, dueAt or items are invalid."); }
        await catalog.ValidateEquipment(loan.Items.Select(i => i.EquipmentTypeId).Distinct().ToArray(), correlationId, ct);
        // Recheck the due date if the dependency took time.
        if (request.DueAt <= DateTimeOffset.UtcNow) throw new LoanFailure(400, "Due date must be in the future.");
        if (submissionKey is { } idempotencyKey) loan = await repository.AddSubmission(loan, idempotencyKey, hash, ct, correlationId);
        else await repository.Add(loan, ct, correlationId);
        return Dto(loan);
    }
    public static string SubmissionHash(CreateLoanRequest request) => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
    {
        purpose = request.Purpose?.Trim(), dueAt = request.DueAt.ToUniversalTime(),
        equipment = request.Items.Select(i => i.EquipmentTypeId).OrderBy(id => id).ToArray()
    })));
    public async Task<LoanDto> Detail(Guid id, Guid actor, bool staff, CancellationToken ct)
    {
        var loan = await repository.Get(id, ct) ?? throw new LoanFailure(404, "Loan not found.");
        if (!staff && loan.StudentId != actor) throw new LoanFailure(404, "Loan not found.");
        return Dto(loan);
    }
    public async Task<PagedResponse<LoanDto>> List(Guid? student, string? status, int page, int size, CancellationToken ct)
    {
        if (page is < 1 or > 1000000 || size is < 1 or > 100) throw new LoanFailure(400, "Invalid pagination.");
        LoanStatus? filter = null;
        if (status != null)
        {
            if (!Enum.TryParse<LoanStatus>(status, false, out var parsed) || !Enum.IsDefined(parsed) || parsed.ToString() != status)
                throw new LoanFailure(400, "Unknown lifecycle status.");
            filter = parsed;
        }
        var result = await repository.List(student, filter, page, size, ct);
        return new(result.Items.Select(Dto).ToArray(), result.Total, page, size);
    }
    public async Task<LoanDto> Cancel(Guid id, Guid owner, CancellationToken ct) => Dto(await repository.Mutate(id, loan =>
    {
        if (loan.StudentId != owner) throw new LoanFailure(404, "Loan not found.");
        loan.Cancel(owner, DateTimeOffset.UtcNow);
    }, ct));
    public async Task<LoanDto> Reject(Guid id, Guid actor, string reason, CancellationToken ct, string? correlationId = null) => Dto(await repository.Mutate(id,
        loan => loan.Reject(actor, reason, DateTimeOffset.UtcNow), ct, correlationId));
    private static LoanDto Dto(LoanRequest loan) => new(loan.Id, loan.StudentId, loan.Purpose, loan.Status.ToString(),
        loan.IsOverdue(DateTimeOffset.UtcNow), loan.RequestedAt, loan.DueAt, loan.ReviewedBy, loan.ReviewedAt, loan.RejectionReason,
        loan.CreatedAt, loan.UpdatedAt,
        loan.Items.OrderBy(i => i.Id).Select(i => new LoanItemDto(i.Id, i.EquipmentTypeId, i.AssetUnitId, i.ReservationId, i.ItemStatus.ToString(), i.ReturnCondition, i.ReturnedAt, i.AssetSerialNumber, i.IncidentId, i.ReplacesLoanItemId)).ToArray(),
        loan.Histories.OrderBy(h => h.OccurredAt).ThenBy(h => h.Id).Select(h => new LoanHistoryDto(h.Id, h.ActorUserId, h.Action,
            h.FromStatus?.ToString(), h.ToStatus.ToString(), h.OccurredAt, h.Note)).ToArray(), loan.HandedOverBy, loan.HandedOverAt);
}
