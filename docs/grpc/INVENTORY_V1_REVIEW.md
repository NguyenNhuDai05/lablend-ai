# Inventory gRPC v1: M2 owner review và decision record

**Owner: Member 2. Consumer: Member 1. Ngày: 2026-11-16. Baseline: `main` 68933a1 (sau PR #10–#14).**
**Trạng thái: M2 đã review, chờ M1 ký.** Tài liệu này không đổi proto hay server; mọi thay đổi đề xuất ở mục 7 cần M1 đồng ý và đi bằng PR riêng.

Phạm vi: OD-01–03 và gap "reservation ID / lịch sử trả" trong [OPEN_DECISIONS](../architecture/OPEN_DECISIONS.md). Nguồn đã đọc: [proto](../../proto/inventory/v1/inventory.proto), `Inventory.Application/AllocationService.cs`, `Inventory.Domain/Reservation.cs` + `AssetUnit.cs`, `Inventory.Infrastructure/InventoryRepositories.cs`, `Borrowing.Infrastructure/InventoryGrpcClient.cs` + `LoanLifecycle.cs` + `LoanReconciliation.cs`.

## 1. Kết luận

1. **v1 đủ an toàn cho R08/R09 và live demo, với điều kiện Borrowing là writer duy nhất của các loan ID nó tạo** (mục 4). Không cần v2.
2. **Reservation ID: không cần.** `(operation_id, loan_item_id)` đã xác định duy nhất một reservation; Borrowing giữ `ReservationId = null` là đúng.
3. **Return condition/time: chấp nhận giới hạn cho P0.** Inventory có lưu nhưng reply không trả về. Borrowing ghi intent bất biến của staff và thời điểm Borrowing commit; không suy lịch sử từ `asset_status` hiện tại.
4. **Đề xuất P1 (cần M1 đồng ý):** server từ chối replay `ReturnAssets` khác condition, và thêm field additive `return_condition`/`returned_at` vào `Allocation`. Vẫn là v1 vì không đổi tên/số/kiểu field cũ.

## 2. Rà từng RPC

| RPC | Server làm gì | Borrowing dùng thế nào | Kết luận |
|---|---|---|---|
| CheckAvailability | Snapshot đọc, không giữ chỗ; `total` loại RETIRED/LOST | Không dùng khi approve; `ReserveAssets` tự kiểm tồn kho | OK |
| ReserveAssets | Một transaction: ghi `ReserveOperations` (unique `operation_id`) + mỗi item lấy 1 unit `FOR UPDATE SKIP LOCKED`; thiếu 1 item → rollback hết. Replay cùng payload → allocations hiện tại; khác payload → `ALREADY_EXISTS`; operation đã RELEASED → `FAILED_PRECONDITION` | Lưu APPROVING + `operation_id` trước; gọi `GetOperationStatus` trước, chỉ `Reserve` cùng ID khi `found=false` | OK |
| ConfirmHandover | Theo loan: HELD → HANDED_OVER, unit RESERVED → ON_LOAN; replay trả hiện trạng; không còn reservation active → `NOT_FOUND` | Kiểm HELD/HANDED_OVER trước và HANDED_OVER + ON_LOAN sau | OK |
| ReturnAssets | Theo `loan_item_id`: NORMAL → AVAILABLE, DAMAGED → MAINTENANCE + maintenance record, LOST → LOST. Item đã RETURNED bị bỏ qua, **kể cả khi condition gửi lại khác** | Intent trả bất biến (`SameReturns` chặn lệnh khác khi operation còn mở); kiểm mọi item đã gửi là RETURNED | OK khi chỉ Borrowing ghi; xem G-2 |
| ReleaseReservations | Chỉ HELD → RELEASED; HANDED_OVER/RETURNED không bao giờ bị release; gọi lần hai trả danh sách rỗng | Sau release luôn đọc lại `GetOperationStatus` và kiểm RELEASED | OK |
| GetOperationStatus | Trả mọi reservation của operation ở mọi trạng thái; `found=false` = chưa có operation đã commit | Xem quy tắc bên dưới | OK |

**Quy tắc bắt buộc cho mọi client:** `found=false` chỉ là snapshot. Một request cùng `operation_id` còn đang chạy vẫn có thể commit sau đó. Client không được kết luận "không giữ gì" rồi release hay đánh FAILED; phải replay `ReserveAssets` **cùng** `operation_id`. Lần replay sẽ chờ trên unique key của request trước và trả đúng allocation cũ. Borrowing đã làm đúng ở cả nhánh approve và compensation (`LoanLifecycle.Compensate`).

## 3. Vòng đời reservation

- Reservation: `HELD → HANDED_OVER → RETURNED`, hoặc `HELD → RELEASED`. Unit: `AVAILABLE → RESERVED → ON_LOAN → AVAILABLE | MAINTENANCE | LOST`, hoặc `RESERVED → AVAILABLE`.
- Unique index: mỗi loan item và mỗi unit chỉ có một reservation active (HELD/HANDED_OVER).
- Inventory không tự hết hạn HELD. Borrowing worker tạo operation RELEASE khi quá `PickupHours` (mặc định 24 h) hoặc quá `dueAt`.
- Concurrency: bảng `Reservations` không có `xmin` riêng, nhưng mọi chuyển trạng thái đều cập nhật `AssetUnit` (có `xmin`) trong cùng `SaveChanges`. Hai writer cùng lúc trên một reservation thì một bên nhận `ABORTED` và retry được.
- Mọi đổi trạng thái unit đều để lại một dòng `StockMovements`, kể cả lần reserve bằng SQL. Dòng do gRPC tạo mang `loanId`; dòng do REST tạo mang người thao tác.

## 4. Writer ownership

- Chỉ gRPC đổi reservation. REST Inventory không chạm reservation, và domain chặn REST đổi unit đang RESERVED/ON_LOAN: bảo trì chỉ từ AVAILABLE, retire và đổi loại thiết bị bị chặn khi unit đã cấp.
- `INVENTORY_SERVICE_TOKEN` được cấp cho `inventory`, `borrowing`, `borrowing-worker` (compose) và `scripts/Demo-Inventory.ps1` (DemoClient, luôn dùng loan ID ngẫu nhiên mới).
- **Quy tắc:** với loan ID do Borrowing tạo, Borrowing (API + worker, cùng `LoanLock`) là writer duy nhất. Không chạy DemoClient, grpcurl hay script nào gọi RPC ghi với loan ID thật. Vi phạm thì recovery không còn được đảm bảo và loan phải vào MANUAL_REVIEW.
- Static token là đủ cho coursework local (OD-03), nhưng nó không định danh từng caller: Inventory không phân biệt Borrowing với DemoClient. Short-lived service JWT là hướng nâng cấp, ngoài P0.

## 5. Gap lịch sử trả và reservation ID

| ID | Gap | Quyết định |
|---|---|---|
| G-1 | Reply không có reservation ID | Không cần cho v1. Borrowing giữ `AssetUnitId`; reservation được định danh bằng `(operation_id, loan_item_id)`. Không thêm field. |
| G-2 | Reply không có return condition; replay khác condition bị nuốt và trả thành công với condition cũ | Chấp nhận cho P0 vì Borrowing chỉ replay đúng intent đã lưu. Đề xuất P1 ở mục 7. |
| G-3 | Reply không có thời điểm trả | Chấp nhận cho P0: `returnedAt` của Borrowing là lúc Borrowing ghi nhận. Nếu reply bị mất, worker khôi phục sau tối đa vài phút (backoff ≤ 5 phút/lần, `MaxAttempts` = 8). Thời điểm thật vẫn còn trong Inventory: `StockMovements.OccurredAt` (ON_LOAN → …, kèm `loanId`). |

`asset_status` trong reply là trạng thái **hiện tại** của unit. Unit có thể đã sửa xong hoặc đang được loan khác giữ, nên trường này không phải bằng chứng lịch sử. Borrowing chỉ ràng buộc `asset_status` khi reservation còn HELD hoặc HANDED_OVER, đúng với quy tắc này.

## 6. Khi nào MANUAL_REVIEW và bằng chứng phía Inventory

Borrowing chuyển operation sang MANUAL_REVIEW khi lỗi không chắc chắn vượt `MaxAttempts`, hoặc khi HANDOVER/RETURN/RELEASE nhận lỗi xác định (`INVALID_ARGUMENT`, `NOT_FOUND`, `FAILED_PRECONDITION`, `ALREADY_EXISTS`). APPROVE nhận lỗi xác định thì thành APPROVAL_FAILED, không vào MANUAL_REVIEW. Operation ở trạng thái này chặn mọi lệnh khác của loan và không tự reset.

| Tình huống | Bằng chứng Inventory | Hướng xử lý |
|---|---|---|
| Allocation không khớp loan (`INVENTORY_ALLOCATION_MISMATCH` lặp tới `MaxAttempts`) | `GetOperationStatus(approval operation_id)` | So từng loan item: số unit, unit trùng, trạng thái reservation. Có reservation RELEASED/RETURNED mà Borrowing không ra lệnh thì nghi writer ngoài (mục 4). |
| Loan còn APPROVED nhưng reservation đã RELEASED | Movement `RESERVED → AVAILABLE` mang `loanId` | Không bàn giao được. Loan cần được đóng (EXPIRED) bằng lệnh phía Borrowing. |
| Return lỗi xác định | `GET /api/inventory/assets/{assetUnitId}/movements` (staff) | So movement `ON_LOAN → …` của loan với intent đã lưu trong Borrowing. |
| Token sai hoặc thiếu (T27) | Log `UNAUTHENTICATED` của Inventory | Sửa cấu hình. Không phải lỗi dữ liệu. |

Không sửa tay bảng `Reservations` hay `AssetUnits`. Chỉ được đọc (REST movements, hoặc SQL read-only theo `LoanId`); mọi sửa chữa phải đi qua RPC hoặc REST có guard.

**Gap cho M1, ngoài Inventory:** chưa có lệnh hay endpoint để operator đóng một operation MANUAL_REVIEW hoặc chuyển loan sang trạng thái cuối sau khi đã đối chiếu.

## 7. Có cần v2 không

**Không.** Theo quy tắc của T23, chỉ đổi tên/số/kiểu field hoặc RPC mới phải lên `v2`. Các đề xuất dưới đây đều additive, và chỉ cập nhật snapshot T23.

Đề xuất P1, PR riêng, chỉ làm khi M1 đồng ý:

1. **Server:** `ReturnAssets` replay một item đã RETURNED với condition khác → `FAILED_PRECONDITION` + `lablend-code: RETURN_CONDITION_CONFLICT`, thay vì trả thành công. Client hiện tại không bị ảnh hưởng vì Borrowing không bao giờ gửi condition khác. Kèm một test.
2. **Proto (additive):** `Allocation` thêm `string return_condition = 6;` (rỗng khi chưa trả) và `string returned_at = 7;` (UTC ISO-8601, rỗng khi chưa trả). Khi đó Borrowing có thể kiểm Inventory đã ghi đúng condition và lấy thời điểm trả thật. Phía Borrowing là việc của M1.

Không thêm `reservation_id` (G-1).

## 8. Đối chiếu test

| Test | Kiểm tra gì | Bằng chứng |
|---|---|---|
| T09 | Mất reply sau commit; timeout giữa lúc reserve | `InventoryTests.Lost_reply_is_resolved_by_status_and_replay_without_double_allocation`, `GrpcOverHttp2Tests.Reserve_that_times_out_rolls_back_and_the_same_operation_recovers`; Gate B: `Sprint2RecoveryTests.GateB_real_RPC_reply_loss_process_crash_compensation_expiry_and_bounded_retry` |
| T23 | Snapshot schema v1 và đủ 6 RPC qua HTTP/2 | `GrpcOverHttp2Tests.Proto_v1_schema_is_unchanged`, `Every_rpc_works_between_separate_client_and_server_over_http2` |
| T27 | Thiếu hoặc sai service token | `InventoryTests.Grpc_business_calls_require_the_service_token`, kiểm tra lại trên HTTP/2; Gate B: token sai dừng ở MANUAL_REVIEW |
| T28 | Compensation, hết hạn nhận, process crash | `InventoryTests.Release_frees_held_units_and_blocks_replaying_the_released_operation` (server); Gate B (Borrowing thật bị kill) |

M2 đã chạy lại `tests/Integration/Inventory.IntegrationTests` trên baseline này; kết quả ở mục 9. Gate A/B lấy theo [SPRINT2_M1_ACCEPTANCE](../testing/SPRINT2_M1_ACCEPTANCE.md), chưa chạy lại.

**Chưa có test:** replay `ReturnAssets` với condition khác (sẽ thêm cùng đề xuất 7.1). Writer ngoài Borrowing được loại khỏi phạm vi bằng quy tắc ở mục 4, không phải bằng test.

## 9. Ký review

| Thành viên | Vai trò | Kết quả | Ngày |
|---|---|---|---|
| M2 | Owner Inventory | Đã review. v1 giữ nguyên; G-1 đóng; G-2/G-3 chấp nhận cho P0; đề xuất mục 7. Chạy lại `dotnet test tests/Integration/Inventory.IntegrationTests -c Release` (Testcontainers PostgreSQL riêng): 11 passed, 0 failed, 0 skipped | 2026-11-16 |
| M1 | Consumer Borrowing | Chờ: xác nhận quy tắc mục 2 và 4, chọn có làm mục 7 không | |
