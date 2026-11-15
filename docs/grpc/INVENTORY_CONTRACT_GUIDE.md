# Inventory gRPC contract guide

**Owner schema: Member 2. Consumer/reviewer: Member 1. Status: PROPOSAL.**

`proto/inventory/v1/inventory.proto` hiện chỉ chứa syntax/package/namespace và danh sách RPC dự kiến bằng comment. Chưa có business messages/service được chốt hoặc generate. M2 thêm schema và package Grpc.Tools/Google.Protobuf khi review OD-01–03 xong; ghim version hỗ trợ net8.0 bằng Central Package Management.

| Proposed RPC | Ý nghĩa cần thống nhất |
|---|---|
| CheckAvailability | Read-only, không reserve; batch equipment types, snapshot time; không đảm bảo stock cho lần approve sau |
| ReserveAssets | Đủ mọi LoanItem trong local transaction hoặc rollback tất cả; ổn định operationId; không giữ Borrowing transaction qua network |
| ConfirmHandover | Reservation → on-loan; idempotent; xác thực loan/reservation ownership |
| ReturnAssets | Trả từng physical unit với NORMAL/DAMAGED/LOST; record movements và trạng thái inventory phù hợp |
| ReleaseReservations | Compensation/expiry idempotent; chỉ giải phóng đúng loan/operation |
| GetOperationStatus | Giải quyết timeout/unknown result, phục vụ reconciliation |

Đề xuất chung cần review: UUID strings; UTC timestamp format; operationId, loanId, loanItemId; stable payload hash cho replay conflict; clear error mapping (InvalidArgument, NotFound, FailedPrecondition, ResourceExhausted/Aborted, DeadlineExceeded, Unauthenticated). Không fix field numbers/schema cuối cùng ở Sprint 0.

Client/server phải thống nhất deadline, retry chỉ khi idempotent, correlation metadata và service authentication. Env `INVENTORY_SERVICE_TOKEN` mới là reserved configuration, chưa phải implementation auth business RPC. Business credentials thiếu/sai phải fail T27; public Gateway không proxy cổng gRPC.

Inventory.Host hiện dùng HTTP/1 REST 8080 và HTTP/2 plaintext internal 8081 cho local isolated Docker; local SDK dùng 5103 và 5181. Có **standard grpc.health.v1.Health**, không có allocation. Kiểm tra transport thật:

```powershell
docker compose exec -T inventory dotnet /probe/HealthProbe.dll grpc http://localhost:8081
```

Kết quả health không chứng minh R08/R09 nghiệp vụ hoàn tất. Sprint sau Borrowing approve phải gọi ReserveAssets thực, persist APPROVING/operationId trước RPC và reconcile/compensate theo V4; M2 viết T06–T09/T23/T27, M1+M2 viết T28.
