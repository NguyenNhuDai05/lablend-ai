using System.Text.Json;
using Borrowing.Application;
using Borrowing.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Borrowing.Infrastructure;

public sealed class LoanLifecycle(BorrowingDbContext db, IInventoryClient inventory, TimeProvider clock, IOptions<BorrowingWorkflowOptions> options, ILogger<LoanLifecycle> logger) : ILoanLifecycle
{
    private static bool Active(InventoryOperation op) => op.State is not ("COMPLETED" or "FAILED");
    public async Task Execute(Guid loanId, string type, Guid actor, string correlationId, ReturnLoanRequest? request, CancellationToken ct)
    {
        await using var gate = await LoanLock.TryAcquire(db, loanId, ct) ?? throw new LoanFailure(409, "Another loan operation is in progress.");
        InventoryOperation? operation;
        // Serialize with existing cancel/reject aggregate mutations, and commit the command BEFORE any network I/O.
        await using (var tx = await db.Database.BeginTransactionAsync(ct))
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM \"LoanRequests\" WHERE \"Id\" = {loanId} FOR UPDATE", ct);
            var loan = await Load(loanId, ct);
            operation = await db.InventoryOperations.SingleOrDefaultAsync(o => o.LoanId == loanId && o.State != "COMPLETED" && o.State != "FAILED", ct);
            var returns = type == "RETURN" ? ValidateReturns(loan, request) : new Dictionary<Guid, string>();
            if (type == "RETURN" && await db.EquipmentIncidents.AnyAsync(i => i.LoanId == loanId && returns.Keys.Contains(i.LoanItemId)
                && i.Status != IncidentStatus.CANCELLED && i.Status != IncidentStatus.RESOLVED_REPLACED && i.Status != IncidentStatus.RESOLVED_NO_FAULT
                && i.Status != IncidentStatus.RESOLVED_REPAIRED && i.Status != IncidentStatus.RESOLVED_NO_REPLACEMENT, ct))
                throw new LoanFailure(409, "Thiết bị có sự cố đang xử lý; nhận trả từ màn hình sự cố để giữ kết quả kiểm tra nhất quán.", "OPEN_INCIDENT");
            if (operation != null)
            {
                if (operation.OperationType != type || operation.State is "MANUAL_REVIEW" or "COMPENSATING"
                    || type == "RETURN" && !SameReturns(operation.RequestJson, returns))
                    throw new LoanFailure(409, "A persisted operation must be recovered before a different command.");
            }
            else
            {
                if (type == "HANDOVER" && loan.Status == LoanStatus.BORROWED || type == "RETURN" && returns.Count == 0) return;
                operation = new InventoryOperation { LoanId = loanId, OperationType = type, ActorId = actor,
                    CorrelationId = correlationId, StartedAt = clock.GetUtcNow(), NextRetryAt = clock.GetUtcNow(), RequestJson = JsonSerializer.Serialize(returns) };
                switch (type)
                {
                    case "APPROVE": loan.BeginApproval(operation.OperationId, actor, clock.GetUtcNow()); break;
                    case "HANDOVER" when loan.Status == LoanStatus.APPROVED: break;
                    case "RETURN" when loan.Status == LoanStatus.BORROWED: break;
                    default: throw new LoanFailure(409, "Loan lifecycle does not allow this operation.");
                }
                db.InventoryOperations.Add(operation);
                await db.SaveChangesAsync(ct);
            }
            await tx.CommitAsync(ct);
        }
        await Apply(operation, ct);
    }

    private async Task<LoanRequest> Load(Guid id, CancellationToken ct) =>
        await db.LoanRequests.Include(l => l.Items).Include(l => l.Histories).AsSplitQuery().SingleOrDefaultAsync(l => l.Id == id, ct)
        ?? throw new LoanFailure(404, "Loan not found.");

    private static Dictionary<Guid, string> ValidateReturns(LoanRequest loan, ReturnLoanRequest? request)
    {
        if (request?.Items == null || request.Items.Count is < 1 or > 20 || request.Items.Any(i => i == null)
            || request.Items.Select(i => i.LoanItemId).Distinct().Count() != request.Items.Count)
            throw new LoanFailure(400, "Provide 1–20 distinct loan items.");
        if (loan.Status is not (LoanStatus.BORROWED or LoanStatus.RETURNED)) throw new LoanFailure(409, "Loan is not borrowed.");
        var result = new Dictionary<Guid, string>();
        foreach (var input in request.Items)
        {
            if (input.Condition is not ("NORMAL" or "DAMAGED" or "LOST")) throw new LoanFailure(400, "Condition must be NORMAL, DAMAGED or LOST.");
            var item = loan.Items.SingleOrDefault(i => i.Id == input.LoanItemId) ?? throw new LoanFailure(400, "Item does not belong to this loan.");
            if (item.ItemStatus == ItemStatus.BORROWED) result.Add(input.LoanItemId, input.Condition);
            else if (item.ReturnCondition != input.Condition) throw new LoanFailure(409, "Already returned with a different condition.");
        }
        return result;
    }
    private static bool SameReturns(string json, Dictionary<Guid, string> expected)
    {
        var stored = JsonSerializer.Deserialize<Dictionary<Guid, string>>(json)!;
        return stored.Count == expected.Count && stored.All(i => expected.GetValueOrDefault(i.Key) == i.Value);
    }

    // Caller holds LoanLock. Every command reuses its immutable intent and the approval operation's inventory reference.
    internal async Task Apply(InventoryOperation operation, CancellationToken ct)
    {
        if (!Active(operation)) return;
        var loan = await Load(operation.LoanId, ct);
        db.EventCorrelationId = operation.CorrelationId;
        try
        {
            var status = operation.OperationType == "RETURN"
                ? await inventory.LoanStatus(loan.Id, loan.ApprovalOperationId ?? operation.OperationId, operation.CorrelationId, ct)
                : await inventory.Status(loan.ApprovalOperationId ?? operation.OperationId, operation.CorrelationId, ct);
            if (operation.State == "COMPENSATING" || operation.OperationType == "RELEASE"
                || operation.OperationType == "APPROVE" && loan.DueAt <= clock.GetUtcNow())
            {
                await Compensate(loan, operation, status, ct);
                return;
            }
            InventorySnapshot result;
            switch (operation.OperationType)
            {
                case "APPROVE":
                    result = status.Found ? status : await inventory.Reserve(operation.OperationId, loan.Id,
                        loan.Items.ToDictionary(i => i.Id, i => i.EquipmentTypeId), operation.CorrelationId, ct);
                    Verify(loan, result, "HELD");
                    foreach (var allocation in result.Allocations)
                    {
                        var item = loan.Items.Single(i => i.Id == allocation.LoanItemId);
                        item.AssetUnitId = allocation.AssetUnitId; item.ItemStatus = ItemStatus.RESERVED;
                        item.AssetSerialNumber = allocation.SerialNumber;
                        // v1 does not expose reservation IDs; do not invent one.
                    }
                    loan.Approve(operation.ActorId, clock.GetUtcNow());
                    break;
                case "HANDOVER":
                    Verify(loan, status, "HELD", "HANDED_OVER");
                    result = status.Allocations.All(i => i.ReservationStatus == "HANDED_OVER") ? status
                        : await inventory.Handover(loan.Id, operation.CorrelationId, ct);
                    Verify(loan, result, "HANDED_OVER");
                    loan.Handover(operation.ActorId, clock.GetUtcNow());
                    break;
                case "RETURN":
                    Verify(loan, status, "HANDED_OVER", "RETURNED");
                    var returns = JsonSerializer.Deserialize<Dictionary<Guid, string>>(operation.RequestJson)!;
                    result = await inventory.Return(loan.Id, returns, operation.CorrelationId, ct);
                    Verify(loan, result, "HANDED_OVER", "RETURNED");
                    if (returns.Keys.Any(id => result.Allocations.Single(i => i.LoanItemId == id).ReservationStatus != "RETURNED"))
                        throw new InventoryFailure(502, "INCOMPLETE_RETURN_REPLY", true);
                    loan.RecordReturn(returns, operation.ActorId, clock.GetUtcNow());
                    break;
                default: throw new LoanFailure(409, "Unknown persisted operation type.");
            }
            operation.State = "COMPLETED"; operation.CompletedAt = clock.GetUtcNow(); operation.LastError = null;
            // SaveChanges is one local transaction: assignments + lifecycle + history + operation completion.
            await db.SaveChangesAsync(ct);
            logger.LogInformation("Loan {LoanId} operation {OperationId} {Type} completed; correlationId={CorrelationId}", loan.Id, operation.OperationId, operation.OperationType, operation.CorrelationId);
        }
        catch (InventoryFailure failure)
        {
            // No loan mutation has been saved. Clear it before recording a retry/failure.
            db.ChangeTracker.Clear();
            operation = await db.InventoryOperations.SingleAsync(o => o.OperationId == operation.OperationId, ct);
            if (operation.State == "COMPLETED") return; // A competing recovery may already have committed after a lost lock session.
            if (operation.State == "FAILED") throw new LoanFailure(409, "The persisted operation has already failed.");
            operation.RetryCount++; operation.LastError = failure.Message;
            operation.NextRetryAt = clock.GetUtcNow().AddSeconds(Math.Min(300, Math.Pow(2, Math.Min(operation.RetryCount, 8))));
            if (!failure.Uncertain && operation.OperationType == "APPROVE")
            {
                operation.State = "FAILED"; operation.CompletedAt = clock.GetUtcNow();
                (await Load(operation.LoanId, ct)).FailApproval(operation.ActorId, clock.GetUtcNow(), failure.Message);
            }
            else operation.State = operation.RetryCount >= options.Value.MaxAttempts || !failure.Uncertain ? "MANUAL_REVIEW"
                : operation.State == "COMPENSATING" ? "COMPENSATING" : "RETRY";
            await db.SaveChangesAsync(ct);
            logger.LogWarning("Loan {LoanId} operation {OperationId} deferred: {Code}; state={State}; correlationId={CorrelationId}", operation.LoanId, operation.OperationId, failure.Message, operation.State, operation.CorrelationId);
            throw new LoanFailure(failure.Status, "Inventory operation could not be completed: " + failure.Message);
        }
        catch (DbUpdateException) when (!ct.IsCancellationRequested)
        {
            // An acknowledged reserve followed by a failed local transaction must leave durable compensation intent.
            // If the database itself is down this write can also fail; the original PENDING intent still survives.
            db.ChangeTracker.Clear();
            operation = await db.InventoryOperations.SingleAsync(o => o.OperationId == operation.OperationId, ct);
            if (operation.State == "COMPLETED") return;
            if (operation.State == "FAILED") throw new LoanFailure(409, "The persisted operation has already failed.");
            operation.RetryCount++; operation.LastError = "LOCAL_COMMIT_FAILED";
            operation.State = operation.RetryCount >= options.Value.MaxAttempts ? "MANUAL_REVIEW"
                : operation.OperationType == "APPROVE" ? "COMPENSATING" : "RETRY";
            operation.NextRetryAt = clock.GetUtcNow().AddSeconds(5);
            await db.SaveChangesAsync(ct);
            throw new LoanFailure(503, "Local commit failed; the persisted operation will be reconciled.");
        }
    }

    private async Task Compensate(LoanRequest loan, InventoryOperation operation, InventorySnapshot status, CancellationToken ct)
    {
        if (operation.OperationType == "APPROVE" ? loan.Status != LoanStatus.APPROVING
            : operation.OperationType != "RELEASE" || loan.Status != LoanStatus.APPROVED)
            throw new InventoryFailure(503, "COMPENSATION_LOCAL_STATE_REQUIRES_REVIEW", true);
        if (!status.Found)
        {
            // found=false is only a snapshot: an older request might still be in flight. Same-ID replay
            // joins the server's operation serialization before release, preventing a late orphaned hold.
            try { status = await inventory.Reserve(loan.ApprovalOperationId!.Value, loan.Id,
                loan.Items.ToDictionary(i => i.Id, i => i.EquipmentTypeId), operation.CorrelationId, ct); }
            catch (InventoryFailure e) when (!e.Uncertain && e.Message == "INSUFFICIENT_STOCK")
            {
                loan.Expire(operation.ActorId, clock.GetUtcNow());
                operation.State = "COMPLETED"; operation.CompletedAt = clock.GetUtcNow();
                await db.SaveChangesAsync(ct); return;
            }
        }
        Verify(loan, status, "HELD", "RELEASED"); // HANDED_OVER/RETURNED is never released.
        await inventory.Release(loan.Id, operation.CorrelationId, ct);
        var released = await inventory.Status(loan.ApprovalOperationId!.Value, operation.CorrelationId, ct);
        Verify(loan, released, "RELEASED");
        if (operation.OperationType == "RELEASE" || loan.DueAt <= clock.GetUtcNow()) loan.Expire(operation.ActorId, clock.GetUtcNow());
        else loan.FailApproval(operation.ActorId, clock.GetUtcNow(), "Reservation compensated after local commit failure.");
        operation.State = "COMPLETED"; operation.CompletedAt = clock.GetUtcNow(); operation.LastError = "RESERVATIONS_RELEASED";
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Compensated loan {LoanId}, operation {OperationId}; correlationId={CorrelationId}", loan.Id, operation.OperationId, operation.CorrelationId);
    }

    private static void Verify(LoanRequest loan, InventorySnapshot result, params string[] allowed)
    {
        if (!result.Found || result.LoanId != loan.Id || result.Allocations.Count != loan.Items.Count
            || result.Allocations.Select(i => i.LoanItemId).Distinct().Count() != loan.Items.Count
            || result.Allocations.Select(i => i.AssetUnitId).Distinct().Count() != loan.Items.Count
            || result.Allocations.Any(a => a.AssetUnitId == Guid.Empty || !allowed.Contains(a.ReservationStatus)
                || a.ReservationStatus == "HELD" && a.AssetStatus != "RESERVED"
                || a.ReservationStatus == "HANDED_OVER" && a.AssetStatus != "ON_LOAN"
                || !loan.Items.Any(i => i.Id == a.LoanItemId && (i.AssetUnitId == null || i.AssetUnitId == a.AssetUnitId))))
            throw new InventoryFailure(502, "INVENTORY_ALLOCATION_MISMATCH", true);
    }
}
