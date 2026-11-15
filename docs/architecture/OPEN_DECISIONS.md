# Open decisions

Tất cả mục dưới đây **OPEN / PROPOSAL**, chưa thay thế review của người sở hữu. Sửa trạng thái và ghi người review/PR trước khi producer/consumer hoặc client/server được triển khai.

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

Đã chọn cho Sprint 0: .NET/EF Core 8; sáu DB/sáu login; PascalCase .NET folders; apps/web; một Dockerfile kỹ thuật dùng build args; core default + bonus profile; health không phải business acceptance. Những lựa chọn này không chốt business schema thay M2/M3.
