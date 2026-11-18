# Demo Flow 3 — Báo lỗi và cấp đổi thiết bị

PowerShell 7, .NET 8, Node theo package.json, Docker Desktop Linux và trình duyệt Chromium. Không cần Gemini hay tài khoản trả phí. Chạy từ repository; không xóa volumes, không reset shared database.

## Build và kiểm tra tự động

```powershell
Set-Location D:\PRN232\Assigment\lablend-ai
dotnet restore LabLendAI.sln
dotnet build LabLendAI.sln -c Release --no-restore
Push-Location apps/web
npm ci
npx playwright install chromium
npm run build
npm test
Pop-Location
docker volume ls
$env:LABLEND_POSTGRES_TESTS='1'
$env:LABLEND_BROWSER_TESTS='1'
dotnet test LabLendAI.sln -c Release --no-build --no-restore
```

Chạy riêng Flow 3:

```powershell
dotnet test tests/Integration/LabLend.Sprint1.IntegrationTests -c Release --no-build --no-restore --filter 'FullyQualifiedName~IncidentWorkflowTests|FullyQualifiedName~IncidentImageTests|FullyQualifiedName~Incident_report_inspection' --logger 'trx;LogFileName=incidents.trx' --results-directory .tmp/incident-tests
```

Test tự tạo PostgreSQL/Redis fixture riêng, tài khoản và dữ liệu riêng qua API; migration được áp dụng tự động, teardown giữ volumes. Ảnh browser tại `.tmp/incident-browser/`. Không chạy build image nặng đồng thời nếu máy ít RAM. Có thể dùng `$env:LABLEND_BROWSER_CHANNEL='msedge'` trên Windows nếu chỉ cài Edge; bỏ biến này để dùng Playwright Chromium.

## Cập nhật runtime local có dữ liệu hiện hữu

```powershell
./scripts/Initialize-Environment.ps1
docker compose config --quiet
docker compose build inventory borrowing borrowing-worker notification gateway web
docker compose up -d --no-build --wait
docker compose ps
```

Script initialize giữ nguyên `.env` đã có. Migration mới chạy theo workflow startup của Inventory/Borrowing. Backup dữ liệu theo quy trình hiện tại trước khi triển khai môi trường quan trọng. Chạy migration thủ công chỉ khi dùng dev-port override hiện có:

```powershell
dotnet tool restore
. ./scripts/Set-LocalEnvironment.ps1 -Service inventory
dotnet ef database update --project src/Services/Inventory/Inventory.Infrastructure --startup-project src/Services/Inventory/Inventory.Infrastructure
. ./scripts/Set-LocalEnvironment.ps1 -Service borrowing
dotnet ef database update --project src/Services/Borrowing/Borrowing.Infrastructure --startup-project src/Services/Borrowing/Borrowing.Infrastructure
```

Không in ConnectionStrings, JWT hoặc nội dung `.env`. Hai lệnh `database update` chỉ nên hướng đến DB local đã xác định; test tự dùng DB fixture.

## Ba tài khoản thật

Nếu có ADMIN hiện hữu, dùng tài khoản đó. Với DB mới, bootstrap ADMIN theo phần Identity trong README (không có mật khẩu mặc định). Tạo tài khoản demo qua API thật:

```powershell
$env:LABLEND_DEMO_ADMIN_PASSWORD = Read-Host 'Mật khẩu admin local hiện có' -MaskInput
$env:LABLEND_DEMO_USER_PASSWORD = Read-Host 'Mật khẩu cho tài khoản demo mới' -MaskInput
./scripts/Initialize-DemoData.ps1 -AdminEmail admin@demo.test
Remove-Item Env:LABLEND_DEMO_ADMIN_PASSWORD,Env:LABLEND_DEMO_USER_PASSWORD
```

Script tạo/tìm `prn232-student@demo.test` (STUDENT), `prn232-lab_staff@demo.test` (LAB_STAFF); ADMIN `admin@demo.test` phải có sẵn. Không đổi password hoặc quyền tài khoản cũ. Mở ba browser profile/private session riêng tại `http://localhost:3000` (hoặc WEB_PORT đã cấu hình).

## Câu chuyện demo liên tục

1. STAFF mở Quản lý danh mục, tạo loại Arduino Uno nếu chưa có; mở Kho thiết bị, thêm **hai máy mới** cùng loại với serial demo không trùng, ví dụ `INC-UNO-001`, `INC-UNO-002`. Không sửa serial/tồn kho của máy đang cho mượn.
2. STUDENT mở danh mục, chọn Arduino, tạo phiếu một máy, hạn trả còn vài ngày.
3. STAFF duyệt và bàn giao phiếu qua UI. Ghi lại LoanId, hạn trả và serial thật được hệ thống cấp.
4. STUDENT mở chi tiết phiếu đang mượn → **Báo sự cố** tại đúng mục. Chọn không lên nguồn, nhập mô tả, tùy chọn ảnh; chọn muốn thay thế và xác nhận mang máy đến phòng lab; xem lại rồi gửi.
5. STAFF mở Thông báo: nhận thông báo sự cố thật từ Redis/outbox. Mở link hoặc hàng đợi Sự cố thiết bị.
6. Mang máy thật đến quầy. STAFF bấm Bắt đầu kiểm tra; nhập kết quả, chọn xác nhận thiết bị lỗi, nhập đúng serial đang cầm và xác nhận đã nhận/đối chiếu thực tế. Không dùng ảnh để tự kết luận.
7. STAFF xem tồn kho cùng loại; xác nhận đang bàn giao đúng người mượn → Cấp đổi thiết bị cùng loại → xác nhận modal.
8. Kiểm tra sự cố **Đã cấp đổi**. Phiếu giữ nguyên ID và hạn trả. Bảng có mục gốc hư hỏng/đã trả và mục mới đang mượn, serial mới khác; có link quan hệ cấp đổi.
9. STAFF mở kho: máy gốc MAINTENANCE, máy mới ON_LOAN, lịch sử biến động và maintenance được ghi thật.
10. STUDENT mở sự cố, thấy kết quả, serial mới, timeline; mở Thông báo để xem kết quả kiểm tra/cấp đổi/đóng thật.
11. ADMIN mở **Thống kê sự cố**, lọc Arduino và ngày demo: tổng/đã cấp đổi/lỗi đã xác nhận tăng theo dữ liệu thật. Báo cáo chưa kiểm tra không tính là lỗi xác nhận.
12. STAFF hoàn tất bảo trì máy gốc bằng UI kho và ghi kết quả. Sự cố vẫn đã giải quyết cho sinh viên; trạng thái maintenance độc lập.
13. STUDENT trả máy mới sau khi làm xong. STAFF nhận trả NORMAL trên phiếu cũ; phiếu chuyển RETURNED. Không đổi lịch sử máy gốc.

## Ba nhánh bổ sung

- Không lỗi/khắc phục tại chỗ: lập báo cáo khác trên máy đang mượn, kiểm tra trực tiếp, chọn đúng kết luận. Phiếu và asset vẫn BORROWED/ON_LOAN, không cấp mới hay mở maintenance.
- Hết hàng: dùng loại chỉ có một máy đang mượn; cấp đổi trả 409, không tự chuyển trạng thái kho. STAFF nhận trả lỗi; STUDENT chọn chờ hoặc hủy. Nếu thêm máy cùng loại và còn hạn, STAFF bàn giao. Phiếu gốc RETURNED thì UI liên kết **phiếu tiếp nối riêng**, không gọi đó là phiếu gốc. Có thể đóng không cấp đổi với lý do sau khi nhận trả.
- Thất lạc: STAFF xác minh và ghi chú, chọn LOST (không giả nhận máy), ghi nhận thất lạc qua sự cố. Không hiện thao tác cấp đổi.

Mất phản hồi: xem trạng thái đang xử lý/đối soát; chờ worker hoặc Staff bấm đối soát lại thao tác đã lưu. Không tạo ý định cấp đổi mới khi chưa rõ kết quả. Test fault injection tự động là bằng chứng cho trường hợp commit/restart; không cần chủ động phá DB demo.
