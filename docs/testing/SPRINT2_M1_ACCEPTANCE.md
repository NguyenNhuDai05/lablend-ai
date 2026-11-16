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
| B: failure recovery | TESTED / PASS | [Gate B](evidence/sprint2-gate-b.txt), 46 s: real reserve/handover/return replies dropped after commit; test-owned Borrowing process killed during local approval commit; same allocation restored; failed local commit compensated; pickup expiry; no release after handover; bad service token; bounded retry to manual review. Initial test cleanup pause was too long and was corrected before the passing run. |
| C: Streams | TESTED / PASS | [Gate C](evidence/sprint2-gate-c.txt): 2 tests, real Redis outage/reconnect, atomic PostgreSQL rollback, concurrent XADD, actual M3 consumer/Notification REST, duplicate EventId and staged post-inbox/pre-XACK recovery with real XAUTOCLAIM. Initial fixture failure traced to Docker changing an ephemeral port on restart; [diagnostic](evidence/sprint2-redis-port-diagnostic.txt), stable private port fixed it. |
| D: scheduled jobs | TESTED / PASS | [Gate D/E](evidence/sprint2-gate-d-e.txt): 2 tests; real hosted schedules, naturally elapsed due date, two worker processes, restart, unique reminder keys, all four job logs and dependency readiness. |
| E: complete E2E / regression / Docker | TESTED / PASS (scoped evidence below) | Actual Identity JWT→Catalog→Borrowing REST→Inventory gRPC reserve/handover→scheduled Outbox→Redis→M3 Notification REST→overdue job→return in private real-service fixtures. Backend/frontend regression passed. Updated Borrowing Docker images built, migrations applied concurrently at startup, 11-container runtime/isolated-account checks passed. |

No mock response is evidence of E2E acceptance. IMPLEMENTED describes source only; TESTED/PASS requires execution evidence.

Lifecycle PR: [#10](https://github.com/NguyenNhuDai05/lablend-ai/pull/10), commit `5d23245428d22b0f7533adc87d859f9f86898820`. Hosted [CI](https://github.com/NguyenNhuDai05/lablend-ai/actions/runs/43821389658) completed successfully for that exact commit (build, full backend suites with real PostgreSQL, frontend build/tests, Compose validation). No merge performed.

Recovery PR: [#11](https://github.com/NguyenNhuDai05/lablend-ai/pull/11), stacked on #10, commit `315777aede72cae6a82ea860feb89090536dbb3d`. Hosted [CI](https://github.com/NguyenNhuDai05/lablend-ai/actions/runs/43822285722) completed successfully for that exact commit. No merge performed.

Outbox PR: [#12](https://github.com/NguyenNhuDai05/lablend-ai/pull/12), stacked on #11, commit `d4d2a3d1a52ac980092614cfd2767d1440098441`. Hosted [CI](https://github.com/NguyenNhuDai05/lablend-ai/actions/runs/43824491341) completed successfully for that exact commit. No merge performed.

Worker PR: [#13](https://github.com/NguyenNhuDai05/lablend-ai/pull/13), stacked on #12, implementation commit `08c6fe23a69a3c310013f66043b37ad1706e586f`. Initial [CI](https://github.com/NguyenNhuDai05/lablend-ai/actions/runs/43825611023) passed. Final business guard/test commit `b04c29e0e9180f0ff6728848df228abecb1eb828` also has successful full hosted [CI](https://github.com/NguyenNhuDai05/lablend-ai/actions/runs/43827198705), including the added recovery fact. No merge performed.

## Full regression checkpoint

[Local Release regression](evidence/sprint2-regression-backend.txt): 59 passed, zero failed, zero skipped (20 unit, 10 platform integration, 3 Catalog, 11 Inventory, 15 Identity/Borrowing/member integration including the six initial Sprint 2 facts). Real PostgreSQL/Redis suites enabled with LABLEND_POSTGRES_TESTS=1 and sequential MSBuild. Inventory includes HTTP/2/proto T23, service-token T27, timeout T09, maintenance and allocation regression. Identity JWT/RBAC/refresh and schema/account isolation suites remain intact. Final guard adds one additional fact; its execution is reported separately rather than inflating this historical 59-test count.

Final recovery guard commit: `b04c29e`. [Focused Release tests](evidence/sprint2-recovery-concurrency.txt): 2 passed, including the existing full recovery case and the new late-error case. The fault fixture uses a real Inventory reply and a separate real PostgreSQL transaction to model competing recovery commit after loss of coordination, then verifies both lost-reply and stale-xmin-save paths: one APPROVED history, one Outbox event, completed command preserved and reservation never compensated. Together with the full checkpoint, 60 distinct backend facts have been exercised locally; no false 61-test count from the repeated recovery fact.

[Frontend regression](evidence/sprint2-regression-frontend.txt): production TypeScript/Vite build PASS; 11 Node tests passed, zero failed/skipped, covering login/session/refresh races, role routing and Staff Inventory/StockMovements error preservation. No frontend source modification.

## Docker and final audit

- [API image](evidence/sprint2-docker-build-api.txt) and [worker image](evidence/sprint2-docker-build-worker.txt) built sequentially. Shared Linux build compiled the entire solution: zero warnings/errors. API image manifest list `5a62e8bc6f20b60f46a87fde7f07bbcfb571e40b2337c6b0292eefebadc5cc2d`; worker `bec1f78081c34e5ea7c2ec8a0ba68b706b5484471a117dd024e1f8de9143fe1e`.
- [Compose upgrade](evidence/sprint2-compose-up.txt): replaced only Borrowing API/worker, preserved all database/Redis volumes and existing other-service containers. Both became healthy; [11-container snapshot](evidence/sprint2-compose-ps.txt).
- [Read-only runtime verification](evidence/sprint2-docker-runtime.txt), via scripts/Verify-Sprint2.ps1 -IncludeBonus: Gateway/nginx readiness/correlation/OpenAPI, anonymous new commands 401, four completed scheduled jobs, authenticated Inventory dependency readiness, all four Borrowing migrations, Redis consumer group and six isolated database accounts PASS.
- Shared runtime Sprint 2 tables were empty at verification (operations/outbox/reminders=0). Business/event delivery/fault evidence comes from private integration fixtures, not from these empty tables or health endpoints. Redis group metadata alone is not producer acceptance. Shared runtime business records were not seeded or rewritten for this check.
- Empty-database migrations were exercised by private fixtures; Docker verification upgraded the existing stack. A destructive clean-volume reset or separate full-stack clean-machine installation was not performed.
- Final origin fetch still resolves main to `24ed2e9691f0fa3aa3ee1f09f297c2b0ea939850`; private `lablend-s1-test-*` container inventory is empty. No hard reset/force push/main push/merge, global prune, shared volume removal, paid services/billing or AI calls.
- Final code branch: `feature/m1-background-jobs`; verified business-code commit `b04c29e0e9180f0ff6728848df228abecb1eb828`. Subsequent evidence/handover commit does not change business implementation. The final response reports its exact HEAD.
- Diff audit: no Identity/Catalog/Inventory/Notification/Recommendation/web/proto business file changed. Shared edits limited to exact existing package version pins, worker environment, private test Compose/helpers/references and documentation/read-only verification.

## Acceptance scope and remaining review

**TESTED:** M1 contribution to R05 (real scheduled jobs), R06/R07 (Streams producer→consumer/Inbox), R09 (REST→gRPC), R10/R14 (owned migrations/transactions), R11/R16 (DI/options/logs/ProblemDetails/Swagger), and R12/R15 (local Docker upgrade/startup). R03 and existing Catalog/Inventory/Notification/session behavior retain regression evidence. R13/R17 documentation/source are updated; complete-coursework or R18 defense acceptance is not inferred from these tests.

**IMPLEMENTED + TESTED:** approval/handover/partial returns, stable operation intent, bounded recovery, safe compensation/expiry, atomic Outbox, Streams publishing, real consumer compatibility, four hosted jobs, durable reminder dedupe and coordinated instances. Delivery is at least once.

**PROVISIONAL / OWNER REVIEW PENDING:** OD-04/05 event/retention policy with M3; M2 historical return-condition/time/reservation-ID contract gap and consumer assumptions. No team approval is claimed. Manual-review operations remain durable/blocked to new mutations until an owner verifies the authoritative state; the handover describes operator procedure.

**PLANNED / OUTSIDE M1 SPRINT 2:** M3 Staff loan UI extensions, bonus Gemini, full defense/demo package and any owner-approved v2 contract enhancement. No mandatory path depends on them.

**BLOCKED:** no unresolved technical execution blocker for the scoped gates. Owner reviews remain pending; this report does not close OPEN_DECISIONS or certify external reservation writers that bypass Borrowing. T15's post-inbox/pre-XACK crash window is deterministically staged with M3's actual inbox implementation and replayed by its real restarted consumer; T28 physically terminates the real Borrowing process.
