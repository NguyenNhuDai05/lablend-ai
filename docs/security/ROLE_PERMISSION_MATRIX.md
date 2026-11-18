# Ma trận phân quyền

## Bổ sung Flow 3 — 18/11/2026

| Hành động sự cố | STUDENT | LAB_STAFF | ADMIN |
|---|---|---|---|
| Báo thiết bị BORROWED của mình | Có, JWT owner | 403 | 403 |
| Xem báo cáo/ảnh riêng tư | Chỉ của mình, người khác 404 | Mọi sự cố | Mọi sự cố |
| Hủy báo cáo chưa kiểm tra hoặc hủy chờ | Chỉ chủ báo cáo, đúng trạng thái | 403 | 403 |
| Đồng ý chờ cấp đổi sau trả lỗi | Chỉ chủ báo cáo, còn hạn | 403 | 403 |
| Bắt đầu kiểm tra, kết luận, nhận trả lỗi/thất lạc | 403 | Có, kiểm tra vật lý và serial theo kết luận | Có, cùng ràng buộc |
| Cấp đổi cùng loại và bàn giao | 403 | Có, chỉ sau kiểm tra và đúng trạng thái | Có, cùng ràng buộc |
| Đối soát operation và đóng sự cố | 403 | Có | Có |
| Thống kê toàn cục | 403 | 403 | Có, dữ liệu DB thật |

Tất cả yêu cầu ẩn danh trả 401. Gateway bảo vệ `/api/incidents/**`; Borrowing kiểm tra lại quyền/ownership, không tin role/studentId/serial hoặc asset status từ trình duyệt. Upload chỉ cho owner STUDENT khi chưa bắt đầu kiểm tra; download chỉ cho owner/Staff/Admin, không có URL public. Internal RPC không qua Gateway và vẫn dùng service credential hiện có.

Trang `/incidents`, `/incidents/new`, `/incidents/:id` chỉ STUDENT; `/staff/incidents`, `/staff/incidents/:id` cho Staff/Admin; `/admin/incidents` chỉ ADMIN. Nút Báo sự cố chỉ trên mục BORROWED của chính Student. ADMIN không vượt điều kiện đã nhận máy/đúng serial, tồn kho AVAILABLE, cùng loại hoặc hạn trả. Mọi trạng thái đã đóng là bất biến ở API. Thiết kế và kiểm thử: [Flow 3](../features/EQUIPMENT_INCIDENT_WORKFLOW.md), [contract](../api/INCIDENT_API_CONTRACT.md).

Phần kiểm toán nền tảng bên dưới được giữ nguyên theo mốc lịch sử đã ghi.

Mô tả hành vi **đang có** trên `main` tại `f85c357` (17/11/2026), đọc trực tiếp từ mã nguồn. Kết quả rà soát và bằng chứng kiểm thử: [AUTHORIZATION_AUDIT](AUTHORIZATION_AUDIT.md).

## 1. Vai trò

| Vai trò | Cách có được |
|---|---|
| Khách | Chưa đăng nhập |
| `STUDENT` | Tự đăng ký. Hệ thống luôn gán `STUDENT`, bỏ qua mọi trường `role` gửi lên |
| `LAB_STAFF` | Do `ADMIN` gán |
| `ADMIN` | Tạo sẵn khi khởi tạo hệ thống, hoặc do `ADMIN` khác gán. Không được hạ quyền hay khoá quản trị viên cuối cùng (trả 409) |

Mỗi tài khoản có **đúng một vai trò**: đổi vai trò sẽ thay toàn bộ vai trò cũ. Giao diện chọn một vai trò đại diện từ danh sách `roles[]`, nên hai phía khớp nhau.

Chính sách dùng chung ở backend: `StaffOrAdmin` (LAB_STAFF hoặc ADMIN) và `Admin` (chỉ ADMIN).

## 2. Quyền theo vai trò

✅ được phép · ❌ bị từ chối (mã HTTP trả về)

| Quyền | Khách | STUDENT | LAB_STAFF | ADMIN |
|---|---|---|---|---|
| Xem danh mục, chi tiết và tình trạng sẵn có của thiết bị | ✅ | ✅ | ✅ | ✅ |
| Xem cả mục đã ngừng (`includeInactive`) | ❌ bị bỏ qua | ❌ bị bỏ qua | ✅ | ✅ |
| Tạo phiếu mượn | ❌ 401 | ✅ | ❌ 403 | ❌ 403 |
| Xem danh sách phiếu của mình | ❌ 401 | ✅ | ✅ (rỗng) | ✅ (rỗng) |
| Xem chi tiết phiếu | ❌ 401 | Chỉ phiếu của mình; phiếu người khác trả 404 | ✅ mọi phiếu | ✅ mọi phiếu |
| Huỷ phiếu đang chờ duyệt | ❌ 401 | Chỉ phiếu của mình | ❌ 403 | ❌ 403 |
| Xem hàng đợi; duyệt, từ chối, bàn giao, nhận trả | ❌ 401 | ❌ 403 | ✅ | ✅ |
| Thêm, sửa, ngừng danh mục và loại thiết bị | ❌ 401 | ❌ 403 | ✅ | ✅ |
| Quản lý kho: tài sản, bảo trì, lịch sử biến động | ❌ 401 | ❌ 403 | ✅ | ✅ |
| Quản lý tài khoản: xem, đổi vai trò, khoá/mở | ❌ 401 | ❌ 403 | ❌ 403 | ✅ |
| Hồ sơ và mật khẩu của mình | ❌ 401 | ✅ | ✅ | ✅ |
| Thông báo của mình; thông báo của người khác trả 404 | ❌ 401 | ✅ | ✅ | ✅ |

Ghi chú:

- Mã người dùng luôn lấy từ JWT (claim `sub`), không bao giờ lấy từ dữ liệu trình duyệt gửi lên.
- `ADMIN` không vượt được ràng buộc nghiệp vụ: không duyệt khi thiếu thiết bị, không cho ngừng tài sản đang giữ chỗ hoặc đang cho mượn, không nhận trả lại thiết bị đã trả.
- Việc giữ chỗ, bàn giao và nhận trả thiết bị trong kho chỉ do dịch vụ Borrowing gọi qua gRPC kèm khoá dịch vụ. Trình duyệt không gọi được, và cổng gRPC không đi qua Gateway.

## 3. Đường dẫn API

| Đường dẫn | Gateway | Dịch vụ |
|---|---|---|
| `POST /api/auth/register`, `login`, `refresh`, `logout` | Mở | Mở; bắt buộc Origin hợp lệ và header `X-LabLend-CSRF: 1` |
| `GET /api/auth/me`, `/api/users/me`, `POST /api/users/me/change-password` | Cần đăng nhập (`/api/auth/me` mở ở Gateway) | Cần đăng nhập |
| `/api/users`, `/api/users/{id}`, `…/active`, `…/role` | Cần đăng nhập | `Admin` |
| `GET /api/categories…`, `GET /api/equipment-types…`, `GET /api/inventory/availability` | Mở | Mở |
| `POST`, `PUT`, `DELETE` của danh mục và loại thiết bị | Mở | `StaffOrAdmin` |
| `/api/inventory/assets/**` | Mở | `StaffOrAdmin` |
| `POST /api/loans`, `POST /api/loans/{id}/cancel` | Cần đăng nhập | `STUDENT`; huỷ thì phải là chủ phiếu |
| `GET /api/loans/my`, `GET /api/loans/{id}` | Cần đăng nhập | Cần đăng nhập; chi tiết chỉ cho chủ phiếu hoặc nhân viên |
| `GET /api/loans`, `POST /api/loans/{id}/reject`, `approve`, `handover`, `returns` | Cần đăng nhập | `StaffOrAdmin` |
| `/api/notifications/**` | Cần đăng nhập | Cần đăng nhập; chỉ dữ liệu của chính mình |

Gateway để mở các đường dẫn danh mục và kho vì có phần xem công khai. Quyền ghi do từng dịch vụ tự kiểm, không dựa vào Gateway.

## 4. Trang giao diện

| Trang | Khách | STUDENT | LAB_STAFF | ADMIN |
|---|---|---|---|---|
| `/`, `/catalog`, `/equipment/:id`, `/login`, `/register` | ✅ | ✅ | ✅ | ✅ |
| `/profile`, `/notifications`, `/loans/:id` | → đăng nhập | ✅ | ✅ | ✅ |
| `/loans`, `/loans/new` | → đăng nhập | ✅ | 403 | 403 |
| `/staff/loans`, `/staff/loans/:id`, `/staff/catalog`, `/staff/inventory` | → đăng nhập | 403 | ✅ | ✅ |
| `/admin/users`, `/admin/users/:id` | → đăng nhập | 403 | 403 | ✅ |

- Khi chưa đăng nhập, người dùng được chuyển tới trang đăng nhập. Đăng nhập xong sẽ quay lại trang cũ, nhưng chỉ khi đó là đường dẫn nội bộ (chặn chuyển hướng ra ngoài).
- Khi thiếu quyền, trang hiện "Không có quyền truy cập" (403).

**Menu và nút:**
- Nhóm "Quản lý phòng lab": LAB_STAFF, ADMIN.
- "Tài khoản": ADMIN.
- "Phiếu mượn" của sinh viên và nút "Tạo phiếu mượn": STUDENT.
- "Hủy phiếu mượn": chủ phiếu, khi phiếu đang chờ duyệt.
- "Duyệt phiếu" và "Từ chối": nhân viên, khi phiếu đang chờ duyệt.
- "Bàn giao thiết bị": nhân viên, khi phiếu đã duyệt.
- Mẫu nhận trả: nhân viên, khi phiếu đang mượn; chỉ liệt kê thiết bị chưa trả.

Ẩn nút chỉ để giao diện gọn. Quyền thật được kiểm ở mục 2 và 3.

## 5. Phiên đăng nhập

- Token truy cập (JWT) sống 10 phút và chỉ nằm trong bộ nhớ ứng dụng React, không lưu vào localStorage hay sessionStorage.
- Token làm mới nằm trong cookie `lablend.refresh` với `HttpOnly`, `SameSite=Strict`, `Path=/api/auth`, sống 7 ngày. Cookie có `Secure` ở mọi môi trường trừ Development.
- Mỗi lần làm mới sẽ cấp token mới. Dùng lại token cũ thì cả chuỗi phiên bị thu hồi.
- Đăng xuất, đổi mật khẩu, đổi vai trò hoặc khoá tài khoản đều thu hồi token làm mới.
- Dịch vụ Identity kiểm tài khoản còn hoạt động và vai trò còn khớp ở mọi yêu cầu. Các dịch vụ khác chỉ kiểm JWT, nên token cũ còn dùng được tối đa 10 phút 30 giây (theo quyết định OD-06).
- Khi nhiều yêu cầu cùng hết hạn, giao diện chỉ làm mới một lần. Nếu làm mới thất bại thì đăng xuất, không lặp lại.

## 6. Mã lỗi

| Mã | Ý nghĩa | Thông báo trên giao diện |
|---|---|---|
| 401 | Chưa đăng nhập hoặc phiên hết hạn | Tự làm mới một lần; thất bại thì báo "Phiên đăng nhập đã hết hạn." |
| 403 | Không đủ quyền | "Bạn không có quyền thực hiện thao tác này." (vẫn giữ phiên) |
| 404 | Không tồn tại hoặc không thuộc tài khoản | "Không tìm thấy dữ liệu hoặc dữ liệu không thuộc tài khoản…" |
| 409 | Trạng thái đã thay đổi | "Dữ liệu đã thay đổi hoặc thao tác không còn hợp lệ…" |
| 429 | Vượt 120 yêu cầu/phút/IP tại Gateway | Chưa có thông báo riêng |
| 503 | Dịch vụ phụ thuộc không sẵn sàng | "Dịch vụ tạm thời không sẵn sàng." |
