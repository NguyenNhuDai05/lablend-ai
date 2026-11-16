# Final source audit 2026-11-17

Baseline: `origin/main` **68933a1f91d9c9e2e6921998f44a1030c35fab06** (PR #14 merged M3). Initial working tree was clean on M3 `4622b17`; that commit is an ancestor of the verified main. M1 PRs #10–13 and M2 PR #8 remain ancestors. No existing changes were discarded. PR #15 `feature/m2-grpc-review` is open; its three documentation files are outside this change. Do not merge it implicitly or claim its human review as complete.

Sources read: official three-page assignment PDF, Master Plan V4 (R01–R18, T01–T28), current README/traceability/open decisions, REST Identity/Borrowing/Catalog/Inventory/Notification contracts, proto v1, producer/consumer, React routes/features, automated tests, Dockerfiles/Compose, M1/M2/M3 acceptance reports and demo runbook. A named endpoint/file alone is not execution evidence.

| Requirement | Existing code | Existing tests | Missing implementation or evidence | Risk | Proposed correction |
|---|---|---|---|---|---|
| F1 public catalog | catalog API + CatalogPage, real availability | CatalogApiTests, browser | Current Docker runtime unverified | Medium | Preserve contracts; run real stack gates |
| F1 create loan | LoanService, CreateLoanPage, 1–20 items/UTC/owner from JWT | BorrowingTests, workflows tests, browser | Explicit review; server create idempotency | High | Optional compatible idempotency header, scoped persisted key/payload hash; review UI |
| F1 list/timeline | LoanDto/history, LoanPages, derived overdue | Lifecycle/browser | Related notices only on notifications page | Medium | Link detail to actual related notifications; keep real history |
| F1 notification | Outbox/Streams/Inbox/REST, badge/polling | Messaging/event/browser | English backend message copy; bounded hidden-tab polling present | Low | Localized labels while preserving event text; preserve stop-on-unmount |
| F2 staff lifecycle | LoanLifecycle + Inventory HTTP/2 RPC | Lifecycle/recovery/concurrency | Serial display; return review | Medium | Staff-only Inventory read API; explicit return confirmation |
| F2 ambiguity | persisted immutable operation, reconciliation | lost reply/timeout tests | Full-stack rerun unavailable yet | High | Keep pending/recovery handling; no automatic UI mutation retry |
| F3 inventory | asset CRUD/maintenance/movements | InventoryTests, MemberIntegrationTests | Browser only covers loan return, not full maintenance UI | Medium | Extend E2E; preserve M2 implementation |
| Admin | Identity admin CRUD + last-admin lock | IdentityTests/browser | Global search/role/active filters | Medium | Add optional server query fields and UI; compatible defaults |
| Profile | real profile/password + revoke refresh | Identity/auth/browser | No major implementation gap established | Low | Preserve and regress |
| Worker/Docker | four jobs, migration lock, dependency heartbeat | WorkerTests and historical evidence | Current Linux daemon pipe missing; worker exit state unavailable | High | Bounded preflight captures state/logs when available; no speculative code fix |
| T20 | setup and sequential builds; isolated test DB fixture | Existing integration is not fresh Docker installation | Fresh clone + all images + real web E2E | High | Private randomized project/volumes/ports, real command scenario and owned DB assertions |
| OD-04/05 | v1 envelope, XADD/event/group/XACK/XAUTOCLAIM/Inbox/parking | seven producer cases, messaging faults/replay | Human approval/retention/replay horizon | Medium | Keep PENDING HUMAN REVIEW; no unsafe trim or invented approval |
| gRPC v1 | six RPCs + stable reserve operation ID | HTTP/2 snapshot/auth/concurrency | reservation ID, historical return condition/time in response | Medium | Preserve v1; Borrowing persisted return intent; document single-writer limitation |
| R01–R18/docs | required architecture/REST/jobs/broker/gRPC/EF/readme | Historical acceptance | Current runtime/clean install, deck render/rehearsal | High | Current checkpoint with exact results, visual QA where supported, human review remains explicit |
| Gemini/dashboard | optional skeleton/deferred | No live AI acceptance | P0 not yet stable | Low | Keep disabled; do not start bonus before P0 gate |

## Stabilization evidence

The initial Release restore/build completed with **0 errors, 16 NU1900 warnings** (NuGet vulnerability endpoint unreachable). Baseline frontend production build and **21/21 tests passed**. New final-run results supersede these counts only after execution.

Docker client 29.5.3, desktop-linux context: Linux engine named pipe does not exist. `docker desktop start` reports already running, while server connection fails. A non-destructive `docker desktop restart --detach --timeout 30` did not restore the pipe. Host logs show repeated waits for two backend processes and missing `dockerBackendApiServer`. At inspection, Windows reported ~5.94 GiB visible RAM and ~0.30 GiB free, C: ~6.56 GiB free. These are resource constraints and daemon symptoms, **not proof that OOM or code caused the earlier exit 139**. Container state/OOMKilled/native crash/image architecture cannot be recovered through an unavailable engine.

Initial classification: Docker runtime **BLOCKED**, historical Worker 139 root cause **UNRESOLVED**. No source/config change is justified solely by that code. No volumes/database/WSL data were removed. Close unnecessary applications and restart Docker Desktop normally on a machine with sufficient free memory/disk; if it remains stuck, save work and reboot Windows. Then run `scripts/Test-FinalPreflight.ps1` and T20; inspect worker `.State`, health and all four completed passes before acceptance. Do not factory reset, prune volumes or replace the team's database.

`Test-CleanInstall.ps1` was executed and stopped at its daemon gate with **BLOCKED**, before clone/build/migrations/E2E. Its generated result is under ignored `.tmp/clean-install`. Harness syntax checks do not certify T20.

## Runtime recovery followup

The non-destructive Desktop restart eventually restored the Linux engine. The repeated preflight captured linux/amd64 Docker 29.5.3, worker running/healthy with OOMKilled=false and exit 0. `Verify-Sprint2.ps1` then passed ten required containers, real service readiness/OpenAPI/anonymous authorization, old migrations, all four actual job passes, Redis group metadata and independent gRPC standard health. This verifies the existing baseline stack, not the new source deployment. It does not establish the historical 139 root cause. New-source images and fresh-install acceptance are recorded separately in the final acceptance report.
