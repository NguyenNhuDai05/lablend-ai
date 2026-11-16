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
