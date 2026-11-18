using System.Text.Json;
using Borrowing.Application;
using Borrowing.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Borrowing.Infrastructure;

public sealed record IncidentIntent(Guid IncidentId, ReplacementCommand? Replacement, string? Condition);

public sealed class IncidentWorkflow(BorrowingDbContext db, IncidentService incidents, IInventoryIncidentClient replacements,
    IInventoryClient inventory, TimeProvider clock, IOptions<BorrowingWorkflowOptions> options, ILogger<IncidentWorkflow> logger)
{
    public async Task<IncidentDto> Execute(Guid id, string type, Guid key, Guid actor, bool confirmHandover, string correlation, CancellationToken ct)
    {
        var entry = await incidents.Detail(id, actor, true, ct);
        await using var gate = await LoanLock.TryAcquire(db, entry.LoanId, ct) ?? throw new LoanFailure(409, "Phiếu đang có thao tác khác.", "OPERATION_PENDING");
        var operation = await db.InventoryOperations.SingleOrDefaultAsync(o => o.OperationId == key, ct);
        if (operation != null)
        {
            var previous = JsonSerializer.Deserialize<IncidentIntent>(operation.RequestJson);
            if (operation.OperationType != type || previous?.IncidentId != id || operation.ActorId != actor)
                throw new LoanFailure(409, "Mã thao tác đã dùng cho yêu cầu khác.", "IDEMPOTENCY_KEY_REUSED");
            if (operation.State == "FAILED") throw new LoanFailure(409, "Thao tác trước không thành công. Kiểm tra tồn kho và dùng một yêu cầu mới khi cấp lại.", operation.LastError ?? "OPERATION_FAILED");
            if (operation.State == "MANUAL_REVIEW") throw new LoanFailure(409, "Cần đối soát thao tác đã lưu; không tạo lần cấp đổi mới.", "MANUAL_REVIEW");
        }
        else
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var incident = await incidents.Load(id, ct); var loan = await incidents.Loan(incident.LoanId, ct);
            if (await db.InventoryOperations.AnyAsync(o => o.LoanId == loan.Id && o.State != "COMPLETED" && o.State != "FAILED", ct))
                throw new LoanFailure(409, "Cần hoàn tất thao tác trước.", "OPERATION_PENDING");
            var old = loan.Items.Single(i => i.Id == incident.LoanItemId);
            if (incident.Inspection == null || incident.Status is not (IncidentStatus.CONFIRMED_DEFECT or IncidentStatus.WAITING_REPLACEMENT))
                throw new LoanFailure(409, "Phải có kết quả kiểm tra được xác nhận trước.", "INSPECTION_REQUIRED");
            IncidentIntent intent;
            if (type == "INC_REPLACE")
            {
                if (!confirmHandover || !incident.ReplacementRequested || incident.Inspection.Outcome != InspectionOutcome.DEFECTIVE || incident.Inspection.PhysicallyReceivedAt == null)
                    throw new LoanFailure(409, "Cần xác nhận đã nhận máy lỗi và đang bàn giao máy thay thế cho người mượn.", "PHYSICAL_HANDOVER_REQUIRED");
                if (loan.DueAt <= clock.GetUtcNow()) throw new LoanFailure(409, "Hạn trả gốc đã hết. Không thể cấp đổi với hạn trả này.", "DUE_DATE_PASSED");
                var returned = old.ItemStatus == ItemStatus.DAMAGED;
                if (old.ItemStatus != ItemStatus.BORROWED && !returned || returned && !incident.WaitingConsented)
                    throw new LoanFailure(409, "Người mượn cần đồng ý chờ trước khi cấp thiết bị sau lần trả.", "WAIT_CONSENT_REQUIRED");
                var targetId = loan.Status == LoanStatus.RETURNED ? Guid.NewGuid() : loan.Id;
                var newItemId = Guid.NewGuid();
                if (targetId != loan.Id) db.LoanRequests.Add(LoanRequest.ReplacementFollowUp(targetId, newItemId, loan, incident.Id, old.Id, actor, clock.GetUtcNow()));
                else if (loan.Status != LoanStatus.BORROWED) throw new LoanFailure(409, "Phiếu không đủ điều kiện cấp đổi.");
                var command = new ReplacementCommand(key, id, loan.Id, old.Id, incident.OriginalAssetUnitId, incident.EquipmentTypeId, targetId, newItemId, actor, returned, loan.DueAt);
                intent = new(id, command, null);
                incident.Replacements.Add(new() { OperationId = key, IncidentId = id, ReplacementLoanId = targetId, ReplacementLoanItemId = newItemId, ReplacedBy = actor });
            }
            else if (type == "INC_RETURN")
            {
                if (old.ItemStatus != ItemStatus.BORROWED || loan.Status != LoanStatus.BORROWED) throw new LoanFailure(409, "Thiết bị đã được ghi nhận trả.");
                if (incident.Inspection.Outcome is not (InspectionOutcome.DEFECTIVE or InspectionOutcome.LOST)) throw new LoanFailure(409, "Kết luận không yêu cầu nhận trả.");
                intent = new(id, null, incident.Inspection.Outcome == InspectionOutcome.LOST ? "LOST" : "DAMAGED");
            }
            else throw new LoanFailure(400, "Loại thao tác không hợp lệ.");
            operation = new() { OperationId = key, LoanId = loan.Id, OperationType = type, ActorId = actor, CorrelationId = correlation,
                RequestJson = JsonSerializer.Serialize(intent), StartedAt = clock.GetUtcNow(), NextRetryAt = clock.GetUtcNow() };
            incident.BeginOperation(key, actor, clock.GetUtcNow());
            db.EventCorrelationId = correlation; db.InventoryOperations.Add(operation); await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        }
        if (operation.State != "COMPLETED") await Apply(operation, ct);
        return await incidents.Detail(id, actor, true, ct);
    }

    // The API and worker both hold the original LoanLock. A DB transaction never spans gRPC.
    internal async Task Apply(InventoryOperation operation, CancellationToken ct)
    {
        if (operation.State is "COMPLETED" or "FAILED" or "MANUAL_REVIEW") return;
        var intent = JsonSerializer.Deserialize<IncidentIntent>(operation.RequestJson) ?? throw new InvalidDataException("Invalid persisted incident intent.");
        try
        {
            ReplacementSnapshot? receipt = null;
            var incident = await incidents.Load(intent.IncidentId, ct);
            var loan = await incidents.Loan(operation.LoanId, ct);
            if (intent.Replacement is { } command)
            {
                // An unknown outcome is queried first; if absent, repeat the EXACT durable command and operation ID.
                receipt = await replacements.ReplacementStatus(operation.OperationId, operation.CorrelationId, ct);
                if (!receipt.Found) receipt = await replacements.Replace(command, operation.CorrelationId, ct);
                if (!receipt.Found || receipt.RequestHash != command.Hash() || receipt.OriginalLoanId != loan.Id
                    || receipt.OriginalLoanItemId != incident.LoanItemId || receipt.OriginalAssetUnitId != incident.OriginalAssetUnitId
                    || receipt.ReplacementLoanId != command.ReplacementLoanId || receipt.ReplacementLoanItemId != command.ReplacementLoanItemId
                    || receipt.ReplacementAssetUnitId == Guid.Empty || receipt.ReplacementAssetUnitId == incident.OriginalAssetUnitId || string.IsNullOrWhiteSpace(receipt.SerialNumber))
                    throw new InventoryFailure(502, "REPLACEMENT_RECEIPT_MISMATCH", true);
            }
            else
            {
                var result = await inventory.Return(loan.Id, new Dictionary<Guid, string> { [incident.LoanItemId] = intent.Condition! }, operation.CorrelationId, ct);
                if (!result.Found || result.LoanId != loan.Id || !result.Allocations.Any(a => a.LoanItemId == incident.LoanItemId
                    && a.AssetUnitId == incident.OriginalAssetUnitId && a.ReservationStatus == "RETURNED")) throw new InventoryFailure(502, "INCOMPLETE_INCIDENT_RETURN", true);
            }
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            db.EventCorrelationId = operation.CorrelationId;
            if (receipt != null)
            {
                var record = incident.Replacements.Single(r => r.OperationId == operation.OperationId);
                if (receipt.ReplacementLoanId == loan.Id)
                    loan.ReplaceItem(incident.LoanItemId, receipt.ReplacementLoanItemId, receipt.ReplacementAssetUnitId, receipt.SerialNumber, incident.Id, operation.ActorId, clock.GetUtcNow(), operation.StartedAt);
                else
                {
                    var followUp = await incidents.Loan(receipt.ReplacementLoanId, ct);
                    followUp.FulfillFollowUp(receipt.ReplacementAssetUnitId, receipt.SerialNumber, operation.ActorId, clock.GetUtcNow());
                }
                record.ReplacementAssetUnitId = receipt.ReplacementAssetUnitId; record.SerialNumber = receipt.SerialNumber; record.ReplacedAt = clock.GetUtcNow();
                incident.Replaced(operation.ActorId, clock.GetUtcNow());
            }
            else
            {
                loan.RecordReturn(new Dictionary<Guid, string> { [incident.LoanItemId] = intent.Condition! }, operation.ActorId, clock.GetUtcNow());
                incident.Returned(operation.ActorId, intent.Condition!, clock.GetUtcNow());
            }
            operation.State = "COMPLETED"; operation.CompletedAt = clock.GetUtcNow(); operation.LastError = null;
            await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
            logger.LogInformation("Incident {IncidentId} operation {OperationId} finalized", incident.Id, operation.OperationId);
        }
        catch (InventoryFailure failure)
        {
            await Defer(operation.OperationId, intent, failure.Message, !failure.Uncertain, ct);
            throw new LoanFailure(failure.Status, failure.Message == "OUT_OF_STOCK"
                ? "Chưa có thiết bị cùng loại sẵn sàng. Có thể nhận trả máy lỗi rồi để sinh viên chọn chờ hoặc đóng sự cố."
                : "Thao tác thiết bị chưa hoàn tất. Kiểm tra trạng thái đối soát trước khi thử thao tác khác.", failure.Message);
        }
        catch (DbUpdateException) when (!ct.IsCancellationRequested)
        {
            await Defer(operation.OperationId, intent, "LOCAL_COMMIT_FAILED", false, ct);
            throw new LoanFailure(503, "Kết quả Inventory sẽ được đối soát từ thao tác đã lưu.", "RECONCILIATION_PENDING");
        }
    }
    private async Task Defer(Guid operationId, IncidentIntent intent, string error, bool permanent, CancellationToken ct)
    {
        db.ChangeTracker.Clear();
        var operation = await db.InventoryOperations.SingleAsync(o => o.OperationId == operationId, ct);
        if (operation.State == "COMPLETED") return;
        var incident = await incidents.Load(intent.IncidentId, ct);
        operation.RetryCount++; operation.LastError = error; operation.NextRetryAt = clock.GetUtcNow().AddSeconds(Math.Min(300, Math.Pow(2, Math.Min(operation.RetryCount, 8))));
        var manual = !permanent && operation.RetryCount >= options.Value.MaxAttempts;
        operation.State = permanent ? "FAILED" : manual ? "MANUAL_REVIEW" : "RETRY";
        if (permanent || manual) incident.OperationFailed(operation.ActorId, manual, error, clock.GetUtcNow());
        if (permanent)
        {
            operation.CompletedAt = clock.GetUtcNow();
            if (intent.Replacement is { } command && command.ReplacementLoanId != operation.LoanId)
                (await incidents.Loan(command.ReplacementLoanId, ct)).FailApproval(operation.ActorId, clock.GetUtcNow(), "Không thể cấp thiết bị thay thế: " + error);
        }
        db.EventCorrelationId = operation.CorrelationId; await db.SaveChangesAsync(ct);
    }
    public async Task<IncidentDto> Retry(Guid id, Guid actor, CancellationToken ct)
    {
        var entry = await incidents.Detail(id, actor, true, ct);
        await using var gate = await LoanLock.TryAcquire(db, entry.LoanId, ct) ?? throw new LoanFailure(409, "Phiếu đang được xử lý.");
        var incident = await incidents.Load(id, ct);
        if (incident.PendingOperationId == null) throw new LoanFailure(409, "Không có thao tác đang chờ đối soát.");
        var operation = await db.InventoryOperations.SingleAsync(o => o.OperationId == incident.PendingOperationId, ct);
        if (operation.State == "MANUAL_REVIEW") incident.ResumeOperation(actor, clock.GetUtcNow());
        if (operation.State is not ("MANUAL_REVIEW" or "PENDING" or "RETRY")) throw new LoanFailure(409, "Thao tác không cần phục hồi.");
        operation.State = "RETRY"; operation.RetryCount = 0; operation.NextRetryAt = clock.GetUtcNow();
        await db.SaveChangesAsync(ct); await Apply(operation, ct);
        return await incidents.Detail(id, actor, true, ct);
    }
}
