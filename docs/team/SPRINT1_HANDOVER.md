# Sprint 1 Member 1 handover

Identity and Borrowing Core source are implemented on `feature/m1-identity-jwt`, based on Sprint 0 commit 3431927. Initial checkout was clean; local main/origin/main remain 196a09e without foundation. No push, merge, hard reset or destructive DB reset was performed. Both milestones are in this local branch; a dependent Borrowing PR must not be based on the old main. No PR was submitted remotely.

## Delivered source

Identity has ApplicationUser/Role, RefreshToken/UserAuditEvent, ASP.NET Core Identity stores, application services, repository/transactions, JWT issuance and real migration InitialIdentity. APIs include registration/login/refresh/logout/current user/profile/password and admin list/detail/profile/activate/role. Password hashing is Identity's implementation. Refresh tokens are hashed and single-use with family revocation on reuse; concurrent refresh is serialized in PostgreSQL. Active-role checks and last-admin protection apply at Identity.

Borrowing has LoanRequest/LoanItem/LoanHistory, domain invariants, application use cases, repository row locking, migration InitialBorrowing and six REST operations: create, my list, staff list, detail, pending cancel and pending reject. Aggregate/items/history commit together. User ownership comes from sub. No approve endpoint, fake asset allocation, gRPC business client, Redis publisher, worker business job or other members' implementation was added.

Layer boundaries remain API→Application→Repository; Infrastructure implements persistence, Identity stores and external HTTP adapters. Every backend still targets net8.0, with EF/Npgsql pinned to the existing 8.x versions. New Identity package versions are 8.0.34 and centrally managed. No paid service/provider or secret was added.

## Member 2

Review [Borrowing contract](../api/BORROWING_API_CONTRACT.md), especially one LoanItem per unit, repeated EquipmentTypeId, UTC dueAt within 30 days, purpose 10–2000 and max 20 items. Required Catalog adapter shape is GET /api/equipment-types/{id} → JSON id UUID and isActive boolean; unknown ID 404. Configuration Catalog:BaseAddress is service DNS in Docker and localhost:5102 locally, with 5-second total budget and correlation propagation.

Catalog currently has no endpoint; positive runtime integration is BLOCKED. Tests use explicit Catalog HTTP doubles, never fabricate Catalog data in production. Do not change Catalog schema to match a consumer silently; review this shape first. Inventory proto/ReserveAssets/auth/idempotency/recovery decisions remain OD-01–03 and Sprint 2 work. Borrowing DB keeps logical type/user references only, no cross-service FK.

## Member 3

Use [Identity contract](../api/IDENTITY_API_CONTRACT.md): all request/response shapes, endpoints, auth errors, cookies/CORS/CSRF and JWT limits are specified. React access token stays in memory; all auth POSTs send X-LabLend-CSRF:1, Origin is browser-generated and must be allowlisted, credentials include allows the refresh cookie. Serialize refresh; racing refresh intentionally invalidates the family and requires login. Do not store raw refresh tokens in localStorage.

OD-06/08 defaults were accepted by the repository owner in this session; M2/M3 review remains pending. No claim that either member already approved was made. UI should distinguish 401 session expiry, 403 role/CSRF, 404 inaccessible loan, 409 lifecycle conflict and 503 dependency failure. Logout does not instantly invalidate stateless access tokens at other services. Use /api/loans/my; never send studentId to choose another user's scope.

## Shared configuration impact

- Gateway: explicitly permits X-LabLend-CSRF and credentialed CORS for its existing exact origins. Routes/prefixes remain unchanged.
- ServiceDefaults: validates user UUID sub, adds 503 ProblemDetails code, updates Swagger description/title and validates issuer/audience configuration. No signing format change.
- Compose: Identity allowed web origin follows WEB_PORT; Borrowing Catalog base address is internal DNS. Development config applies only the two owned services' real migrations and local HTTP cookie settings.
- Dockerfile remains the Sprint 0 version; no Docker Desktop memory/disk settings were changed. Image builds use the existing sequential script.
- CI: enables isolated PostgreSQL suites through LABLEND_POSTGRES_TESTS=1. Hosted run remains unverified until an authorized push.
- Tests add one Sprint 1 integration project; existing platform projects remain and gain relevant domain/JWT coverage. No source under Catalog/Inventory/Notification/Recommendation/apps/web/Workers changed.

## Validation and remaining blockers

Read [SPRINT1_ACCEPTANCE](../testing/SPRINT1_ACCEPTANCE.md) for exact results. After a transient disk-full/containerd failure, Docker/WSL recovered; full suite 30/30, final targeted checks and Release build passed. Nine application images built; core Compose up --build --wait and Verify-Sprint1 -IncludeBonus passed; all 11 containers are healthy. Both owned migrations were read from the actual Compose DBs using their service users. No team volume/container metadata was deleted; only the private test project's own anonymous volume is removed at cleanup. Actual Catalog acceptance still awaits M2.

To repeat: restore/build; set LABLEND_POSTGRES_TESTS=1 and run dotnet test; sequential Build-Images.ps1; docker compose up -d --build --wait; docker compose ps; scripts/Verify-Sprint1.ps1 (add -IncludeBonus when running that host). GatewayFlowTests have passed and their sanitized actual responses are retained in acceptance evidence. Their Catalog remains a test double; actual Catalog acceptance waits for M2.

## Sprint 2 preparation

Implement reviewed Borrowing→Inventory gRPC and PENDING→APPROVING→APPROVED after atomic reservations; compensation, operation status and reconciliation for unknown outcomes; local Outbox alongside state/history; Redis publisher and M3 inbox consumer; overdue/reminder worker with dedupe and derived overdue display. None is implemented or marked complete in Sprint 1.

## PR #8 integration update (2026-11-16)

The earlier Catalog blocker above describes the original M1 checkpoint. PR #8 integrates M2 Catalog and Inventory, including independent migrations and six-RPC v1 server. MemberIntegrationTests adds real-service Catalog→Borrowing validation and Identity-issued Staff→Catalog/Inventory authorization through Gateway, with no Catalog double. Staff pages use the existing React router/session instead of the retired token-paste shell. See [PR8 acceptance](../testing/PR8_INTEGRATION_ACCEPTANCE.md) for measured results and final PR state. Borrowing approval gRPC client/Outbox and Notification event-contract approval remain Sprint 2/OD-01–05 work.
