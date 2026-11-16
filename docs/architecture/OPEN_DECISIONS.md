# Open decisions

Các mục còn OPEN/PROPOSAL chưa thay thế review của người sở hữu. OD-06/OD-08 đã có implementation defaults được chủ repository chấp thuận trong phiên Sprint 1; review consumer M2/M3 vẫn pending. Không ghi là hai thành viên đã review khi chưa có bằng chứng.

| ID | Quyết định cần chốt | Owner và reviewers | Điều kiện đóng |
|---|---|---|---|
| OD-01 | Inventory proto v1: UUID format, LoanItem 1 unit, quantity representation, availability scope | M2 owner; M1 consumer | Proto diff + contract tests được hai bên review |
| OD-02 | Reserve all-or-nothing, operationId/replay conflict, operation status, hold expiry, compensation | M2 + M1 | Semantics khớp APPROVING/reconciliation và T07–T09/T28 |
| OD-03 | gRPC service auth: reserved env credential vs short-lived service JWT; health allowance | M2 + M1 | Business RPC auth tests T27; không dùng user role như service credential |
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
