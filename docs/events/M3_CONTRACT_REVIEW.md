# M3 P0 implementation review — 2026-11-16

This is a technical review of code and executed tests, not a record of approval by M1, M2 or M3. OD-04/05 remain **OPEN / PROVISIONAL**. The user requested taking over M3 P0 and deferred Gemini/dashboard. The supplied gap-analysis document is a scope reference, not authorization to approve team decisions.

## Current compatibility

`Borrowing.Infrastructure.LoanEvents` writes camelCase v1. `Notification.Application.ProposedLoanEventMapper` accepts the seven event names, nonempty event ID/aggregate/time, UUID recipient, title ≤200, message ≤2000. The producer supplies `loanId == aggregateId` and no purpose or rejection free text. The mapper does not enforce UUID aggregate, correlation, UTC offset, loanId equality, or reject unknown properties; the JSON schema records the stricter **producer output**, not a claim of full consumer schema enforcement. Invalid non-string recipient types can throw during mapping and follow retry/parking handling.

The executable seven-case `Member3EventContractTests` maps real producer envelopes through the current mapper, checks identity, deterministic event IDs and minimal payload. It is an in-process compatibility test. Real PostgreSQL/Redis outage, duplicate delivery and pending replay are covered separately by `Sprint2MessagingTests`; the M3 browser test adds the real worker→consumer→REST→React path.

| Event | Title | Trigger and display |
|---|---|---|
| LoanSubmitted | Loan submitted | Student commits a request |
| LoanApproved | Loan approved | Complete reserve/approval |
| LoanRejected | Loan rejected | Staff rejection; private reason stays in authorized REST |
| LoanHandedOver | Equipment handed over | Handover confirmed |
| LoanReturned | Equipment return recorded | Each committed partial/full return; REST carries final status |
| LoanDueSoon | Equipment due soon | Once per loan/due/threshold while borrowed |
| LoanOverdue | Equipment overdue | Once per loan/due while an item remains borrowed |

All payloads contain `{recipientUserId, title, message, loanId}`. See [schema](loan-event.v1.schema.json), [seven illustrative JSON examples](loan-event.v1.examples.json), [event guide](EVENT_CONTRACT_GUIDE.md) and `Member3EventContractTests`. Example UUIDs/timestamps are documentation data, not captured production events; the executable tests generate all seven envelopes from the actual producer. No eighth cancellation/expiry event is invented.

## Delivery and operator review

Current stream `lablend.events.v1`, field `event`, group `notification-consumers`, group created from `0-0`. Consumer persists Notification+Inbox in one PostgreSQL transaction, then XACK. Event ID is the inbox deduplication key. It reclaims pending entries after 30 seconds and parks non-Redis processing failures at five attempts in `lablend.events.v1.notification.parking`. Redis failures remain pending. A crash between parking XADD and source XACK can duplicate a parking record; consumers/operators must not treat parking entries as unique effects.

Guarantee: **at least once**, with idempotent persisted notification effects. No exactly-once delivery claim and no global event ordering claim. Two workers may interleave events across loans. UI polls every 15 seconds while visible; the unread header polls every 30 seconds and refreshes immediately after marking read.

Retention is intentionally **not configured**: publisher/consumer do not trim streams, delete inbox history, unpublished outbox or parking records. This avoids silent loss but storage is unbounded. Owners must agree a measurable retention period, disk monitoring, export/archive, privacy policy and replay horizon before enabling scheduled cleanup. Inbox retention must cover the entire permitted replay horizon. No production retention number is invented in this change.

Replay procedure for an operator after owner review: inspect pending/parking counts and a redacted sample; fix the mapper/data problem; replay the original valid envelope with the **same eventId** into the source stream; verify Inbox/Notification and eventual acknowledgement; record outcome before considering archival. Never generate a fresh ID to evade deduplication or reset/delete a consumer group during an incident. No replay command is run against the shared stack by this documentation change.

## Still open

- Human M1/M3 approval of envelope/schema evolution, retention and replay/parking policy (OD-04/05).
- Inventory gRPC v1 lacks reservation ID and historical return condition/time. This frontend uses Borrowing's persisted return intent/history; it does not infer historical return condition from current Inventory status or change M2's proto.
- Human defense rehearsal, participant allocation and final submission signoff. Automated browser evidence does not substitute for those activities.
