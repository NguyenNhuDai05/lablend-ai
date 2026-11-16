# Test plan

## Sprint 0 implemented checks

- Unit: correlation ID chống control characters/whitespace/non-ASCII và bounded length.
- Integration (in-process WebApplicationFactory): Gateway live health, correlation response, CORS allowlist/reject, anonymous protected route 401, ProblemDetails 404.
- Runtime script: all expected Compose containers healthy; Gateway → each actual readiness; correlation forwarding; per-host OpenAPI; nginx → Gateway; standard gRPC health Borrowing container → Inventory; six DB logins và 30 cross-database connect denials.

`scripts/Verify-Sprint0.ps1 -IncludeBonus` thêm Recommendation host khi profile bonus đã chạy. Script đọc .env nhưng không in credentials và không thay business data. Kiểm tra SQL chỉ SELECT, auth và connect permissions.

## Business suites planned

Giữ nguyên T01–T28 trong plan V4. T01–T18, T20–T24, T27–T28 là P0 cần thực thi ở các sprint nghiệp vụ; T19/T25/T26 là AI bonus dùng mock provider để không tốn quota. Không ghi PASS toàn bộ R01–R18 hoặc T20 chỉ vì Sprint 0 chạy healthy.

| Suite | Mục tiêu |
|---|---|
| Unit | Rules/state/validation/permission không cần DB |
| Integration | EF PostgreSQL thật, migrations/constraints, Redis Streams thật; Testcontainers hoặc Compose |
| Contract | REST DTO/OpenAPI, reviewed proto client/server, event version/schema |
| Concurrency | Hai approve tranh unit cuối; multi-item rollback; idempotent replay |
| EndToEnd | Login → create → approve REST→gRPC → event→notification → overdue job → return; Docker clean setup |

Mỗi thay đổi state phải có evidence DB/log/correlation ID; mock không thay test transaction/concurrency. Crash trước XACK và sau XADD, Redis down/recovery, gRPC unknown result, Borrowing crash sau reserve phải được kiểm thử có chủ đích. Không bịa KPI, throughput hoặc tỷ lệ pass.

## Sprint 1 executable suites

- Unit LoanRulesTests: due dates/item limits, repeated type→separate units, owner-only cancellation and idempotency. Existing correlation tests retained.
- Platform JwtSecurityTests: invalid signature/issuer/audience/expiry rejected at Gateway before proxy. Existing platform integration retained.
- Sprint1 IntegrationTests opt-in `LABLEND_POSTGRES_TESTS=1`: isolated Compose PostgreSQL with migrations, real Identity password/login/roles, registration race, lockout, current profile/password, admin guard, cookie/CSRF, refresh hashes/replay/expiry/concurrent rotation/logout, inactive accounts; loans ownership/pagination/input validation/DB rollback/state concurrency; repeated migrations/normalized unique constraints/DB isolation.
- GatewayFlowTests: YARP proxies real Kestrel Identity/Borrowing; real register→login→JWT→me, student/staff admin denial, loan ownership and dependency failure. Catalog is an explicit HTTP test double. No fake Inventory allocation and no evidence claim for real Catalog.
- scripts/Verify-Sprint1.ps1: read-only current Compose checks, including the existing platform regression verifier, twenty real OpenAPI operations, anonymous protected requests and both applied migration IDs read as their own DB users. Add -IncludeBonus only when that host is running.

Fixture creates unique Compose project without shared data volumes/credentials and removes only that project; missing Docker fails enabled tests. Without opt-in they are visibly skipped, never silently passed. CI enables them; hosted results need a real run URL after authorized push. Build and tests should precede Docker image build on small machines; do not run both heavy workloads together.

Kết quả thực, lỗi đã sửa và giới hạn: [SPRINT0_ACCEPTANCE](SPRINT0_ACCEPTANCE.md), [SPRINT1_ACCEPTANCE](SPRINT1_ACCEPTANCE.md).
