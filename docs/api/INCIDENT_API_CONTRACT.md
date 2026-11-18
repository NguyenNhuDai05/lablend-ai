# Incident REST và gRPC contract

Base public: Gateway `/api/incidents`; service xử lý: Borrowing. JWT giữ nguyên, `sub` là actor. JSON dùng camelCase; không nhận studentId, role, asset status hoặc serial gốc trong báo cáo. Trường JSON không biết bị từ chối. API sinh correlation ID như các luồng hiện có.

## Endpoint

| Method/path | Quyền | Nội dung |
|---|---|---|
| POST `/api/incidents` | STUDENT | Báo mục BORROWED của mình; 201 hoặc 409 |
| GET `/api/incidents/my` | STUDENT | Chỉ sự cố của mình, phân trang |
| GET `/api/incidents` | LAB_STAFF/ADMIN | Hàng đợi toàn cục, phân trang/bộ lọc |
| GET `/api/incidents/{id}` | Owner hoặc Staff/Admin | Chi tiết, kết quả, timeline, attempts, metadata ảnh và trạng thái đối soát |
| POST `/{id}/cancel` | STUDENT owner | Hủy khi REPORTED hoặc WAITING_REPLACEMENT |
| POST `/{id}/wait` | STUDENT owner | Đồng ý chờ sau khi đã trả lỗi, còn hạn, có yêu cầu cấp đổi |
| POST `/{id}/start-inspection` | Staff/Admin | Bắt đầu kiểm tra; lặp lại cùng trạng thái không tạo history trùng |
| POST `/{id}/inspection` | Staff/Admin | Lưu kết luận đúng trạng thái và xác minh vật lý |
| POST `/{id}/replace` | Staff/Admin | Cấp cùng loại và xác nhận bàn giao, chỉ trả 200 sau finalize |
| POST `/{id}/defective-return` | Staff/Admin | Nhận trả DAMAGED hoặc LOST theo kết luận đã lưu |
| POST `/{id}/resolve` | Staff/Admin | Đóng không cấp đổi, phải đã nhận trả lỗi/thất lạc |
| POST `/{id}/retry-operation` | Staff/Admin | Đối soát lại đúng operation đang pending/manual review |
| POST `/{id}/evidence` | STUDENT owner | Multipart field `file`; trước khi bắt đầu kiểm tra |
| GET `/{id}/evidence/{evidenceId}` | Owner hoặc Staff/Admin | Nội dung raster riêng tư, không URL public |
| GET `/api/incidents/stats` | ADMIN | Số liệu aggregate từ DB |
| GET `/api/loans/{id}/asset-identities` | Owner hoặc Staff/Admin | Serial đáng tin của các allocation của phiếu |

Các đường dẫn rút gọn `/{id}/...` đều nằm dưới `/api/incidents`. Gateway route yêu cầu JWT; từng endpoint tự kiểm vai trò/ownership. gRPC không qua YARP.

## Body và idempotency

`POST /api/incidents` bắt buộc header `Idempotency-Key: <nonempty UUID>`:

```json
{"loanId":"UUID","loanItemId":"UUID","problemCategory":"POWER_FAILURE","description":"Thiết bị không lên nguồn khi thực hành.","replacementRequested":true}
```

Description sau trim 10–2000 ký tự; category POWER_FAILURE, PHYSICAL_DAMAGE, FUNCTIONAL_ERROR, MISSING_COMPONENT hoặc OTHER. Backend đọc Loan/Asset binding và serial thật. Cùng key + payload trả lại báo cáo; khác payload trả 409; khác key khi còn sự cố mở trả 409 OPEN_INCIDENT. Không giữ DB transaction khi đọc Inventory.

Inspection:

```json
{"outcome":"DEFECTIVE","notes":"Đã kiểm tra trực tiếp và xác nhận lỗi nguồn.","correctiveAction":null,"physicallyReceived":true,"verifiedSerialNumber":"ARD-001"}
```

Outcome NO_FAULT, FIXED, DEFECTIVE hoặc LOST. Notes 10–2000. FIXED yêu cầu correctiveAction 10–2000. Trừ LOST, cần physicallyReceived=true và verifiedSerialNumber khớp serial đã lưu. LOST yêu cầu physicallyReceived=false và không cấp đổi.

Replace bắt buộc UUID Idempotency-Key và `{"confirmHandover":true}`. Defective-return cũng bắt buộc UUID key, không nhận condition từ trình duyệt; condition lấy từ inspection. Borrowing dùng key làm operation ID, lưu RequestJson cố định trước RPC. Cùng key, cùng actor/incident/type replay kết quả; khác nội dung liên quan bị từ chối. Sau lỗi dứt khoát (ví dụ OUT_OF_STOCK), một ý định cấp mới cần key mới; kết quả chưa rõ phải đối soát ID cũ trước. Resolve: `{"notes":"Sinh viên không còn nhu cầu sau khi đã nhận trả máy lỗi."}`.

## Lọc và thống kê

List/my/stats nhận `status`, `from`, `to` (ISO-8601), `equipmentTypeId`, `staffId`, `search` (code/serial, tối đa 100), `page` (1..1.000.000), `pageSize` (1..100; mặc định 10). Stats dùng cùng bộ lọc, không lấy số liệu từ một trang UI. Danh sách trả `{items,total,page,pageSize}`.

Stats: `total`, `awaitingInspection`, `waitingReplacement`, `successfullyReplaced`, `closedWithoutReplacement`, `averageResolutionHours` (null nếu chưa có), `verifiedFailures:[{equipmentTypeId,count}]` (20 loại có nhiều lỗi đã xác nhận nhất). Chỉ inspection DEFECTIVE tính lỗi đã xác nhận; không dùng category hoặc mọi report làm lỗi. Average tính các sự cố có ResolvedAt, bao gồm yêu cầu hủy; count đóng không cấp đổi chỉ các trạng thái RESOLVED_NO_FAULT/REPAIRED/NO_REPLACEMENT.

## Lỗi

ProblemDetails có `status`, `title`, `code`, `correlationId`. 400 dữ liệu/ảnh/key không hợp lệ; 401 chưa xác thực; 403 sai vai trò; 404 không tồn tại hoặc không sở hữu; 409 sai trạng thái, hết hàng, quá hạn, trùng key hoặc thao tác khác đang chạy; 503 dependency hoặc kết quả cần đối soát. Mã nghiệp vụ 4xx như OUT_OF_STOCK, OPEN_INCIDENT, WAIT_CONSENT_REQUIRED, DUE_DATE_PASSED được giữ. Chi tiết 5xx vẫn được che theo policy chung.

## gRPC extension không phá v1

Giữ nguyên số field cũ; thêm GetLoanAllocations, ReplaceLoanAsset và GetReplacementOperationStatus vào InventoryAllocation. Mọi RPC vẫn cần service token và correlation metadata qua interceptor hiện có.

ReplaceLoanAssetRequest: operation_id, incident_id, original_loan_id, original_loan_item_id, original_asset_unit_id, equipment_type_id, replacement_loan_id, replacement_loan_item_id, actor_id, already_returned, due_at. Tất cả ID không rỗng; item cũ/mới khác nhau; due_at là hạn gốc UTC. Giao dịch mới từ chối khi đã quá hạn. already_returned chỉ cho reservation gốc RETURNED DAMAGED; nếu false cần HANDED_OVER và máy ON_LOAN, cùng LoanId.

ReplacementOperationReply: found, request_hash, liên kết loan/item/asset cũ/mới, serial mới và error_code (rỗng cho thành công). Kết luận hết hàng/quá hạn/không hợp lệ được lưu với error_code để replay vẫn từ chối ngay cả khi có hàng trở lại. Client Borrowing ánh xạ kết luận từ chối thành business 409. Biên nhận là snapshot bất biến, không đổi theo bảo trì/return sau đó. GetReplacementOperationStatus dùng operation_id. found=false không chứng minh request cũ đã ngừng chạy: nếu phải retry, luôn dùng lại exact payload/ID để serialize với request trước. Request hash bao gồm mọi danh tính nghiệp vụ, actor, mode và due_at.

Inventory bảo đảm một local transaction cho old return, maintenance, movements, new reservation/handover và receipt. Borrowing finalize local và publish outbox sau kết quả thật. Xem [thiết kế phục hồi](../features/EQUIPMENT_INCIDENT_WORKFLOW.md).
