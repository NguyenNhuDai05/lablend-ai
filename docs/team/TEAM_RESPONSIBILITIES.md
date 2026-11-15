# Team responsibilities and handover

Mục tiêu tổng công sức M1/M2/M3 khoảng 34%/33%/33%, điều chỉnh theo thực tế. Owner chịu trách nhiệm code, tests, migrations, docs và review integration của module. Sprint 0 chỉ tạo nền tảng để phát triển độc lập.

| Member | Owned paths | Shared reviews |
|---|---|---|
| M1 Tech Lead | src/Gateway, src/Services/Identity, src/Services/Borrowing, src/Workers, infrastructure, shared technical config | Contracts, Compose, CI; proto consumer; event producer |
| M2 Catalog/Inventory | src/Services/Catalog, src/Services/Inventory, proto/inventory, Staff feature UI | Inventory proto với M1; Catalog DTO với M3; frontend integration theo shell của M3 |
| M3 Frontend/Notification/AI | apps/web shell/common/Admin/AI UI, src/Services/Notification, src/Services/Recommendation | Event với M1; REST/auth DTO với M1/M2 |

Không dùng CODEOWNERS với account giả. Khi có GitHub handles thật, chủ repo có thể thêm CODEOWNERS theo OD-10. Sprint 0 branch chưa tự merge main.

## Member 2 bắt đầu

1. Clone repo; `git fetch origin`; `git switch chore/sprint0-foundation` nếu chưa merge main; tạo `feature/m2-catalog-crud` từ nền này.
2. Chạy script Initialize-Environment và Compose infra dev theo README.
3. Catalog: `Catalog.Api/Controllers` gọi `Catalog.Application` services; repository interfaces ở Application, implement trong Infrastructure; model chỉ trong Catalog.Domain và catalog_db. Tạo migrations thật, implement CRUD/search/filter/sort/page, T04 + authorization.
4. Inventory: `Inventory.Host` đã có REST 8080/gRPC 8081 Docker, local 5103/5181. REST admin controllers và business gRPC handlers phải dùng cùng Application layer/inventory_db. Không thêm Inventory host trùng.
5. Soạn `proto/inventory/v1/inventory.proto`, chốt OD-01–03 với M1 trước generate. Không đổi package/namespace/schema mà không báo consumer.
6. Atomic multi-item reservation, database transaction/locking + constraints, stable operationId/replay semantics, caller auth, GetOperationStatus/release/reconciliation phải thiết kế chung. Tests T06–T09/T11/T23/T27, cùng M1 làm T28.
7. Staff UI phối hợp M3: M3 owns shell/router/auth/shared components; M2 phát triển Staff feature sau khi thống nhất paths/DTOs, tránh sửa cùng App.tsx/router trong hai PR.

Chạy host: `./scripts/Run-LocalHost.ps1 -Service catalog` và `-Service inventory` ở hai terminal; Gateway terminal thứ ba. Migrations instructions ở DATABASE_CONVENTIONS. API/contract changes phải có PR review M1/M3 liên quan.

## Member 3 bắt đầu

1. Tạo `feature/m3-web-shell` hoặc `feature/m3-notification-inbox` từ bootstrap đã fetch. `apps/web`: copy .env.example → .env.local, npm ci, npm run dev/build.
2. Tiếp quản routing, frontend auth client, pages, loading/error/empty states và shared UI. `src/lib/api.ts` có API base URL; chưa có token/session persistence hay business pages. Review OD-06/07 trước auth client.
3. Notification API/Application/Domain/Infrastructure thuộc M3; chỉ notification_db. Consumer **chưa tồn tại**: chốt OD-04/05, implement XREADGROUP → Notifications+Inbox local transaction → XACK; pending reclaim/retry/parking. Cùng M1 viết duplicate/crash/Redis-down T12–T18.
4. Recommendation ở `src/Services/Recommendation`, bonus profile. Chưa gọi Gemini. Khi đến bonus, dùng backend HttpClientFactory, key từ env backend, validate Catalog IDs, Inventory availability read; không bịa stock/không tự reserve, không rule-based/local model. Không PII hoặc secret vào prompts; trả AI_UNAVAILABLE khi key/quota thiếu. Core không phụ thuộc AI.
   Host bonus hiện chỉ ở backend internal network; khi tích hợp HTTPS provider, review OD-09 để thêm egress riêng cho Recommendation. Không mở outbound cho toàn core để giải quyết lỗi kết nối AI.
5. npm/React/TypeScript/Vite versions đã khóa; không commit node_modules/dist/.env. Giao Staff feature integration points cho M2 qua PR nhỏ, không để M2/M3 cùng viết lại shell.

## Member 1 next sprint

Sprint 1 bắt đầu sau acceptance nền tảng: ASP.NET Identity password hashing + JWT issuance/refresh/revoke, user roles/policies, real Identity model/migrations; Borrowing loan model/state/owner checks và review contract với M2. Tiếp theo triển khai approve gRPC + APPROVING/idempotency/reconciliation, lifecycle, Outbox Publisher, overdue/reconciliation BackgroundServices. Không dùng worker heartbeat hiện tại như nghiệp vụ hoàn chỉnh.

## Suggested next issues và PR

| ID đề xuất | Owner | Outcome / dependency |
|---|---|---|
| S1-M1-01 | M1 | Identity register/login/JWT/refresh + migrations + T01/T02/T22; OD-06 |
| S1-M1-02 | M1 | Borrowing create/my/detail/state/owner + DB; OD-08, T03/T05 |
| S1-M2-01 | M2 | Catalog CRUD/search/filter/sort/page + Swagger/migrations; OD-07, T04 |
| S1-M2-02 | M2 + M1 | Review Inventory v1 proto + service auth + idempotency semantics; OD-01–03 |
| S1-M2-03 | M2 | Asset CRUD/maintenance + atomic reserve/concurrency; T07–T09/T27 |
| S1-M3-01 | M3 | React router/layout/auth flow + agreed DTOs; OD-06/07 |
| S1-M3-02 | M3 + M1 | Event schema + Inbox/Outbox integration design; OD-04/05 |
| S2-M1-01 | M1 + M2 | REST approve → actual gRPC + compensation/reconciliation, T06/T28 |
| S2-M3-01 | M3 + M1 | Consumer/publisher + crash/idempotency integration T12–T16 |
| S2-M1-02 | M1 | Reminder/overdue job + dedupe/outbox, T17/T18 |
| BONUS-M3-01 | M3 | Gemini backend integration only after core acceptance, T19/T25/T26 |

Đây là danh sách để nhóm tạo Issues/PR tiếp; chưa tự đăng Issue hoặc gán người dùng GitHub. PR bootstrap chứa foundation; từng feature PR liên kết issue, tests thật và contract review. Không force push hoặc tự merge main.
