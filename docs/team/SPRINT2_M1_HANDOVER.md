# Sprint 2 Member 1 handover

Baseline: origin/main `24ed2e9691f0fa3aa3ee1f09f297c2b0ea939850`, PR #8 merged. Current work starts on `feature/m1-grpc-loan-lifecycle`.
Acceptance and blockers: [Sprint 2 acceptance](../testing/SPRINT2_M1_ACCEPTANCE.md).

## Review order

1. `feature/m1-grpc-loan-lifecycle`: generated gRPC adapter, approval/handover/partial returns, durable operation foundation, Borrowing-only migration, real-service Gate A tests.
2. `feature/m1-reservation-recovery`: depends on lifecycle; bounded reconciliation, safe release/expiry and crash/failure tests.
3. `feature/m1-outbox-redis`: depends on recovery; transactional outbox and at-least-once Streams publishing, compatibility tests against unchanged M3 consumer.
4. `feature/m1-background-jobs`: depends on outbox; persistent reminder dedupe, schedules, multi-instance coordination, readiness and E2E evidence.

PRs must use the previous feature branch as base until dependencies merge. Never merge automatically.

## Owner review requests (not approval claims)

- M2: confirm consumer interpretation of OD-01–03, especially operation status/release after lost replies, no automatic server expiry, and the missing historical return condition. Proto/server/database remain untouched.
- M3: review provisional v1 envelope and payload (`recipientUserId`, `title`, `message`, `loanId`), stream field `event`, retention/durability policy and crash-before-XACK evidence. Compatibility tests do not substitute for owner approval.
- Pending operations requiring manual review must block new mutations until an operator verifies Inventory status; do not generate a new reservation ID to bypass an unresolved command.

## Interfaces

Staff/Admin POST `/api/loans/{id}/approve`, `/handover`, `/returns`. Returns body: `{"items":[{"loanItemId":"UUID","condition":"NORMAL|DAMAGED|LOST"}]}`.
Service auth uses Inventory configuration, never a user's JWT. PostgreSQL session advisory locks coordinate a loan across processes without holding a transaction during RPC. Persisted operation intent survives process failure. No cross-service database access in production.
