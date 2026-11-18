namespace Borrowing.Domain;

public enum IncidentStatus { REPORTED, INSPECTING, CONFIRMED_DEFECT, WAITING_REPLACEMENT, REPLACEMENT_IN_PROGRESS, RESOLVED_REPLACED, RESOLVED_NO_FAULT, RESOLVED_REPAIRED, RESOLVED_NO_REPLACEMENT, CANCELLED, MANUAL_REVIEW }
public enum ProblemCategory { POWER_FAILURE, PHYSICAL_DAMAGE, FUNCTIONAL_ERROR, MISSING_COMPONENT, OTHER }
public enum InspectionOutcome { NO_FAULT, FIXED, DEFECTIVE, LOST }

public sealed class EquipmentIncident
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public string Code { get; private set; } = "";
    public Guid LoanId { get; private set; }
    public Guid LoanItemId { get; private set; }
    public Guid StudentId { get; private set; }
    public Guid OriginalAssetUnitId { get; private set; }
    public Guid EquipmentTypeId { get; private set; }
    public string SerialNumber { get; private set; } = "";
    public ProblemCategory ProblemCategory { get; private set; }
    public string Description { get; private set; } = "";
    public bool ReplacementRequested { get; private set; }
    public bool WaitingConsented { get; private set; }
    public IncidentStatus Status { get; private set; } = IncidentStatus.REPORTED;
    public Guid SubmissionKey { get; private set; }
    public string RequestHash { get; private set; } = "";
    public Guid? PendingOperationId { get; set; }
    public string? Resolution { get; private set; }
    public Guid? ResolvedBy { get; private set; }
    public DateTimeOffset? ResolvedAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public uint Version { get; private set; }
    public IncidentInspection? Inspection { get; private set; }
    public List<IncidentHistory> History { get; private set; } = [];
    public List<EquipmentReplacement> Replacements { get; private set; } = [];
    public List<IncidentEvidence> Evidence { get; private set; } = [];
    public bool Closed => Status.ToString().StartsWith("RESOLVED_", StringComparison.Ordinal) || Status == IncidentStatus.CANCELLED;

    public static EquipmentIncident Report(LoanRequest loan, Guid itemId, string serial, string code,
        ProblemCategory category, string description, bool replacement, Guid key, string hash, DateTimeOffset now)
    {
        var item = loan.Items.SingleOrDefault(i => i.Id == itemId);
        if (loan.Status != LoanStatus.BORROWED || item?.ItemStatus != ItemStatus.BORROWED || item.AssetUnitId == null)
            throw new LoanStateException("Chỉ báo sự cố cho thiết bị đang mượn.");
        if (!Enum.IsDefined(category) || string.IsNullOrWhiteSpace(description) || description.Trim().Length is < 10 or > 2000 || string.IsNullOrWhiteSpace(serial))
            throw new ArgumentException("Mô tả cần từ 10 đến 2.000 ký tự và thiết bị phải có serial hợp lệ.");
        var incident = new EquipmentIncident { Code = code, LoanId = loan.Id, LoanItemId = item.Id, StudentId = loan.StudentId,
            OriginalAssetUnitId = item.AssetUnitId.Value, EquipmentTypeId = item.EquipmentTypeId, SerialNumber = serial,
            ProblemCategory = category, Description = description.Trim(), ReplacementRequested = replacement,
            SubmissionKey = key, RequestHash = hash, CreatedAt = now, UpdatedAt = now };
        incident.History.Add(new() { IncidentId = incident.Id, ActorUserId = loan.StudentId, Action = "REPORTED", ToStatus = incident.Status, OccurredAt = now });
        return incident;
    }

    public void StartInspection(Guid actor, DateTimeOffset now)
    {
        if (Status == IncidentStatus.INSPECTING) return;
        Require(IncidentStatus.REPORTED);
        Move(IncidentStatus.INSPECTING, "INSPECTION_STARTED", actor, null, now);
    }
    public void Inspect(InspectionOutcome outcome, string notes, string? correctiveAction, bool received, string verifiedSerial, Guid actor, DateTimeOffset now)
    {
        Require(IncidentStatus.INSPECTING);
        if (!Enum.IsDefined(outcome) || string.IsNullOrWhiteSpace(notes) || notes.Trim().Length is < 10 or > 2000)
            throw new ArgumentException("Kết quả kiểm tra cần từ 10 đến 2.000 ký tự.");
        if (outcome != InspectionOutcome.LOST && (!received || !string.Equals(verifiedSerial?.Trim(), SerialNumber, StringComparison.OrdinalIgnoreCase)))
            throw new LoanStateException("Phải nhận thiết bị thực tế và đối chiếu đúng serial trước khi kết luận.");
        if (outcome == InspectionOutcome.LOST && received) throw new ArgumentException("Thiết bị thất lạc không được ghi là đã nhận thực tế.");
        if (outcome == InspectionOutcome.FIXED && (string.IsNullOrWhiteSpace(correctiveAction) || correctiveAction.Trim().Length is < 10 or > 2000))
            throw new ArgumentException("Cần ghi biện pháp khắc phục từ 10 đến 2.000 ký tự.");
        Inspection = new() { IncidentId = Id, Outcome = outcome, Notes = notes.Trim(), CorrectiveAction = correctiveAction?.Trim(),
            InspectedBy = actor, InspectedAt = now, PhysicallyReceivedAt = received ? now : null, VerifiedSerialNumber = received ? SerialNumber : null };
        var next = outcome switch { InspectionOutcome.NO_FAULT => IncidentStatus.RESOLVED_NO_FAULT,
            InspectionOutcome.FIXED => IncidentStatus.RESOLVED_REPAIRED, _ => IncidentStatus.CONFIRMED_DEFECT };
        Move(next, "INSPECTION_COMPLETED", actor, notes.Trim(), now);
        if (Closed) Finish(actor, outcome == InspectionOutcome.FIXED ? correctiveAction! : notes, now);
    }
    public void BeginOperation(Guid operationId, Guid actor, DateTimeOffset now)
    {
        if (Status is not (IncidentStatus.CONFIRMED_DEFECT or IncidentStatus.WAITING_REPLACEMENT) || Inspection == null)
            throw new LoanStateException("Sự cố chưa đủ điều kiện xử lý thiết bị.");
        PendingOperationId = operationId;
        Move(IncidentStatus.REPLACEMENT_IN_PROGRESS, "OPERATION_STARTED", actor, null, now);
    }
    public void OperationFailed(Guid actor, bool manual, string note, DateTimeOffset now)
    {
        Require(IncidentStatus.REPLACEMENT_IN_PROGRESS);
        if (!manual) PendingOperationId = null;
        Move(manual ? IncidentStatus.MANUAL_REVIEW : WaitingConsented ? IncidentStatus.WAITING_REPLACEMENT : IncidentStatus.CONFIRMED_DEFECT,
            manual ? "MANUAL_REVIEW" : "OPERATION_REJECTED", actor, note, now);
    }
    public void ResumeOperation(Guid actor, DateTimeOffset now)
    {
        Require(IncidentStatus.MANUAL_REVIEW);
        if (PendingOperationId == null) throw new LoanStateException("Không có thao tác để khôi phục.");
        Move(IncidentStatus.REPLACEMENT_IN_PROGRESS, "RECOVERY_REQUESTED", actor, "Nhân viên yêu cầu đối soát lại đúng thao tác đã lưu.", now);
    }
    public void Returned(Guid actor, string condition, DateTimeOffset now)
    {
        Require(IncidentStatus.REPLACEMENT_IN_PROGRESS); PendingOperationId = null;
        Move(IncidentStatus.CONFIRMED_DEFECT, "DEFECTIVE_RETURN_RECORDED", actor, condition, now);
        if (condition == "LOST") Resolve(actor, "Đã ghi nhận thiết bị thất lạc; không cấp đổi thiết bị.", now);
    }
    public void ConsentToWait(Guid actor, DateTimeOffset now)
    {
        Require(IncidentStatus.CONFIRMED_DEFECT);
        if (actor != StudentId || Inspection?.Outcome != InspectionOutcome.DEFECTIVE || !ReplacementRequested)
            throw new LoanStateException("Chỉ người mượn mới có thể đồng ý chờ thiết bị thay thế.");
        WaitingConsented = true; Move(IncidentStatus.WAITING_REPLACEMENT, "WAIT_CONSENTED", actor, null, now);
    }
    public void Replaced(Guid actor, DateTimeOffset now)
    {
        Require(IncidentStatus.REPLACEMENT_IN_PROGRESS);
        if (!ReplacementRequested || Inspection?.Outcome != InspectionOutcome.DEFECTIVE || Inspection.PhysicallyReceivedAt == null)
            throw new LoanStateException("Chỉ cấp đổi cho lỗi được kiểm tra và thiết bị đã nhận thực tế.");
        PendingOperationId = null;
        Move(IncidentStatus.RESOLVED_REPLACED, "EQUIPMENT_REPLACED", actor, null, now);
        Finish(actor, "Đã bàn giao thiết bị cùng loại thay thế; giữ nguyên hạn trả.", now);
    }
    public void Resolve(Guid actor, string notes, DateTimeOffset now)
    {
        if (Status is not (IncidentStatus.CONFIRMED_DEFECT or IncidentStatus.WAITING_REPLACEMENT))
            throw new LoanStateException("Không thể đóng sự cố ở trạng thái hiện tại.");
        if (string.IsNullOrWhiteSpace(notes) || notes.Trim().Length is < 10 or > 2000) throw new ArgumentException("Ghi lý do đóng từ 10 đến 2.000 ký tự.");
        Move(IncidentStatus.RESOLVED_NO_REPLACEMENT, "RESOLVED", actor, notes.Trim(), now); Finish(actor, notes.Trim(), now);
    }
    public void Cancel(Guid actor, DateTimeOffset now)
    {
        if (actor != StudentId || Status is not (IncidentStatus.REPORTED or IncidentStatus.WAITING_REPLACEMENT))
            throw new LoanStateException("Chỉ hủy báo cáo chưa kiểm tra hoặc yêu cầu đang chờ thiết bị.");
        Move(IncidentStatus.CANCELLED, "CANCELLED", actor, null, now); Finish(actor, "Người mượn hủy yêu cầu.", now);
    }
    private void Finish(Guid actor, string resolution, DateTimeOffset now) { Resolution = resolution; ResolvedBy = actor; ResolvedAt = now; }
    private void Require(IncidentStatus expected) { if (Status != expected) throw new LoanStateException("Trạng thái sự cố không cho phép thao tác này."); }
    private void Move(IncidentStatus next, string action, Guid actor, string? notes, DateTimeOffset now)
    {
        History.Add(new() { IncidentId = Id, Action = action, FromStatus = Status, ToStatus = next, ActorUserId = actor, Notes = notes, OccurredAt = now });
        Status = next; UpdatedAt = now;
    }
}
public sealed class IncidentInspection
{
    public Guid IncidentId { get; set; }
    public InspectionOutcome Outcome { get; set; }
    public string Notes { get; set; } = "";
    public string? CorrectiveAction { get; set; }
    public Guid InspectedBy { get; set; }
    public DateTimeOffset InspectedAt { get; set; }
    public DateTimeOffset? PhysicallyReceivedAt { get; set; }
    public string? VerifiedSerialNumber { get; set; }
}
public sealed class IncidentHistory
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid IncidentId { get; set; }
    public string Action { get; set; } = "";
    public IncidentStatus? FromStatus { get; set; }
    public IncidentStatus ToStatus { get; set; }
    public Guid ActorUserId { get; set; }
    public string? Notes { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
}
public sealed class EquipmentReplacement
{
    public Guid OperationId { get; set; }
    public Guid IncidentId { get; set; }
    public Guid ReplacementLoanId { get; set; }
    public Guid ReplacementLoanItemId { get; set; }
    public Guid? ReplacementAssetUnitId { get; set; }
    public string? SerialNumber { get; set; }
    public Guid ReplacedBy { get; set; }
    public DateTimeOffset? ReplacedAt { get; set; }
}
public sealed class IncidentEvidence
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid IncidentId { get; set; }
    public string ContentType { get; set; } = "";
    public byte[] Content { get; set; } = [];
    public string ContentHash { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
}
