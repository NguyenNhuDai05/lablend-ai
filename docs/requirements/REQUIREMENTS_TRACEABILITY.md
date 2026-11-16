# Requirements traceability

Nguồn chuẩn: [PRN232 Final Assignment, 3 trang](PRN232%20-%20Final%20Assignment.pdf). Plan nhóm: [V4](../../PRN232_LabLend_AI_Master_Implementation_Plan_V4.docx). Nếu xung đột, PDF ưu tiên. Mã R01–R18 giữ theo V4 để đối chiếu; PDF không bắt buộc sáu microservice, Gemini hoặc frontend.

**Implemented** = phần được nêu đã có implementation; **Scaffolded** = mới có nền/cấu hình; **Planned** = chưa triển khai; **Blocked** = có blocker cụ thể. Một yêu cầu cuối môn vẫn chưa đạt nếu evidence mới chứng minh skeleton.

| ID | Yêu cầu | Status ở Sprint 0 | Nơi code/docs và evidence cần có để nghiệm thu cuối |
|---|---|---|---|
| R01 | ASP.NET Core REST/RESTful/CRUD | Scaffolded | src/Services/*/*.Api; REST hosts chạy nhưng business CRUD Planned; cần POST/GET/PUT/DELETE thật |
| R02 | API → Services → Repository | Scaffolded | Api/Host → Application → Domain; Infrastructure contexts/DI; cần application services/repository implementation và tests |
| R03 | JWT authentication | Scaffolded | Observability/ServiceDefaults: validation/policies; Gateway anonymous route test 401; issuance/refresh/persistence Sprint 1 |
| R04 | Search/filter/sort/pagination | Planned | Contracts/PagedResponse DTO + REST conventions; Catalog owner M2 phải implement queries thật |
| R05 | Background job | Scaffolded | Workers/.../FoundationWorker là dependency heartbeat; overdue/reminder/reconciliation business jobs Planned |
| R06 | Kafka hoặc Redis Streams | Scaffolded | Redis container/auth/volume/PING; Stream operations chưa implement |
| R07 | Producer và consumer thật | Planned | M1 Outbox Publisher + M3 Notification Consumer; cần XADD/XREADGROUP/XACK + Inbox evidence |
| R08 | Independent gRPC service | Scaffolded | Inventory.Host độc lập, HTTP/2 standard health; business schema/handlers chờ M2 |
| R09 | REST API gọi gRPC | Planned | Borrowing business REST→Inventory allocation chưa có; health probe không thay yêu cầu này |
| R10 | EF Core + relational DB | Scaffolded | Empty per-service DbContexts, EF/Npgsql 8 packages, PostgreSQL 6 DB; model/migrations/real writes Planned |
| R11 | DI/config/logs/exception handling | Implemented foundation | ServiceDefaults, Infrastructure/DependencyInjection, env scripts, JSON logging, correlation, ProblemDetails; extend business exception mapping later |
| R12 | Docker/Cloud toàn hệ thống | Scaffolded | Compose deploys foundation hosts; business E2E/migrations chưa có; runtime evidence trong SPRINT0_ACCEPTANCE |
| R13 | Complete source | Scaffolded | src/apps/tests có Sprint 0 source; chưa phải complete assignment implementation |
| R14 | Database scripts/migrations | Scaffolded | infrastructure/database bootstrap/isolation; business EF migrations/seed Planned |
| R15 | Dockerfiles/Compose | Implemented foundation | Shared multi-stage .NET Dockerfile + web Dockerfile + core/bonus Compose; foundation build/start evidence trong acceptance |
| R16 | API Swagger/OpenAPI | Scaffolded | Swagger cho mọi REST host + gateway schema proxy; business operations chưa có |
| R17 | README đủ 6 mục bắt buộc | Implemented for Sprint 0 | Description/architecture/stack/install/deploy/responsibilities + dev/Git/env/troubleshooting; cập nhật khi nghiệm thu cuối |
| R18 | Demo 7 nội dung bắt buộc | Planned | Cần architecture/REST/job/broker/gRPC/deploy/E2E business demo; foundation runtime script chỉ là một phần deployment evidence |

Rubric PDF: architecture/design 20%; REST 20%; background 10%; broker 15%; gRPC 15%; deployment 10%; docs/presentation 10%. Không quy đổi trạng thái Sprint 0 thành điểm hoặc ghi đủ 100% MUST khi business còn Planned.

## Sprint 1 Member 1 delta

Bảng trên được giữ như baseline Sprint 0. Trạng thái hiện hành riêng phạm vi Member 1:

| IDs | Sprint 1 status | Implementation and remaining scope |
|---|---|---|
| R01/R02 | IMPLEMENTED Identity + Borrowing Core | Controllers → Application services → repositories; read/write thực; Catalog/Inventory CRUD do M2, không chứng nhận toàn bộ Final |
| R03 | IMPLEMENTED | Identity UserManager password hashing, JWT issue/validation, RBAC, refresh hash/rotation/replay/revoke; [Identity contract](../api/IDENTITY_API_CONTRACT.md) |
| R04 | IMPLEMENTED scoped lists | User/Loan pagination, status filter, deterministic date sort; Catalog search/sort vẫn Planned M2 |
| R10/R14 | IMPLEMENTED scoped schemas | Identity/Borrowing EF migrations, constraints, local transactions và PostgreSQL integration; other contexts Scaffolded |
| R11/R16 | IMPLEMENTED extended | DI, options, sanitized ProblemDetails, actual Identity/Borrowing Swagger operations |
| R12/R15 | Verification per acceptance | Compose/build/runtime results phải đọc [Sprint 1 acceptance](../testing/SPRINT1_ACCEPTANCE.md) |
| R05–R09/R13/R18 | Incomplete final requirements | Overdue job, Streams producer/consumer, business gRPC, full source/E2E final vẫn Planned; không đổi trạng thái thành complete |
| T01/T02/T22 | Evidence per acceptance | Identity PostgreSQL/password/RBAC/session suites |
| T03/T05 | TESTED with explicit dependency double; real Catalog BLOCKED | PostgreSQL loans + real Identity JWT; Catalog HTTP validation test double; chưa chứng nhận equipment trong Catalog thật |

Test P0/bonus và evidence: [TEST_PLAN](../testing/TEST_PLAN.md), [SPRINT0_ACCEPTANCE](../testing/SPRINT0_ACCEPTANCE.md), [SPRINT1_ACCEPTANCE](../testing/SPRINT1_ACCEPTANCE.md). Mọi PASS phải trỏ tới kết quả thực; healthy không chứng minh allocation hoặc message delivery.
