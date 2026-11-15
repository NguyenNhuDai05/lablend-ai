# Sprint 0 handover

## Kết quả và phạm vi

Foundation của Member 1 được triển khai trên `chore/sprint0-foundation`, không merge main. Các service của Member 2/3 chỉ được tạo skeleton/DI/config/health; không có model hoặc nghiệp vụ giả, không chốt business proto/event payload thay owner. Kết quả thực thi mới nhất: [SPRINT0_ACCEPTANCE](../testing/SPRINT0_ACCEPTANCE.md).

## Các file và project chính

- `LabLendAI.sln`, `global.json`, `Directory.Build.props`, `Directory.Packages.props`, `.config/dotnet-tools.json`: 31 projects gồm 6 service × 4 layers, Gateway, Worker, 2 technical libraries, health probe và 2 test projects; backend net8.0.
- `src/Gateway/LabLend.Gateway`: YARP routes, CORS allowlist, JWT validation, rate limiter và diagnostic proxy.
- `src/Services/{Identity,Catalog,Inventory,Borrowing,Notification,Recommendation}`: REST/dual-transport hosts, service-local empty contexts, project references và DI; không có business Controllers/entities.
- `src/BuildingBlocks/{Contracts,Observability}`: pagination/role DTOs, correlation, JSON logging, ProblemDetails, JWT setup và connection health probes; không shared database/domain model.
- `src/Workers/LabLend.Borrowing.Worker`: actual BackgroundService process với dependency heartbeat; business jobs Planned.
- `docker-compose.yml`, `infrastructure/docker`, `infrastructure/database`, `.env.example`, `scripts`: local DB/Redis/core/bonus hosts, shared multi-stage build, per-DB bootstrap/isolation checks và environment/dev/verify scripts.
- `apps/web`: React/TypeScript/Vite starter, API base URL, actual Gateway health action, production nginx image, npm lockfile; không có UI nghiệp vụ.
- `.github`: CI, Issue templates, PR template. Module ownership dùng tài liệu, chưa có CODEOWNERS với account chưa biết.
- `proto/inventory/v1/inventory.proto`: proposal comments/package; M2 hoàn thiện schema sau review. `docs/api`, `docs/grpc`, `docs/events`: conventions và proposal contracts.
- `docs/requirements`: PDF đề và R01–R18 traceability. Plan Word gốc được giữ nguyên tại repo root.

## Kiến trúc thực tế

Xem [ARCHITECTURE_OVERVIEW](../architecture/ARCHITECTURE_OVERVIEW.md). Core default không gọi Gemini, không cần cloud hoặc billing. PostgreSQL một instance, sáu DB/sáu login giới hạn quyền; Redis có authentication và persistence. API/gRPC/DB/Redis ở internal network, chỉ web/gateway publish loopback. Inventory REST và standard gRPC health là hai listener trong một process riêng với Borrowing.

Health checks chứng minh process/dependency/transport, không chứng minh CRUD, allocation, Streams delivery, migrations hoặc overdue business flow.

## Kiểm chứng và giới hạn

Các lệnh, PASS/FAIL/BLOCKED và lỗi đã sửa được ghi tại [acceptance](../testing/SPRINT0_ACCEPTANCE.md). Không suy diễn hosted Actions PASS từ local tests. Business T01–T28 và P0 cuối môn vẫn cần triển khai/kiểm thử ở các sprint sau. Không có paid resources được tạo và không gọi Gemini.

## Decisions cần review

OD-01–03: M2/M1 chốt Inventory proto, idempotency/recovery và service auth. OD-04–05: M1/M3 chốt envelope/payload/consumer retries. OD-06–08: frontend auth/catalog/loan DTOs. OD-09: bonus Gemini Free Tier, minimization và egress. OD-10: GitHub handles/review ownership. Chi tiết: [OPEN_DECISIONS](../architecture/OPEN_DECISIONS.md).

## Nhóm bắt đầu code

Clone và checkout nền bootstrap nếu chưa merge main, chạy `Initialize-Environment.ps1`, `Build-Images.ps1`, `docker compose up -d --build --wait`, rồi `Verify-Sprint0.ps1` như README. Để code bằng SDK, chỉ start PostgreSQL/Redis với Compose dev override và dùng `Run-LocalHost.ps1` cho host của mình; frontend `npm ci` + `npm run dev`. Không cần API key hoặc thẻ thanh toán.

Hướng dẫn riêng M2/M3, ownership paths, migration commands, proposed Issues/PR và Sprint 1 của M1: [TEAM_RESPONSIBILITIES](TEAM_RESPONSIBILITIES.md). Chỉ tạo feature branch/PR từ base đã thống nhất; không tự merge main hoặc đổi schema của người khác.
