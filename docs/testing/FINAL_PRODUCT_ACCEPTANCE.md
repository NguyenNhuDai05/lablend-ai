# Final product acceptance 2026-11-17

**Automated P0 gates: PASS. Overall submission status: PROVISIONAL.** Team rehearsal, deck visual QA and owner decisions remain open; this is not production-readiness or team-submission approval. Final code checkpoint is `430ff1b15ce7441a3862ad247316943e9a91acd2` on `feature/final-c-acceptance`. Fresh Docker T20 tested `ea868e4f22ebc13fbc2371ca1bfb73ef99435179`; `git diff --exit-code` confirms identical runtime, web, proto, scripts, infrastructure and Compose content at the checkpoint. This followup adds evidence/documentation only. Verified main remains `68933a1f91d9c9e2e6921998f44a1030c35fab06`, including M3 PR #14. PR-B is `7e850fc`, based on PR-A `9608470`. Main is unchanged by this work.

## Implemented and preserved

- Added optional student-scoped create idempotency, one additive Borrowing migration, explicit request/return review dialogs, real Staff Inventory serial reads, global Admin keyword/role/active filters, and owner-scoped loan notifications. Queues show bounded real equipment names/quantities and creation time; Catalog renders real image URLs; seven existing notification types have Vietnamese display copy.
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
| Frontend unit/transport tests | PASS | 25 passed, 0 failed, 0 skipped |
| Full real backend regression | PASS | Final rerun: **71 passed, 0 failed, 0 skipped** (20 unit, 10 platform, 3 Catalog, 11 Inventory, 27 Sprint1/lifecycle/recovery/messaging/jobs/browser/final-workflow). First run 69/70 caught modal cleanup; corrected before this result. [Per-test outcomes](evidence/final-test-results.json) |
| Real browser E2E rerun | PASS | Full roles/auth/Catalog/create/cancel/approve/handover/partial damaged return, maintenance UI/history assertions, Admin global search/role/active/last-admin, Profile/password, worker notifications and mobile completed; final evidence manifest records run |
| Existing Docker core runtime | PASS | Recovered daemon; Verify-Sprint2: ten required containers healthy, readiness/OpenAPI/401 checks, migrations, all four real job passes, Redis group and standard HTTP/2 health; this shared stack predates PR-B |
| Final local demo deployment | PASS | Reused the eight exact `ea868e4` application images from successful T20, preserving original .env/named volumes and rollback image tags. Ten core containers healthy; Verify-Sprint2 passes; new migration and header/Admin/aggregate OpenAPI parameters verified. [Runtime JSON](evidence/final-deployed-runtime.json), [checks](evidence/final-deployed-runtime.txt). [Web](http://localhost:3000) and [settled mobile Catalog](evidence/final-demo-mobile-catalog.png) verified with existing real data |
| Worker exit 139 diagnosis | PASS | Fresh-stack reproduction in five hosts: Unix-socket PostgreSQL health accepted the temporary bootstrap server, then migration connections failed (refused/57P03). TCP health corrected; sequential Start-Core added. Final fresh T20 starts all ten core containers and verifies restart health. [Diagnostic evidence](FINAL_DOCKER_DIAGNOSIS.md). No kernel OOM claim; older M3 crash lacks retained logs |
| Clean installation T20 | PASS | Final project `lablend-t20-98c82cde5abe`, source `ea868e4`: all **13 stages**, all **10 production E2E stages**, six owning-role isolation checks, demo preparation twice and scoped cleanup exit 0. No volume removal. [Result](evidence/final-t20/t20-result.json), [expected/actual E2E](evidence/final-t20/docker-e2e.json), [stage log](evidence/final-t20/stages.txt), [ten healthy containers](evidence/final-t20/containers.json). Earlier attempts are recorded below |
| Deck visual QA | BLOCKED | Existing deck imports but PNG renderer stalls at slide 1; native Computer Use service discovery timed out. Source deck preserved, no visual certification |
| Team rehearsal/contract approvals | PROVISIONAL | Proposed speaking assignments/checklist provided; actual rehearsal and OD owner signoff remain human tasks |
| Gemini | NOT RUN | Optional skeleton, disabled; no provider call or bonus completion claimed. P0 verification takes priority |

## Three main flows

| Flow | Expected behavior | Executed result |
|---|---|---|
| 1 Student borrowing | Real Catalog/detail; select quantities; validate and review; single confirmed create; actual list/history and owned notifications | PASS in real-service browser regression and production Docker. Double click produces one POST/one DB loan. Concurrent replay/key conflict/student scope pass in PostgreSQL tests. Actual reminders/inbox and mobile equipment names verified |
| 2 Staff fulfillment | Review; all-or-nothing reserve via real HTTP/2; handover; partial/full NORMAL/DAMAGED/LOST; truthful recovery and role checks | PASS in real-service regression and production Docker. Owning DB confirms 3 RESERVED, then 3 ON_LOAN; partial/full returns yield MAINTENANCE/LOST/AVAILABLE and final RETURNED. Duplicate approval/no stock 409, invalid condition 400 and repeated normal return safe. Timeout/lost reply/concurrency verified separately in real integration suites |
| 3 Inventory lifecycle | Real type/physical asset registration, allocation, repair, maintenance and append-only StockMovements | PASS in real Inventory suites and production Docker. Catalog type/3 units persist; damaged unit repaired, maintained and returned to AVAILABLE through Staff UI; 3 movements appended, every pre-existing movement unchanged |

Expected/actual per-test outcomes and durations belong in `docs/testing/evidence/final-test-results.json`; Docker scenario records each stage in `docker-e2e.json`. A test implementation alone is not an executed PASS. Fixtures create their own accounts/data using business APIs for E2E; repository tests may explicitly prepare integration-only rows for focused repository validation.

## R01 to R18

| ID | Source and execution trace | Status |
|---|---|---|
| R01 REST CRUD | Catalog/Inventory controllers and real PostgreSQL suites; Identity/Borrowing/Notification APIs | PASS |
| R02 Layered architecture | Controllers, Application ports/services, Infrastructure repositories; no cross-service DbContext | PASS |
| R03 JWT | Identity hashing/rotation/CSRF, policies, memory token; real roles/browser tests | PASS |
| R04 Search/filter/sort/page | Catalog and Inventory server queries; Admin global filters; exact request and PostgreSQL query tests | PASS |
| R05 Background job | Due/overdue/reconciliation/outbox jobs; real reminder, two-worker and restart tests | PASS |
| R06 Redis | Real Streams publisher/consumer, XACK/XAUTOCLAIM, outage/reconnect/replay suites | PASS execution; owner policy review separate |
| R07 Producer and consumer | Outbox to Redis to Inbox to REST/UI; real browser delivery, outage/replay integration suites | PASS; retention owner approval separate |
| R08 Independent gRPC | Inventory host/proto/auth; HTTP/2 real PostgreSQL tests | PASS |
| R09 REST invokes gRPC | Real approve/handover/returns in Docker plus lifecycle, timeout/lost-reply and concurrency suites | PASS |
| R10 EF and relational DB | Owned contexts/migrations, new submission PK/FK, fresh T20 and real PostgreSQL assertions | PASS |
| R11 DI/config/log/errors | Options validation, correlation, ProblemDetails, health and structured worker logs | PASS exercised paths |
| R12 Deploy system | Fresh clone, sequential Linux image build, ten healthy core containers and production E2E | PASS local Docker; no paid hosting |
| R13 Complete source | Three stacked review branches/PRs, M1/M2/M3 integration preserved, changed-file manifest | PASS delivered; merge/review remains human |
| R14 DB scripts/migrations | Existing migrations retained, additive idempotency table, fresh bootstrap and six-role isolation | PASS |
| R15 Dockerfiles/Compose | Final fresh Linux images build and run in T20 | PASS |
| R16 API docs | Five-service OpenAPI through Gateway; compatible extensions documented and exercised by final tests/T20 | PASS |
| R17 README | Required six topics plus clean install, scripts, migration, roles, tests, troubleshooting | PASS documentation coverage |
| R18 Seven live demo topics | Runbook, proposed speakers, C4/ERD, evidence and review checklist | PROVISIONAL: team rehearsal and deck visual inspection pending |

## Migrations and operational limits

`20261116180731_AddLoanSubmissionIdempotency` creates `LoanSubmissions(StudentId,Key,RequestHash,LoanId)` only in borrowing_db. API/worker retain their migration advisory lock. No existing migration was deleted, no shared volume was removed, and no database was reset. Replay keys are retained with their loan; a new browser draft/reload has a new in-memory key, so users must inspect My Loans after an unknown outcome.

OD-04/05 and retention/archive/replay horizon remain **PENDING HUMAN REVIEW**. No trimming or fabricated signoff. Inventory v1 reservation ID/historical condition/time gap and documented single-writer recovery assumption remain; PR #15 supplies independent M2 owner review. Current asset state is not historical evidence. Refresh revocation does not instantly revoke already issued JWTs in other services. .NET 8 is preserved for coursework; long-term public operation needs a separately reviewed upgrade.

## Pull requests and final review

1. [PR-A #16](https://github.com/NguyenNhuDai05/lablend-ai/pull/16), base main: isolated preflight/T20 infrastructure and source audit.
2. [PR-B #17](https://github.com/NguyenNhuDai05/lablend-ai/pull/17), base PR-A branch: workflow/backend/UI corrections and compatible contracts.
3. [PR-C #18](https://github.com/NguyenNhuDai05/lablend-ai/pull/18): final packaging branch, base PR-B. Merge only after A, then B, retargeting dependent bases to main as appropriate.

Reviewers must inspect final CI SHAs, the actual clean-install result, source diff, secrets, migration, event/gRPC boundaries and remaining human tasks. Do not auto-merge. See [handover](../team/FINAL_COMPLETION_HANDOVER.md) and [seven-topic defense checklist](../demo/FINAL_DEFENSE_CHECKLIST.md).

## Exact source CI and evidence

[PR-B runtime CI](https://github.com/NguyenNhuDai05/lablend-ai/actions/runs/43928095975) completed **success** for `2808d7707a35b07d9a82c7403a706c95cd379153`. This runs the existing workflow with both PostgreSQL/browser flags and installed Playwright Chromium. Local Windows rerun used Edge. Later documentation/harness commits have their own CI results; a green older SHA never certifies a new runtime change.

[Final code CI](https://github.com/NguyenNhuDai05/lablend-ai/actions/runs/43966081127) completed success for `ea868e4f22ebc13fbc2371ca1bfb73ef99435179`. [Merged stack CI](https://github.com/NguyenNhuDai05/lablend-ai/actions/runs/43967053632) also completed success for `430ff1b15ce7441a3862ad247316943e9a91acd2`: both confirm **71 backend and 25 frontend tests, 0 failed/skipped**. [Exact merged-SHA step counts](evidence/final-ci-43967053632.json). The seven producer event payloads and v1 proto are unchanged. PR-A [exact-head CI](https://github.com/NguyenNhuDai05/lablend-ai/actions/runs/43966750863) and PR-B [exact-head CI](https://github.com/NguyenNhuDai05/lablend-ai/actions/runs/43966754975) also succeeded.

The earlier successful T20 at `ae0695c` was followed by the final `ea868e4` rerun after the queue preview and test-fixture TCP fix. Historical failed attempts include unavailable daemon, clone environment discovery, the reproduced PostgreSQL startup race, and a verifier CRLF transport error after all six isolation assertions had passed. Those defects were corrected; no failed attempt was relabeled PASS. See [evidence index](evidence/FINAL_EVIDENCE.md), [changed files](evidence/final-files.txt), [desktop maintenance screenshot](evidence/final-t20/t20-maintenance-history.png), [real notifications](evidence/final-t20/t20-notifications.png) and [mobile queue](evidence/final-t20/t20-mobile-loans.png). Screenshots contain only disposable fixture accounts/data, not passwords or tokens.
