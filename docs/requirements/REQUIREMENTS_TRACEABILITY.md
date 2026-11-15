# Requirements traceability

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
| R18 | Demo 7 nội dung bắt buộc | Planned | Cần architecture/REST/job/broker/gRPC/deploy/E2E business demo; foundation runtime script chỉ là một phần deployment evidence |

Rubric PDF: architecture/design 20%; REST 20%; background 10%; broker 15%; gRPC 15%; deployment 10%; docs/presentation 10%. Không quy đổi trạng thái Sprint 0 thành điểm hoặc ghi đủ 100% MUST khi business còn Planned.

Test P0/bonus và evidence: [TEST_PLAN](../testing/TEST_PLAN.md), [SPRINT0_ACCEPTANCE](../testing/SPRINT0_ACCEPTANCE.md). Mọi ghi nhận PASS phải trỏ tới lệnh/kết quả thực; không coi healthy là bằng chứng database migrations, allocation hoặc message delivery.
