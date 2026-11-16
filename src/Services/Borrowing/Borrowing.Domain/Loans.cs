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
