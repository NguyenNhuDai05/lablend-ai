# Open decisions

M3 P0 technical review (2026-11-16): [producer schema, mapper compatibility, retry/parking and retention audit](../events/M3_CONTRACT_REVIEW.md) added. OD-04/05 remain OPEN; no human owner signoff is asserted. Inventory v1 historical-return/reservation-ID gap below remains OPEN.

## Sprint 2 M1 consumer audit (2026-11-16)

OD-01–03 reviewed by the M1 implementation against the unchanged v1 proto/server: UUIDs, one item/unit, atomic reserve, stable operation ID and same-payload replay, GetOperationStatus, explicit HELD release, service Bearer metadata and a 5-second deadline are the consumer defaults. This records consumer implementation review, not an invented M2 approval of new contract changes.

OD-04/05 remain **PROVISIONAL / M3 review pending**: the producer will use the existing consumer's camelCase v1 envelope, `recipientUserId/title/message/loanId` payload, `lablend.events.v1` stream and `event` field. Real compatibility/crash/replay tests are required; tests do not close team review.

v1 gap for M2 review: allocations omit reservation ID and historical return condition/time. Borrowing leaves ReservationId null and persists immutable staff return intent before RPC. Current asset status is not historical return evidence after another borrower or maintenance changes it. Recovery assumes reservation mutations originate from Borrowing; external command writers cannot be safely reconciled without additional contract evidence. No proto or M2 implementation change is made here.

Các mục còn OPEN/PROPOSAL chưa thay thế review của người sở hữu. OD-06/OD-08 đã có implementation defaults được chủ repository chấp thuận trong phiên Sprint 1; review consumer M2/M3 vẫn pending. Không ghi là hai thành viên đã review khi chưa có bằng chứng.

| ID | Quyết định cần chốt | Owner và reviewers | Điều kiện đóng |
|---|---|---|---|
| OD-01 | Inventory proto v1: UUID format, LoanItem 1 unit, quantity representation, availability scope | M2 owner; M1 consumer | **PROPOSED + implemented (M2), chờ M1 review**: UUID string, 1 LoanItem = 1 unit, quantity N = N items, availability loại RETIRED/LOST |
| OD-02 | Reserve all-or-nothing, operationId/replay conflict, operation status, hold expiry, compensation | M2 + M1 | **PROPOSED + implemented (M2), chờ M1 review**: SKIP LOCKED all-or-nothing, SHA-256 payload hash → ALREADY_EXISTS, GetOperationStatus, không auto-expiry (Worker gọi Release); T28 còn lại |
| OD-03 | gRPC service auth: reserved env credential vs short-lived service JWT; health allowance | M2 + M1 | **PROPOSED + implemented (M2), chờ M1 review**: static `INVENTORY_SERVICE_TOKEN` Bearer, health anonymous; T27 pass |
| OD-04 | Event envelope casing/time format/type names, payload cho 7 loan events | M1 producer + M3 consumer | JSON examples/schema và versioning review hai bên |
| OD-05 | Stream fields, group, pending/retry/XAUTOCLAIM, max attempts/parking stream | M1 + M3 | Crash replay/duplicate/Redis-down test plan thống nhất |
| OD-06 | Auth response, refresh transport, logout/revoke, frontend token handling | M1 + M3 | UX/auth contract và CORS/cookie/CSRF policy được thống nhất |
| OD-07 | Pagination/filter/sort DTOs và catalog shape | M2 owner; M3 frontend; M1 conventions | M2/M3 review API examples và errors |
| OD-08 | Loan constraints/due dates/item returns and timeline response | M1 owner; M2/M3 reviewers | State/DTO review trước UI + schema migrations |
| OD-09 | Gemini API model Free Tier, data consent/minimization và network egress riêng | M3 owner; M1 review | Chỉ sau P0; không billing; disabled khi key/quota không khả dụng; không mở egress cho toàn core |
| OD-10 | GitHub handles M2/M3 và optional CODEOWNERS/branch protection | Chủ repo | Cung cấp handle và thống nhất review workflow; chưa tự cấp quyền/settings |

## Sprint 1 implemented defaults pending consumer review

- **OD-06 IMPLEMENTED / owner accepted / M3 review pending:** access JWT 10 phút trong memory; refresh 7 ngày qua HttpOnly SameSite=Strict cookie /api/auth; Secure ngoài Development. Origin exact allowlist + X-LabLend-CSRF:1 bắt buộc cho register/login/refresh/logout; Gateway credentialed CORS. Reuse thu hồi family; SPA phải serialize refresh. Logout/password/role/deactivation revoke refresh; JWT stateless ở service khác tồn tại tối đa expiry + 30s skew. Identity kiểm tra account active và role hiện tại. Chi tiết [IDENTITY_API_CONTRACT](../api/IDENTITY_API_CONTRACT.md).
- **OD-08 IMPLEMENTED / owner accepted / M2/M3 review pending:** purpose trimmed 10–2000; dueAt > now và <=30 ngày; 1–20 items, mỗi item 1 unit; lặp EquipmentTypeId để mượn nhiều unit. Chỉ PENDING→CANCELLED/REJECTED; cancel lặp 200/no duplicate history, reject lặp 409; non-owner 404; OVERDUE derived. [BORROWING_API_CONTRACT](../api/BORROWING_API_CONTRACT.md). Catalog required GET /api/equipment-types/{id} với id/isActive là proposal cho M2; positive integration thật BLOCKED khi skeleton chưa có endpoint.

Không đóng OD-01–05/07/09/10 và không thay proto/event/schema thuộc thành viên khác. Việc triển khai defaults OD-06/08 trước consumer review được chủ repo cho phép trong phiên này; review vẫn là điều kiện tích hợp frontend/Catalog chính thức.

Đã chọn cho Sprint 0: .NET/EF Core 8; sáu DB/sáu login; PascalCase .NET folders; apps/web; một Dockerfile kỹ thuật dùng build args; core default + bonus profile; health không phải business acceptance. Những lựa chọn này không chốt business schema thay M2/M3.
