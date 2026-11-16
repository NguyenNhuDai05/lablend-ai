# Final product acceptance 2026-11-17

**Overall status: PROVISIONAL.** This checkpoint records executed work and remaining gates. It is not a production-readiness or team-submission approval. Runtime code is PR-B commit `2808d7707a35b07d9a82c7403a706c95cd379153`; verified main baseline is `68933a1f91d9c9e2e6921998f44a1030c35fab06`, including M3 PR #14. Final packaging branch: `feature/final-c-acceptance`, based on PR-B, which is based on PR-A `0fe5d05`. Main is unchanged by this work.

## Implemented and preserved

- Added optional student-scoped create idempotency, one additive Borrowing migration, explicit request/return review dialogs, real Staff Inventory serial reads, global Admin keyword/role/active filters, and owner-scoped loan notifications.
- Preserved .NET/EF Core 8, PostgreSQL ownership, YARP, all existing migrations/tests, Inventory v1 proto and allocation business logic, lifecycle/recovery/compensation, transactional Outbox/Inbox, Redis Streams and four scheduled jobs.
- Added fresh-clone T20 and bounded read-only preflight, a production-web Docker scenario with owning-DB assertions, reusable REST demo preparation, C4/ERD/state/interaction documentation and defense checklist.
- No real secrets, provider key, paid hosting, billing, force push or main merge. Gemini/dashboard remain optional and disabled/deferred.

## Current gates

| Gate | Status | Actual evidence and limits |
|---|---|---|
| Source integration | PASS | Fresh origin fetch; M3 is ancestor of main; clean initial tree; M1/M2 code preserved; open M2 PR #15 left independent |
| Release build | PASS | 0 errors, 16 NU1900 warnings: NuGet vulnerability-data endpoint unreachable; not a completed vulnerability audit |
| EF model/migration | PASS | `has-pending-model-changes` reports no changes; only new table LoanSubmissions in borrowing_db |
| Frontend production build | PASS | TypeScript and Vite complete |
| Frontend unit/transport tests | PASS | 24 passed, 0 failed, 0 skipped |
| Full real backend regression | NOT RUN | Final rerun in progress at initial packaging checkpoint; first run 69 passed/1 failed/0 skipped, failure was modal cleanup and was corrected |
| Real browser E2E rerun | PASS | Full roles/auth/Catalog/create/cancel/approve/handover/partial damaged return, maintenance UI/history assertions, Admin global search/role/active/last-admin, Profile/password, worker notifications and mobile completed; final evidence manifest records run |
| Existing Docker core runtime | PASS | Recovered daemon; Verify-Sprint2: ten required containers healthy, readiness/OpenAPI/401 checks, migrations, all four real job passes, Redis group and standard HTTP/2 health; this shared stack predates PR-B |
| Worker exit 139 diagnosis | PROVISIONAL | Current worker healthy, OOMKilled=false, exit 0 after Desktop recovery; historical 139 cause unresolved, no native dump available; no speculative application fix |
| Clean installation T20 | NOT RUN | First attempt BLOCKED before clone when daemon unavailable. Final fresh-project attempt pending; never substitute existing-stack health |
| Deck visual QA | BLOCKED | Existing deck imports but PNG renderer stalls at slide 1; native Computer Use service discovery timed out. Source deck preserved, no visual certification |
| Team rehearsal/contract approvals | PROVISIONAL | Proposed speaking assignments/checklist provided; actual rehearsal and OD owner signoff remain human tasks |
| Gemini | NOT RUN | Optional skeleton, disabled; no provider call or bonus completion claimed. P0 verification takes priority |

## Three main flows

| Flow | Expected behavior | Executed result |
|---|---|---|
| 1 Student borrowing | Real Catalog/detail; select quantities; validate and review; single confirmed create; actual list/history and owned notifications | Private real-service browser rerun PASS. Concurrent replay/key conflict/student scoping covered by new PostgreSQL tests. Production Docker run tracked separately by T20 |
| 2 Staff fulfillment | Review; all-or-nothing reserve via real HTTP/2; handover; partial/full NORMAL/DAMAGED/LOST; truthful recovery and role checks | Browser approval/handover/damaged+normal partial/full PASS. Existing lifecycle/recovery/concurrency/LOST/timeout/lost-reply suites remain mandatory in full regression; T20 adds all three conditions |
| 3 Inventory lifecycle | Real type/physical asset registration, allocation, repair, maintenance and append-only StockMovements | Existing real Inventory suites PASS; browser rerun completes maintenance cycle and preserves previous movement records; production web/DB assertions belong to T20 |

Expected/actual per-test outcomes and durations belong in `docs/testing/evidence/final-test-results.json`; Docker scenario records each stage in `docker-e2e.json`. A test implementation alone is not an executed PASS. Fixtures create their own accounts/data using business APIs for E2E; repository tests may explicitly prepare integration-only rows for focused repository validation.

## R01 to R18

| ID | Source and execution trace | Status |
|---|---|---|
| R01 REST CRUD | Catalog/Inventory controllers and real PostgreSQL suites; Identity/Borrowing/Notification APIs | PASS for executed suites; final consolidated gate above |
| R02 Layered architecture | Controllers, Application ports/services, Infrastructure repositories; no cross-service DbContext | PASS |
| R03 JWT | Identity hashing/rotation/CSRF, policies, memory token; real roles/browser tests | PASS for browser and platform suites |
| R04 Search/filter/sort/page | Catalog and Inventory server queries; Admin global filters; exact request tests | PASS frontend; real Admin query result in final suite |
| R05 Background job | Due/overdue/reconciliation/outbox jobs; actual browser reminders, worker readiness passes | PASS for observed jobs; worker fault/restart suite in consolidated gate |
| R06 Redis | Real Streams publisher/consumer, XACK/XAUTOCLAIM, outage/reconnect suites | PROVISIONAL until final consolidated gate |
| R07 Producer and consumer | Outbox to Redis to Inbox to REST/UI; real browser delivery | PASS delivery; replay/failure gate separate |
| R08 Independent gRPC | Inventory host/proto/auth; HTTP/2 real PostgreSQL tests | PASS |
| R09 REST invokes gRPC | Real approve/handover/returns browser and lifecycle suites | PASS browser; failures/concurrency final suite separate |
| R10 EF and relational DB | Owned contexts/migrations, new submission PK/FK and real PostgreSQL assertions | PASS exercised contexts; fresh Docker migration T20 separate |
| R11 DI/config/log/errors | Options validation, correlation, ProblemDetails, health and structured worker logs | PASS exercised paths |
| R12 Deploy system | Existing-stack recovered health; fresh production-web installation required | PROVISIONAL pending T20 |
| R13 Complete source | Three stacked review branches/PRs, M1/M2/M3 integration preserved | PROVISIONAL pending reviews |
| R14 DB scripts/migrations | Existing migrations retained, additive idempotency table, bootstrap/isolation scripts | PASS source/model; T20 execution separate |
| R15 Dockerfiles/Compose | Compose validates and existing images run; final fresh images required | PROVISIONAL pending T20 |
| R16 API docs | Real five-service OpenAPI checked through Gateway; compatible extensions documented | PASS baseline runtime; new contracts exercised by final tests/T20 |
| R17 README | Required six topics plus clean install, scripts, migration, roles, tests, troubleshooting | PASS documentation coverage |
| R18 Seven live demo topics | Runbook, proposed speakers, C4/ERD, evidence and review checklist | PROVISIONAL: team rehearsal and deck visual inspection pending |

## Migrations and operational limits

`20261116180731_AddLoanSubmissionIdempotency` creates `LoanSubmissions(StudentId,Key,RequestHash,LoanId)` only in borrowing_db. API/worker retain their migration advisory lock. No existing migration was deleted, no shared volume was removed, and no database was reset. Replay keys are retained with their loan; a new browser draft/reload has a new in-memory key, so users must inspect My Loans after an unknown outcome.

OD-04/05 and retention/archive/replay horizon remain **PENDING HUMAN REVIEW**. No trimming or fabricated signoff. Inventory v1 reservation ID/historical condition/time gap and documented single-writer recovery assumption remain; PR #15 supplies independent M2 owner review. Current asset state is not historical evidence. Refresh revocation does not instantly revoke already issued JWTs in other services. .NET 8 is preserved for coursework; long-term public operation needs a separately reviewed upgrade.

## Pull requests and final review

1. [PR-A #16](https://github.com/NguyenNhuDai05/lablend-ai/pull/16), base main: isolated preflight/T20 infrastructure and source audit.
2. [PR-B #17](https://github.com/NguyenNhuDai05/lablend-ai/pull/17), base PR-A branch: workflow/backend/UI corrections and compatible contracts.
3. PR-C: final packaging branch, URL added after creation. Merge only after A, then B, retargeting dependent bases to main as appropriate.

Reviewers must inspect final CI SHAs, the actual clean-install result, source diff, secrets, migration, event/gRPC boundaries and remaining human tasks. Do not auto-merge. See [handover](../team/FINAL_COMPLETION_HANDOVER.md) and [seven-topic defense checklist](../demo/FINAL_DEFENSE_CHECKLIST.md).
