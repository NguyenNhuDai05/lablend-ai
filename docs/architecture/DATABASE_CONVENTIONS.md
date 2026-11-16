# Database ownership and migrations

| Service | Database | Login | Owner |
|---|---|---|---|
| Identity | identity_db | identity_user | M1 |
| Catalog | catalog_db | catalog_user | M2 |
| Inventory | inventory_db | inventory_user | M2 |
| Borrowing / Worker | borrowing_db | borrowing_user | M1 |
| Notification | notification_db | notification_user | M3 |
| Recommendation | recommendation_db | recommendation_user | M3 |

Một PostgreSQL instance; mỗi database có owner/login riêng, PUBLIC bị revoke database privileges và schema privileges. Service chỉ nhận connection string của chính mình; không chia sẻ admin login. Worker cùng bounded context Borrowing nên được dùng borrowing_db. DB role bootstrap dùng env password ngẫu nhiên, không có password trong script SQL/source.

Quy tắc: UUID keys, UTC timestamps, local FK chỉ trong cùng database. Cross-service IDs chỉ logical references. Không shared DbContext/domain model; không cross-DB join/FK. Sprint 1: Identity/Borrowing có entities, constraints, repositories và migrations thật; Catalog, Inventory và Notification cũng có model/migrations thật; Recommendation vẫn scaffolded. Catalog InitialCatalog seed categories/equipment types; Inventory InitialInventory và AddStockMovements giữ constraints/locking và lịch sử trạng thái; Notification InitialNotification giữ inbox deduplication độc lập.

Identity migration InitialIdentity tạo ASP.NET Identity tables + RefreshTokens/UserAuditEvents, normalized email unique, refresh hash unique, family/expiry indexes, xmin refresh concurrency. Borrowing InitialBorrowing tạo LoanRequests/LoanItems/LoanHistories, local FKs, purpose/due/status checks, owner/status/time indexes và xmin. Development startup của hai host chạy MigrateAsync khi Database:ApplyMigrations=true; production mặc định false, chạy migration có kiểm soát bằng user DB riêng. Không EnsureCreated.

PostgreSQL integration dùng `infrastructure/docker/compose.tests.yml`, project name ngẫu nhiên và password tự sinh, không mount production volumes. Fixture chỉ tạo/xóa container của chính project test; không đọc .env để lấy credential. Test role identity_user/borrowing_user bị deny CONNECT sang DB khác. Không truyền connection string môi trường nhóm vào test fixture.

## Tạo migration khi đã có model thật

Ví dụ Catalog, chạy từ root với PostgreSQL dev ports đang bật:

```powershell
docker compose -f docker-compose.yml -f infrastructure/docker/compose.dev.yml up -d --wait postgres redis
. ./scripts/Set-LocalEnvironment.ps1 -Service catalog
dotnet tool restore
dotnet ef migrations add InitialCreate --project src/Services/Catalog/Catalog.Infrastructure --startup-project src/Services/Catalog/Catalog.Api --output-dir Migrations
dotnet ef database update --project src/Services/Catalog/Catalog.Infrastructure --startup-project src/Services/Catalog/Catalog.Api
```

Thay tên project/env tương ứng service; Inventory startup là `Inventory.Host`. Mỗi migration chỉ ghi DB của service. Không dùng `EnsureCreated` song song migrations. Sprint sau thêm migrations job/script idempotent chạy trước business app, seed lặp lại không phá dữ liệu; Catalog và Inventory tự gọi `Database.Migrate()` khi khởi động (idempotent, một instance); seed demo nằm trong migration với ID cố định. Inventory (`InitialInventory`) dùng `xmin` làm optimistic concurrency token, check constraint cho status và partial unique index (một reservation active cho mỗi unit/loan item, một maintenance mở cho mỗi unit) làm lưới an toàn thứ hai sau `FOR UPDATE SKIP LOCKED`. `AddStockMovements` thêm bảng append-only `StockMovements` (khoá `bigint` identity = thứ tự ghi, FK restrict tới `AssetUnits`): domain ghi movement ở mọi lần đổi status, riêng lệnh SQL reserve tự ghi trong cùng transaction; migration chèn một dòng baseline cho các unit đã có.

EF tool/EF Core ghim 8.0.34; provider Npgsql EF 8.0.11; Central Package Management pin transitive EF về cùng patch. Không nâng EF lên 9/10 để xử lý restore. Kiểm tra `dotnet list <project> package --include-transitive` khi đổi package.

## Init và reapply

`01-init-databases.sh` tự chạy **chỉ khi named volume PostgreSQL chưa được init**. Script kiểm tra tồn tại role/database trước khi tạo, không xóa database, cập nhật password/grants theo env hiện tại. Việc `.env` thay đổi không tự đổi password trong volume cũ. Muốn reapply có chủ đích:

```powershell
# Container cần nhận env mới trước; không xóa volume.
docker compose up -d postgres
docker compose exec -T postgres bash /docker-entrypoint-initdb.d/01-init-databases.sh
```

Giữ `.env` cũ nếu muốn giữ credentials. `POSTGRES_ADMIN_PASSWORD` cũng chỉ dùng bootstrap khi volume mới: đổi env không đổi password admin đã lưu; cập nhật admin credential thủ công qua phiên quản trị nếu cần. Không in secrets vào terminal, commit hoặc issue. Chạy kiểm tra quyền thực:

```powershell
docker compose cp infrastructure/database/verify-isolation.sh postgres:/tmp/lablend-verify-isolation.sh
docker compose exec -T postgres bash /tmp/lablend-verify-isolation.sh
```

## Dừng và reset development

`docker compose down` dừng/xóa container và network của project, **giữ data volumes**. Khởi động lại bằng `docker compose up -d --build --wait`. Không script nào tự xóa volume hoặc dữ liệu người dùng.

Nếu chính bạn đã xác nhận đây là dữ liệu development có thể bỏ, lệnh `docker compose down -v` xóa toàn bộ named volumes của project; lần up sau sẽ bootstrap lại. Không dùng lệnh này trên dữ liệu cần giữ, không gộp vào setup/preflight.

## Final additive migration

`20261116180731_AddLoanSubmissionIdempotency` adds `LoanSubmissions` to borrowing_db only. Composite `(StudentId,Key)` primary key, required bounded RequestHash, local LoanId FK with restrictive delete. Mapping/history/items/outbox share the submission transaction. API/worker reuse their migration advisory lock; no old migration is removed. Identity global filters and Notification owned aggregate filtering need no schema change. Fresh-install execution is tracked in final acceptance; `dotnet ef migrations has-pending-model-changes --configuration Release --no-build` verifies model consistency without touching a live database.
