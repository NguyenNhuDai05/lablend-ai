# Sprint 1 Member 1 acceptance

**IMPLEMENTED and TESTED — local build/tests/Compose PASS; actual Catalog integration BLOCKED.** This is not completion of the PRN232 Final Assignment. Branch feature/m1-identity-jwt, foundation 3431927; local main is 196a09e without Sprint 0. Initial worktree clean. No remote push/merge or destructive reset. Session spans 15–16 November 2026, Asia/Bangkok.

## 1 Executive summary

Identity authentication/session/profile/admin and Borrowing Core are implemented with real EF Core 8 migrations. Identity basic APIs passed real PostgreSQL tests before Borrowing work began. The full suite passed 30/30 (12 unit, 10 platform, eight PostgreSQL/YARP), with final targeted security checks also passing. Final Release build has zero warnings/errors. All nine application images built, core up --build --wait succeeded and all 11 containers are healthy after restoring the optional host. Tests use an explicitly labelled Catalog HTTP double; equipment existence in M2's actual Catalog is not certified.

Initial baseline build was attempted but returned MSB5021 cancellation, so it was not recorded as PASS. Subsequent full Release and regression validations are the successful results reported here.

## 2 Files changed

See [file inventory](../team/SPRINT1_FILES.md) for every created/modified path and purpose. Identity/Borrowing own four-layer implementations, Controllers, models, repositories, migration snapshots, options/bootstrap, contracts and tests. Small shared changes: Gateway credentialed CORS/CSRF header; JWT subject/options/503/OpenAPI defaults; Compose internal Catalog address/web origin; solution test project and CI test opt-in. Dockerfile and source of Catalog/Inventory/Notification/Recommendation/frontend/Worker remain unchanged.

## 3 Database

- Identity InitialIdentity: 20261115162335_InitialIdentity. ApplicationUser/Role using standard ASP.NET Identity tables, RefreshTokens and UserAuditEvents. Unique normalized email/username and token hash; local user/replacement/audit FKs, family/expiry/user-time indexes, refresh expiry check and xmin.
- Borrowing InitialBorrowing: 20261115163138_InitialBorrowing. LoanRequests/Items/Histories with local FKs, UUID logical user/type references, purpose/due/lifecycle constraints, owner/status/history/type indexes and xmin.
- Real PostgreSQL tests applied both migrations to disposable identity_db/borrowing_db and verified transactions, registration/refresh/loan races, migration reapply, normalized-email unique constraints and DB cross-connect denial. No EnsureCreated/InMemory/SQLite was used. SchemaTests passed again after private anonymous-volume cleanup was added.
- Actual Compose identity_db and borrowing_db also applied their migrations; Verify-Sprint1.ps1 read the expected migration IDs using each service's own PostgreSQL user. [Runtime evidence](evidence/sprint1-runtime.txt).
- Development startup migrates and seeds roles idempotently; production auto-migrate defaults false. Admin bootstrap is Development-only, opt-in and environment-configured; no password reset/promotion of existing users.

## 4 API implementation

Actual endpoints and DTOs: [Identity contract](../api/IDENTITY_API_CONTRACT.md), [Borrowing contract](../api/BORROWING_API_CONTRACT.md). Identity has 14 operations across auth/users (PUT/PATCH share controlled role assignment); Borrowing has six. Swagger maps real Controllers, including typed session/current-user/loan responses. Success 200/201/204; validation 400; missing/invalid JWT 401; role/CSRF 403; private/missing loan 404; state/duplicate/last-admin 409; Gateway rate limit 429; dependency 503. Errors are sanitized ProblemDetails with correlationId/code.

## 5 Security

UserManager owns password hashing, unique normalized email and lockout. Register always grants STUDENT and rejects extra JSON fields. JWT HS256 uses configured key/issuer/audience and includes sub/role/iat/nbf/exp/jti. Existing shared validation is reused. Refresh token has 64 CSPRNG bytes, SHA-256 persistence, one-use rotation and family revocation after reuse. A PostgreSQL advisory transaction lock serializes account mutations across replicas; last-admin protections use the same lock. Failed-login/reuse effects commit before 401.

HttpOnly Strict cookie + mandatory exact Origin/custom CSRF header; Secure outside Development; Gateway CORS allows credentials only for explicit origins. Identity validates current active state and roles; other services use stateless JWT until expiry/skew. Logout never claims immediate global access-token invalidation. UUID sub controls ownership; no client-selected owner/role/allocation accepted. No new cloud/billing/external auth/AI dependency.

## 6 Test evidence

| Test ID | Description | Result | Evidence |
|---|---|---|---|
| T01 | PostgreSQL register/login/profile/password/lockout; concurrent duplicate register | PASS | IdentityTests in recovered full suite |
| T02 | Student admin denial, admin CRUD/last-admin/deactivation, Origin/CSRF, stale-role and inactive-login denial | PASS | IdentityTests in recovered full suite |
| T22 | Hash-only storage, concurrent refresh, replay family revoke, expiry, logout | PASS | IdentityTests in recovered full suite |
| T03/T05 | Real Identity JWT + PostgreSQL create/list/detail ownership/cancel/reject | PASS with Catalog test double; real Catalog BLOCKED | BorrowingTests + live YARP flow; no claim that M2 equipment exists |
| Loan concurrency | Cancel/cancel idempotency, cancel/reject and reject/reject one terminal history | PASS | BorrowingTests in recovered full suite |
| Loan rollback | Bad input/dependency and DB purpose check leave no partial aggregate/items/history | PASS | BorrowingTests in recovered full suite |
| Unit regression | Correlation + loan due/item/state rules | PASS latest | 12/12 in final full-solution run |
| JWT/Gateway regression | Existing five checks plus wrong signature/issuer/audience/expiry/subject | PASS latest | 10/10 in final full-solution run |
| Schema | Explicit migration reapply, direct normalized-email unique, DB cross-connect denial | PASS | Full suite + targeted SchemaTests after private-volume cleanup |
| YARP business flow | Register/login/me/admin denial/loans over real Kestrel hosts; cross-user profile update denied | PASS with Catalog double | [Actual sanitized responses](evidence/sprint1-gateway.txt); targeted GatewayFlowTests passed after final assertion |
| Runtime regression | PostgreSQL/Redis readiness, routing, core/bonus skeletons | PASS | 11 healthy; current Verify-Sprint1 -IncludeBonus confirms six readiness/schema proxies, nginx, standard gRPC health and six own-DB logins/30 cross-DB denials |
| Secret/scope scan | Exact generated local secrets absent; M2/M3/Worker sources unchanged | PASS | 10 local generated values checked against repository text; no matches; empty diff for other owners' sources |

The new suite is enabled by LABLEND_POSTGRES_TESTS=1. Disabled suites are SKIPPED, not passed. An intermediate run failed all eight fixture setups while Docker was unavailable; after recovery all 30 tests passed. Full-suite output: [test results](evidence/sprint1-tests.txt). Raw access/refresh tokens and passwords are omitted from retained evidence. No E2E Final, gRPC allocation, Streams or overdue test is marked PASS.

## 7 Build and deployment commands

| Command | Exit | Observed result |
|---|---|---|
| dotnet restore LabLendAI.sln | 0 | 32 projects restored/current |
| dotnet build LabLendAI.sln -c Release --no-restore --maxcpucount:2 -p:UseSharedCompilation=false | 0 | Final source: 0 warnings, 0 errors; 18.89 seconds; [output](evidence/sprint1-build.txt) |
| dotnet test Identity/Sprint1 project (earlier three-method run) | 0 | 3 passed; real PostgreSQL; 21.0027 seconds |
| dotnet test Sprint1 project (earlier six-method run) | 0 | 6 passed; real PostgreSQL; 59.1814 seconds |
| LABLEND_POSTGRES_TESTS=1; dotnet test LabLendAI.sln -c Release --no-build --no-restore | 0 | After Docker recovery: 12 unit + 10 platform + eight PostgreSQL/YARP passed, zero failed/skipped |
| Targeted GatewayFlowTests after cross-user update assertion | 0 | 1 passed; 36.5077 seconds; actual response 403 retained |
| Targeted SchemaTests after cleanup change | 0 | 1 passed; 32.0575 seconds; private test-volume removal exercised |
| Targeted T02 after matching-password forbidden-role input | 0 | 1 passed; 36.7150 seconds; registration cannot submit ADMIN; PATCH role assignment verified |
| docker compose config --quiet | 0 | Configuration valid |
| scripts/Build-Images.ps1 -IncludeBonus | 0 | All nine application images built; Linux solution 0 warnings/errors; [output](evidence/sprint1-images.txt) |
| docker compose up -d --build --wait --wait-timeout 180 | 0 | Ten core containers healthy; [output](evidence/sprint1-compose-up.txt) |
| docker compose --profile bonus up -d --wait --wait-timeout 180 recommendation | 0 | Previously running optional host restored; Gemini remains disabled |
| docker compose ps | 0 | Eleven containers running/healthy; [status](evidence/sprint1-compose-ps.txt) |
| scripts/Verify-Sprint1.ps1 -IncludeBonus | 0 | Twenty actual API operations, anonymous 401s, both real migrations, all platform runtime regressions; [output](evidence/sprint1-runtime.txt) |

An intermediate Docker build lost its connection with EOF. Diagnostics observed containerd SIGBUS in bbolt snapshot metadata and ~0.5 GB free on C. The user freed space (~5 GB subsequently observed); restarting Docker and its stalled WSL mount restored the engine. Enabled PostgreSQL/YARP tests then passed. No team volume/metadata/factory reset or manual disk repair was performed. Fixture cleanup removes only its generated private project's anonymous PostgreSQL volume.

## 8 Open issues

1. Actual Catalog ID validation is BLOCKED pending M2's real endpoint and id/isActive review. Runtime fails closed; no fake equipment or approval is emitted. Borrowing Core is implemented/tested, but its real-Catalog integration criterion remains unmet.
2. OD-06/08 owner defaults accepted; consumer M2/M3 review pending. Hosted CI unverified because no remote push is authorized/performed.
3. Stateless access-token deactivation/role/password/logout exposure at other services is bounded by 10-minute expiry + 30-second skew. Immediate cross-service revocation remains future work.

No local infrastructure blocker remains. The earlier disk-full/Docker failures were recovered and the final commands above succeeded. The legacy Sprint 0 verifier's closing Planned sentence describes its foundation baseline; Sprint 1 CRUD/session behavior is separately established by the current tests and contracts, not inferred from health.

## 9 Member handover

M2: Catalog GET detail shape and item-per-unit rules; no schema ownership change. M3: all browser auth/session/error/loan DTOs documented; credentials include, custom CSRF header, memory access token, serialized refresh. Detailed [SPRINT1_HANDOVER](../team/SPRINT1_HANDOVER.md).

## 10 Sprint 2 preparation

PLANNED only: Borrowing REST→Inventory gRPC, real reservation/approval, compensation/reconciliation, Outbox/Redis publisher and overdue/reminder worker with dedupe. Member 2 Inventory and Member 3 consumer/UI remain their work. No final-assignment completion claim.
