# Requirements traceability

Current M3 P0 delta: real Catalog/Student/Staff loan/Profile/Admin/Notification UI and seven-section demo materials. See [M3 acceptance](../testing/M3_P0_ACCEPTANCE.md) for executed gates and remaining human decisions. Gemini/dashboard deferred; M2 fresh-install T20 is separate. Historical tables below remain provenance and do not override this checkpoint.

## Current P0 checkpoint (M3 implementation branch)

This table is current; older sprint tables below remain historical. Test counts, exact CI SHA and runtime results are recorded in [M3 acceptance](../testing/M3_P0_ACCEPTANCE.md). “Implemented” does not mean human/team final approval.

| ID | Current implementation / evidence | Remaining boundary |
|---|---|---|
| R01 REST/CRUD | Identity users/auth, Catalog CRUD/search, Inventory assets/maintenance, Borrowing lifecycle, Notification/read; corresponding integration suites and React browser flow | Recommendation is deferred bonus |
| R02 Layering | Existing REST controllers → Application services/ports → Infrastructure repositories/EF; M3 UI consumes contracts | No cross-service repository access added |
| R03 JWT | Memory access token, HttpOnly refresh cookie, CSRF/Origin, role guards, existing 11 auth regressions plus browser re-login | Access JWT expiry window after role/password changes remains documented |
| R04 Search/filter/sort/page | Public Catalog and loan status/page UI use real server APIs; Admin page/list/detail | Admin search is current-page filtering; global search API absent |
| R05 Jobs | Four actual Borrowing jobs, persisted reminder keys, restart/two-worker tests and browser due/overdue notifications | Not a foundation heartbeat claim |
| R06 Broker | Redis Streams XADD/XREADGROUP/XAUTOCLAIM/XACK/parking | Retention/archive horizon OPEN |
| R07 Real producer/consumer | Transactional Outbox → Redis → Notification+Inbox → REST → UI; real outage/replay gates | OD-04/05 human signoff OPEN; at least once |
| R08 Independent gRPC | Separate Inventory HTTP2 service, unchanged v1 proto/server and service credential | M2 historical condition/time/reservation-ID gap OPEN |
| R09 REST→gRPC | Approve/reserve, handover and partial returns, stable operations/recovery; browser confirms real Inventory | No client-invented reservation IDs |
| R10 EF + relational DB | Existing per-service PostgreSQL contexts and migrations; fixture-owned real DB tests | No cross-service SQL joins |
| R11 DI/config/log/errors | Existing options/DI/structured logs/correlation/ProblemDetails; UI handles auth/conflict/unavailable | NuGet audit-network warnings reported separately from compile results |
| R12 Docker/deployment | Updated web image build passed; private real-service E2E passed | Shared-stack recheck pending: worker exit 139 then Docker daemon unavailable; M2 T20 clean-clone/fresh-volume acceptance separate |
| R13 Source | M1 #10–13 merged; M3 P0 code/tests/docs on review branch; M2 sources preserved | M3 branch merge requires its own review |
| R14 DB scripts | Existing ownership/bootstrap and Identity/Catalog/Inventory/Borrowing/Notification migrations | No DB reset/volume deletion in this work |
| R15 Dockerfiles/Compose | Existing multi-stage images, frontend dist image, configured health/dependencies | No paid infrastructure or billing enabled |
| R16 OpenAPI | Gateway exposes five business-service schemas; runtime verifier checks Borrowing commands and auth | Recommendation remains optional foundation |
| R17 README | Description, architecture, stack, setup/deploy/responsibilities retained; M3 handover and current matrix added | Team must review final submission packet |
| R18 Seven-topic demo | Editable slide deck, seven-section runbook, role sequence/fallback/Q&A and real browser screenshots | Human rehearsal/presenter signoff/video recording not asserted |

## Historical M1 Sprint 2 checkpoint

Current M1 Sprint 2 delta: R09 TESTED by Gate A REST→Inventory HTTP/2 lifecycle and Gate B failure recovery; R06/R07 TESTED by Gate C PostgreSQL Outbox→XADD→unchanged M3 consumer→Notification REST, Redis outage and replay; R05 TESTED by real scheduled jobs/two worker processes/restart/notification in Gate D/E. Borrowing migrations add operations/outbox/reminder dedupe only to borrowing_db (R10/R14). R11/R16 extend options/logs/readiness/OpenAPI. R12/R15 Docker upgrade/startup passed with 11 healthy existing-stack containers; no separate destructive clean-volume reinstall. Backend/frontend regression and exact-commit CI evidence are tracked in [Sprint 2 acceptance](../testing/SPRINT2_M1_ACCEPTANCE.md). Historical Sprint 0/1 tables below do not supersede this checkpoint or certify complete-coursework/defense acceptance. M2/M3 contract review remains pending.

Nguồn chuẩn: [PRN232 Final Assignment, 3 trang](PRN232%20-%20Final%20Assignment.pdf). Plan nhóm: [V4](../../PRN232_LabLend_AI_Master_Implementation_Plan_V4.docx). Nếu xung đột, PDF ưu tiên. Mã R01–R18 giữ theo V4 để đối chiếu; PDF không bắt buộc sáu microservice, Gemini hoặc frontend.

**Implemented** = phần được nêu đã có implementation; **Scaffolded** = mới có nền/cấu hình; **Planned** = chưa triển khai; **Blocked** = có blocker cụ thể. Một yêu cầu cuối môn vẫn chưa đạt nếu evidence mới chứng minh skeleton.

| ID | Yêu cầu | Status ở Sprint 0 | Nơi code/docs và evidence cần có để nghiệm thu cuối |
|---|---|---|---|
| R01 | ASP.NET Core REST/RESTful/CRUD | Partial: Catalog, Inventory Implemented | Catalog.Api: CRUD /api/categories, /api/equipment-types (POST/GET/PUT/DELETE soft-deactivate, StaffOrAdmin writes); Inventory.Host: CRUD /api/inventory/assets (retire soft) + maintenance + public availability; evidence tests/Integration/Catalog.IntegrationTests, Inventory.IntegrationTests. Identity/Borrowing/Notification CRUD Planned |
| R02 | API → Services → Repository | Partial: Catalog, Inventory Implemented | Catalog: Controllers → CategoryService/EquipmentTypeService → ICategoryRepository/IEquipmentTypeRepository (Infrastructure EF). Inventory: Controllers/gRPC → AssetService/AllocationService → IAssetRepository/IAllocationRepository. Các service khác Planned |
| R03 | JWT authentication | Scaffolded | Observability/ServiceDefaults: validation/policies; Gateway anonymous route test 401; issuance/refresh/persistence Sprint 1 |
| R04 | Search/filter/sort/pagination | Implemented (Catalog) | GET /api/equipment-types keyword/categoryId/tag, sortBy name/createdAt whitelist, page/pageSize ≤ 100, PagedResponse; T04 trong Catalog.IntegrationTests |
| R05 | Background job | Scaffolded | Workers/.../FoundationWorker là dependency heartbeat; overdue/reminder/reconciliation business jobs Planned |
| R06 | Kafka hoặc Redis Streams | Scaffolded | Redis container/auth/volume/PING; Stream operations chưa implement |
| R07 | Producer và consumer thật | Planned | M1 Outbox Publisher + M3 Notification Consumer; cần XADD/XREADGROUP/XACK + Inbox evidence |
| R08 | Independent gRPC service | Implemented (server) | Inventory.Host: InventoryAllocation v1 (6 RPC, service-token auth, idempotent reserve); evidence Inventory.IntegrationTests T07–T11/T27, T23 contract + HTTP/2 thật qua Kestrel (GrpcOverHttp2Tests). OD-01–03 chờ M1 review |
| R09 | REST API gọi gRPC | Planned | Server sẵn sàng; Borrowing REST approve/handover/return → Inventory gRPC client (M1) chưa có; health probe không thay yêu cầu này |
| R10 | EF Core + relational DB | Partial: Catalog, Inventory Implemented | CatalogDbContext/InitialCatalog trên catalog_db; InventoryDbContext/InitialInventory + AddStockMovements trên inventory_db (xmin concurrency, partial unique indexes, lịch sử status); các context khác Planned |
| R11 | DI/config/logs/exception handling | Implemented foundation | ServiceDefaults, Infrastructure/DependencyInjection, env scripts, JSON logging, correlation, ProblemDetails; extend business exception mapping later |
| R12 | Docker/Cloud toàn hệ thống | Scaffolded | Compose deploys foundation hosts; business E2E/migrations chưa có; runtime evidence trong SPRINT0_ACCEPTANCE |
| R13 | Complete source | Scaffolded | src/apps/tests có Sprint 0 source; chưa phải complete assignment implementation |
| R14 | Database scripts/migrations | Partial | infrastructure/database bootstrap/isolation; Catalog, Inventory migration + seed (tự áp dụng khi khởi động); migrations các service khác Planned |
| R15 | Dockerfiles/Compose | Implemented foundation | Shared multi-stage .NET Dockerfile + web Dockerfile + core/bonus Compose; foundation build/start evidence trong acceptance |
| R16 | API Swagger/OpenAPI | Partial | Swagger cho mọi REST host + gateway schema proxy; Catalog và Inventory đã có business operations (`/platform/catalog/openapi.json`, `/platform/inventory/openapi.json`), service khác chưa có |
| R17 | README đủ 6 mục bắt buộc | Implemented for Sprint 0 | Description/architecture/stack/install/deploy/responsibilities + dev/Git/env/troubleshooting; cập nhật khi nghiệm thu cuối |
| R18 | Demo 7 nội dung bắt buộc | Partial: phần M2 có script | Cần architecture/REST/job/broker/gRPC/deploy/E2E business demo; foundation runtime script chỉ là một phần deployment evidence. M2: `scripts/Demo-Inventory.ps1` (REST Catalog/Inventory qua Gateway + gRPC tranh chấp/idempotency trên Docker) |

Rubric PDF: architecture/design 20%; REST 20%; background 10%; broker 15%; gRPC 15%; deployment 10%; docs/presentation 10%. Không quy đổi trạng thái Sprint 0 thành điểm hoặc ghi đủ 100% MUST khi business còn Planned.

## Sprint 1 Member 1 delta

Bảng trên được giữ như baseline Sprint 0. Trạng thái hiện hành riêng phạm vi Member 1:

| IDs | Sprint 1 status | Implementation and remaining scope |
|---|---|---|
| R01/R02 | IMPLEMENTED Identity + Borrowing Core | Controllers → Application services → repositories; read/write thực; Catalog/Inventory CRUD do M2, không chứng nhận toàn bộ Final |
| R03 | IMPLEMENTED | Identity UserManager password hashing, JWT issue/validation, RBAC, refresh hash/rotation/replay/revoke; [Identity contract](../api/IDENTITY_API_CONTRACT.md) |
| R04 | IMPLEMENTED scoped lists | User/Loan pagination, status filter, deterministic date sort; Catalog search/filter/sort/page đã implemented ở M2 (PR #8 integration) |
| R10/R14 | IMPLEMENTED scoped schemas | Identity/Borrowing EF migrations, constraints, local transactions và PostgreSQL integration; Catalog/Inventory/Notification cũng có migrations thật; Recommendation scaffolded |
| R11/R16 | IMPLEMENTED extended | DI, options, sanitized ProblemDetails, actual Identity/Borrowing Swagger operations |
| R12/R15 | Verification per acceptance | Compose/build/runtime results phải đọc [Sprint 1 acceptance](../testing/SPRINT1_ACCEPTANCE.md) |
| R05–R09/R13/R18 | Incomplete final requirements | Overdue job, Streams producer/consumer, business gRPC, full source/E2E final vẫn Planned; không đổi trạng thái thành complete |
| T01/T02/T22 | Evidence per acceptance | Identity PostgreSQL/password/RBAC/session suites |
| T03/T05 | Real-service integration added in PR #8 | MemberIntegrationTests: real Identity-issued Staff/Student JWT, Catalog CRUD, Borrowing→Catalog active/missing/inactive, Inventory maintenance/retire/movements; kết quả thực tại PR8_INTEGRATION_ACCEPTANCE.md. Regression double cũ vẫn có nhãn |

Test P0/bonus và evidence: [TEST_PLAN](../testing/TEST_PLAN.md), [SPRINT0_ACCEPTANCE](../testing/SPRINT0_ACCEPTANCE.md), [SPRINT1_ACCEPTANCE](../testing/SPRINT1_ACCEPTANCE.md). Mọi PASS phải trỏ tới kết quả thực; healthy không chứng minh allocation hoặc message delivery.

## PR #8 integration checkpoint

Bảng Sprint 0 ở trên là lịch sử; trạng thái hợp nhất hiện hành: Identity/Borrowing Core (M1), Catalog/Inventory REST + six-RPC HTTP/2 server (M2), routed auth/Staff/Notification UI và Notification REST/consumer proposal (M3) được bảo toàn. [PR8 acceptance](../testing/PR8_INTEGRATION_ACCEPTANCE.md) ghi kết quả build/tests/runtime/CI thực tế. R08 có implementation phía server; **R09 REST Borrowing→Inventory allocation vẫn PLANNED**. Outbox, overdue jobs và Redis event end-to-end vẫn PLANNED; không coi health probes hoặc direct gRPC tests là hoàn thành các yêu cầu đó.
