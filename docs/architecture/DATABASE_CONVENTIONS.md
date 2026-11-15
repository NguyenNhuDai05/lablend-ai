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

Quy tắc: UUID keys, UTC timestamps, local FK chỉ trong cùng database. Cross-service IDs chỉ logical references. Không shared DbContext/domain model; không cross-DB join/FK. Model thật phải có constraints/indexes/idempotency theo use case, không chỉ kiểm tra ở UI. Catalog đã có model + migration `InitialCatalog` + seed (**Implemented**); các context khác vẫn **Scaffolded**.

## Tạo migration khi đã có model thật

Ví dụ Catalog, chạy từ root với PostgreSQL dev ports đang bật:

```powershell
docker compose -f docker-compose.yml -f infrastructure/docker/compose.dev.yml up -d --wait postgres redis
. ./scripts/Set-LocalEnvironment.ps1 -Service catalog
dotnet tool restore
dotnet ef migrations add InitialCreate --project src/Services/Catalog/Catalog.Infrastructure --startup-project src/Services/Catalog/Catalog.Api --output-dir Migrations
dotnet ef database update --project src/Services/Catalog/Catalog.Infrastructure --startup-project src/Services/Catalog/Catalog.Api
```

Thay tên project/env tương ứng service; Inventory startup là `Inventory.Host`. Mỗi migration chỉ ghi DB của service. Không dùng `EnsureCreated` song song migrations. Sprint sau thêm migrations job/script idempotent chạy trước business app, seed lặp lại không phá dữ liệu; Catalog tự gọi `Database.Migrate()` khi khởi động (idempotent, một instance); seed demo nằm trong migration với ID cố định.

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
