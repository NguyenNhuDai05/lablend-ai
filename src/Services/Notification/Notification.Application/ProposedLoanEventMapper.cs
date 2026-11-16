using System.Text.Json;

namespace Notification.Application;

// OD-04 proposal boundary. M1/M3 must review the payload field names before treating this contract as final.
public sealed class ProposedLoanEventMapper : ILoanEventMapper
{
    private static readonly HashSet<string> SupportedTypes =
    ["LoanSubmitted", "LoanApproved", "LoanRejected", "LoanHandedOver", "LoanReturned", "LoanDueSoon", "LoanOverdue"];

    public IncomingNotification Map(string streamMessageId, string json)
    {
        LoanEventEnvelope envelope;
        try { envelope = JsonSerializer.Deserialize<LoanEventEnvelope>(json, SerializerOptions) ?? throw Invalid("Envelope is empty."); }
        catch (JsonException exception) { throw Invalid("Envelope is not valid JSON.", exception); }

        if (envelope.EventId == Guid.Empty || envelope.EventVersion != 1 || !SupportedTypes.Contains(envelope.EventType)
            || string.IsNullOrWhiteSpace(envelope.AggregateId) || envelope.OccurredAt == default)
            throw Invalid("Envelope fields or version are unsupported.");
        if (envelope.Payload.ValueKind != JsonValueKind.Object) throw Invalid("Payload must be an object.");

        var recipient = RequiredGuid(envelope.Payload, "recipientUserId");
        var title = RequiredString(envelope.Payload, "title", 200);
        var message = RequiredString(envelope.Payload, "message", 2000);
        return new IncomingNotification(envelope.EventId, envelope.EventType, envelope.AggregateId, recipient,
            title, message, envelope.OccurredAt, streamMessageId);
    }

    private static Guid RequiredGuid(JsonElement payload, string name) =>
        payload.TryGetProperty(name, out var value) && Guid.TryParse(value.GetString(), out var result) && result != Guid.Empty
            ? result : throw Invalid($"Payload field {name} must be a UUID.");

    private static string RequiredString(JsonElement payload, string name, int maxLength)
    {
        if (!payload.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String) throw Invalid($"Payload field {name} is required.");
        var result = value.GetString()?.Trim();
        return !string.IsNullOrEmpty(result) && result.Length <= maxLength ? result : throw Invalid($"Payload field {name} is invalid.");
    }

    private static InvalidDataException Invalid(string message, Exception? inner = null) => new(message, inner);
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
}
