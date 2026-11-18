namespace Borrowing.Domain;

public enum LoanStatus { PENDING, APPROVING, APPROVED, BORROWED, RETURNED, REJECTED, CANCELLED, APPROVAL_FAILED, EXPIRED }
public enum ItemStatus { REQUESTED, RESERVED, BORROWED, RETURNED, DAMAGED, LOST, CANCELLED, REJECTED }
public sealed class LoanStateException(string message) : Exception(message);

public sealed class LoanRequest
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid StudentId { get; private set; }
    public string Purpose { get; private set; } = string.Empty;
    public LoanStatus Status { get; private set; } = LoanStatus.PENDING;
    public DateTimeOffset RequestedAt { get; private set; }
    public DateTimeOffset DueAt { get; private set; }
    public Guid? ReviewedBy { get; private set; }
    public DateTimeOffset? ReviewedAt { get; private set; }
    public string? RejectionReason { get; private set; }
    public Guid? ApprovalOperationId { get; private set; }
    public Guid? HandedOverBy { get; private set; }
    public DateTimeOffset? HandedOverAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public uint Version { get; private set; }
    public List<LoanItem> Items { get; private set; } = [];
    public List<LoanHistory> Histories { get; private set; } = [];
    public bool IsOverdue(DateTimeOffset now) => Status == LoanStatus.BORROWED && DueAt < now &&
        Items.Any(i => i.ItemStatus == ItemStatus.BORROWED);

    public static LoanRequest Create(Guid student, string purpose, DateTimeOffset due, IEnumerable<Guid> equipment, DateTimeOffset now)
    {
        var ids = equipment.ToArray();
        if (student == Guid.Empty || string.IsNullOrWhiteSpace(purpose) || purpose.Trim().Length is < 10 or > 2000 ||
            due <= now || due > now.AddDays(30) || ids.Length is < 1 or > 20 || ids.Contains(Guid.Empty))
            throw new ArgumentException("Invalid loan request.");
        var loan = new LoanRequest { StudentId = student, Purpose = purpose.Trim(), DueAt = due.ToUniversalTime(),
            RequestedAt = now, CreatedAt = now, UpdatedAt = now };
        loan.Items.AddRange(ids.Select(id => new LoanItem { LoanRequestId = loan.Id, EquipmentTypeId = id }));
        loan.Histories.Add(new() { LoanRequestId = loan.Id, ActorUserId = student, Action = "SUBMITTED", ToStatus = LoanStatus.PENDING, OccurredAt = now });
        return loan;
    }

    public void Cancel(Guid actor, DateTimeOffset now)
    {
        if (actor != StudentId) throw new LoanStateException("Only the owner may cancel this request.");
        if (Status == LoanStatus.CANCELLED) return;
        if (Status != LoanStatus.PENDING) throw new LoanStateException("Only PENDING requests may be cancelled.");
        Transition(actor, LoanStatus.CANCELLED, "CANCELLED", now, null);
        foreach (var item in Items) item.ItemStatus = ItemStatus.CANCELLED;
    }
    public void Reject(Guid actor, string reason, DateTimeOffset now)
    {
        if (Status != LoanStatus.PENDING) throw new LoanStateException("Only PENDING requests may be rejected.");
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length is < 3 or > 1000) throw new ArgumentException("A valid rejection reason is required.");
        RejectionReason = reason.Trim(); ReviewedBy = actor; ReviewedAt = now;
        Transition(actor, LoanStatus.REJECTED, "REJECTED", now, RejectionReason);
        foreach (var item in Items) item.ItemStatus = ItemStatus.REJECTED;
    }
    public void BeginApproval(Guid operationId, Guid actor, DateTimeOffset now)
    {
        if (Status != LoanStatus.PENDING || DueAt <= now) throw new LoanStateException("Only a PENDING loan with a future due date may be approved.");
        ApprovalOperationId = operationId;
        Transition(actor, LoanStatus.APPROVING, "APPROVING", now, null);
    }

    public void Approve(Guid actor, DateTimeOffset now)
    {
        if (Status != LoanStatus.APPROVING || Items.Any(i => i.AssetUnitId == null || i.ItemStatus != ItemStatus.RESERVED))
            throw new LoanStateException("Approval requires every item to have a verified allocation.");
        ReviewedBy = actor; ReviewedAt = now;
        Transition(actor, LoanStatus.APPROVED, "APPROVED", now, null);
    }

    public void FailApproval(Guid actor, DateTimeOffset now, string reason)
    {
        if (Status != LoanStatus.APPROVING) throw new LoanStateException("Approval is not in progress.");
        Transition(actor, LoanStatus.APPROVAL_FAILED, "APPROVAL_FAILED", now, reason);
    }

    public void Handover(Guid actor, DateTimeOffset now)
    {
        if (Status != LoanStatus.APPROVED) throw new LoanStateException("Only APPROVED loans may be handed over.");
        foreach (var item in Items) item.ItemStatus = ItemStatus.BORROWED;
        HandedOverBy = actor; HandedOverAt = now;
        Transition(actor, LoanStatus.BORROWED, "HANDED_OVER", now, null);
    }

    public void RecordReturn(IReadOnlyDictionary<Guid, string> conditions, Guid actor, DateTimeOffset now)
    {
        if (Status != LoanStatus.BORROWED) throw new LoanStateException("Only BORROWED loans may be returned.");
        foreach (var (id, condition) in conditions)
        {
            var item = Items.Single(i => i.Id == id);
            if (item.ItemStatus != ItemStatus.BORROWED) throw new LoanStateException("Item is already returned.");
            item.ReturnCondition = condition; item.ReturnedAt = now;
            item.ItemStatus = condition switch { "NORMAL" => ItemStatus.RETURNED, "DAMAGED" => ItemStatus.DAMAGED, "LOST" => ItemStatus.LOST, _ => throw new ArgumentException("Invalid return condition.") };
        }
        Transition(actor, Items.All(i => i.ItemStatus != ItemStatus.BORROWED) ? LoanStatus.RETURNED : LoanStatus.BORROWED, "RETURNED", now, null);
    }

    public void Expire(Guid actor, DateTimeOffset now)
    {
        if (Status is not (LoanStatus.APPROVED or LoanStatus.APPROVING)) throw new LoanStateException("Only reservations awaiting pickup may expire.");
        foreach (var item in Items) item.ItemStatus = ItemStatus.CANCELLED;
        Transition(actor, LoanStatus.EXPIRED, "EXPIRED", now, null);
    }

    public void ReplaceItem(Guid originalItemId, Guid newItemId, Guid assetId, string serial, Guid incidentId, Guid actor, DateTimeOffset now, DateTimeOffset? authorizedAt = null)
    {
        if (Status != LoanStatus.BORROWED || DueAt <= (authorizedAt ?? now)) throw new LoanStateException("Chỉ cấp đổi trên phiếu đang mượn và còn hạn khi xác nhận cấp đổi.");
        var old = Items.Single(i => i.Id == originalItemId);
        if (newItemId == Guid.Empty || assetId == Guid.Empty || old.AssetUnitId == null || old.AssetUnitId == assetId
            || string.IsNullOrWhiteSpace(serial) || Items.Any(i => i.Id == newItemId || i.ReplacesLoanItemId == old.Id))
            throw new LoanStateException("Danh tính thiết bị cấp đổi không hợp lệ hoặc mục này đã được cấp đổi.");
        if (old.ItemStatus == ItemStatus.BORROWED)
        {
            old.ItemStatus = ItemStatus.DAMAGED; old.ReturnCondition = "DAMAGED"; old.ReturnedAt = now;
        }
        else if (old.ItemStatus != ItemStatus.DAMAGED) throw new LoanStateException("Thiết bị gốc không đủ điều kiện đổi.");
        Items.Add(new() { Id = newItemId, LoanRequestId = Id, EquipmentTypeId = old.EquipmentTypeId, AssetUnitId = assetId,
            AssetSerialNumber = serial, ItemStatus = ItemStatus.BORROWED, IncidentId = incidentId, ReplacesLoanItemId = old.Id });
        Transition(actor, LoanStatus.BORROWED, "EQUIPMENT_REPLACED", now, $"Sự cố {incidentId}: mục mới {newItemId}, giữ lịch sử mục {old.Id}.");
    }
    public static LoanRequest ReplacementFollowUp(Guid id, Guid itemId, LoanRequest original, Guid incidentId, Guid oldItemId, Guid actor, DateTimeOffset now)
    {
        if (original.Status != LoanStatus.RETURNED || original.DueAt <= now) throw new LoanStateException("Phiếu tiếp nối yêu cầu phiếu gốc đã trả và hạn trả vẫn còn hiệu lực.");
        var old = original.Items.Single(i => i.Id == oldItemId);
        if (old.ItemStatus != ItemStatus.DAMAGED || old.ReturnedAt == null) throw new LoanStateException("Phiếu tiếp nối chỉ áp dụng cho thiết bị đã nhận trả lỗi.");
        var loan = Create(original.StudentId, $"Cấp đổi cho sự cố {incidentId}", original.DueAt, [old.EquipmentTypeId], now);
        loan.Id = id; loan.Items.Clear(); loan.Histories.Clear(); loan.Status = LoanStatus.APPROVING;
        loan.Items.Add(new() { Id = itemId, LoanRequestId = id, EquipmentTypeId = old.EquipmentTypeId, IncidentId = incidentId, ReplacesLoanItemId = oldItemId });
        loan.Transition(actor, LoanStatus.APPROVING, "REPLACEMENT_FOLLOWUP_PENDING", now, $"Phiếu gốc {original.Id}; không mở lại phiếu đã trả.");
        return loan;
    }
    public void FulfillFollowUp(Guid assetId, string serial, Guid actor, DateTimeOffset now)
    {
        if (Status != LoanStatus.APPROVING || Items.Count != 1 || Items[0].IncidentId == null) throw new LoanStateException("Phiếu tiếp nối không hợp lệ.");
        Items[0].AssetUnitId = assetId; Items[0].AssetSerialNumber = serial; Items[0].ItemStatus = ItemStatus.BORROWED;
        ReviewedBy = HandedOverBy = actor; ReviewedAt = HandedOverAt = now;
        Transition(actor, LoanStatus.BORROWED, "REPLACEMENT_FULFILLED", now, "Đã bàn giao thiết bị thay thế trên phiếu tiếp nối.");
    }

    private void Transition(Guid actor, LoanStatus next, string action, DateTimeOffset now, string? note)
    {
        Histories.Add(new() { LoanRequestId = Id, ActorUserId = actor, Action = action, FromStatus = Status,
            ToStatus = next, OccurredAt = now, Note = note });
        Status = next; UpdatedAt = now;
    }
}

public sealed class LoanItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid LoanRequestId { get; set; }
    public Guid EquipmentTypeId { get; set; }
    public Guid? AssetUnitId { get; set; }
    public Guid? ReservationId { get; set; }
    public string? AssetSerialNumber { get; set; }
    public Guid? IncidentId { get; set; }
    public Guid? ReplacesLoanItemId { get; set; }
    public string? ReturnCondition { get; set; }
    public DateTimeOffset? ReturnedAt { get; set; }
    public ItemStatus ItemStatus { get; set; } = ItemStatus.REQUESTED;
}

public sealed class LoanHistory
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid LoanRequestId { get; set; }
    public Guid ActorUserId { get; set; }
    public string Action { get; set; } = string.Empty;
    public LoanStatus? FromStatus { get; set; }
    public LoanStatus ToStatus { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public string? Note { get; set; }
}
