# Flow 3 — Bằng chứng kiểm thử

Ngày chạy: 18/11/2026, Windows/.NET 8/Node 22, Docker Desktop Linux, Chromium. Các test dữ liệu thật dùng PostgreSQL/Redis fixture riêng, Kestrel thật và gRPC HTTP/2. Không reset hoặc xóa volume/database demo. Cấu hình `LABLEND_POSTGRES_TESTS=1`, `LABLEND_BROWSER_TESTS=1`; không đánh dấu PASS cho test bị bỏ qua.

## Lệnh đã thực thi

```powershell
dotnet restore LabLendAI.sln
dotnet build LabLendAI.sln -c Release --no-restore
dotnet test LabLendAI.sln -c Release --no-build --no-restore --logger 'console;verbosity=normal' --logger trx --results-directory .tmp/incident-final-tests
dotnet test tests/Integration/LabLend.Platform.IntegrationTests -c Release --no-build --no-restore --logger trx --results-directory .tmp/incident-final-corrections
dotnet test tests/Integration/Inventory.IntegrationTests -c Release --no-build --no-restore --logger trx --results-directory .tmp/incident-final-corrections
Push-Location apps/web
npm ci
npm run build
npm test
npm run test:recommendations
Pop-Location
docker compose config --quiet
```

Đã sinh migration bằng `dotnet ef migrations add` cho từng Infrastructure project, áp dụng qua startup của fixture thật. `dotnet ef migrations has-pending-model-changes --configuration Release --no-build` cho cả Borrowing và Inventory: **không có model change chưa được migration hóa**. Design-time dùng connection giả không kết nối; không chạy migration thủ công vào shared DB.

## Kết quả cuối theo project

| Kiểm tra | Kết quả |
|---|---|
| Restore và Release build | PASS; 0 compile errors |
| LabLend.Platform.UnitTests | 27 PASS |
| LabLend.Platform.IntegrationTests | 11 PASS |
| Catalog.IntegrationTests | 3 PASS |
| Inventory.IntegrationTests | 11 PASS |
| LabLend.Sprint1.IntegrationTests | 36 PASS, gồm browser Flow 3 và M3 cũ |
| Recommendation.Tests | 67 PASS |
| Frontend production build | PASS |
| Frontend Node tests | 33 PASS, 0 fail/skip |
| Browser contract của Recommendation | PASS; Gemini HTTP được mock có ghi rõ, không chứng nhận live Gemini |
| Model/migration alignment và Compose config | PASS |

Lượt full solution đầu phát hiện hai vấn đề trong test: snapshot proto cũ coi mọi RPC bổ sung là thay đổi cấm, và test quota dùng kết nối TCP bị từ chối quá chậm nên trôi qua nhiều cửa sổ một phút. Snapshot đã giữ nguyên toàn bộ field/RPC cũ và pin rõ phần mở rộng; test Gateway chuyển sang transport probe để kiểm tra quota thật trong cùng một cửa sổ, không tắt limiter. Hai project liên quan được build và chạy lại đầy đủ. Đây không phải bỏ test hoặc đổi kỳ vọng nghiệp vụ để che lỗi.

Build có cảnh báo NU1900 do không đọc được NuGet vulnerability feed. `npm ci` báo 4 vấn đề high trong bộ dependencies hiện hữu; feature không thêm hoặc nâng dependency. Không chứng nhận security audit toàn bộ dependency đã sạch. Không gọi Gemini live, không bật billing. Chưa triển khai feature vào shared Docker runtime hay xác nhận vận hành cloud/multi-instance; bằng chứng chạy là các fixture thật, không phải screenshot giả.

## Ma trận 25 kịch bản yêu cầu

| # | Kịch bản | Bằng chứng |
|---|---|---|
| 1–5 | Báo thiết bị đang mượn, chặn người khác/đã trả, trùng báo cáo, chặn Student kiểm tra | IncidentWorkflowTests.Ownership_roles_duplicate_reports_private_evidence_no_fault_and_minor_fix_are_enforced |
| 6–7 | Không lỗi/khắc phục tại chỗ, giữ ON_LOAN, không maintenance/cấp mới | Cùng test ownership + IncidentDomainTests |
| 8–13 | Cấp đổi thật; máy gốc MAINTENANCE, máy mới ON_LOAN; phiếu vẫn BORROWED; item cũ/mới và hạn gốc được giữ | IncidentWorkflowTests.Immediate_exchange_is_atomic_auditable_idempotent_and_normal_return_still_works |
| 14 | Hết hàng không phát sinh allocation/event; trả lỗi, chờ có đồng ý, phiếu tiếp nối, hủy, LOST | IncidentWorkflowTests.Shortage_waiting_consent_followup_cancellation_and_lost_return_use_real_allocations |
| 15 | Hai sinh viên tranh một máy cuối: một thành công, một 409, một reservation hoạt động | IncidentWorkflowTests.Concurrent_students_cannot_exchange_into_the_same_last_available_serial |
| 16, 20 | Double click/retry một lần cấp, không cấp lại sự cố đã đóng | Immediate_exchange + immutable receipt/hash tests |
| 17–19 | Rơi phản hồi sau commit Inventory, trigger làm fail transaction Borrowing, worker context mới đối soát; không success event trước finalize | IncidentWorkflowTests.Lost_committed_reply_and_failed_borrowing_commit_reconcile_without_duplicate_exchange_or_false_events |
| 21 | Timeline có actor/action/timestamp, record trả và liên kết cũ/mới riêng | IncidentDomainTests + Immediate_exchange |
| 22 | Quyền/bất biến bảo trì cũ, hoàn tất bảo trì sau cấp đổi | InventoryTests.Rest_asset_crud_maintenance_and_public_availability + browser Flow 3 |
| 23 | Stats DB thật, empty/null average, không tính no-fault/minor report thành lỗi xác nhận | Ownership_roles_duplicate... + browser Admin |
| 24 | Loan approval/handover/normal/damaged/lost return và worker cũ | Sprint2LifecycleTests, InventoryTests, browser M3 cũ |
| 25 | Recommendation backend, transport/dependency contract, frontend cũ | 67 Recommendation.Tests + 33 frontend tests + recommendations.mjs |

Tổng kết lượt cuối của từng project: **155 test .NET PASS, 0 fail/skip**; frontend **33 PASS, 0 fail/skip**. Hai project sửa test đã chạy lại đầy đủ sau lượt solution ban đầu, không cộng lặp kết quả. Không chạy thêm test sau yêu cầu dừng kiểm thử của người dùng; lượt Inventory cuối đã hoàn tất trước khi ghi nhận yêu cầu.

Kiểm tra bổ sung: ảnh PNG thật dù filename JPEG, image riêng tư/owner check, 3 ảnh thành công/ảnh thứ 4 bị từ chối mà không mất report; mã key trùng khác payload; receipt bất biến kể cả từ chối OUT_OF_STOCK sau restock; phục hồi khi quá hạn sau commit; MANUAL_REVIEW không tự cấp lại và chỉ Staff mới được retry; quota của hai JWT cùng IP độc lập; mọi RPC mới qua HTTP/2 và yêu cầu service token.

## Browser và hiện vật

`Member3BrowserTests.Incident_report_inspection_exchange_notifications_admin_and_maintenance_use_real_services` chạy thật từ tạo phiếu đến trả máy thay thế:

1. Student tạo phiếu, Staff duyệt/bàn giao.
2. Student báo lỗi, upload ảnh riêng tư; Staff nhận notification qua worker/Redis.
3. Staff đối chiếu serial và xác nhận nhận máy trực tiếp, ghi kết luận, bàn giao máy mới.
4. Test đọc lại Gateway thật để xác nhận LoanId/DueAt/item/asset states.
5. Student nhận kết quả/thông báo; Admin xem số liệu đúng record mới.
6. Viewport 390 × 844 không tràn ngang; Staff hoàn tất bảo trì và nhận trả máy mới NORMAL.

Không có business response mock trong browser Flow 3. Ảnh đã xem trực quan: `.tmp/incident-browser/student-report.png`, `staff-replaced.png`, `mobile-incident.png`, `admin-statistics.png`. TRX/log đầy đủ ở `.tmp/incident-final-tests`, `.tmp/incident-final-corrections` và `.tmp/incident-*.log`; thư mục này được gitignore để không commit dữ liệu/tài khoản fixture hoặc log nhạy cảm. Ảnh lỗi cũ có thể còn trong local `.tmp`; chúng không phải trạng thái cuối đã qua kiểm tra.

## Checklist nghiệm thu

| Tiêu chí | Trạng thái / chứng cứ |
|---|---|
| Student báo sự cố từ phiếu thật và Staff kiểm tra vật lý | PASS — API ownership và browser |
| Cấp một máy AVAILABLE thật cùng loại | PASS — transaction/gRPC và browser |
| Máy lỗi, máy mới, item gốc/item mới, cùng LoanId/DueAt | PASS — Immediate_exchange |
| Chống cấp hai lần/cùng serial cho hai sinh viên | PASS — replay/hash + concurrency |
| Hết hàng an toàn, chờ/hủy, phiếu tiếp nối không mở lại terminal loan | PASS — Shortage_waiting... |
| Trạng thái đúng, retry/manual review minh bạch, không false success | PASS — fault injection và recovery |
| Thông báo qua sự kiện thật và quyền backend | PASS — outbox/mapper + Redis/browser + ownership tests |
| Admin có stats thật, lỗi đã xác nhận tách report | PASS — SQL aggregate + browser |
| Luồng cũ Borrowing/Inventory/Recommendation | PASS — 36 Sprint1, 11 Inventory và 67 Recommendation, gồm browser M3 cũ |
| Migration, test, setup/demo có tài liệu | PASS — model checks và packet Flow 3 |

Chưa thực hiện: demo bằng thiết bị vật lý ngoài đời, Gemini live (ngoài phạm vi Flow 3), deploy cloud/multi-instance, equivalence khác loại và tự thông báo có hàng trở lại. Browser xác nhận hành động nhận máy của Staff; hệ thống không thể tự chứng minh việc cầm máy ngoài đời.
