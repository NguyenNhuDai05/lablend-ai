# Bàn giao: giao diện và phân quyền theo vai trò

- **Ngày:** 17/11/2026
- **Người thực hiện:** M2
- **Nhánh:** `feature/role-based-ui-authorization`, tách từ `main` tại `f85c357`
- **Trạng thái:** xong Phase A (rà soát). Phase B–F chưa bắt đầu.

## 1. Đã làm

- Rà soát quyền ở toàn bộ controller của 5 dịch vụ, Gateway, gRPC kho, phiên đăng nhập và giao diện React.
- Chạy lại nhóm test phân quyền trên Postgres thật: backend 34/34 đạt, frontend 25/25 đạt, build Release và `npm run build` đạt.
- **Không sửa mã nguồn.** Nhánh này chỉ thêm 4 tài liệu:

| Tài liệu | Nội dung |
|---|---|
| [ROLE_PERMISSION_MATRIX](../security/ROLE_PERMISSION_MATRIX.md) | Quyền hiện hành theo vai trò, API, trang, menu, phiên đăng nhập, mã lỗi |
| [AUTHORIZATION_AUDIT](../security/AUTHORIZATION_AUDIT.md) | Kết quả rà soát, đối chiếu test, vấn đề G1–G5, kết quả chạy test |
| [ROLE_BASED_UI_ACCEPTANCE](../testing/ROLE_BASED_UI_ACCEPTANCE.md) | Trạng thái kịch bản A–D và tiêu chí hoàn thành |
| Tài liệu này | Bàn giao và việc tiếp theo |

## 2. Kết luận chính

- Không có lỗ hổng xem dữ liệu người khác hay leo thang quyền.
- Backend tự kiểm quyền ở mọi thao tác ghi, không phụ thuộc giao diện.
- Phần còn thiếu chủ yếu là test bổ sung, cách tổ chức mã giao diện, và hai số liệu tổng quan chỉ tính trên 100 mục đầu.

## 3. Việc tiếp theo

| Phase | Việc | Vấn đề | Người sở hữu | Cần duyệt |
|---|---|---|---|---|
| B | Hàm `can(user, quyền)` dùng chung có test; thêm thông báo lỗi 429 | G3, G5 | M3 | M3 |
| C, D | Bộ lọc quá hạn cho `GET /api/loans` và API tổng kho, hoặc ghi rõ giới hạn trên giao diện | G1 | M1, M2 | M1, M2, M3 |
| C, E | Trang tổng quan riêng cho sinh viên và quản trị viên (chỉ dùng dữ liệu thật) | — | M3 | Cả nhóm |
| E | Tải lại thông tin người dùng sau khi quản trị viên đổi vai trò hoặc khoá chính mình | — | M3 | M1 |
| F | Test toàn bộ bảng vai trò × đường dẫn qua Gateway thật | G2 | M1 | M1 |
| F | Mở rộng E2E: gõ URL trái phép, đăng nhập lại sau khi đổi vai trò, chụp lại ảnh | G4 | M3 | M3 |
| F | Chạy lại toàn bộ test, E2E và Docker trên commit cuối | — | Cả nhóm | — |

## 4. Cần nhóm quyết định

1. **Một tài khoản một vai trò:** xác nhận quy tắc này và ghi vào `IDENTITY_API_CONTRACT.md` (M1).
2. **Xử lý G1:** thêm API phía server, hay chấp nhận giới hạn 100 mục và ghi rõ trên giao diện.
3. **Có cần đồng bộ đăng xuất giữa các tab không:** hiện không có, và đây là hành vi đã ghi nhận.
4. **Ai làm Phase B–F:** phần lớn thay đổi nằm ở phạm vi của M1 (Identity, Borrowing) và M3 (giao diện, phiên đăng nhập), không phải M2.

## 5. Lưu ý quy trình

- Theo mục XV của yêu cầu, làm trên nhánh này và mở PR, không đẩy thẳng lên `main`.
- `main` hiện đã chứa đợt làm lại giao diện được merge trực tiếp ngày 17/11/2026, chưa qua PR, nên M3 chưa duyệt `AppLayout.tsx`. Nên để M3 duyệt cùng lúc với Phase B.
