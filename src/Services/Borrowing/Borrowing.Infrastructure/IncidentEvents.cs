using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Borrowing.Domain;
using Microsoft.EntityFrameworkCore;

namespace Borrowing.Infrastructure;

internal static class IncidentEvents
{
    internal static void Capture(BorrowingDbContext db)
    {
        foreach (var history in db.ChangeTracker.Entries<IncidentHistory>().Where(e => e.State == EntityState.Added).Select(e => e.Entity).ToArray())
        {
            var incident = db.ChangeTracker.Entries<EquipmentIncident>().Single(e => e.Entity.Id == history.IncidentId).Entity;
            var type = history.Action switch { "REPORTED" => "IncidentReported", "INSPECTION_STARTED" => "IncidentInspectionStarted",
                "INSPECTION_COMPLETED" => "IncidentInspectionCompleted", "EQUIPMENT_REPLACED" => "EquipmentReplaced",
                "RESOLVED" or "CANCELLED" => "IncidentResolved", _ => null };
            if (type == null) continue;
            var recipient = incident.StudentId;
            if (type == "IncidentReported")
            {
                var loan = db.ChangeTracker.Entries<LoanRequest>().Single(e => e.Entity.Id == incident.LoanId).Entity;
                recipient = loan.HandedOverBy ?? loan.ReviewedBy ?? throw new InvalidOperationException("Borrowed loan has no responsible staff member.");
            }
            Add(db, incident, history, type, recipient);
            if (history.ToStatus.ToString().StartsWith("RESOLVED_", StringComparison.Ordinal) && type != "IncidentResolved")
                Add(db, incident, history, "IncidentResolved", incident.StudentId);
        }
    }
    private static void Add(BorrowingDbContext db, EquipmentIncident incident, IncidentHistory history, string type, Guid recipient)
    {
        var id = new Guid(SHA256.HashData(Encoding.UTF8.GetBytes($"{history.Id}:{type}:{recipient}")).AsSpan(0, 16));
        if (db.ChangeTracker.Entries<OutboxMessage>().Any(e => e.Entity.EventId == id)) return;
        var title = type switch { "IncidentReported" => "Có báo cáo sự cố thiết bị", "IncidentInspectionStarted" => "Đang kiểm tra thiết bị",
            "IncidentInspectionCompleted" => "Đã có kết quả kiểm tra thiết bị", "EquipmentReplaced" => "Đã bàn giao thiết bị thay thế", _ => "Sự cố đã kết thúc" };
        var message = $"{incident.Code} · {incident.SerialNumber}. {title}. {(type == "IncidentReported" ? "Cần kiểm tra trực tiếp; ảnh chỉ là bằng chứng hỗ trợ." : history.Notes ?? incident.Resolution ?? "Xem chi tiết sự cố để theo dõi.")}";
        if (message.Length > 2000) message = message[..(char.IsHighSurrogate(message[1999]) ? 1999 : 2000)];
        db.OutboxMessages.Add(new() { EventId = id, EventType = type, AggregateId = incident.Id, CorrelationId = db.EventCorrelationId,
            OccurredAt = history.OccurredAt, NextAttemptAt = history.OccurredAt,
            PayloadJson = JsonSerializer.Serialize(new { recipientUserId = recipient, title,
                message, incidentId = incident.Id, loanId = incident.LoanId }) });
    }
}
