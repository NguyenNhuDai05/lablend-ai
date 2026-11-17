# M2-03: nghiệm thu Catalog/Inventory/StockMovements với Loan thật

**Kết quả: PASS. Không phát hiện lỗi.** Có thêm 1 regression test cho khoảng trống duy nhất tìm được.

| | |
|---|---|
| Ngày | 2026-11-16 |
| Source | `main` 68933a1 + `M2LoanStockMovementTests.cs` (mới) |
| Người thực hiện | Member 2 |
| Môi trường | Stack Docker Compose của nhóm, build lại từ `main` trước khi chạy; test chạy trên PostgreSQL riêng do test tạo |

## 1. Khoảng trống

| Phần | Đã có trước M2-03 | Còn thiếu |
|---|---|---|
| Category/type/asset, bảo trì, ngừng dùng, StockMovements qua Gateway | `MemberIntegrationTests.Real_identity_staff_catalog_inventory_and_borrowing_interoperate_through_gateway` (PR #8), mọi movement mang `changedBy` của staff | Không |
| Movement do gRPC | `InventoryTests`: gọi gRPC trực tiếp với loan ID tự tạo | Chưa đi qua Borrowing thật |
| Loan thật qua Borrowing → Inventory | `Sprint2LifecycleTests.GateA_…` kiểm tra trạng thái asset và maintenance record | **StockMovements, availability và thứ tự thời gian giữa hai service** |

## 2. Regression test

`LabLend.Sprint1.IntegrationTests.M2LoanStockMovementTests.Real_loan_writes_one_movement_per_transition_inside_its_loan_step_and_availability_follows`

Test dùng Identity, Catalog, Inventory (gRPC HTTP/2) và Borrowing thật, mỗi service có database riêng. Kịch bản: 2 unit, 1 phiếu mượn được duyệt → bàn giao (gọi 2 lần) → trả DAMAGED (gọi 2 lần) → staff hoàn tất bảo trì → ngừng dùng unit còn lại. Test kiểm tra:

- **Availability** lần lượt là 2/2 → 1/2 (duyệt) → 1/2 (đang mượn) → 1/2 (đang bảo trì sau DAMAGED) → 2/2 (sửa xong) → 1/1 (ngừng dùng).
- **Movement của unit được mượn** đúng 5 dòng, theo thứ tự: `-→AVAILABLE`, `AVAILABLE→RESERVED`, `RESERVED→ON_LOAN`, `ON_LOAN→MAINTENANCE`, `MAINTENANCE→AVAILABLE`.
  - Gọi lại handover/return không sinh thêm dòng nào.
  - Hai lần staff thử xoá hoặc bảo trì unit khi đang cho mượn bị từ chối 409 và không ghi movement.
- **Ai gây ra movement:**
  - 3 dòng do gRPC có `loanId` = phiếu và `changedBy` = null.
  - 2 dòng do REST có `changedBy` = staff và `loanId` = null.
  - Unit còn lại chỉ có `-→AVAILABLE`, `AVAILABLE→RETIRED`.
- **Thứ tự thời gian giữa hai service:**
  - Tạo unit ≤ APPROVING ≤ RESERVED ≤ APPROVED ≤ ON_LOAN ≤ HANDED_OVER ≤ MAINTENANCE ≤ `returnedAt` ≤ AVAILABLE.
  - Nghĩa là mỗi thay đổi ở Inventory xảy ra bên trong đúng bước Borrowing đã gây ra nó.
- **Maintenance record:**
  - Có đúng 1 record, reason chứa loan ID và `openedBy` = null.
  - Record được đóng bởi chính staff đã hoàn tất bảo trì.

Kết quả ([log](evidence/m2-03-test.txt)):

- Chạy riêng: **1 passed**.
- Chạy chung với `MemberIntegrationTests` và `Sprint2LifecycleTests.GateA_…` (lệnh ở mục 6): **3 passed, 0 failed, 0 skipped**.
- Không có container `lablend-s1-test-*` nào sót lại.

## 3. Chạy thật trên Docker qua Gateway và UI Staff

Trước khi chạy, mình phát hiện stack đang dùng image Inventory/Catalog từ 15/11 và Borrowing build trước PR #12. Vì vậy đã build lại `inventory`, `catalog`, `borrowing` từ `main` và khởi động lại bằng `up -d --no-deps --wait`. Volume không bị xoá. `identity` và `gateway` build lại thì trúng cache, tức là source không đổi.

| Bước | Kênh | Kết quả |
|---|---|---|
| Staff tạo category, type `M2-03 Kính lúp 1116234406`, unit A và B | Script → Gateway | 201. Availability 2/2 |
| Staff cho B vào bảo trì, rồi hoàn tất | Script → Gateway | 201/200. Availability 1/2 → 2/2 |
| Student tạo phiếu `25e2b216-…` | Script → Gateway | 201 PENDING |
| Staff duyệt, bàn giao, trả DAMAGED | **UI Staff** `/staff/loans/25e2b216-…` | 3 lệnh đều 200. Inventory cấp unit A (`fbd5c8ad-…`). UI hiển thị Đã duyệt → Đang mượn → Đã trả, item "Hư hỏng / DAMAGED" |
| Staff xem lịch sử unit A | **UI Staff** Kho thiết bị → Lịch sử | 5 dòng trạng thái: 3 dòng ghi "Phiếu mượn 25e2b216", 2 dòng ghi "Nhân viên 05464e53"; kèm maintenance record của phiếu |
| Student đọc movements | Script → Gateway | 403 `FORBIDDEN` |
| Staff hoàn tất bảo trì A, ngừng dùng B | Script → Gateway | 200/204. Availability 1/2 → 1/1 |

UI dùng phiên đăng nhập thật (refresh cookie HttpOnly), không dùng dev token. `localStorage`, `sessionStorage` và cookie đọc được bằng JS đều rỗng. Request/response xem tại [m2-03-gateway.txt](evidence/m2-03-gateway.txt); login response và access token không được ghi lại.

### Dòng thời gian đối chiếu từ hai database (UTC, query read-only)

Nguồn: [m2-03-db.txt](evidence/m2-03-db.txt). Mỗi phiên `psql` đều chạy `SET default_transaction_read_only = on` trước. Hai database không join với nhau, chỉ so theo thời gian.

| Thời điểm 16:xx | DB | Sự kiện |
|---|---|---|
| 44:14.461 | inventory | #33 A `-→AVAILABLE` (staff) |
| 44:16.836 | inventory | #34 B `-→AVAILABLE` (staff) |
| 44:21.339 | inventory | #35 B `AVAILABLE→MAINTENANCE` (staff), mở maintenance record |
| 44:25.507 | inventory | #36 B `MAINTENANCE→AVAILABLE` (staff), đóng record |
| 44:29.789 | borrowing | SUBMITTED (student) |
| 45:38.145 | borrowing | InventoryOperation APPROVE `0fb68413-…` bắt đầu |
| 45:38.147 | borrowing | History APPROVING |
| 45:38.356 | inventory | ReserveOperation `0fb68413-…` được tạo |
| 45:38.397 | inventory | #37 A `AVAILABLE→RESERVED`, loan 25e2b216 |
| 45:38.481 | borrowing | History APPROVED, operation COMPLETED |
| 45:49.994 | borrowing | InventoryOperation HANDOVER bắt đầu |
| 45:50.032 | inventory | #38 A `RESERVED→ON_LOAN`, loan 25e2b216 |
| 45:50.040 | borrowing | History HANDED_OVER, operation COMPLETED |
| 46:04.128 | borrowing | InventoryOperation RETURN bắt đầu |
| 46:04.157 | inventory | #39 A `ON_LOAN→MAINTENANCE`, loan 25e2b216. Reservation RETURNED/DAMAGED. Mở maintenance record (`openedBy` null) |
| 46:04.165 | borrowing | History RETURNED, item DAMAGED với `returnedAt` 46:04.165, operation COMPLETED |
| 46:58.165 | inventory | #40 A `MAINTENANCE→AVAILABLE` (staff), đóng record |
| 46:58.188 | inventory | #41 B `AVAILABLE→RETIRED` (staff) |

Kết luận từ bảng:

- Thứ tự `Id` của StockMovements trùng với thứ tự thời gian.
- Mỗi movement do gRPC nằm giữa thời điểm Borrowing ghi ý định (InventoryOperation) và thời điểm Borrowing ghi kết quả.
- Không có movement thừa.
- Phía Borrowing có `ReservationId` = null và `RetryCount` = 0 ở cả 3 operation, đúng với [INVENTORY_V1_REVIEW](../grpc/INVENTORY_V1_REVIEW.md).

## 4. Nhận xét (không phải lỗi)

- **Maintenance record tạo khi trả DAMAGED có `openedBy` = null.** gRPC v1 không mang thông tin user, nên Inventory không biết staff nào nhận trả. Người nhận trả được ghi ở `LoanHistories.ActorUserId` bên Borrowing, còn record ghi loan ID trong `reason`. Chấp nhận được cho P0.
- **Hai service dùng hai đồng hồ khác nhau.** `OccurredAt` của movement theo đồng hồ Inventory, history theo đồng hồ Borrowing. Trên cùng một máy Docker thì so sánh được. Nếu chạy trên nhiều máy, thứ tự chuẩn trong Inventory là `Id`, không phải thời gian.
- **Image Docker có thể bị cũ.** Image cũ không gây lỗi trong lần chạy này, nhưng preflight demo (M2-04) nên build lại từ `main` trước khi trình diễn.

## 5. Đối chiếu tiêu chí nghiệm thu M2-03

| Tiêu chí | Bằng chứng |
|---|---|
| Request/response | [m2-03-gateway.txt](evidence/m2-03-gateway.txt) |
| Snapshot | Availability ở từng bước; text hộp thoại Lịch sử trên UI Staff (trong m2-03-gateway.txt) |
| Tên test | Mục 2 và mục 6 |
| Query DB read-only | [m2-03-db.txt](evidence/m2-03-db.txt) |
| Không dùng response giả hay dev token trong browser | Đăng nhập thật qua Identity; storage rỗng; mọi lệnh đi qua Gateway tới service thật |

Chưa có ảnh chụp màn hình vì cửa sổ trình duyệt bị thu nhỏ trong lúc chạy. Text của UI đã được ghi lại thay cho ảnh.

## 6. Chạy lại

Test:

```powershell
$env:LABLEND_POSTGRES_TESTS = '1'
dotnet test tests/Integration/LabLend.Sprint1.IntegrationTests -c Release --filter "FullyQualifiedName~M2LoanStockMovementTests|FullyQualifiedName~MemberIntegrationTests|FullyQualifiedName~Sprint2LifecycleTests"
```

Chạy thật: đăng nhập Staff trên web và làm lại các bước ở bảng mục 3, sau đó chạy các query trong `m2-03-db.txt`. Script tạo dữ liệu dùng tài khoản test cục bộ nên không commit.
