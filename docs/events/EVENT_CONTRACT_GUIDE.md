# Event contract guide

**PROPOSAL — Member 1 producer và Member 3 consumer phải review trước khi code.** Sprint 0 chưa có producer, consumer, Outbox, Inbox hay business event schema được phát hành trong shared library.

Đề xuất stream `lablend.events.v1`, group `notification-consumers`. Envelope JSON camelCase:

```json
{
  "eventId": "UUID",
  "eventType": "LoanApproved",
  "eventVersion": 1,
  "aggregateId": "loan-UUID",
  "correlationId": "request-correlation-id",
  "occurredAt": "2026-11-15T00:00:00Z",
  "payload": {}
}
```

Payload đang để trống trong ví dụ để không tự chốt schema thay M3. Events dự kiến: LoanSubmitted, LoanApproved, LoanRejected, LoanHandedOver, LoanReturned, LoanDueSoon, LoanOverdue. Hai bên cần chốt từng payload tối thiểu (recipient selection, loan/student references, timestamp), không gửi password/token/PII không cần thiết. Envelope là DTO sự kiện, không là shared Domain entity.

OD-04/05 phải thống nhất stream field chứa JSON, eventVersion compatibility, unique inbox key, consumer name per instance, retry budget, pending reclaim, parking stream và retention.

Guarantee đích: **at-least-once**. Borrowing commit business change + Outbox trong cùng transaction; Publisher XADD rồi đánh dấu PublishedAt, có thể publish trùng khi crash. Consumer XREADGROUP → transaction Notifications+Inbox commit → XACK. Crash trước XACK → XAUTOCLAIM/retry → inbox dedupe; không gọi đây là exactly-once delivery. Redis unavailable không được rollback một business commit đã có Outbox.

Reminder dedupe theo LoanId/ReminderType/date-threshold cần review cùng M1; không dùng heartbeat làm LoanOverdue event. T12–T18 cần real PostgreSQL/Redis integration, crash replay và recovery, không chỉ mock assertion.
