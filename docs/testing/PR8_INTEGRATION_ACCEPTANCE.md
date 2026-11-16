# PR #8 integration acceptance — M1 + M2 + M3

Date: 2026-11-16. PR: https://github.com/NguyenNhuDai05/lablend-ai/pull/8.
Source `feature/m2-catalog-inventory`; target `main`. This report is a measured checkpoint; pending gates below are not PASS.

## 1. Git before and after

- Initial working tree: clean on main, HEAD/origin/main `dcbf21ff328a71e0daeecc2b0ae43878e0d39975`.
- Fetched PR head: `daeda5989aadc603cdd64e4dc29b65f30608fce3` (11 PR commits).
- PR initially OPEN, CONFLICTING/DIRTY; original two successful checks were for the old head and are not reused as integration evidence.
- Integration: tracking source branch, `git merge --no-ff --no-commit origin/main`; no reset, force push, bulk ours/theirs, history rewrite, or direct main push.
- Post-integration SHA / final remote-main receipt: PENDING publication and GitHub verification.

## 2. Contribution history preserved

M2: `e6bd9e3` Catalog; `6a717e5` Inventory REST/gRPC; `a16b110` StockMovements; `48185b1` maintenance/retire; `cf8becc` Staff UI; `ef9e2c5` HTTP/2 T09/T23; `0290a51` movements dialog; merges `161df32`, `e8b109b`, `4a60e43`; `daeda59` demo script.

Main: M3 `e152e7a` / PR #9 merge `49aa318`; M1 `eb6756c`; M1/M3 integration `363baac`; main merge `8dac358`; docs `dcbf21f`. Existing commits remain ancestors; no contribution commits deleted.

## 3–4. Five conflicts and their resolutions

| File | Resolution |
|---|---|
| LabLendAI.sln | Preserve every common project/GUID. Combine M2 Catalog/Inventory integration tests and Inventory.DemoClient with M1 Sprint1 integration tests; retain configuration and nested-folder mappings. 35 unique csproj entries; solution list/restore/build succeeds. |
| apps/web/src/features/staff/api.ts | Keep M2 DTOs including StockMovement and every business request; use M3 shared apiRequest. Remove legacy token storage/parser/unauthorized transport. Shared memory JWT, cookie refresh, CSRF and ProblemDetails preserve 400/401/403/409 semantics. |
| apps/web/src/main.tsx | Keep a single createRoot, BrowserRouter, AuthProvider and App shell. Existing Student, Login/Register and Notification routes remain. Staff pages are mounted by existing protected role routes; add /staff redirect. |
| docs/api/REST_CONTRACTS.md | Combine Identity/Borrowing contracts, actual Catalog/Inventory routes/DTOs/statuses, Notification owner REST and explicitly planned Recommendation/approval endpoints. Remove obsolete skeleton claims. |
| docs/architecture/DATABASE_CONVENTIONS.md | Preserve six independent logical databases/users, local FKs only, M1 migrations/guidelines, M2 xmin/partial indexes/SKIP LOCKED/StockMovements and M3 Notification schema. No cross-service DB access. |

No conflict markers remain. StaffApp's obsolete dev-token paste screen becomes a compatibility redirect; CatalogPage, InventoryPage, maintenance and StockMovements remain available through authenticated routes. Dev-token CLI remains local tooling only.

## 5. Files changed for integration

Besides the five conflicts: App.tsx (/staff entry), StaffApp.tsx (retired shell), AuthContext.tsx/lib/api.ts/AuthPages.tsx (session/redirect race guards), PlaceholderPages.tsx (accurate user-facing planned UI text), Verify-Sprint0.ps1/Verify-Sprint1.ps1 (remove obsolete status footers), apps/web/tests/api.test.mjs (shared Staff auth/error regression), Sprint1 PostgresFixture.cs/LiveApi.cs/GatewayFlowTests.cs/csproj (private real-service harness), new MemberIntegrationTests.cs, README, requirements traceability, Sprint1 handover, this report and PR8 evidence. M2 business implementation/proto/tests are retained. Identity/Borrowing/Notification business source and migrations are unchanged relative to fetched main.

## 6. Gateway and frontend

Existing Gateway routing/CORS/auth conventions are preserved; integration adds no bypass endpoint. Shared apiRequest supplies Bearer JWT from React memory, credentials include, refresh singleflight/session fencing and validation details. No localStorage access/refresh token, hardcoded dev JWT, second router/root, direct database call, or discarded Student/Notification page. Browser testing exposed unconditional late logout cleanup; it now checks session generation before clearing. Protected requests also recheck generation after refresh success/failure. Login retains the requested protected-route destination during the authenticated render. Both obsolete M1/M2 sessionStorage token keys are removed.

## 7–8. .NET restore/build/tests

- SDK/.NET 8; 35 projects. Restore PASS. Release build PASS, **0 warnings / 0 errors**.
- `LABLEND_POSTGRES_TESTS=1 dotnet test LabLendAI.sln -c Release --no-build --no-restore -maxcpucount:1`: **53 passed, 0 failed, 0 skipped** after integration.
- Unit 20; Platform/Gateway/JWT 10; Catalog PostgreSQL 3; Inventory PostgreSQL/gRPC 11; Sprint1 Identity/Borrowing/real-service integration 9.
- M2 tests use real Testcontainers PostgreSQL. M1 fixture uses random task-owned Compose project, generated password, four separate databases/roles; only its own test volume is removed. No team .env/DB used by tests.
- Evidence: [local gates](evidence/pr8-local-gates.txt). Raw local TRX/logs are ignored under tmp/pr8-test-results and tmp/pr8-*.txt; credentials are excluded from committed evidence.

## 9. Frontend

`npm ci` PASS (0 audit vulnerabilities); TypeScript + Vite production build PASS. `npm test`: **11 passed, 0 failed, 0 skipped**. Existing seven session/RBAC tests retained; two Staff tests cover expired StockMovements requests through shared refresh and validation/403/409 preservation; two account-switch tests cover refresh success/failure arriving after a new session.

Direct browser acceptance on real isolated Docker data: Staff login, category create/edit/deactivate, equipment create/edit/search/filter/descending sort/page 1→2, asset create/edit/maintenance/complete/retire, four persisted StockMovements, cookie session restore after reload, Student login/Notification empty state/existing loan UI and /staff 403 verified. The native type-deactivation confirmation required a user click because the browser driver blocked; persisted inactive state was verified afterward. Admin/rapid-account-switch retest on final frontend remains pending RAM recovery. Screenshots: evidence/pr8-staff-history.jpg and evidence/pr8-student-denied.jpg.

## 10. Docker Compose

Config validation PASS. Initial existing stack had 11 healthy containers; applications were temporarily stopped to conserve resources, with databases/volumes preserved. Sequential build of all nine images PASS (Linux build 0 warnings/errors); main and private stacks each reached 11 healthy containers. Five service migration histories verified, Catalog seed/Inventory availability through nginx→Gateway and Docker HTTP/2 health PASS. Main verifier confirmed six own-DB logins/30 cross-DB denials and 20 Identity/Borrowing OpenAPI operations. Final frontend was rebuilt/redeployed after auth guards. Main applications were temporarily stopped again while isolating memory pressure; final restore is pending. Disk space was increased by the user from 2.23 GB to 11.56 GB before image build. No Docker reset, shared volume deletion or personal-file deletion.

## 11. Actual Borrowing ↔ Catalog and Identity ↔ Staff

**PASS**: MemberIntegrationTests launches real Identity, Catalog, Inventory and Borrowing service executables, each against its own PostgreSQL database/login, through YARP. Real admin login promotes registered user to LAB_STAFF; real Staff login token authorizes Catalog/Inventory. Student receives 403 for staff writes/list. Staff category/type CRUD, search/filter/sort/page, asset create/update, maintenance/complete, retire and persisted movements are asserted.

Student creates a loan with the just-created actual Catalog type (201); nonexistent type returns 400; deactivated type returns 400. No adapter change was needed: M2 `id/isActive` matches M1 contract. Original explicitly labelled Catalog-double regression tests remain and are not used as this E2E proof.

## 12. Inventory gRPC / T09 / T23

**PASS** in the full 53-test run. Proto schema contract retains six RPCs; Kestrel + separate client use real HTTP/2, anonymous health and unauthenticated-business rejection, valid service authentication, all six calls, reservation idempotency/stock locking, rollback on deadline and same-operation recovery. T09/T23 tests are enabled and unchanged. Docker HTTP/2 gRPC health PASS in both private and main runtime checks; main verifier also probes Inventory from the Borrowing container (health only).

R08 is implemented on the server. **R09 Borrowing REST→Inventory allocation remains PLANNED**; direct client/contract tests and health probes do not satisfy R09.

## 13. GitHub CI and merge policy

Authenticated audit: main protection endpoint returns 404 “Branch not protected”; branch rules and repository rulesets are empty; PR reviews empty, reviewDecision unset. Repository permits merge commits and current account has push/merge permissions. These facts do not waive the user's local/CI gates.

New integration-head push/PR checks: PENDING. Normal merge only after fresh main-ancestor check, all checks successful, no unresolved review request/blocker; never --admin/bypass/force/direct-main push.

## 14. PR final status / receipt

Current checkpoint: OPEN, local conflicts resolved; NOT MERGED while final Docker E2E/browser/CI gates remain pending. This report is committed before the conditional GitHub merge. Authoritative final mergedAt/mergeCommitSHA and main ancestry must be checked through the [PR timeline](https://github.com/NguyenNhuDai05/lablend-ai/pull/8) and remote Git after merge; no successful CLI request alone is accepted as proof.

## 15. Planned / blocked scope

Current local resource blocker: Windows reports 6,081 MB total RAM and only 25–57 MB free; Kestrel reports heartbeat/thread-pool starvation. Docker real Student auth/403/Notification and persisted UI data are verified; a Docker POST loan returned 503 under this pressure, so Docker loan E2E is NOT accepted yet. Isolated .NET real-service loan tests passed. Build servers were shut down and duplicate main applications stopped; user was asked to free RAM before retry. This is not hidden by replacing the test or increasing adapter/client timeouts.

- PLANNED: Borrowing approval/handover/returns gRPC client, Outbox publisher, overdue business jobs, full event-delivery E2E and complete loan UI; outside this integration scope.
- Notification REST/domain/consumer/migration and UI are preserved. OD-04/05 event proposal unchanged; no fabricated Borrowing producer/event completion.
- Recommendation/Gemini business API remains planned bonus; no API billing or paid service enabled.
- Pending gates above are not failures hidden as PASS; final receipt must state any actual blocker.
