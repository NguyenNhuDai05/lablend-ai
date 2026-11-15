# REST standards and proposed service contracts

## Chuẩn kỹ thuật đã wiring

- Resource paths: `/api/equipment-types`, danh từ số nhiều, kebab-case; UUID trong path.
- JSON camelCase, timestamps ISO-8601 UTC. Tất cả IDs cross-service là logical UUID, không database FK.
- 200 đọc/cập nhật có body; 201 tạo kèm Location; 204 delete hoặc command không body; 400 validation; 401 token thiếu/sai/hết hạn; 403 sai quyền; 404 resource không thuộc người dùng hoặc không có; 409 state/stock/idempotency conflict; 429 throttling; 502/503 dependency failure.
- Lỗi dùng `application/problem+json`: status/title, `code`, `correlationId`; validation có `errors`. Không trả stack trace/key/connection string.
- X-Correlation-ID: tối đa 64 ASCII chữ/số hoặc `-_.`; invalid/missing được thay bằng UUID mới. Gateway chuyển ID tới upstream. HttpClient/gRPC/event producer tương lai phải propagate ID này; không log tokens/password/prompt chứa PII.
- JWT validation có issuer/audience/lifetime/signature, claim `sub` là user UUID, `role` dùng STUDENT/LAB_STAFF/ADMIN. Chưa có issuance/login ở Sprint 0. `[Authorize]`, `StaffOrAdmin`, `Admin` là policies đã wiring; service vẫn phải kiểm tra owner/state.
- Gateway public prefixes không thay authorization ở Controllers. `/api/auth/*` chứa login/register public; `/api/users/*` phải auth. Catalog read public, mutations require Staff/Admin. Inventory Admin mutations require Staff/Admin. Hành vi cụ thể chờ owner implement/review.
- `[ApiController]` + DTO validation chạy ở transport; Application kiểm tra business invariants. Repository không quyết định permission.

## Proposal cần M2/M3 review

Pagination: `items`, `total`, `page` (1-based), `pageSize`. DTO kỹ thuật tại Contracts/PagedResponse.cs. Đề xuất default pageSize=10, max=100; reject page<1/size ngoài giới hạn; whitelist sortBy/direction. Không nối input trực tiếp vào SQL. Exact filters/response fields chờ OD-07.

| Service / owner | Planned routes | Review/acceptance |
|---|---|---|
| Identity / M1 | POST /api/auth/register, /login, /refresh, /logout; GET /api/auth/me; GET/PUT /api/users/me; admin users/roles | OD-06; password hashing, refresh hash/rotation, T01–T03/T22 |
| Catalog / M2 | CRUD /api/categories và /api/equipment-types; GET equipment-types?keyword=&categoryId=&sortBy=&direction=&page=&pageSize= | OD-07; real DB + search/filter/sort/page, T04 |
| Inventory Admin / M2 | Implemented — xem Inventory v1 bên dưới | OD-01–03 chờ M1 review |
| Borrowing / M1 | POST /api/loans; GET /api/loans/my; GET /api/loans/{id}; staff list; POST .../{id}/cancel,/reject,/approve,/handover,/returns; GET .../{id}/timeline | OD-08; owner from sub; PENDING→APPROVING→APPROVED; approval calls gRPC, T05–T11/T28 |
| Notification / M3 | GET /api/notifications; PATCH /api/notifications/{id}/read; GET /api/notifications/unread-count | OD-04–05; owner from sub; inbox dedupe; T12–T18 |
| Recommendation / M3 | POST /api/recommendations; GET /api/recommendations/history | Bonus only; OD-09; 503 AI_UNAVAILABLE / 502 AI_INVALID_RESPONSE; T19/T25/T26 |

## Catalog v1 (Implemented, M2, chờ M3 review OD-07)

| Method / route | Auth | Kết quả |
|---|---|---|
| GET /api/categories?includeInactive= | Public | 200 `CategoryResponse[]` theo tên; `includeInactive` chỉ có hiệu lực với LAB_STAFF/ADMIN |
| GET /api/categories/{id} | Public | 200 / 404 |
| POST /api/categories | StaffOrAdmin | 201 + Location; 400; 409 trùng tên |
| PUT /api/categories/{id} | StaffOrAdmin | 200; 404; 409 trùng tên hoặc tắt category còn equipment type active |
| DELETE /api/categories/{id} | StaffOrAdmin | 204 soft deactivate (idempotent); 409 nếu còn equipment type active |
| GET /api/equipment-types?keyword=&categoryId=&tag=&includeInactive=&sortBy=name\|createdAt&direction=asc\|desc&page=1&pageSize=10 | Public | 200 `PagedResponse<EquipmentTypeResponse>`; 400 nếu sortBy/direction ngoài whitelist, page<1, pageSize ngoài 1–100 |
| GET /api/equipment-types/{id} | Public | 200 (kể cả đã deactivate, để loan cũ vẫn tra được) / 404 |
| POST /api/equipment-types | StaffOrAdmin | 201; 400 category không tồn tại/inactive; 409 trùng tên trong cùng category |
| PUT /api/equipment-types/{id} | StaffOrAdmin | 200; 400; 404; 409 |
| DELETE /api/equipment-types/{id} | StaffOrAdmin | 204 soft deactivate (idempotent) |

`keyword` tìm ILIKE trên name/description; `tag` so khớp chính xác sau khi chuẩn hóa lowercase. Tags được trim/lowercase/distinct khi lưu. Body mẫu:

```json
{ "categoryId": "c0000000-0000-0000-0000-000000000001", "name": "Máy hiện sóng số 100MHz",
  "description": "...", "specifications": "2 kênh; 100 MHz", "tags": ["oscilloscope"], "imageUrl": null }
```

PUT nhận thêm `isActive`. Seed qua migration: 3 categories `c0000000-…-00000000000{1..3}` và 5 equipment types `e0000000-…-00000000000{1..5}`, ID cố định để Inventory seed asset theo logical reference. Migrations tự áp dụng khi Catalog khởi động.

## Inventory v1 (Implemented, M2)

| Method / route | Auth | Kết quả |
|---|---|---|
| GET /api/inventory/availability?equipmentTypeIds=…&equipmentTypeIds=… | Public | 200 `[{equipmentTypeId, available, total}]`, `total` loại RETIRED/LOST; 400 nếu ngoài 1–100 ids |
| GET /api/inventory/assets?equipmentTypeId=&status=&keyword=&sortBy=serialNumber\|createdAt\|status&direction=&page=1&pageSize=20 | StaffOrAdmin | 200 `PagedResponse<AssetResponse>`; `keyword` ILIKE serial/location; 400 status/sort ngoài whitelist |
| GET /api/inventory/assets/{id} | StaffOrAdmin | 200 / 404 |
| POST /api/inventory/assets | StaffOrAdmin | 201 + Location; serial trim + upper-case; 409 trùng serial |
| PUT /api/inventory/assets/{id} | StaffOrAdmin | 200; 404; 409 trùng serial hoặc đổi type khi RESERVED/ON_LOAN |
| DELETE /api/inventory/assets/{id} | StaffOrAdmin | 204 retire (soft, idempotent); 409 khi RESERVED/ON_LOAN |
| GET /api/inventory/assets/{id}/maintenance | StaffOrAdmin | 200 lịch sử, mới nhất trước |
| POST /api/inventory/assets/{id}/maintenance `{reason}` | StaffOrAdmin | 201; 409 nếu unit không AVAILABLE |
| POST /api/inventory/assets/{id}/maintenance/complete `{notes}` | StaffOrAdmin | 200 `AssetResponse` (AVAILABLE); 409 nếu không trong MAINTENANCE |
| GET /api/inventory/assets/{id}/movements | StaffOrAdmin | 200 `[{id, assetUnitId, fromStatus, toStatus, loanId, changedBy, occurredAt}]` mới nhất trước; 404 |

Asset status: AVAILABLE, RESERVED, ON_LOAN, MAINTENANCE, LOST, RETIRED. Mỗi lần đổi status ghi một dòng StockMovements trong cùng transaction: `fromStatus` null = trạng thái đầu tiên (tạo mới, hoặc baseline khi migration chạy); thay đổi qua REST có `changedBy` (JWT `sub`), thay đổi qua gRPC có `loanId`. Replay gRPC và retire lặp lại không ghi thêm dòng. Reserve/handover/return **không** có REST: chỉ Borrowing gọi qua gRPC ([guide](../grpc/INVENTORY_CONTRACT_GUIDE.md)). Seed 13 units `a0000000-…-0000000000NN` cho 5 equipment types của Catalog; `e…04` (kính hiển vi) chỉ có 1 unit để demo tranh chấp. Lỗi nghiệp vụ trả `code` như `CONFLICT`, `NOT_FOUND`.

**Các route khác trong bảng là Planned.** Gateway routing config chỉ chuẩn bị prefixes. Request tới một business path có thể trả 401 hoặc 404; không có mock HTTP 200 làm giả CRUD.

Swagger ở mỗi REST host: `/swagger`; schema `/swagger/v1/swagger.json`. Gateway allowlist xuất schema qua `/platform/<service>/openapi.json`. Health endpoints không được coi là CRUD operations. Khi thêm controller thực, viết response/status documentation và authorize ở chính service.
