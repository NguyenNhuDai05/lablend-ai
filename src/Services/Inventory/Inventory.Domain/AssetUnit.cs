namespace Inventory.Domain;

public static class AssetStatus
{
    public const string Available = "AVAILABLE";
    public const string Reserved = "RESERVED";
    public const string OnLoan = "ON_LOAN";
    public const string Maintenance = "MAINTENANCE";
    public const string Lost = "LOST";
    public const string Retired = "RETIRED";

    public static readonly string[] All = [Available, Reserved, OnLoan, Maintenance, Lost, Retired];
}

/// <summary>One physical, serial-numbered unit of a Catalog equipment type (logical reference, no FK).</summary>
public sealed class AssetUnit
{
    private AssetUnit() { } // EF Core

    public AssetUnit(Guid equipmentTypeId, string serialNumber, string location, string? notes, Guid? createdBy)
    {
        Id = Guid.NewGuid();
        Status = AssetStatus.Available;
        CreatedAt = DateTimeOffset.UtcNow;
        Apply(equipmentTypeId, serialNumber, location, notes);
        movements.Add(new StockMovement(Id, null, Status, null, createdBy));
    }

    public Guid Id { get; private set; }
    public Guid EquipmentTypeId { get; private set; }
    public string SerialNumber { get; private set; } = string.Empty;
    public string Location { get; private set; } = string.Empty;
    public string? Notes { get; private set; }
    public string Status { get; private set; } = AssetStatus.Available;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private readonly List<StockMovement> movements = [];
    /// <summary>Movements recorded on this instance, inserted by SaveChanges. Never loaded: read history via the repository.</summary>
    public IReadOnlyList<StockMovement> Movements => movements;

    private bool Allocated => Status is AssetStatus.Reserved or AssetStatus.OnLoan;

    public void UpdateDetails(Guid equipmentTypeId, string serialNumber, string location, string? notes)
    {
        if (equipmentTypeId != EquipmentTypeId && Allocated)
            throw new DomainRuleException("Cannot change the equipment type of a reserved or loaned unit.");
        Apply(equipmentTypeId, serialNumber, location, notes);
    }

    public void StartMaintenance(Guid? changedBy)
    {
        if (Status != AssetStatus.Available)
            throw new DomainRuleException($"Only AVAILABLE units can enter maintenance (current: {Status}).");
        SetStatus(AssetStatus.Maintenance, null, changedBy);
    }

    public void CompleteMaintenance(Guid? changedBy)
    {
        if (Status != AssetStatus.Maintenance)
            throw new DomainRuleException($"Unit is not in maintenance (current: {Status}).");
        SetStatus(AssetStatus.Available, null, changedBy);
    }

    public void Retire(Guid? changedBy)
    {
        if (Allocated)
            throw new DomainRuleException("Cannot retire a reserved or loaned unit.");
        SetStatus(AssetStatus.Retired, null, changedBy);
    }

    // Allocation transitions are driven by Reservation (gRPC); Reserve itself is a locked SQL update
    // that writes its own movement row (AllocationRepository.TryReserveAsync).
    public void HandOver(Guid loanId, Guid? actor = null) => Transition(AssetStatus.Reserved, AssetStatus.OnLoan, loanId, actor);
    public void Release(Guid loanId) => Transition(AssetStatus.Reserved, AssetStatus.Available, loanId);

    public void Return(string condition, Guid loanId, Guid? actor = null) => Transition(AssetStatus.OnLoan, condition switch
    {
        ReturnCondition.Normal => AssetStatus.Available,
        ReturnCondition.Damaged => AssetStatus.Maintenance,
        ReturnCondition.Lost => AssetStatus.Lost,
        _ => throw new DomainRuleException($"Unknown return condition '{condition}'.")
    }, loanId, actor);

    private void Transition(string from, string to, Guid loanId, Guid? actor = null)
    {
        if (Status != from)
            throw new DomainRuleException($"Unit {SerialNumber} is {Status}, expected {from}.");
        SetStatus(to, loanId, actor);
    }

    // Single choke point for status changes, so every change leaves a movement row.
    private void SetStatus(string status, Guid? loanId, Guid? changedBy)
    {
        movements.Add(new StockMovement(Id, Status, status, loanId, changedBy));
        Status = status;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    private void Apply(Guid equipmentTypeId, string serialNumber, string location, string? notes)
    {
        EquipmentTypeId = equipmentTypeId;
        SerialNumber = serialNumber.Trim().ToUpperInvariant();
        Location = location.Trim();
        Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}

public sealed class DomainRuleException(string message) : Exception(message);
