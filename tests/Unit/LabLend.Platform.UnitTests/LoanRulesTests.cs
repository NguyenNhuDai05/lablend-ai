using Borrowing.Domain;
using Xunit;

namespace LabLend.Platform.UnitTests;

public sealed class LoanRulesTests
{
    [Fact]
    public void One_item_represents_one_unit_including_repeated_equipment_types()
    {
        var now = DateTimeOffset.UtcNow;
        var equipment = Guid.NewGuid(); var student = Guid.NewGuid();
        var loan = LoanRequest.Create(student, "Two units for a laboratory experiment", now.AddDays(30), [equipment, equipment], now);
        Assert.Equal(2, loan.Items.Count); Assert.Equal(2, loan.Items.Select(i => i.Id).Distinct().Count());
        Assert.All(loan.Items, i => { Assert.Null(i.AssetUnitId); Assert.Null(i.ReservationId); });
        Assert.Equal(LoanStatus.PENDING, loan.Status); Assert.False(loan.IsOverdue(now.AddDays(31)));
        Assert.Throws<LoanStateException>(() => loan.Cancel(Guid.NewGuid(), now));
        loan.Cancel(student, now.AddMinutes(1)); loan.Cancel(student, now.AddMinutes(2));
        Assert.Equal(2, loan.Histories.Count); Assert.All(loan.Items, i => Assert.Equal(ItemStatus.CANCELLED, i.ItemStatus));
        Assert.Throws<LoanStateException>(() => loan.Reject(Guid.NewGuid(), "Too late to reject", now));
    }
    [Theory]
    [InlineData(-1, 1)] [InlineData(0, 1)] [InlineData(31, 1)] [InlineData(1, 0)] [InlineData(1, 21)]
    public void Submission_enforces_due_window_and_item_count(int days, int count)
    {
        var now = DateTimeOffset.UtcNow;
        Assert.Throws<ArgumentException>(() => LoanRequest.Create(Guid.NewGuid(), "Valid experiment purpose", now.AddDays(days),
            Enumerable.Repeat(Guid.NewGuid(), count), now));
    }
}
