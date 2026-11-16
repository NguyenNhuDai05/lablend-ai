using System.Text.Json;
using Borrowing.Domain;
using Borrowing.Infrastructure;
using Notification.Application;
using Xunit;

namespace LabLend.Sprint1.IntegrationTests;

// In-process compatibility evidence, separate from the real PostgreSQL/Redis replay gates.
public sealed class Member3EventContractTests
{
    [Theory]
    [InlineData("LoanSubmitted")]
    [InlineData("LoanApproved")]
    [InlineData("LoanRejected")]
    [InlineData("LoanHandedOver")]
    [InlineData("LoanReturned")]
    [InlineData("LoanDueSoon")]
    [InlineData("LoanOverdue")]
    public void Producer_v1_preserves_recipient_identity_dedup_key_and_minimal_payload(string eventType)
    {
        var now = DateTimeOffset.UtcNow;
        var student = Guid.NewGuid();
        var loan = LoanRequest.Create(student, "Private purpose must not enter notification payload", now.AddDays(2), [Guid.NewGuid()], now);
        var id = Guid.NewGuid();
        var message = LoanEvents.Create(loan, eventType, id, "m3-contract-review", now);
        var envelope = LoanEvents.Envelope(message);
        using var document = JsonDocument.Parse(envelope);
        var root = document.RootElement;
        Assert.Equal(1, root.GetProperty("eventVersion").GetInt32());
        Assert.Equal(loan.Id, root.GetProperty("aggregateId").GetGuid());
        Assert.Equal(loan.Id, root.GetProperty("payload").GetProperty("loanId").GetGuid());
        Assert.Equal(new[] { "loanId", "message", "recipientUserId", "title" }, root.GetProperty("payload").EnumerateObject().Select(p => p.Name).Order().ToArray());
        Assert.DoesNotContain(loan.Purpose, envelope);
        var incoming = new ProposedLoanEventMapper().Map("1-0", envelope);
        Assert.Equal(id, incoming.EventId);
        Assert.Equal(student, incoming.RecipientUserId);
        Assert.Equal(eventType, incoming.EventType);
        Assert.InRange(incoming.Title.Length, 1, 200);
        Assert.InRange(incoming.Message.Length, 1, 2000);
        Assert.Equal(envelope, LoanEvents.Envelope(message));
    }
}
