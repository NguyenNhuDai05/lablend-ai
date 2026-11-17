# Rà soát phân quyền (Phase A)

- **Ngày:** 17/11/2026
- **Mã nguồn:** `main` tại `f85c357`
- **Nhánh:** `feature/role-based-ui-authorization`
- **Phạm vi:** quyền của Khách, STUDENT, LAB_STAFF và ADMIN ở định tuyến React, menu và nút, Gateway, controller, tầng nghiệp vụ và gRPC.
- **Ngoài phạm vi:** dịch vụ gợi ý (Gemini, OD-09).

Ma trận quyền hiện hành: [ROLE_PERMISSION_MATRIX](ROLE_PERMISSION_MATRIX.md).

## 1. Kết luận

- **Không phát hiện lỗ hổng truy cập dữ liệu người khác hay leo thang quyền.**
- Mọi thao tác ghi đều được dịch vụ tự kiểm quyền, không dựa vào Gateway hay việc ẩn nút.
- Chủ sở hữu dữ liệu luôn được xác định từ JWT.
- Còn 1 vấn đề mức Trung bình (số liệu tổng quan) và 4 vấn đề mức Thấp (mục 3).

## 2. Đối chiếu theo vai trò

Cột "Kiểm thử" ghi lớp test làm bằng chứng; "—" là chưa có.

| Vai trò | Thao tác | Kết quả mong đợi | Kiểm thử |
|---|---|---|---|
| Khách | Xem danh mục, tình trạng sẵn có | 200 | `CatalogApiTests`, `InventoryTests` |
| Khách | Gửi `includeInactive=true` | Bị bỏ qua | `CatalogApiTests` |
| Khách | Gọi API cần đăng nhập | 401 | `BorrowingTests`, `GatewayFlowTests`, `GatewayTests`, `InventoryTests`, `CatalogApiTests` |
| Khách | JWT giả: sai issuer, audience, chữ ký, hết hạn, `sub` | 401 tại Gateway | `JwtSecurityTests` |
| STUDENT | Đăng ký kèm `role=ADMIN` | Vẫn là STUDENT | `IdentityTests` |
| STUDENT | Tạo, xem, huỷ phiếu của mình | 201/200 | `BorrowingTests`, E2E `m3-p0` |
| STUDENT | Xem, huỷ phiếu người khác | 404 | `BorrowingTests`, `GatewayFlowTests` |
| STUDENT | Xem hàng đợi, từ chối phiếu | 403 | `BorrowingTests` |
| STUDENT | Duyệt, bàn giao, nhận trả | 403 | — (**G2**) |
| STUDENT | Ghi danh mục/kho, xem kho, xem lịch sử biến động | 403 | `MemberIntegrationTests`, `CatalogApiTests`, `InventoryTests` |
| STUDENT | Quản lý tài khoản | 403 | `IdentityTests`, `GatewayFlowTests` |
| STUDENT | Gõ `/staff/loans`, `/admin/users` | Trang 403 | E2E `m3-p0` |
| STUDENT | Gõ `/staff/inventory` | Trang 403 | — (**G4**) |
| STUDENT | Thông báo của người khác | 404 | `FinalWorkflowTests`, `NotificationServiceTests` |
| LAB_STAFF | Hàng đợi, duyệt, bàn giao, nhận trả từng thiết bị | 200 | `BorrowingTests`, `Sprint2LifecycleTests`, E2E `m3-p0` |
| LAB_STAFF | Quản lý danh mục, kho, bảo trì | 200 | `CatalogApiTests`, `InventoryTests`, `MemberIntegrationTests`, E2E `m3-p0` |
| LAB_STAFF | Tạo hoặc huỷ phiếu | 403 | — (**G2**) |
| LAB_STAFF | Xem danh sách tài khoản | 403 | `GatewayFlowTests` |
| LAB_STAFF | Tự đổi vai trò | 403 | — (**G2**) |
| LAB_STAFF | Gõ `/admin/users` | Trang 403 | — (**G4**) |
| ADMIN | Tìm, lọc tài khoản trên toàn bộ dữ liệu | 200 | `IdentityTests`, `FinalWorkflowTests`, E2E `m3-p0` |
| ADMIN | Đổi vai trò, khoá/mở; bảo vệ quản trị viên cuối | 200 / 409 | `IdentityTests`, `MemberIntegrationTests`, E2E `m3-p0` |
| ADMIN | Tạo phiếu | 403 | — (**G2**) |
| Dịch vụ | Gọi gRPC kho khi không có khoá dịch vụ | Bị từ chối | `InventoryTests` |
| Dịch vụ | CORS chỉ cho đúng origin; CSRF ở các lệnh dùng cookie | Đúng | `GatewayTests`, `IdentityTests` |
| Dịch vụ | Làm mới token xoay vòng, phát hiện dùng lại, gọi đồng thời, đăng xuất | Đúng | `IdentityTests` |
| Trình duyệt | Không lưu token trong storage | Đúng | E2E `m3-p0` |

## 3. Vấn đề phát hiện

| ID | Mức | Vấn đề | Hướng xử lý | Cần ai |
|---|---|---|---|---|
| G1 | Trung bình | Số "Quá hạn" ở hàng đợi chỉ đếm trong 100 phiếu đang mượn đầu tiên. Số liệu kho trang chủ chỉ tính trên 100 loại thiết bị đầu. Dữ liệu demo đúng, nhưng sai khi vượt 100 (`features/loans/api.ts`, `pages/HomePage.tsx`). | Thêm bộ lọc quá hạn cho `GET /api/loans` và một API tổng kho; tạm thời ghi rõ giới hạn trên giao diện | M1 (Borrowing), M2 (Inventory) |
| G2 | Thấp | Code đã chặn đúng nhưng thiếu test cho: STUDENT duyệt/bàn giao/nhận trả; LAB_STAFF và ADMIN tạo/huỷ phiếu; LAB_STAFF tự đổi vai trò. | Thêm một test chạy toàn bộ bảng vai trò × đường dẫn qua Gateway thật | M1 |
| G3 | Thấp | Giao diện kiểm vai trò ở 5 nơi khác nhau, không có bảng quyền dùng chung nên khó test và dễ lệch. | Thêm hàm `can(user, quyền)` có test, thay các chỗ so sánh trực tiếp | M3 |
| G4 | Thấp | E2E chưa thử STUDENT gõ `/staff/inventory` và LAB_STAFF gõ `/admin/users`. | Bổ sung vào kịch bản trình duyệt | M3 |
| G5 | Thấp | Chưa có thông báo tiếng Việt cho lỗi 429. | Thêm một dòng vào bảng thông báo lỗi | M3 |

**Hành vi có chủ đích (không phải lỗi):**
- Sau khi bị khoá hoặc đổi vai trò, JWT cũ còn dùng được ở các dịch vụ ngoài Identity tối đa 10 phút 30 giây (OD-06). Giao diện chỉ thấy vai trò mới sau khi tải lại trang hoặc phiên bị từ chối.
- Xem theo mã một danh mục hoặc loại thiết bị đã ngừng vẫn trả dữ liệu, vì phiếu cũ cần hiện tên thiết bị và Borrowing cần đọc `isActive`.
- `GET /api/loans/my` trả danh sách rỗng cho nhân viên, vì chỉ sinh viên tạo được phiếu.
- Đăng xuất ở một tab không tự đăng xuất các tab khác. Tab đó sẽ hết phiên khi token hết hạn.
- Quy tắc "một tài khoản một vai trò" chưa được ghi trong hợp đồng API của Identity; cần M1 xác nhận rồi bổ sung.

## 4. Kết quả kiểm thử

Chạy ngày 17/11/2026 trên `f85c357`, chưa sửa mã nguồn, dùng Postgres thật (`LABLEND_POSTGRES_TESTS=1`).

| Nhóm | Số test | Kết quả |
|---|---|---|
| Build Release | — | Đạt |
| `LabLend.Platform.UnitTests` (thông báo) | 2 | Đạt |
| `LabLend.Platform.IntegrationTests` (Gateway, JWT) | 10 | Đạt |
| `Catalog.IntegrationTests` | 3 | Đạt |
| `Inventory.IntegrationTests` | 8 | Đạt |
| `LabLend.Sprint1.IntegrationTests` (Identity, Borrowing, Gateway, liên dịch vụ) | 11 | Đạt |
| Frontend `npm test` | 25 | Đạt |

Tổng: 34/34 test backend và 25/25 test frontend đạt, không test nào bị bỏ qua. Lỗi `duplicate key … IX_Categories_Name` trong log là ca kiểm trùng tên có chủ đích.

## 5. Chưa kiểm chứng

- Chưa dựng Docker trong phase này. Lần Docker đạt gần nhất được ghi ở `docs/testing/FINAL_PRODUCT_ACCEPTANCE.md`, trên một commit trước `f85c357`.
- Chưa chạy lại E2E trình duyệt trên `f85c357`. Lần đạt gần nhất là trên nhánh `feature/web-redesign`, có cùng nội dung `apps/web`.
- Chưa thử trên trình duyệt trường hợp ADMIN tự đổi vai trò của chính mình.
