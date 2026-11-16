# Sprint 2 Member 1 acceptance

Audit date: 2026-11-16 (Asia/Bangkok). Baseline fetched from origin/main: `24ed2e9691f0fa3aa3ee1f09f297c2b0ea939850`.
PR [#8](https://github.com/NguyenNhuDai05/lablend-ai/pull/8): CLOSED / merged, confirmed through GitHub REST API; no open PRs at audit time.
Initial working tree was clean. Feature branch: `feature/m1-grpc-loan-lifecycle`. No reset, force push, shared volume removal, or M2/M3 business changes.

## Contract audit

Read Borrowing domain/application/repository/controllers/migrations/tests, authoritative inventory v1 proto and server implementation, inventory guide, Notification proposal/mapper/consumer/inbox, OD-01–05, REST/event contracts, and the repository owner's Master Implementation Plan V4.

- OD-01: one item per physical unit; UUIDs; multiple units represented by distinct LoanItems. Client generates from the owner's unchanged proto.
- OD-02: reserve all-or-nothing; stable persisted approval operation ID; operation status includes current allocations; release affects HELD only. Consumer never invents reservation IDs.
- OD-03: static service Bearer credential, separate from user JWT; 5-second deadline, correlation metadata. No automatic transport retry.
- OD-04/05: producer payload and field `event` remain PROVISIONAL pending M3 owner review; M3 consumes `recipientUserId/title/message`. Stream `lablend.events.v1`, group `notification-consumers`, inbox dedupe by EventId.
- Contract gap: v1 does not expose reservation IDs or historical return condition/time. Borrowing persists the staff's immutable return command before RPC; it must not infer historical condition from the current asset status, which can change after reuse/maintenance. Recovery is safe for commands issued exclusively through Borrowing; external writers changing reservations require M2 review.

## Resource preflight

Existing shared stack: eleven healthy containers. Docker memory allocation 3,015,319,552 bytes; host visible RAM about 6 GB, free about 450 MB. C: about 10 GB free; D: about 71 GB free. Sequential builds/tests only, no second full stack. Test fixtures own a generated private Compose project and its disposable databases. Shared volumes are preserved.

## Gates (updated as executed)

| Gate | Status | Evidence |
|---|---|---|
| A: lifecycle | TESTED / PASS | [Gate A execution](evidence/sprint2-gate-a.txt): one comprehensive real-service test, 47 s; real JWT, Catalog REST, Borrowing REST, Inventory HTTP/2, PostgreSQL. T06/07/08/10/11. First attempt exposed missing Origin in test client, fixed; production auth unchanged. |
| B: failure recovery | PLANNED | Persisted operations are prerequisite foundation; recovery tests pending |
| C: Streams | PLANNED | Existing M3 consumer unchanged |
| D: scheduled jobs | PLANNED | Foundation heartbeat still retained until real jobs pass |
| E: complete E2E / regression / Docker | PLANNED | No acceptance claimed from existing healthy baseline |

No mock response is evidence of E2E acceptance. IMPLEMENTED describes source only; TESTED/PASS requires execution evidence.
