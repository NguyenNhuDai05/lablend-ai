using Borrowing.Domain;
using Xunit;

namespace LabLend.Platform.UnitTests;

public sealed class IncidentDomainTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;
    private static readonly Guid Staff = Guid.NewGuid();
    private static LoanRequest Borrowed()
    {
        var loan = LoanRequest.Create(Guid.NewGuid(), "Laboratory incident domain test", Now.AddDays(2), [Guid.NewGuid()], Now);
        loan.BeginApproval(Guid.NewGuid(), Staff, Now);
        loan.Items[0].AssetUnitId = Guid.NewGuid(); loan.Items[0].ItemStatus = ItemStatus.RESERVED;
        loan.Approve(Staff, Now); loan.Handover(Staff, Now); return loan;
    }
    private static EquipmentIncident Report(LoanRequest loan) => EquipmentIncident.Report(loan, loan.Items[0].Id, "UNO-001", "INC-2026-000001", ProblemCategory.POWER_FAILURE,
        "Device no longer powers on during laboratory work", true, Guid.NewGuid(), new string('A', 64), Now);

    [Theory]
    [InlineData(InspectionOutcome.NO_FAULT, IncidentStatus.RESOLVED_NO_FAULT)]
    [InlineData(InspectionOutcome.FIXED, IncidentStatus.RESOLVED_REPAIRED)]
    public void Inspection_can_resolve_without_mutating_borrowing_or_allocating_equipment(InspectionOutcome outcome, IncidentStatus expected)
    {
        var loan = Borrowed(); var asset = loan.Items[0].AssetUnitId; var incident = Report(loan);
        incident.StartInspection(Staff, Now);
        incident.Inspect(outcome, "Physical inspection has been completed", "Cable reseated and operation verified", true, "uno-001", Staff, Now);
        Assert.Equal(expected, incident.Status); Assert.True(incident.Closed); Assert.Equal(Staff, incident.ResolvedBy);
        Assert.Single(loan.Items); Assert.Equal(asset, loan.Items[0].AssetUnitId); Assert.Equal(LoanStatus.BORROWED, loan.Status);
        Assert.Throws<LoanStateException>(() => incident.StartInspection(Staff, Now));
        Assert.Throws<LoanStateException>(() => incident.BeginOperation(Guid.NewGuid(), Staff, Now));
    }
    [Fact]
    public void Photos_never_replace_physical_receipt_and_serial_verification()
    {
        var incident = Report(Borrowed()); incident.StartInspection(Staff, Now);
        Assert.Throws<LoanStateException>(() => incident.Inspect(InspectionOutcome.DEFECTIVE, "Power circuit is not operating", null, false, "UNO-001", Staff, Now));
        Assert.Throws<LoanStateException>(() => incident.Inspect(InspectionOutcome.DEFECTIVE, "Power circuit is not operating", null, true, "OTHER-001", Staff, Now));
        Assert.Null(incident.Inspection); Assert.Equal(IncidentStatus.INSPECTING, incident.Status);
    }
    [Fact]
    public void Immediate_exchange_preserves_original_identity_due_date_and_distinct_new_item()
    {
        var loan = Borrowed(); var old = loan.Items[0]; var oldId = old.Id; var asset = old.AssetUnitId; var due = loan.DueAt;
        var incident = Report(loan); incident.StartInspection(Staff, Now);
        incident.Inspect(InspectionOutcome.DEFECTIVE, "Defective power circuit confirmed physically", null, true, "UNO-001", Staff, Now);
        incident.BeginOperation(Guid.NewGuid(), Staff, Now);
        var newId = Guid.NewGuid(); var newAsset = Guid.NewGuid();
        loan.ReplaceItem(oldId, newId, newAsset, "UNO-002", incident.Id, Staff, Now);
        incident.Replaced(Staff, Now);
        Assert.Equal(asset, old.AssetUnitId); Assert.Equal(ItemStatus.DAMAGED, old.ItemStatus); Assert.Equal("DAMAGED", old.ReturnCondition);
        var replacement = Assert.Single(loan.Items, i => i.Id != oldId);
        Assert.Equal(newId, replacement.Id); Assert.Equal(oldId, replacement.ReplacesLoanItemId); Assert.Equal(incident.Id, replacement.IncidentId);
        Assert.Equal(LoanStatus.BORROWED, loan.Status); Assert.Equal(due, loan.DueAt); Assert.Equal(IncidentStatus.RESOLVED_REPLACED, incident.Status);
        loan.RecordReturn(new Dictionary<Guid, string> { [newId] = "NORMAL" }, Staff, Now);
        Assert.Equal(LoanStatus.RETURNED, loan.Status);
    }
    [Fact]
    public void Delayed_fulfillment_creates_explicit_linked_followup_and_never_reopens_terminal_loan()
    {
        var original = Borrowed(); var old = original.Items[0]; var incident = Report(original);
        original.RecordReturn(new Dictionary<Guid, string> { [old.Id] = "DAMAGED" }, Staff, Now);
        Assert.Throws<LoanStateException>(() => original.ReplaceItem(old.Id, Guid.NewGuid(), Guid.NewGuid(), "UNO-002", incident.Id, Staff, Now));
        var followUp = LoanRequest.ReplacementFollowUp(Guid.NewGuid(), Guid.NewGuid(), original, incident.Id, old.Id, Staff, Now);
        followUp.FulfillFollowUp(Guid.NewGuid(), "UNO-002", Staff, Now);
        Assert.NotEqual(original.Id, followUp.Id); Assert.Equal(LoanStatus.RETURNED, original.Status); Assert.Equal(LoanStatus.BORROWED, followUp.Status);
        Assert.Equal(original.DueAt, followUp.DueAt); Assert.Equal(old.Id, followUp.Items[0].ReplacesLoanItemId);
        Assert.Throws<LoanStateException>(() => LoanRequest.ReplacementFollowUp(Guid.NewGuid(), Guid.NewGuid(), original, incident.Id, old.Id, Staff, Now.AddDays(3)));
    }
    [Fact]
    public void Waiting_requires_owner_consent_and_can_be_cancelled_with_audit()
    {
        var loan = Borrowed(); var incident = Report(loan); incident.StartInspection(Staff, Now);
        incident.Inspect(InspectionOutcome.DEFECTIVE, "Power circuit fault confirmed at laboratory", null, true, "UNO-001", Staff, Now);
        incident.BeginOperation(Guid.NewGuid(), Staff, Now); incident.Returned(Staff, "DAMAGED", Now);
        Assert.Throws<LoanStateException>(() => incident.ConsentToWait(Guid.NewGuid(), Now));
        incident.ConsentToWait(loan.StudentId, Now); Assert.Equal(IncidentStatus.WAITING_REPLACEMENT, incident.Status);
        incident.Cancel(loan.StudentId, Now);
        Assert.Equal(IncidentStatus.CANCELLED, incident.Status); Assert.Equal(loan.StudentId, incident.History[^1].ActorUserId);
        Assert.All(incident.History, h => Assert.Equal(Now, h.OccurredAt));
    }
    [Fact]
    public void Lost_inspection_does_not_claim_physical_receipt_and_closes_only_after_lost_return()
    {
        var incident = Report(Borrowed()); incident.StartInspection(Staff, Now);
        incident.Inspect(InspectionOutcome.LOST, "Student and laboratory verified missing unit", null, false, "", Staff, Now);
        Assert.Null(incident.Inspection!.PhysicallyReceivedAt); incident.BeginOperation(Guid.NewGuid(), Staff, Now);
        incident.Returned(Staff, "LOST", Now); Assert.Equal(IncidentStatus.RESOLVED_NO_REPLACEMENT, incident.Status);
    }
}
