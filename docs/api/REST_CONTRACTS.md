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
| Inventory Admin / M2 | GET/POST /api/inventory/assets; GET/PUT/DELETE /api/inventory/assets/{id}; POST /api/inventory/assets/{id}/maintenance; availability read | OD-01–03; serial unique, deactivate policy, Staff/Admin writes |
| Borrowing / M1 | POST /api/loans; GET /api/loans/my; GET /api/loans/{id}; staff list; POST .../{id}/cancel,/reject,/approve,/handover,/returns; GET .../{id}/timeline | OD-08; owner from sub; PENDING→APPROVING→APPROVED; approval calls gRPC, T05–T11/T28 |
| Notification / M3 | GET /api/notifications; PATCH /api/notifications/{id}/read; GET /api/notifications/unread-count | OD-04–05; owner from sub; inbox dedupe; T12–T18 |
| Recommendation / M3 | POST /api/recommendations; GET /api/recommendations/history | Bonus only; OD-09; 503 AI_UNAVAILABLE / 502 AI_INVALID_RESPONSE; T19/T25/T26 |

Sprint 1: Identity và Borrowing Core đã có Controllers/OpenAPI thật. Hợp đồng hiện hành: [Identity](IDENTITY_API_CONTRACT.md), [Borrowing](BORROWING_API_CONTRACT.md). Các route approve/handover/returns/timeline/dashboard và các module M2/M3 trong bảng vẫn Planned. Catalog validation của create loan chưa nghiệm thu với Catalog thật. Không dùng HTTP 200 giả để thay CRUD.

Swagger ở mỗi REST host: `/swagger`; schema `/swagger/v1/swagger.json`. Gateway allowlist xuất schema qua `/platform/<service>/openapi.json`. Health endpoints không được coi là CRUD operations. Khi thêm controller thực, viết response/status documentation và authorize ở chính service.
