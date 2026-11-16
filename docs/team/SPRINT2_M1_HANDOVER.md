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

## Recovery operations

`BorrowingWorkflow` options: BatchSize=20 (1–100), MaxAttempts=8 (1–20), PickupHours=24 (1–168), PollSeconds=15 (1–3600). Pending/RETRY/COMPENSATING commands are scanned by NextRetryAt with bounded exponential backoff (up to five minutes). Each loan uses the same session advisory key across API and reconciler; re-read state after acquiring it. MANUAL_REVIEW blocks further mutations and is never automatically reset.

Use borrowing_user only to inspect InventoryOperations for OperationId, LoanId, OperationType, State, RetryCount, LastError and NextRetryAt. For manual review, query Inventory through authenticated GetOperationStatus with the loan's ApprovalOperationId, compare every item/asset/status against the durable intent, and resolve the underlying credential/network/contract problem. Do not set APPROVED manually, delete intent, or generate a new operation ID to bypass a conflict. An owner-reviewed repair procedure is required for external reservation writers or inconsistent HANDED_OVER/RETURNED state; those holdings must never be released as pickup expiry.

An acknowledged reserve followed by failed local commit records COMPENSATING when the database is reachable. Reconciliation verifies HELD/RELEASED, calls ReleaseReservations and rechecks status before finalizing local failure/expiry. If the database was unavailable, the earlier PENDING command survives and can roll forward on recovery. A process crash with no recorded compensation rolls forward the same allocation when still valid. Expired approvals stabilize an unknown reserve through the same-ID replay before release: found=false alone cannot prove an older request is no longer in flight. Handover/return recovery uses the approval reference plus immutable per-item return intent; it never assigns a new asset.

Draft lifecycle PR: [#10](https://github.com/NguyenNhuDai05/lablend-ai/pull/10). Exact-head CI for `5d23245`: [success](https://github.com/NguyenNhuDai05/lablend-ai/actions/runs/43821389658).

Draft recovery PR: [#11](https://github.com/NguyenNhuDai05/lablend-ai/pull/11), base #10. Exact-head CI for `315777a`: [success](https://github.com/NguyenNhuDai05/lablend-ai/actions/runs/43822285722).

Draft outbox PR: [#12](https://github.com/NguyenNhuDai05/lablend-ai/pull/12), base #11. Exact-head CI for `d4d2a3d`: [success](https://github.com/NguyenNhuDai05/lablend-ai/actions/runs/43824491341).

## Scheduled jobs and readiness

DueReminderJob and OverdueDetectionJob default to a 60-second poll, batch 20; Reminders:DueSoonMinutes defaults to 1440. DueSoon dispatch key is `(LoanId, LoanDueSoon, DueAt, configured threshold minutes)`; Overdue key uses threshold 0, one event per loan due date. Configuring a new due-soon threshold permits one new reminder for that threshold. A local unique index and per-loan advisory lock coordinate processes; Dispatch and Outbox commit together. Jobs recheck BORROWED items after locking and skip unresolved Inventory operations. OVERDUE remains derived; no lifecycle overwrite. Terminal/partially returned items are handled from remaining BORROWED units. Restart does not clear dispatches.

OutboxPublisherJob default interval is 2 seconds; ReservationReconciliationJob uses BorrowingWorkflow:PollSeconds (15). Every hosted job creates a fresh DI scope per pass. Record failures are isolated; failed passes retry on the next schedule. Job logs include names, record IDs/counts and safe error types/codes. The heartbeat now requires PostgreSQL, Redis, an authenticated Inventory RPC and recent passes from all four jobs. Default heartbeat interval is 15 seconds; a failed dependency removes the heartbeat file. Jobs keep attempting recovery even when readiness is unhealthy.

Worker requires ConnectionStrings__Database for borrowing_db only, ConnectionStrings__Redis, Inventory__GrpcAddress and Inventory__ServiceToken. Compose supplies them. API and worker apply Borrowing migrations under a shared schema advisory lock; Database:ApplyMigrations can disable automatic application for an externally managed environment. No other service database is migrated by these hosts.
