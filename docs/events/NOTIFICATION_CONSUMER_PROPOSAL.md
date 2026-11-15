# Notification consumer proposal (M3 implementation boundary)

This is an implementation proposal pending M1 review of OD-04 and OD-05. It does not finalize the
Borrowing producer contract.

- Stream: `lablend.events.v1`; field containing JSON: `event`; group: `notification-consumers`.
- Parking stream: `lablend.events.v1.notification.parking`; maximum attempts: 5; pending idle time: 30 seconds.
- The v1 envelope follows `EVENT_CONTRACT_GUIDE.md`. The provisional payload fields consumed by M3 are
  `recipientUserId` (UUID), `title` (up to 200 characters), and `message` (up to 2000 characters).
- The mapper is isolated behind `ILoanEventMapper` so the reviewed M1 payload can replace this proposal
  without changing persistence or Redis delivery mechanics.
- Processing is at least once: `XREADGROUP`/`XAUTOCLAIM` → one PostgreSQL transaction for Notification +
  Inbox → `XACK`. `eventId` is the Inbox primary key. A duplicate delivery is acknowledged only after the
  committed Inbox record proves it was already processed.
- A non-Redis processing failure increments a Redis attempt hash. At the configured limit, the original
  message id, original event JSON, failure type, attempt count, and parking time are written to the parking
  stream before the source message is acknowledged.

M1/M3 must still review casing, event names, producer stream fields, payload minimization, retry budget,
retention, and the real PostgreSQL/Redis crash/replay test evidence before closing OD-04/05.
