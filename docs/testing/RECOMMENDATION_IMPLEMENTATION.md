# Báo cáo Gemini Recommendation v1

Thực hiện 17–18/11/2026, Windows PowerShell, .NET SDK 8.0.423, Node 22.22.3, Docker Desktop Linux engine 29.5.3. Đã pull `main` bằng `git pull --ff-only`, từ `ef26b47` lên `f85c357`. Giữ nguyên thư mục `docs/planning/` chưa được theo dõi có sẵn trước task. Chưa commit/push thay đổi này.

Source đã triển khai luồng Student → Recommendation backend → Google Gemini REST → xác minh Catalog → Inventory read-only → giao diện chọn nhiều thiết bị → form Borrowing → history. **Chưa chứng nhận DONE cho live Gemini demo**: không tạo/lấy key, không bật billing, không gọi Google trong tests.

## A. Files Changed

Danh sách đầy đủ ở cuối báo cáo. Thay đổi tập trung vào Recommendation, frontend AI, nhận selections trong CreateLoanPage, cấu hình bonus và kiểm thử/tài liệu liên quan. Không đổi proto Inventory, thuật toán giữ kho, Borrowing approval/handover/return, Notification, Worker hay test cũ.

## B. Backend

- Domain: RecommendationSession/RecommendationItem với UUID, text bounds, UTC timestamp, thứ tự 1–6 và chống trùng thiết bị.
- Application: IGeminiClient, ICatalogClient, IInventoryAvailabilityClient, IRecommendationRepository, IRecommendationService; orchestration riêng khỏi Controller.
- GeminiClient dùng IHttpClientFactory, Google HTTPS generateContent, system instruction riêng và structured JSON Schema. Chỉ Gemini lựa chọn thiết bị; không scoring/keyword fallback. Key ở header; không có user JWT/PII tài khoản trong provider context; không log provider request hoặc raw error body. Timeout/cancellation, output tokens và response byte ceiling đều có giới hạn.
- CatalogClient đọc pagination thực (pageSize 100), bộ lọc category rõ ràng, giới hạn 200 candidates/100000 context bytes; vượt giới hạn thì yêu cầu thu hẹp, không cắt âm thầm.
- Output kiểm tra JSON/DTO, GUID, tập ứng viên active, độ dài, số items; duplicate giữ lần đầu và thứ tự Gemini. Đọc lại selected Catalog records sau inference để phát hiện deactivate/missing.
- InventoryAvailabilityClient chỉ gọi CheckAvailability, HTTP/2, service Bearer token, correlation metadata và deadline. Từ chối response thiếu/trùng/sai ID hoặc số lượng vô lý; lỗi không biến thành stock=0.
- DI typed HttpClients, singleton gRPC channel, scoped repository/service. JWT STUDENT tại service. Filter trả ProblemDetails có machine code/correlationId; lỗi phụ thuộc không lộ secrets.
- Rate limit mặc định 3 create/phút/user/process và 20 attempts/ngày UTC/user bền vững trong PostgreSQL. Provider failure vẫn tính lượt ngày; feature disabled/missing settings không gọi dependency/provider.

## C. Database

Migration `20261117170224_InitialRecommendations` với designer và EF model snapshot thật:

- RecommendationSessions: PK Id, UserId, Purpose ≤2000, Constraints text[], optional external CategoryId, ModelUsed ≤100, CreatedAt timestamptz; index (UserId, CreatedAt).
- RecommendationItems: PK Id; FK SessionId trong cùng database với cascade; external EquipmentTypeId không FK xuyên service; Rank 1–6; Reason/UsageNotes ≤2000 và không rỗng; index SessionId, unique (SessionId, EquipmentTypeId), unique (SessionId, Rank).
- RecommendationDailyUsage: PK (UserId, Day), atomic attempt upsert an toàn khi nhiều request/replica; không lưu provider response hay secrets.

Session và items được lưu cùng transaction sau khi output/availability hợp lệ. Empty result là success riêng biệt; Catalog rỗng dùng marker minh bạch `catalog-empty`, không tuyên bố đã gọi Gemini. History detail đọc lại Catalog/Inventory và có availabilityCheckedAt; list history không trả stock cũ. Không EnsureCreated/reset/xóa dữ liệu.

## D. APIs

POST `/api/recommendations` → 201 + Location; GET `/api/recommendations/history` → PagedResponse; GET `/api/recommendations/{id}` → owner-only detail. JWT STUDENT, anonymous 401, Staff/Admin 403, session khác owner 404. Các mã lỗi AI_UNAVAILABLE, AI_INVALID_RESPONSE, CATALOG_UNAVAILABLE, INVENTORY_UNAVAILABLE, RATE_LIMITED, VALIDATION_ERROR được xử lý riêng. Xem [request/response/error contract](../api/RECOMMENDATION_API_CONTRACT.md).

## E. Frontend

Student navigation và ba routes `/recommendations`, `/recommendations/history`, `/recommendations/:id` dùng ProtectedRoute/RoleRoute hiện có. Form tiếng Việt có ví dụ nhập, counter, validation, optional constraints/category scope, loading hữu hạn, error/retry và về Catalog. Cards render text an toàn, Catalog image/spec/category thật, reason/usage/rank, current availability, inactive/out-of-stock state và multi-select. Tái sử dụng design tokens, themes, cards/buttons/icons hiện có; không redesign Staff/Admin.

React Router state chuyển purpose + selected UUIDs + quantity=1 tới CreateLoanPage. Parser kiểm tra kiểu/ID/duplicate/quantity/≤20 units, phân biệt nguồn AI với query `equipmentTypeId` cũ. Form fetch lại Catalog và availability, cho sửa số lượng/hạn trả; AI prefill được kiểm tra stock lại trước review/confirmation. Luồng Catalog thông thường giữ nguyên khả năng gửi phiếu PENDING khi chưa đủ kho, với cảnh báo tham khảo; Staff vẫn kiểm tra/giữ kho khi duyệt. Giữ idempotency key và loanApi.create. Không tự gửi phiếu hoặc gọi Gemini từ browser.

## F. Infrastructure

Compose bonus truyền Gemini flags/key/model/timeout/output cap, Catalog/Inventory internal addresses và service token. Recommendation có edge network để outbound HTTPS; core services không phụ thuộc AI. Database:AutoMigrate bật rõ trong Compose Development, ngoài Development mặc định tắt. `.env.example` chỉ placeholders; script local đọc flags từ ignored .env. Gateway route/cluster đã có nên không tạo lại. Hướng dẫn [Free Tier/configuration/demo](../demo/RECOMMENDATION_DEMO.md) giải thích generateContent vẫn supported, Interactions GA, model được xác minh từ tài liệu Google và điều kiện không gắn billing.

## G. Testing

| Gate / lệnh thực tế | Trạng thái | Kết quả và giới hạn |
|---|---|---|
| `git pull --ff-only` | PASS | main → f85c357; lần đầu DNS sandbox thất bại, retry được phép thành công |
| `dotnet restore LabLendAI.sln --disable-parallel -m:1 -nr:false` | PASS | Có NU1900 vì vulnerability feed api.nuget.org không truy cập được |
| `dotnet build LabLendAI.sln -c Release --no-restore -m:1 -nr:false` | PASS | 0 errors; 16 NU1900 warnings, không phải kết quả security audit |
| `LABLEND_POSTGRES_TESTS=1; dotnet test .../Recommendation.Tests.csproj -c Release --no-restore -m:1 -nr:false -p:UseSharedCompilation=false` | PASS | 66 passed, 0 failed/skipped; fake Google HTTP; real DB constraints/quota + Catalog HTTP + Inventory HTTP/2 |
| `dotnet test LabLendAI.sln -c Release --no-restore --no-build -m:1 -nr:false` với PostgreSQL flag | BLOCKED ở lượt tổng ban đầu | Platform unit 20, platform integration 10, Catalog 3, Inventory 11 PASS; lượt tổng bị dừng khi RAM cạn. Các suite còn lại được chạy riêng bên dưới |
| `LABLEND_POSTGRES_TESTS=1; dotnet test .../LabLend.Sprint1.IntegrationTests.csproj -c Release --no-build --no-restore ...` | PASS | 27 passed, 0 failed; 1 browser test skipped khi chưa bật flag; thời gian 4 phút 31 giây. Tổng backend qua các suite riêng: 137 PASS |
| Bật thêm `LABLEND_BROWSER_TESTS=1`, filter `Member3BrowserTests`, giữ nguyên test cũ | PASS | 1 passed, 0 failed/skipped; 3 phút 38 giây; real Identity/Catalog/Inventory/Borrowing/Notification/Worker/Gateway + PostgreSQL/Redis/HTTP2. Tổng cuối: 138 distinct backend/E2E tests PASS, không còn test chưa xác minh do flag |
| `npm ci --no-audit --no-fund` | PASS | 75 packages; retry có quyền sau EPERM của sandbox |
| `npm test` | PASS | 30 tests, 0 fail/skip; gồm 5 tests recommendation/prefill mới |
| `npm run build` | PASS | TypeScript + production Vite bundle |
| `npm run test:recommendations` | PASS | Playwright: form validation/loading, safe cards, one/multiple selection, mobile overflow, loan prefill/review/idempotency payload, history, unavailable/rate errors, Staff role guard; tất cả API là fixture được ghi rõ |
| `docker compose --profile bonus config --quiet` | PASS | Không in key/connection strings |
| `docker compose --profile bonus --progress plain build recommendation` | PASS | Linux image built; build toàn solution trong image 0 warnings / 0 errors |
| `dotnet ef migrations has-pending-model-changes ... --configuration Release --no-build` | PASS | Model khớp migration; design-time factory dùng để kiểm tra source, không cập nhật DB |
| `./scripts/Test-RecommendationSmoke.ps1` | PASS | Project/credentials/volume cô lập, real PostgreSQL startup/migrations, readiness, anonymous 401, Staff 403, Student disabled 503 AI_UNAVAILABLE; không Google call |
| Live Gemini + Student tạo phiếu từ kết quả Gemini live | NOT RUN | Chưa tự cấu hình API key/Free Tier; không gọi API thật hoặc bật billing |

Lần đầu chạy real-dependency Recommendation test đồng thời với full regression: 64 PASS/1 FAIL do create trả 503 trong lúc hệ thống gần hết RAM. Dừng stack hồi quy nặng, giữ nguyên kiểm tra nghiệp vụ, chạy riêng real-dependency test → PASS; chạy toàn bộ Recommendation lần cuối → 66/66 PASS. Không đổi test cũ để tạo PASS. Build server được dừng để giải phóng RAM; chỉ container/network của fixture vừa dừng được dọn, không xóa volumes ứng dụng.

Core browser E2E ban đầu phát hiện regression: stock check mới chặn cả phiếu Catalog khi hết hàng, khác semantics cũ cho phép gửi PENDING. Đã sửa source để phân biệt AI navigation state với Catalog query, chỉ chặn AI-prefilled selections thiếu kho; Catalog thông thường hiển thị cảnh báo và giữ workflow cũ. Bổ sung regression cases vào browser test mới, chạy lại npm tests/build/AI browser đều PASS; chạy lại nguyên core browser test cũ với services thật → PASS. Không sửa existing tests.

TRX local ở `.tmp/recommendation-tests/`, screenshots browser ở `.tmp/recommendation-browser/`, [JSON tổng hợp](evidence/recommendation-results.json). Các ảnh kết quả dùng mock fixtures và model `mock-browser-only`, không phải bằng chứng Gemini live. Báo cáo không dùng evidence checkpoint cũ để chứng nhận tính năng mới. Smoke checker ban đầu đọc application/problem+json thành byte[] trong PowerShell; đã sửa decode và chạy lại PASS, không thay contract backend để né kiểm tra.

## H. Demo

Theo [runbook PowerShell](../demo/RECOMMENDATION_DEMO.md): tự cấu hình Free tier key trong .env, build/start bonus, đăng nhập Student, gửi prompt tưới cây, chọn nhiều thiết bị thực tế, chuyển sang `/loans/new`, chọn hạn trả/số lượng, xem lại rồi gửi; xem history; tắt feature flag để demo AI_UNAVAILABLE và tiếp tục Catalog/Borrowing. Không giả định Catalog có bộ Arduino/cảm biến/máy bơm. Gemini không reserve/approve và không bảo đảm tồn kho.

## I. Remaining Work

- Người dùng tự xác minh project/model Free tier, đặt key locally, rồi thực hiện live Gemini + Student demo. Code/mock tests không chứng minh provider quota/region/key eligibility hoặc chất lượng tư vấn live.
- Toàn bộ 138 backend/E2E tests đã được xác minh qua các suite chạy riêng để phù hợp RAM máy. Lệnh tổng trong một process ban đầu bị dừng; không dùng kết quả đó để tuyên bố PASS. Browser AI dùng mocked API riêng; core browser dùng services thật. Live Gemini vẫn chưa chạy.
- Production public cần chính sách retention history/daily usage, quota vận hành phù hợp và runtime còn hỗ trợ; phạm vi task giữ .NET 8 như yêu cầu. Không tự xóa history hoặc bật dịch vụ trả phí.

## Danh sách file đầy đủ

- [.env.example](../../.env.example)
- [LabLendAI.sln](../../LabLendAI.sln)
- [README.md](../../README.md)
- [apps/web/e2e/recommendations.mjs](../../apps/web/e2e/recommendations.mjs)
- [apps/web/package.json](../../apps/web/package.json)
- [apps/web/src/App.tsx](../../apps/web/src/App.tsx)
- [apps/web/src/components/AppLayout.tsx](../../apps/web/src/components/AppLayout.tsx)
- [apps/web/src/components/Icon.tsx](../../apps/web/src/components/Icon.tsx)
- [apps/web/src/features/loans/LoanPages.tsx](../../apps/web/src/features/loans/LoanPages.tsx)
- [apps/web/src/features/loans/prefill.ts](../../apps/web/src/features/loans/prefill.ts)
- [apps/web/src/features/recommendations/RecommendationPages.tsx](../../apps/web/src/features/recommendations/RecommendationPages.tsx)
- [apps/web/src/features/recommendations/recommendationApi.ts](../../apps/web/src/features/recommendations/recommendationApi.ts)
- [apps/web/src/features/recommendations/recommendations.css](../../apps/web/src/features/recommendations/recommendations.css)
- [apps/web/src/lib/api.ts](../../apps/web/src/lib/api.ts)
- [apps/web/tests/recommendations.test.mjs](../../apps/web/tests/recommendations.test.mjs)
- [docker-compose.yml](../../docker-compose.yml)
- [docs/api/RECOMMENDATION_API_CONTRACT.md](../../docs/api/RECOMMENDATION_API_CONTRACT.md)
- [docs/api/REST_CONTRACTS.md](../../docs/api/REST_CONTRACTS.md)
- [docs/architecture/FINAL_ARCHITECTURE.md](../../docs/architecture/FINAL_ARCHITECTURE.md)
- [docs/demo/RECOMMENDATION_DEMO.md](../../docs/demo/RECOMMENDATION_DEMO.md)
- [docs/testing/RECOMMENDATION_IMPLEMENTATION.md](../../docs/testing/RECOMMENDATION_IMPLEMENTATION.md)
- [docs/testing/evidence/recommendation-results.json](../../docs/testing/evidence/recommendation-results.json)
- [scripts/Set-LocalEnvironment.ps1](../../scripts/Set-LocalEnvironment.ps1)
- [scripts/Test-RecommendationSmoke.ps1](../../scripts/Test-RecommendationSmoke.ps1)
- [src/Services/Recommendation/Recommendation.Api/Program.cs](../../src/Services/Recommendation/Recommendation.Api/Program.cs)
- [src/Services/Recommendation/Recommendation.Api/RecommendationExceptionFilter.cs](../../src/Services/Recommendation/Recommendation.Api/RecommendationExceptionFilter.cs)
- [src/Services/Recommendation/Recommendation.Api/RecommendationsController.cs](../../src/Services/Recommendation/Recommendation.Api/RecommendationsController.cs)
- [src/Services/Recommendation/Recommendation.Api/appsettings.json](../../src/Services/Recommendation/Recommendation.Api/appsettings.json)
- [src/Services/Recommendation/Recommendation.Application/Contracts.cs](../../src/Services/Recommendation/Recommendation.Application/Contracts.cs)
- [src/Services/Recommendation/Recommendation.Application/README.md](../../src/Services/Recommendation/Recommendation.Application/README.md)
- [src/Services/Recommendation/Recommendation.Application/RecommendationService.cs](../../src/Services/Recommendation/Recommendation.Application/RecommendationService.cs)
- [src/Services/Recommendation/Recommendation.Domain/README.md](../../src/Services/Recommendation/Recommendation.Domain/README.md)
- [src/Services/Recommendation/Recommendation.Domain/RecommendationSession.cs](../../src/Services/Recommendation/Recommendation.Domain/RecommendationSession.cs)
- [src/Services/Recommendation/Recommendation.Infrastructure/CatalogClient.cs](../../src/Services/Recommendation/Recommendation.Infrastructure/CatalogClient.cs)
- [src/Services/Recommendation/Recommendation.Infrastructure/DependencyInjection.cs](../../src/Services/Recommendation/Recommendation.Infrastructure/DependencyInjection.cs)
- [src/Services/Recommendation/Recommendation.Infrastructure/GeminiClient.cs](../../src/Services/Recommendation/Recommendation.Infrastructure/GeminiClient.cs)
- [src/Services/Recommendation/Recommendation.Infrastructure/InventoryAvailabilityClient.cs](../../src/Services/Recommendation/Recommendation.Infrastructure/InventoryAvailabilityClient.cs)
- [src/Services/Recommendation/Recommendation.Infrastructure/Migrations/20261117170224_InitialRecommendations.Designer.cs](../../src/Services/Recommendation/Recommendation.Infrastructure/Migrations/20261117170224_InitialRecommendations.Designer.cs)
- [src/Services/Recommendation/Recommendation.Infrastructure/Migrations/20261117170224_InitialRecommendations.cs](../../src/Services/Recommendation/Recommendation.Infrastructure/Migrations/20261117170224_InitialRecommendations.cs)
- [src/Services/Recommendation/Recommendation.Infrastructure/Migrations/README.md](../../src/Services/Recommendation/Recommendation.Infrastructure/Migrations/README.md)
- [src/Services/Recommendation/Recommendation.Infrastructure/Migrations/RecommendationDbContextModelSnapshot.cs](../../src/Services/Recommendation/Recommendation.Infrastructure/Migrations/RecommendationDbContextModelSnapshot.cs)
- [src/Services/Recommendation/Recommendation.Infrastructure/Recommendation.Infrastructure.csproj](../../src/Services/Recommendation/Recommendation.Infrastructure/Recommendation.Infrastructure.csproj)
- [src/Services/Recommendation/Recommendation.Infrastructure/RecommendationDbContext.cs](../../src/Services/Recommendation/Recommendation.Infrastructure/RecommendationDbContext.cs)
- [src/Services/Recommendation/Recommendation.Infrastructure/RecommendationDesignTimeFactory.cs](../../src/Services/Recommendation/Recommendation.Infrastructure/RecommendationDesignTimeFactory.cs)
- [src/Services/Recommendation/Recommendation.Infrastructure/RecommendationRepository.cs](../../src/Services/Recommendation/Recommendation.Infrastructure/RecommendationRepository.cs)
- [tests/Integration/Recommendation.Tests/DependencyTests.cs](../../tests/Integration/Recommendation.Tests/DependencyTests.cs)
- [tests/Integration/Recommendation.Tests/GeminiClientTests.cs](../../tests/Integration/Recommendation.Tests/GeminiClientTests.cs)
- [tests/Integration/Recommendation.Tests/RealDependencyFlowTests.cs](../../tests/Integration/Recommendation.Tests/RealDependencyFlowTests.cs)
- [tests/Integration/Recommendation.Tests/Recommendation.Tests.csproj](../../tests/Integration/Recommendation.Tests/Recommendation.Tests.csproj)
- [tests/Integration/Recommendation.Tests/RecommendationApiTests.cs](../../tests/Integration/Recommendation.Tests/RecommendationApiTests.cs)
- [tests/Integration/Recommendation.Tests/RecommendationDatabaseTests.cs](../../tests/Integration/Recommendation.Tests/RecommendationDatabaseTests.cs)
- [tests/Integration/Recommendation.Tests/RecommendationServiceTests.cs](../../tests/Integration/Recommendation.Tests/RecommendationServiceTests.cs)
- [tests/Integration/Recommendation.Tests/TestDoubles.cs](../../tests/Integration/Recommendation.Tests/TestDoubles.cs)
