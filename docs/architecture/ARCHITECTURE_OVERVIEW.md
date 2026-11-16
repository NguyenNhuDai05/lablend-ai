# Architecture overview

## Current checkpoint 2026-11-17

[Final architecture](FINAL_ARCHITECTURE.md) describes implemented core containers, C4 context/container views, local ERD/ownership, loan states and service interactions. Identity, Catalog, Inventory allocation, Borrowing lifecycle/recovery, four business jobs and real Outbox/Streams/Inbox are implemented. Recommendation remains optional/disabled. The sections below record the original Sprint 0 audit and must not be read as current implementation status. Current execution evidence is in [final acceptance](../testing/FINAL_PRODUCT_ACCEPTANCE.md).

## Audit ban đầu

Git root: `lablend-ai`; remote `https://github.com/NguyenNhuDai05/lablend-ai.git`; branch `main`, working tree sạch. HEAD ban đầu `196a09e` nối tiếp `06faf8d`. Chỉ có README, VisualStudio .gitignore và plan V4. Không có csproj/sln/Dockerfile/package.json nên chưa có lệnh build ứng dụng nào để chạy. PDF đề có tại workspace cha; bản nguyên vẹn được thêm vào `docs/requirements` để người clone đọc được.

SDK 8.0.423/9/10 và Node 22/npm hiện có; chưa có global.json nên ban đầu dotnet chọn SDK 10. Skeleton ghim SDK 8.0.423 và target net8.0. Docker CLI/Compose hiện có; Docker Desktop ban đầu tắt, được khởi động cho verification. Nhánh thực hiện: `chore/sprint0-foundation`; không merge main.

## Cấu trúc và dependency

```text
apps/web                     React + TypeScript + Vite, owner M3
src/Gateway/LabLend.Gateway   YARP + JWT validation + CORS + rate limit
src/Services/<Context>/
  <Context>.Api|Host         Controllers / transport / composition root
  <Context>.Application     Use cases, service/repository interfaces (empty boundary)
  <Context>.Domain          Business rules/entities (empty boundary)
  <Context>.Infrastructure  Context-local DbContext and DI; model not implemented
src/Workers/LabLend.Borrowing.Worker
src/BuildingBlocks/Contracts      Pagination/role technical DTOs only
src/BuildingBlocks/Observability  Correlation, logs, ProblemDetails, health/JWT setup
proto/inventory/v1               Member 2 proposal, no generated business schema
infrastructure/docker           Shared multi-stage Dockerfile + runtime health probe
infrastructure/database         Per-database/user bootstrap
tests                           Technical platform tests; business suites Planned
```

Thư mục dùng PascalCase cho .NET (`Services`, `Gateway`, `Workers`, `BuildingBlocks`) theo prompt Sprint 0; frontend chuyển thành `apps/web`. Inventory host tên `Inventory.Host` vì cùng process cung cấp REST HTTP/1 cổng 8080 và gRPC HTTP/2 cổng 8081. Đây là thay đổi tên/cấu trúc so với ví dụ V4, không thay đổi ownership hoặc yêu cầu gRPC độc lập.

Compile references: Api/Host → Application + Infrastructure + Observability; Application → Domain + Contracts; Infrastructure → Application (và Domain qua Application). Không có reference giữa service A và Infrastructure/Domain service B. Worker là cùng bounded context Borrowing nên được reference Borrowing.Infrastructure. Observability là thư viện kỹ thuật; các health probes dùng connection riêng được caller cung cấp, không phải shared DbContext.

## Deployment hiện tại và tương lai

Core default gồm 10 container; Recommendation opt-in `bonus` là container thứ 11. PostgreSQL/Redis/API/Worker chỉ trên `backend` internal network; Gateway ở `edge` + `backend`; nginx web chỉ `edge`. Chỉ web/gateway publish loopback. `compose.dev.yml` chỉ mở DB/Redis loopback khi phát triển ngoài container. Chưa có telemetry dashboard/profile observability vì chưa cần cho Sprint 0.

Implemented: host startup, actual DB/Redis health, Gateway health/readiness proxy, OpenAPI, safe correlation forwarding, JWT Bearer validation configuration, rate limit, environment scripts, isolated DB bootstrap.

Scaffolded: service/application/domain/infrastructure boundaries, service-local empty contexts, BackgroundService process, Inventory gRPC transport (standard health only), frontend entry point.

Planned: Identity issuance/refresh/roles persistence; CRUD; allocation RPCs; Borrowing approval/recovery; Outbox; Redis Streams producer/consumer; overdue jobs; business migrations/seed; full UI; Gemini; observability dashboard.

Luồng đích: Student → Borrowing REST → Inventory gRPC (atomic reserve) → local Borrowing transaction + Outbox → Publisher XADD → Notification consumer local Notification+Inbox commit → XACK. Worker overdue/reconciliation là nghiệp vụ riêng cần triển khai. Không gọi các luồng này là hoàn thành trong Sprint 0.

Readiness hiện tại không xác nhận schema nghiệp vụ hay toàn bộ graph dependency. Gateway own health chỉ process; `/platform/<service>/health/ready` proxy readiness của service. Worker heartbeat chứng minh process/dependency, không chứng minh nhắc hạn.
