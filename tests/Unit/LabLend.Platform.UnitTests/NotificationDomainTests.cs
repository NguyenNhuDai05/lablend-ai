using Notification.Application;
using Notification.Domain;
using System.Text.Json;
using Xunit;

namespace LabLend.Platform.UnitTests;

public sealed class NotificationDomainTests
{
    [Fact]
    public void MarkRead_IsIdempotent()
    {
        var item = NotificationItem.Create(Guid.NewGuid(), Guid.NewGuid(), "LoanApproved", "loan-1",
            "Approved", "Your loan was approved.", DateTimeOffset.UtcNow);
        var first = DateTimeOffset.UtcNow.AddMinutes(1);

        Assert.True(item.MarkRead(first));
        Assert.False(item.MarkRead(first.AddMinutes(1)));
        Assert.Equal(first, item.ReadAt);
    }

    [Fact]
    public void Inbox_RejectsMissingEventId() =>
        Assert.Throws<ArgumentException>(() => InboxMessage.Processed(Guid.Empty, "1-0", "LoanApproved", DateTimeOffset.UtcNow));

    [Fact]
    public void ProposedMapper_MapsReviewedEnvelopeBoundary()
    {
        var eventId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var json = JsonSerializer.Serialize(new
        {
            eventId,
            eventType = "LoanApproved",
            eventVersion = 1,
            aggregateId = "loan-42",
            correlationId = "test",
            occurredAt = DateTimeOffset.Parse("2026-11-15T00:00:00Z"),
            payload = new { recipientUserId = userId, title = "Approved", message = "Ready for handover." }
        });

        var result = new ProposedLoanEventMapper().Map("10-0", json);

        Assert.Equal(eventId, result.EventId);
        Assert.Equal(userId, result.RecipientUserId);
        Assert.Equal("10-0", result.StreamMessageId);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("not-json")]
    [InlineData("{\"eventId\":\"00000000-0000-0000-0000-000000000000\"}")]
    public void ProposedMapper_RejectsInvalidEvents(string json) =>
        Assert.Throws<InvalidDataException>(() => new ProposedLoanEventMapper().Map("10-0", json));
}
