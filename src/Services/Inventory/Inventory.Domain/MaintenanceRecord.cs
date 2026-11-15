namespace Inventory.Domain;

public sealed class MaintenanceRecord
{
    private MaintenanceRecord() { } // EF Core

    public MaintenanceRecord(Guid assetUnitId, string reason, Guid? openedBy)
    {
        Id = Guid.NewGuid();
        AssetUnitId = assetUnitId;
        Reason = reason.Trim();
        OpenedBy = openedBy;
        OpenedAt = DateTimeOffset.UtcNow;
    }

    public Guid Id { get; private set; }
    public Guid AssetUnitId { get; private set; }
    public string Reason { get; private set; } = string.Empty;
    public Guid? OpenedBy { get; private set; }
    public DateTimeOffset OpenedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public Guid? CompletedBy { get; private set; }
    public string? CompletionNotes { get; private set; }

    public void Complete(string? notes, Guid? completedBy)
    {
        CompletedAt = DateTimeOffset.UtcNow;
        CompletedBy = completedBy;
        CompletionNotes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
    }
}
