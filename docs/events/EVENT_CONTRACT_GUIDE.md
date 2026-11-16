# Event contract guide

**PROVISIONAL — M1 producer implemented against the existing M3 mapper; M3 owner review remains pending (OD-04/05).** Compatibility tests do not substitute for team approval. No shared Domain entity or M3 code/schema change.

Đề xuất stream `lablend.events.v1`, group `notification-consumers`. Envelope JSON camelCase:

```json
{
  "eventId": "UUID",
  "eventType": "LoanApproved",
  "eventVersion": 1,
  "aggregateId": "loan-UUID",
  "correlationId": "request-correlation-id",
  "occurredAt": "2026-11-15T00:00:00Z",
  "payload": {
    "recipientUserId": "student-UUID",
    "title": "Loan approved",
    "message": "Loan approved. Loan <UUID>. Due <UTC timestamp>.",
    "loanId": "loan-UUID"
  }
}
```

Producer supports LoanSubmitted, LoanApproved, LoanRejected, LoanHandedOver, LoanReturned, LoanDueSoon and LoanOverdue. Every event targets the loan's StudentId through recipientUserId. Title <=200 and message <=2000 characters, loanId and aggregateId identify the loan, eventId is stable across publishing retries, eventVersion=1, occurredAt is UTC. Payload contains no purpose/free-text rejection reason, password, JWT, credential or unnecessary PII. Partial return transitions produce LoanReturned for each committed return command; full lifecycle closure is visible from REST. Expiry/cancellation do not invent new event types outside the existing M3 contract.

Stream `lablend.events.v1`, JSON field `event`, group `notification-consumers`; XADD only. Existing consumer uses unique EventId inbox persistence, XREADGROUP, XAUTOCLAIM, XACK and its configured parking stream. Publisher does not trim streams or delete failed records; retention/archival requires M1/M3 review so pending/unpublished messages cannot be silently discarded. Redis AOF is configured in the local Compose stack; broker storage loss beyond configured Redis durability is outside the outbox acknowledgement guarantee.

Guarantee đích: **at-least-once**. Borrowing commit business change + Outbox trong cùng transaction; Publisher XADD rồi đánh dấu PublishedAt, có thể publish trùng khi crash. Consumer XREADGROUP → transaction Notifications+Inbox commit → XACK. Crash trước XACK → XAUTOCLAIM/retry → inbox dedupe; không gọi đây là exactly-once delivery. Redis unavailable không được rollback một business commit đã có Outbox.

Outbox capture is part of SaveChanges: submitted/approved/rejected/handed-over/returned history and event share a local PostgreSQL transaction. Old history is not republished on migration. EventId matches the transition History.Id. Publisher selects one eligible record with FOR UPDATE SKIP LOCKED per transaction, up to BatchSize each pass; a short bounded Redis call precedes the published marker. Concurrent publishers can reorder events across aggregates. Failed publishing retains the record with Attempts, LastError and bounded exponential NextAttemptAt (maximum five minutes); retry continues without dropping events.

Reminder policy (dependent background-jobs PR): LoanDueSoon once per `(loanId, dueAt, configured threshold minutes)` while BORROWED and within the future threshold; LoanOverdue once per `(loanId, dueAt)` while an item remains BORROWED. Dispatch and Outbox share one PostgreSQL transaction and unique key. No daily/restart spam. Pending Inventory commands defer reminders until state is reconciled. Changing the configured due-soon threshold deliberately creates a different key. These are implementation defaults for M1/M3 review.

Executed gate status: [Sprint 2 acceptance](../testing/SPRINT2_M1_ACCEPTANCE.md). T12–T18 require real PostgreSQL/Redis evidence; test doubles are not E2E acceptance.
