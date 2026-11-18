using System.Security.Cryptography;
using System.Text.Json;
using Borrowing.Application;
using Borrowing.Domain;
using LabLend.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Borrowing.Infrastructure;

public sealed class IncidentService(BorrowingDbContext db, IInventoryIncidentClient inventory, TimeProvider clock)
{
    internal IQueryable<EquipmentIncident> Full => db.EquipmentIncidents.Include(i => i.Inspection).Include(i => i.History).Include(i => i.Replacements).AsSplitQuery();
    internal async Task<EquipmentIncident> Load(Guid id, CancellationToken ct) => await Full.SingleOrDefaultAsync(i => i.Id == id, ct)
        ?? throw new LoanFailure(404, "Không tìm thấy sự cố.", "NOT_FOUND");
    internal async Task<LoanRequest> Loan(Guid id, CancellationToken ct) => await db.LoanRequests.Include(l => l.Items).Include(l => l.Histories).AsSplitQuery()
        .SingleOrDefaultAsync(l => l.Id == id, ct) ?? throw new LoanFailure(404, "Không tìm thấy phiếu mượn.", "NOT_FOUND");

    public async Task<IncidentDto> Detail(Guid id, Guid actor, bool staff, CancellationToken ct)
    {
        var incident = await Full.AsNoTracking().SingleOrDefaultAsync(i => i.Id == id && (staff || i.StudentId == actor), ct)
            ?? throw new LoanFailure(404, "Không tìm thấy sự cố.", "NOT_FOUND");
        var evidence = await db.IncidentEvidence.AsNoTracking().Where(e => e.IncidentId == id).OrderBy(e => e.CreatedAt)
            .Select(e => new IncidentEvidenceDto(e.Id, e.ContentType, e.Content.Length, e.CreatedAt)).ToArrayAsync(ct);
        var operation = incident.PendingOperationId == null ? null : await db.InventoryOperations.AsNoTracking().SingleOrDefaultAsync(o => o.OperationId == incident.PendingOperationId, ct);
        return Dto(incident, evidence, operation);
    }
    public async Task<PagedResponse<IncidentDto>> List(Guid? studentId, IncidentQuery query, CancellationToken ct)
    {
        var rows = Filter(db.EquipmentIncidents, query).Where(i => studentId == null || i.StudentId == studentId);
        var total = await rows.LongCountAsync(ct);
        var incidents = await rows.AsNoTracking().Include(i => i.Inspection).Include(i => i.Replacements).OrderByDescending(i => i.CreatedAt).ThenBy(i => i.Id)
            .Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToListAsync(ct);
        return new(incidents.Select(i => Dto(i, [], null)).ToArray(), total, query.Page, query.PageSize);
    }
    public async Task<IncidentStatistics> Stats(IncidentQuery query, CancellationToken ct)
    {
        var rows = Filter(db.EquipmentIncidents.AsNoTracking(), query);
        var avg = await rows.Where(i => i.ResolvedAt != null).Select(i => (double?)(i.ResolvedAt!.Value - i.CreatedAt).TotalHours).AverageAsync(ct);
        var failures = await rows.Where(i => i.Inspection != null && i.Inspection.Outcome == InspectionOutcome.DEFECTIVE)
            .GroupBy(i => i.EquipmentTypeId).Select(g => new { EquipmentTypeId = g.Key, Count = g.LongCount() })
            .OrderByDescending(g => g.Count).ThenBy(g => g.EquipmentTypeId).Take(20).ToArrayAsync(ct);
        return new(await rows.LongCountAsync(ct),
            await rows.LongCountAsync(i => i.Status == IncidentStatus.REPORTED || i.Status == IncidentStatus.INSPECTING, ct),
            await rows.LongCountAsync(i => i.Status == IncidentStatus.WAITING_REPLACEMENT || i.Status == IncidentStatus.CONFIRMED_DEFECT && i.ReplacementRequested && i.Inspection != null && i.Inspection.Outcome == InspectionOutcome.DEFECTIVE, ct),
            await rows.LongCountAsync(i => i.Status == IncidentStatus.RESOLVED_REPLACED, ct),
            await rows.LongCountAsync(i => i.Status == IncidentStatus.RESOLVED_NO_FAULT || i.Status == IncidentStatus.RESOLVED_REPAIRED || i.Status == IncidentStatus.RESOLVED_NO_REPLACEMENT, ct), avg,
            failures.Select(f => new VerifiedFailureCount(f.EquipmentTypeId, f.Count)).ToArray());
    }
    private static IQueryable<EquipmentIncident> Filter(IQueryable<EquipmentIncident> rows, IncidentQuery q)
    {
        if (q.Page is < 1 or > 1000000 || q.PageSize is < 1 or > 100 || q.Search?.Length > 100 || q.From > q.To
            || q.EquipmentTypeId == Guid.Empty || q.StaffId == Guid.Empty) throw new LoanFailure(400, "Bộ lọc hoặc phân trang không hợp lệ.", "VALIDATION_ERROR");
        if (!string.IsNullOrEmpty(q.Status))
        {
            if (!Enum.TryParse<IncidentStatus>(q.Status, out var status) || !Enum.IsDefined(status) || status.ToString() != q.Status)
                throw new LoanFailure(400, "Trạng thái sự cố không hợp lệ.", "VALIDATION_ERROR");
            rows = rows.Where(i => i.Status == status);
        }
        var from = q.From?.ToUniversalTime(); var to = q.To?.ToUniversalTime();
        if (from != null) rows = rows.Where(i => i.CreatedAt >= from);
        if (to != null) rows = rows.Where(i => i.CreatedAt <= to);
        if (q.EquipmentTypeId != null) rows = rows.Where(i => i.EquipmentTypeId == q.EquipmentTypeId);
        if (q.StaffId != null) rows = rows.Where(i => i.Inspection != null && i.Inspection.InspectedBy == q.StaffId || i.Replacements.Any(r => r.ReplacedBy == q.StaffId));
        if (!string.IsNullOrWhiteSpace(q.Search)) { var pattern = "%" + q.Search.Trim() + "%"; rows = rows.Where(i => EF.Functions.ILike(i.Code, pattern) || EF.Functions.ILike(i.SerialNumber, pattern)); }
        return rows;
    }

    public async Task<IncidentDto> Report(ReportIncidentRequest r, Guid actor, Guid key, string correlation, CancellationToken ct)
    {
        if (!Enum.TryParse<ProblemCategory>(r.ProblemCategory, out var category) || !Enum.IsDefined(category) || category.ToString() != r.ProblemCategory)
            throw new LoanFailure(400, "Loại sự cố không hợp lệ.", "VALIDATION_ERROR");
        var hash = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new { r.LoanId, r.LoanItemId, r.ProblemCategory, description = r.Description?.Trim(), r.ReplacementRequested })));
        var replay = await db.EquipmentIncidents.AsNoTracking().SingleOrDefaultAsync(i => i.StudentId == actor && i.SubmissionKey == key, ct);
        if (replay != null)
        {
            if (replay.RequestHash != hash) throw new LoanFailure(409, "Mã chống trùng đã dùng cho nội dung khác.", "IDEMPOTENCY_KEY_REUSED");
            return await Detail(replay.Id, actor, false, ct);
        }
        // Ownership is checked before internal reads; the shared aggregate lock spans the command, never a cross-service transaction.
        var initial = await db.LoanRequests.AsNoTracking().SingleOrDefaultAsync(l => l.Id == r.LoanId && l.StudentId == actor, ct)
            ?? throw new LoanFailure(404, "Không tìm thấy phiếu mượn.", "NOT_FOUND");
        await using var gate = await LoanLock.TryAcquire(db, initial.Id, ct) ?? throw new LoanFailure(409, "Phiếu đang được xử lý. Vui lòng thử lại.");
        var snapshot = await inventory.LoanAssets(initial.Id, correlation, ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var submissionScope = $"incident-report:{actor}:{key}";
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({submissionScope}, 0))", ct);
        replay = await db.EquipmentIncidents.AsNoTracking().SingleOrDefaultAsync(i => i.StudentId == actor && i.SubmissionKey == key, ct);
        if (replay != null)
        {
            if (replay.RequestHash != hash) throw new LoanFailure(409, "Mã chống trùng đã dùng cho nội dung khác.", "IDEMPOTENCY_KEY_REUSED");
            await tx.CommitAsync(ct); return await Detail(replay.Id, actor, false, ct);
        }
        var loan = await Loan(initial.Id, ct);
        if (await db.InventoryOperations.AnyAsync(o => o.LoanId == loan.Id && o.State != "COMPLETED" && o.State != "FAILED", ct))
            throw new LoanFailure(409, "Phiếu có thao tác thiết bị đang được đối soát.", "OPERATION_PENDING");
        if (loan.StudentId != actor) throw new LoanFailure(404, "Không tìm thấy phiếu mượn.", "NOT_FOUND");
        var item = loan.Items.SingleOrDefault(i => i.Id == r.LoanItemId);
        var allocation = snapshot.Allocations.SingleOrDefault(a => a.LoanItemId == r.LoanItemId);
        if (!snapshot.Found || snapshot.LoanId != loan.Id || allocation == null || allocation.AssetUnitId != item?.AssetUnitId
            || allocation.ReservationStatus != "HANDED_OVER" || allocation.AssetStatus != "ON_LOAN")
            throw new LoanFailure(409, "Thiết bị không đang được bàn giao cho phiếu này.", "ITEM_NOT_BORROWED");
        if (await db.EquipmentIncidents.AnyAsync(i => i.LoanItemId == r.LoanItemId && i.Status != IncidentStatus.CANCELLED
            && i.Status != IncidentStatus.RESOLVED_REPLACED && i.Status != IncidentStatus.RESOLVED_NO_FAULT && i.Status != IncidentStatus.RESOLVED_REPAIRED && i.Status != IncidentStatus.RESOLVED_NO_REPLACEMENT, ct))
            throw new LoanFailure(409, "Thiết bị đã có sự cố đang xử lý.", "OPEN_INCIDENT");
        var sequence = await db.Database.SqlQueryRaw<long>("SELECT nextval('\"IncidentNumber\"') AS \"Value\"").SingleAsync(ct);
        var incident = EquipmentIncident.Report(loan, r.LoanItemId, allocation.SerialNumber, $"INC-{clock.GetUtcNow():yyyy}-{sequence:D6}", category, r.Description!, r.ReplacementRequested, key, hash, clock.GetUtcNow());
        db.EventCorrelationId = correlation; db.EquipmentIncidents.Add(incident); await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        return await Detail(incident.Id, actor, false, ct);
    }
    public async Task<IncidentDto> Change(Guid id, Guid actor, bool staff, string action, InspectIncidentRequest? inspection, string? notes, string correlation, CancellationToken ct)
    {
        var entry = await Detail(id, actor, staff, ct);
        await using var gate = await LoanLock.TryAcquire(db, entry.LoanId, ct) ?? throw new LoanFailure(409, "Phiếu đang được xử lý.");
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var incident = await Load(id, ct); var loan = await Loan(incident.LoanId, ct);
        if (await db.InventoryOperations.AnyAsync(o => o.LoanId == loan.Id && o.State != "COMPLETED" && o.State != "FAILED", ct))
            throw new LoanFailure(409, "Cần hoàn tất thao tác thiết bị đang xử lý trước.", "OPERATION_PENDING");
        switch (action)
        {
            case "start": incident.StartInspection(actor, clock.GetUtcNow()); break;
            case "inspect":
                if (inspection == null || !Enum.TryParse<InspectionOutcome>(inspection.Outcome, out var outcome) || !Enum.IsDefined(outcome) || outcome.ToString() != inspection.Outcome)
                    throw new LoanFailure(400, "Kết luận kiểm tra không hợp lệ.", "VALIDATION_ERROR");
                incident.Inspect(outcome, inspection.Notes, inspection.CorrectiveAction, inspection.PhysicallyReceived, inspection.VerifiedSerialNumber ?? "", actor, clock.GetUtcNow()); break;
            case "wait":
                if (loan.Items.Single(i => i.Id == incident.LoanItemId).ItemStatus != ItemStatus.DAMAGED)
                    throw new LoanFailure(409, "Phải nhận trả thiết bị lỗi trước khi đăng ký chờ.");
                if (loan.DueAt <= clock.GetUtcNow()) throw new LoanFailure(409, "Hạn trả gốc đã hết; không thể cấp đổi hoặc đăng ký chờ trên hạn này.", "DUE_DATE_PASSED");
                incident.ConsentToWait(actor, clock.GetUtcNow()); break;
            case "cancel": incident.Cancel(actor, clock.GetUtcNow()); break;
            case "resolve":
                if (loan.Items.Single(i => i.Id == incident.LoanItemId).ItemStatus is not (ItemStatus.DAMAGED or ItemStatus.LOST))
                    throw new LoanFailure(409, "Cần ghi nhận trả thiết bị lỗi/thất lạc trước khi đóng sự cố.");
                incident.Resolve(actor, notes ?? "", clock.GetUtcNow()); break;
            default: throw new LoanFailure(400, "Thao tác không hợp lệ.");
        }
        db.EventCorrelationId = correlation; await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        return await Detail(id, actor, staff, ct);
    }

    public async Task<IncidentEvidence> Evidence(Guid incidentId, Guid evidenceId, Guid actor, bool staff, CancellationToken ct)
    {
        if (!await db.EquipmentIncidents.AnyAsync(i => i.Id == incidentId && (staff || i.StudentId == actor), ct)) throw new LoanFailure(404, "Không tìm thấy ảnh.", "NOT_FOUND");
        return await db.IncidentEvidence.AsNoTracking().SingleOrDefaultAsync(e => e.IncidentId == incidentId && e.Id == evidenceId, ct) ?? throw new LoanFailure(404, "Không tìm thấy ảnh.", "NOT_FOUND");
    }
    public async Task<IncidentEvidenceDto> AddEvidence(Guid id, Guid actor, byte[] content, CancellationToken ct)
    {
        var contentType = IncidentImageValidator.Validate(content);
        var hash = Convert.ToHexString(SHA256.HashData(content));
        var entry = await Detail(id, actor, false, ct);
        await using var gate = await LoanLock.TryAcquire(db, entry.LoanId, ct) ?? throw new LoanFailure(409, "Phiếu đang được xử lý.");
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var incident = await Load(id, ct);
        var existing = await db.IncidentEvidence.SingleOrDefaultAsync(e => e.IncidentId == id && e.ContentHash == hash, ct);
        if (existing != null) return new(existing.Id, existing.ContentType, existing.Content.Length, existing.CreatedAt);
        if (incident.Status != IncidentStatus.REPORTED) throw new LoanFailure(409, "Chỉ bổ sung ảnh trước khi nhân viên bắt đầu kiểm tra.");
        if (await db.IncidentEvidence.CountAsync(e => e.IncidentId == id, ct) >= 3) throw new LoanFailure(400, "Tối đa ba ảnh cho mỗi sự cố.");
        var evidence = new IncidentEvidence { IncidentId = id, Content = content, ContentType = contentType, ContentHash = hash, CreatedAt = clock.GetUtcNow() };
        db.IncidentEvidence.Add(evidence); await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        return new(evidence.Id, evidence.ContentType, content.Length, evidence.CreatedAt);
    }
    private static IncidentDto Dto(EquipmentIncident i, IReadOnlyList<IncidentEvidenceDto> evidence, InventoryOperation? operation) => new(i.Id, i.Code, i.LoanId, i.LoanItemId, i.StudentId,
        i.OriginalAssetUnitId, i.EquipmentTypeId, i.SerialNumber, i.ProblemCategory.ToString(), i.Description, i.ReplacementRequested, i.WaitingConsented, i.Status.ToString(), i.CreatedAt, i.UpdatedAt,
        i.Resolution, i.ResolvedBy, i.ResolvedAt, i.PendingOperationId, operation?.State, operation?.LastError,
        i.Inspection == null ? null : new(i.Inspection.Outcome.ToString(), i.Inspection.Notes, i.Inspection.CorrectiveAction, i.Inspection.InspectedBy, i.Inspection.InspectedAt, i.Inspection.PhysicallyReceivedAt),
        i.History.OrderBy(h => h.OccurredAt).ThenBy(h => h.Id).Select(h => new IncidentHistoryDto(h.Id, h.Action, h.FromStatus?.ToString(), h.ToStatus.ToString(), h.ActorUserId, h.Notes, h.OccurredAt)).ToArray(),
        i.Replacements.OrderBy(r => r.OperationId).Select(r => new ReplacementDto(r.OperationId, r.ReplacementLoanId, r.ReplacementLoanItemId, r.ReplacementAssetUnitId, r.SerialNumber, r.ReplacedBy, r.ReplacedAt)).ToArray(), evidence);
}
