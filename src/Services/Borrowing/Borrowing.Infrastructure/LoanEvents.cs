using System.Text.Json;
using Borrowing.Domain;

namespace Borrowing.Infrastructure;

// PROVISIONAL OD-04/05: matches the existing M3 mapper; compatibility tests do not imply owner approval.
public static class LoanEvents
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public static OutboxMessage Create(LoanRequest loan, string type, Guid eventId, string correlation, DateTimeOffset occurredAt)
    {
        var title = type switch
        {
            "LoanSubmitted" => "Loan submitted", "LoanApproved" => "Loan approved", "LoanRejected" => "Loan rejected",
            "LoanHandedOver" => "Equipment handed over", "LoanReturned" => "Equipment return recorded",
            "LoanDueSoon" => "Equipment due soon", "LoanOverdue" => "Equipment overdue", _ => throw new ArgumentException("Unsupported loan event.")
        };
        return new() { EventId = eventId, EventType = type, AggregateId = loan.Id, CorrelationId = correlation,
            OccurredAt = occurredAt, NextAttemptAt = occurredAt,
            PayloadJson = JsonSerializer.Serialize(new { recipientUserId = loan.StudentId, title,
                message = $"{title}. Loan {loan.Id}. Due {loan.DueAt:O}.", loanId = loan.Id }, Json) };
    }
    public static string Envelope(OutboxMessage message) => JsonSerializer.Serialize(new
    {
        message.EventId, message.EventType, message.EventVersion, message.AggregateId, message.CorrelationId,
        message.OccurredAt, payload = JsonSerializer.Deserialize<JsonElement>(message.PayloadJson)
    }, Json);
}
