# Nghiệm thu giao diện và phân quyền theo vai trò

- **Ngày:** 17/11/2026
- **Mã nguồn:** `main` tại `f85c357`
- **Nhánh:** `feature/role-based-ui-authorization`
- **Giai đoạn:** sau Phase A (rà soát). Chưa có thay đổi mã nguồn.

Ký hiệu trạng thái:
- **Đạt:** có bằng chứng tự động.
- **Một phần:** chỉ đúng một phần, hoặc thiếu bằng chứng cho một phần.
- **Chưa kiểm:** chưa chạy trong giai đoạn này.

Chi tiết quyền: [ROLE_PERMISSION_MATRIX](../security/ROLE_PERMISSION_MATRIX.md). Các vấn đề G1–G5: [AUTHORIZATION_AUDIT](../security/AUTHORIZATION_AUDIT.md).

## 1. Kịch bản chính

Kịch bản trình duyệt nằm ở `apps/web/e2e/m3-p0.mjs`, được chạy bởi `Member3BrowserTests`. Kịch bản dùng Identity, Catalog, Inventory, Borrowing, Notification, Worker, Postgres và Redis thật, không giả lập phản hồi nghiệp vụ.

| Kịch bản | Nội dung | Trạng thái | Bằng chứng |
|---|---|---|---|
| A. Sinh viên | Danh mục → chi tiết → đăng nhập quay lại đúng trang → tạo phiếu (bấm gửi hai lần chỉ tạo một phiếu) → phiếu của tôi → thông báo | Đạt | E2E `m3-p0` |
| B. Nhân viên | Duyệt → bàn giao → nhận trả từng thiết bị, có thiết bị hỏng chuyển sang bảo trì → hoàn tất bảo trì → xem lịch sử biến động | Đạt | E2E `m3-p0` |
| C. Quản trị viên | Tìm tài khoản → đổi vai trò → khoá/mở → bị chặn khi hạ quyền quản trị viên cuối | Một phần | E2E `m3-p0` đổi vai trò trên giao diện. Việc quyền mới có hiệu lực mới được kiểm ở mức API (`MemberIntegrationTests`), chưa đăng nhập lại bằng trình duyệt với vai trò mới |
| D. Truy cập trái phép | Sinh viên gõ URL trang nhân viên/quản trị và gọi thẳng API | Một phần | Trang 403 cho `/staff/loans` và `/admin/users` (E2E); API trả 403 (`BorrowingTests`, `InventoryTests`, `IdentityTests`, `MemberIntegrationTests`). Chưa thử `/staff/inventory` (G4) |

Lần chạy E2E đạt gần nhất là ngày 17/11/2026 trên nhánh `feature/web-redesign`, có cùng nội dung `apps/web` với `f85c357`. Chưa chạy lại trên chính `f85c357`.

## 2. Tiêu chí hoàn thành (mục XVI)

| Tiêu chí | Trạng thái | Ghi chú |
|---|---|---|
| 3 vai trò có điều hướng phù hợp | Một phần | Menu đúng theo vai trò. Chưa có trang tổng quan riêng cho sinh viên và quản trị viên |
| Khách chỉ vào được trang và API công khai | Đạt | Các test 401 và trang đăng nhập |
| Sinh viên chỉ thao tác phiếu của mình | Đạt | `BorrowingTests`, `GatewayFlowTests` |
| Nhân viên quản lý mượn/trả và thiết bị đúng quyền | Đạt | `BorrowingTests`, `InventoryTests`, `CatalogApiTests`, E2E |
| Quản trị viên quản lý tài khoản và dùng quyền nhân viên | Đạt | `IdentityTests`, E2E. Luồng duyệt chủ yếu được chạy bằng tài khoản LAB_STAFF |
| Có ProtectedRoute, RoleRoute và kiểm quyền theo nút | Một phần | Đã có ProtectedRoute và RoleRoute. Chưa có bảng quyền dùng chung (G3) |
| Backend tự kiểm quyền độc lập | Đạt | Đã rà soát toàn bộ controller |
| Không có lỗ hổng xem dữ liệu người khác hay leo thang quyền | Đạt | Đúng trong các ca đã kiểm |
| Làm mới phiên và đăng xuất hoạt động đúng | Đạt | `IdentityTests` T22, `api.test.mjs` |
| Đổi vai trò được xử lý theo thời hạn JWT | Đạt | Theo OD-06: Identity từ chối ngay; dịch vụ khác tối đa 10 phút 30 giây |
| Giao diện xử lý đúng 401/403/404/409 | Một phần | Thiếu thông báo cho 429 (G5) |
| Giao diện co giãn được | Đạt | E2E ở độ rộng 390px không bị tràn ngang |
| Không còn menu hay nút sai vai trò | Một phần | Đúng khi đọc mã nguồn. Chưa có test tự động riêng (G3, G4) |
| Không có số liệu giả | Một phần | Không có dữ liệu giả, nhưng hai số liệu chỉ tính trên 100 mục đầu (G1) |
| Backend build Release | Đạt | Ngày 17/11/2026 |
| Frontend build và test | Đạt | `npm run build` đạt; `npm test` 25/25 |
| Test phân quyền | Một phần | 34/34 đạt, nhưng còn thiếu vài ô (G2) |
| E2E trình duyệt | Chưa kiểm | Chưa chạy lại trên `f85c357` |
| Docker chạy thật | Chưa kiểm | Lần đạt gần nhất ghi ở `FINAL_PRODUCT_ACCEPTANCE.md` |
| Test Borrowing/gRPC/Redis/Worker không bị hỏng | Chưa kiểm | Giai đoạn này chỉ chạy nhóm test phân quyền. Mã nguồn chưa đổi nên chưa có rủi ro hồi quy |
| Có tài liệu ma trận quyền và bằng chứng | Đạt | Tài liệu này và hai tài liệu trong `docs/security` |
| Không phát sinh chi phí | Đạt | Không dùng dịch vụ trả phí |

## 3. Ảnh chụp

Ảnh trong `docs/testing/evidence/m3-*.png` được chụp trước đợt làm lại giao diện, nên chưa phản ánh giao diện hiện tại. Cần chụp lại khi chạy E2E trên commit cuối.

## 4. Cách chạy lại

```bash
dotnet build LabLendAI.sln -c Release
```

```bash
LABLEND_POSTGRES_TESTS=1 dotnet test LabLendAI.sln -c Release --no-build --filter "FullyQualifiedName~CatalogApiTests|FullyQualifiedName~InventoryTests|FullyQualifiedName~GatewayTests|FullyQualifiedName~JwtSecurityTests|FullyQualifiedName~BorrowingTests|FullyQualifiedName~IdentityTests|FullyQualifiedName~GatewayFlowTests|FullyQualifiedName~MemberIntegrationTests|FullyQualifiedName~FinalWorkflowTests|FullyQualifiedName~NotificationServiceTests"
```

```bash
LABLEND_POSTGRES_TESTS=1 LABLEND_BROWSER_TESTS=1 LABLEND_BROWSER_CHANNEL=msedge dotnet test tests/Integration/LabLend.Sprint1.IntegrationTests -c Release --no-build --filter "FullyQualifiedName~Member3BrowserTests"
```

```bash
npm --prefix apps/web test
```
