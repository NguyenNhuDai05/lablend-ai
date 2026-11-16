# Inventory gRPC contract guide

## Final executed checkpoint 2026-11-17

Unchanged v1 passes real HTTP/2 PostgreSQL regression, including concurrent/all-or-nothing reserve and timeout/lost-reply recovery. Fresh production Docker E2E verifies Borrowing REST -> Inventory RPC with owning-DB RESERVED/ON_LOAN and NORMAL/DAMAGED/LOST outcomes. See [final acceptance](../testing/FINAL_PRODUCT_ACCEPTANCE.md). This is execution evidence, not approval of new contract fields. Reservation ID and historical condition/time gaps and the single-writer recovery assumption remain documented; independent M2 owner-review PR #15 remains unmerged.

## M1 consumer implementation review — Sprint 2

The M1 consumer was checked against the unchanged v1 proto and server implementation on 2026-11-16. Borrowing.Infrastructure generates Client code directly from this file, uses IOptions Inventory:GrpcAddress/ServiceToken/DeadlineSeconds (default 5), sends the service Bearer credential and x-correlation-id, and maps status/trailer codes into business/dependency failures. User JWTs never authorize Inventory RPCs. Stable durable approval operation IDs, GetOperationStatus, full item/asset/state verification, replay and HELD-only release are covered by [Gate A/B](../testing/SPRINT2_M1_ACCEPTANCE.md).

No schema/server change is proposed by this consumer addition. M2 review remains required for the historical return-condition/reservation-ID gap in OPEN_DECISIONS.md. Borrowing leaves ReservationId null and persists immutable per-item return intent; it does not infer historical condition from a currently reused/repaired asset. Team approval of additional contract fields is not claimed.

**Owner schema: Member 2. Consumer/reviewer: Member 1. Status: v1 IMPLEMENTED (server), chờ M1 review OD-01–03.**

Nguồn chuẩn: [`proto/inventory/v1/inventory.proto`](../../proto/inventory/v1/inventory.proto) (package `lablend.inventory.v1`, C# `LabLend.Inventory.Contracts.V1`). Inventory.Host generate server + client (`GrpcServices="Both"`); Borrowing thêm `<Protobuf Include=... GrpcServices="Client" />` trỏ cùng file, không copy proto.

| RPC | Semantics đã implement |
|---|---|
| CheckAvailability | Read-only snapshot, 1–100 type ids; `total` loại RETIRED/LOST. Không giữ chỗ cho lần reserve sau |
| ReserveAssets | 1–50 items, `loan_item_id` không trùng. Một transaction Inventory: mỗi item lấy 1 unit AVAILABLE bằng `FOR UPDATE SKIP LOCKED`; thiếu 1 item → rollback tất cả, `FAILED_PRECONDITION` + `INSUFFICIENT_STOCK` |
| ConfirmHandover | Mọi reservation HELD của loan → HANDED_OVER, unit → ON_LOAN. Replay trả kết quả hiện tại; loan không có reservation active → `NOT_FOUND` |
| ReturnAssets | Trả từng phần theo `loan_item_id`: NORMAL → AVAILABLE, DAMAGED → MAINTENANCE + maintenance record, LOST → LOST. Replay an toàn |
| ReleaseReservations | Compensation/hết hạn nhận: giải phóng mọi HELD của loan. Không có gì → danh sách rỗng (idempotent) |
| GetOperationStatus | `found=false` nghĩa là operation chưa reserve gì (đã rollback hoặc chưa tới) → Borrowing được retry cùng `operation_id` |

## Idempotency và reconciliation

- `operation_id` ổn định cho một lần approve. Server lưu SHA-256 của `loan_id` + cặp `loan_item_id:equipment_type_id` đã sort.
- Gọi lại cùng `operation_id` + cùng payload → trả allocations cũ, không reserve thêm. Khác payload → `ALREADY_EXISTS` (`OPERATION_ID_REUSED`). Operation đã release → `FAILED_PRECONDITION`; approve lại cần `operation_id` mới.
- Hai request trùng `operation_id` chạy song song: request sau chờ request đầu commit rồi replay.
- Borrowing persist `APPROVING` + `operation_id` **trước** khi gọi; timeout/unknown → `GetOperationStatus` rồi quyết định APPROVED hoặc retry/compensate (T28, M1+M2).
- Hết deadline giữa chừng: server huỷ câu lệnh DB đang chạy (deadline → `CancellationToken` → PostgreSQL cancel) và rollback, client nhận `DEADLINE_EXCEEDED`; `GetOperationStatus` trả `found=false` và gọi lại cùng `operation_id` sẽ cấp phát bình thường. Mất reply sau khi đã commit: status và replay trả đúng unit cũ, không cấp thêm (T09).
- Inventory không tự hết hạn HELD; Worker/Borrowing gọi `ReleaseReservations` khi quá hạn nhận.

## Auth, lỗi, deadline

- Mọi business RPC cần metadata `authorization: Bearer <INVENTORY_SERVICE_TOKEN>` (so sánh constant-time). Thiếu/sai → `UNAUTHENTICATED` (T27). `grpc.health.v1.Health` anonymous. Không dùng JWT người dùng làm service credential.
- Mã lỗi: `INVALID_ARGUMENT` (UUID/items sai), `NOT_FOUND`, `FAILED_PRECONDITION` (hết hàng, sai trạng thái), `ALREADY_EXISTS`, `ABORTED` (`CONCURRENCY_CONFLICT`, retry được), `UNAUTHENTICATED`. Trailer `lablend-code` mang mã máy (`INSUFFICIENT_STOCK`, `OPERATION_ID_REUSED`, …).
- Đề xuất client: deadline 5 s; chỉ retry `UNAVAILABLE`/`DEADLINE_EXCEEDED`/`ABORTED`, luôn cùng `operation_id`; propagate `X-Correlation-ID` qua metadata.

## Wiring và kiểm tra

Compose đã cấp cho Borrowing `Inventory__GrpcAddress=http://inventory:8081` và `Inventory__ServiceToken`; local SDK dùng `http://localhost:5181` (`scripts/Set-LocalEnvironment.ps1`). Inventory từ chối khởi động nếu token < 32 ký tự. Public Gateway không proxy cổng gRPC.

```powershell
docker compose exec -T inventory dotnet /probe/HealthProbe.dll grpc http://localhost:8081
dotnet test tests/Integration/Inventory.IntegrationTests   # cần Docker; T07–T11/T23/T27 + release/replay
pwsh scripts/Demo-Inventory.ps1                             # demo R18 trên stack Docker đang chạy
```

`Demo-Inventory.ps1` (PowerShell 7) chạy trên stack `docker compose up -d`:
- REST qua Gateway: Catalog và tồn kho công khai, 401/403/200, staff CRUD + bảo trì + StockMovements.
- gRPC: `Inventory.DemoClient` chạy trong mạng `backend` bằng image của Inventory (cổng gRPC không publish ra host) và đóng vai Borrowing: thiếu token → `UNAUTHENTICATED`, all-or-nothing, 8 phiếu giành chiếc kính hiển vi cuối cùng (đúng 1 thắng), replay cùng `operation_id`, bàn giao rồi trả.

Demo chạy lại được: kính hiển vi về lại AVAILABLE, thiết bị demo được ngừng dùng nên không làm đổi tồn kho.

`GrpcOverHttp2Tests` chạy app trên Kestrel thật (endpoint gRPC chỉ HTTP/2, như Docker) và gọi bằng client sinh từ cùng file proto: đủ 6 RPC + health, kiểm tra mọi response là HTTP/2, T09 timeout và snapshot schema v1 (T23). Snapshot đỏ nghĩa là thay đổi phá client của Borrowing: thêm field mới với số mới thì cập nhật snapshot; đổi tên/số/kiểu field hoặc RPC thì làm `v2`.

Còn lại cho R09: M1 thêm gRPC client trong Borrowing approve/handover/return và test end-to-end REST → gRPC.
