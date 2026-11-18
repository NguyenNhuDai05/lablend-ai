# Gemini live acceptance ngày 18 tháng 11 năm 2026

**Luồng chức năng Gemini live → chọn thiết bị → Borrowing → history đã PASS trong môi trường nghiệm thu riêng.** Lượt đầu Google trả HTTP 503 và được xử lý đúng. Sau khi người dùng trả lời **“Cho phép thêm 1 lượt”**, lượt bổ sung trả Google **HTTP 200**, Recommendation **HTTP 201**, 4 gợi ý hợp lệ; chọn 3 loại/4 đơn vị và tạo phiếu Borrowing thật. Tổng cộng đúng **2** lượt tạo nội dung, không retry tự động, không thay key và không bật billing. Trạng thái billing/Free Tier của tài khoản vẫn dựa trên xác nhận của người dùng, chưa audit độc lập trong Console.

## Kết quả bắt buộc

| Hạng mục | Trạng thái | Bằng chứng |
|---|---|---|
| Gemini API key configured | PASS | **API key: Configured**; `.env` được Git ignore; không thay giá trị key; không xuất key ra báo cáo/terminal |
| Free Tier project | PASS | Điều kiện được người dùng xác nhận trực tiếp: project Free Tier, không billing trả phí; model có Standard Free Tier theo tài liệu Google, agent không tạo/bật billing. Đây là xác nhận của chủ tài khoản, không phải audit độc lập trong Console; metadata/inference HTTP 200 không được dùng để suy ra trạng thái billing |
| Recommendation container healthy | PASS | Main có 11 container healthy; Recommendation dùng image mới, đúng model/flag/key/config; `/platform/recommendation/health/ready` HTTP 200 |
| Live Gemini HTTP call | PASS | Hai lượt được phép: lần đầu HTTP 503; lần bổ sung HTTP **200**, event 4301, correlation `3c4e4f67-0897-4e65-b849-fef6b54323b5`; đều đi qua backend thật |
| Catalog integration | PASS | Catalog REST/PostgreSQL thật của fixture có 5 active records; metadata không mock; core Catalog qua Gateway main HTTP 200 |
| Inventory integration | PASS | Backend enrichment live dùng CheckAvailability gRPC thật; availability của 4 gợi ý khớp REST Inventory. Reservations=0 sau tư vấn và phiếu PENDING. Main REST/gRPC health đều hoạt động |
| Recommendation database | PASS | Main có 3 tables qua EF migration. Fixture lỗi: 0 sessions/0 items/1 attempt. Fixture thành công: **1 session/4 items/1 attempt**, xác nhận PostgreSQL thật |
| Recommendation frontend | PASS | Production Docker UI: Student login, validation/loading, lỗi lượt đầu và **4 result cards live** lượt sau, metadata/specs/stock khớp service thật |
| Multi-select → Create Loan | PASS | Từ 4 gợi ý Gemini thật chọn **3 loại**, đổi quantity thành **4 đơn vị**, chỉnh due date, review rồi Borrowing HTTP **201**; detail/list xác nhận phiếu thật trong fixture riêng |
| Recommendation history | PASS | Session live có trong history API/UI; Student khác đăng nhập Identity thật nhận 404 detail và history riêng rỗng. Đã kiểm tra lưu/xem lại bằng browser |
| Authorization | PASS | Fixture thật: anonymous 401; JWT LAB_STAFF/ADMIN 403 trước provider; Student đăng nhập Identity thật và gửi request. Other-owner access được kiểm tra bằng automated tests |
| Automated tests | PASS | Lượt solution mới 138 PASS/1 browser timeout; retry riêng đúng bài browser với nguyên assertions/timeouts: 1 PASS, 0 fail/skip, 3m25s. Tổng 139 distinct tests được xác minh trong lượt này, gồm Recommendation 67/67; frontend 30 PASS + mock browser/build PASS |

## Phân biệt bốn loại kết quả

| Kiểm tra | Trạng thái | Kết quả thực tế |
|---|---|---|
| Google model metadata | PASS | GET model HTTP 200; `models/gemini-3.5-flash-lite`, hỗ trợ generateContent. Đây là metadata read, không phải inference thành công |
| Provider HTTP success | PASS | Lượt bổ sung HTTP **200**, sau lượt đầu HTTP 503; không đổi provider/model/key giữa hai lượt |
| Recommendation API success | PASS | HTTP **201** và successful RecommendationResponse, session `2d75f87e-8e56-49c0-a502-e26dd5b664ea` |
| Recommendation business result live | PASS | 4 IDs active thuộc Catalog thật, không trùng; tên/specs khớp Catalog, stock khớp Inventory; đây là JSON thật từ Gemini |
| Core Borrowing sau AI failure | PASS | Catalog thường → 2 thiết bị → review → POST Borrowing HTTP 201 → detail/list; không gửi Recommendation POST thêm |
| Mocked AI automated tests | PASS | Recommendation 67/67 PASS, gồm fake Gemini HTTP và real PostgreSQL/Catalog/Inventory dependencies; không gọi Google |

## Môi trường và bằng chứng

Hệ thống chính: [frontend](http://localhost:3000), [Gateway readiness](http://localhost:5080/health/ready), [Recommendation readiness](http://localhost:5080/platform/recommendation/health/ready). Đã recreate riêng Recommendation và frontend; giữ các core container/database/volumes đang dùng. Migration EF tự áp dụng; không tạo bảng thủ công hoặc xóa dữ liệu.

Live/ghi dữ liệu chạy trên hai project Docker riêng: `lablend-gemini-live-cfd6026e2a` (lượt đầu lỗi + core fallback) và `lablend-gemini-live-572cee811d` (lượt bổ sung được người dùng cho phép). Mỗi project có ports, 6 database và tài khoản nghiệm thu riêng, dùng cùng Docker images với main. Catalog được seed bằng migration thật, không mock REST. Credentials/key chỉ nằm ở file ignored `.tmp/...`; Node/browser harness không nhận Google API key. Chỉ backend Recommendation gọi Google. Cả hai fixture đã dừng; volumes giữ lại, không chạy `down -v`.

Phiếu thật được tạo trong fixture: **`a805c1b0-d0a2-461d-9e3f-26babd1e7932`**, 2 loan items, trạng thái PENDING. EF table `LoanRequests`=1, `LoanItems`=2; Inventory reservations=0. Phiếu này được chọn từ Catalog thường để chứng minh core hoạt động sau lỗi AI, **không phải kết quả luồng Gemini thành công**, và không nằm trong database đang dùng.

Phiếu từ **kết quả Gemini live** ở lượt bổ sung: **`387699cf-f600-4de8-9630-bd13c3a8a780`**, 3 loại/4 loan items, PENDING. Session **`2d75f87e-8e56-49c0-a502-e26dd5b664ea`** lưu 4 RecommendationItems. SQL xác nhận sessions=1, recommendation items=4, daily attempts=1, loans=1, loan items=4, reservations=0. Không tạo phiếu hay session nghiệm thu trong database main.

Bằng chứng đã lọc secrets:

- [Provider HTTP status/correlation](evidence/gemini-live-2026-11-18/provider-status.json): 1 structured event, status 503. Trong log JSON, cùng thông điệp có thể xuất hiện ở Message và State; không đếm hai chuỗi đó thành hai request.
- [Live browser result](evidence/gemini-live-2026-11-18/live-ui-results.json): generationRequests=1, API status 503, không recommendationId/loanId từ AI.
- [Core Borrowing sau lỗi AI](evidence/gemini-live-2026-11-18/core-after-ai-failure.json): HTTP 201, 2 items, recommendationPosts=0.
- [Ảnh lỗi AI thực tế](evidence/gemini-live-2026-11-18/02-provider-error.png).
- [Ảnh phiếu Catalog/Borrowing thật trong fixture](evidence/gemini-live-2026-11-18/05-core-loan-created.png).
- [Lượt bổ sung: live browser result](evidence/gemini-live-2026-11-18/retry-approved/live-ui-results.json) và [Google HTTP 200 đã lọc secrets](evidence/gemini-live-2026-11-18/retry-approved/provider-status.json).
- [4 gợi ý Gemini thật](evidence/gemini-live-2026-11-18/retry-approved/02-live-results.png), [form prefill](evidence/gemini-live-2026-11-18/retry-approved/03-prefilled-loan.png), [bước review](evidence/gemini-live-2026-11-18/retry-approved/04-review.png), [phiếu thật từ AI](evidence/gemini-live-2026-11-18/retry-approved/05-created-loan.png), [history](evidence/gemini-live-2026-11-18/retry-approved/06-history.png).
- [JSON tổng hợp hai lượt và automated tests](evidence/gemini-live-2026-11-18/acceptance-summary.json).

Đã quét key trên các file Git-visible dạng text, bundle frontend được phục vụ thực tế và log tất cả services của live fixture: **không có key**. Không lưu raw provider error body hoặc JWT/password trong các evidence công khai. Không thay key trong `.env` khi sửa model.

## Lỗi tìm được và xử lý

1. `GEMINI_MODEL` ban đầu là tên hiển thị `Gemini 3.5 Flash Lite`, không phải identifier API. **Đã sửa** riêng dòng đó thành `gemini-3.5-flash-lite`, sau khi metadata Google và tài liệu chính thức xác nhận. Key giữ nguyên.
2. Container Recommendation đang dùng skeleton image cũ và chỉ có backend network, chưa nhận cấu hình mới. **Đã sửa** bằng rebuild/recreate riêng Recommendation; outbound edge network, Catalog, Inventory credential, database và feature settings đã đúng.
3. Frontend đang dùng image cũ. **Đã sửa** bằng build/recreate web; route tư vấn đã xuất hiện trong bundle Docker đang phục vụ.
4. Trước đây không có bằng chứng tách biệt HTTP response của Google với lỗi API. **Đã bổ sung** structured event 4301 chỉ chứa HTTP status, có correlation scope; thêm test không log key/prompt/raw response. Không thay cơ chế retry hoặc phát sinh thêm request.
5. Helper nghiệm thu ban đầu tạo YAML override sai do nối mảng PowerShell. **Đã sửa helper** trước khi gửi request Google; không ảnh hưởng production source/data.
6. **Google trả 503 ở lượt đầu:** không suy đoán lỗi key/model/quota chỉ từ status. Sau khi người dùng cho phép thêm đúng một lượt, cùng model/key/cấu hình đã nhận HTTP 200 và toàn bộ luồng live PASS. 503 không tái hiện ở lượt bổ sung; không có retry tự động hoặc thay billing. Không cam kết Google sẽ không gặp 503 trong tương lai.
7. Docker npm install phát hiện 4 high audit findings cùng chuỗi dev dependency picomatch/fdir/tinyglobby/Vite. Registry hiện có picomatch 4.0.7 và advisory chưa có patched version; không force downgrade Vite hoặc tự sửa dependency implementation. `npm audit --omit=dev` có 0 vulnerabilities; final nginx image phục vụ dist, không cài Node build tools. Audit full vẫn cần được theo dõi riêng, không báo security audit PASS.

## Files changed trong lượt nghiệm thu này

- `.env` (ignored): chỉ sửa identifier model, không ghi key vào Git.
- `src/Services/Recommendation/Recommendation.Infrastructure/GeminiClient.cs`: safe provider status log.
- `tests/Integration/Recommendation.Tests/GeminiClientTests.cs`: regression test cho log không chứa secrets/prompt/body.
- `scripts/Initialize-GeminiLiveAcceptance.ps1`: tạo cấu hình/tài khoản test riêng; không tự gọi Google.
- `scripts/Start-GeminiLiveAcceptance.ps1`: reuse Start-Core với project/environment riêng, khôi phục process env khi xong.
- `apps/web/e2e/gemini-live.mjs`: opt-in một lượt live tối đa, persistent attempt marker, không mock response, evidence đã lọc.
- `apps/web/e2e/gemini-unavailable-core.mjs`: chặn mọi tư vấn mới, chứng minh core Borrowing thật hoạt động khi AI thất bại.
- Báo cáo này và `docs/testing/evidence/gemini-live-2026-11-18/`.
- `README.md`: liên kết tới báo cáo live, phân biệt metadata/mock với inference thật.

Các thay đổi triển khai từ lượt trước và `docs/planning/` của người dùng được giữ nguyên; không sửa test cũ để tạo PASS, không commit/push.

## Commands và automated verification

Đã chạy build .NET Release: 0 errors, 32 NU1900 warnings trên host do không đọc được NuGet vulnerability feed; build solution trong Docker 0 errors/0 warnings. `npm test`: 30 PASS; `npm run test:recommendations`: PASS với mock HTTP được ghi rõ; `npm run build`: PASS. `docker compose --profile bonus config --quiet`: PASS. Recommendation và web image build PASS; 11 main services healthy.

Lượt solution mới với `LABLEND_POSTGRES_TESTS=1`, `LABLEND_BROWSER_TESTS=1`, `dotnet test LabLendAI.sln -c Release --no-restore --no-build -m:1 -nr:false`: Unit 20 PASS, Gateway 10 PASS, Catalog 3 PASS, Inventory 11 PASS, Sprint1 27 PASS + 1 browser timeout, Recommendation 67 PASS. Tổng 138 PASS/1 FAIL. Browser timeout chờ response Staff login, khi máy còn khoảng 8 MB RAM trống; không đổi assertion/timeouts để né lỗi. Chạy lại riêng nguyên `Member3BrowserTests`: 1 PASS, 0 fail/skip, 3 phút 25 giây. Tổng cuối 139 distinct tests được xác minh, nhưng không nói lệnh aggregate ban đầu đã PASS. TRX ở `.tmp/gemini-live-tests/`.

Negative cases dùng fake/mock hoặc isolated test containers: disabled, missing key, timeout, invalid JSON, unknown/duplicate IDs, Catalog/Inventory unavailable, rate limit, unauthorized/other-owner access. Không chủ động tắt/phá service đang dùng. Không có real Google call trong automated tests.

## Chạy hệ thống và bước demo cuối

PowerShell 7, từ repo:

```powershell
Set-Location D:\PRN232\Assigment\lablend-ai
./scripts/Initialize-Environment.ps1 # giữ nguyên .env đang có
docker compose --profile bonus config --quiet
./scripts/Start-Core.ps1 -IncludeBonus
docker compose --profile bonus ps
# Khi chỉ thay Gemini config:
docker compose --profile bonus up -d --no-build --wait recommendation
```

Hệ thống hiện đã chạy, không cần build toàn bộ lại. Nếu thay source, dùng `./scripts/Build-Images.ps1 -IncludeBonus` trước Start-Core. Không in `.env` hoặc chạy Compose config thiếu `--quiet`.

Luồng live đã được nghiệm thu PASS trong fixture riêng. Để demo trên dữ liệu/tài khoản chính của bạn, tự kiểm tra **Free Tier/verification/quota** trong AI Studio, giữ billing tắt; đăng nhập Student tại [trang tư vấn](http://localhost:3000/recommendations), mô tả thí nghiệm phù hợp Catalog thật (ví dụ mạch lọc RC điện áp thấp, tạo/quan sát tín hiệu và đo điện áp), rồi chọn thiết bị khả dụng → chỉnh số lượng/hạn trả → review → gửi → xem history. Đây là bước ghi dữ liệu main do bạn chủ động thực hiện; agent đã không tạo phiếu thử ở main. Không cần gửi key trong chat hoặc đổi key chỉ vì lượt đầu từng trả 503.

## Nguồn đối chiếu

- [Model identifier và structured output support](https://aistudio.google.com/docs/models/gemini-3.5-flash-lite).
- [Standard Free tier pricing của model](https://aistudio.google.com/docs/pricing#gemini-3.5-flash-lite).
- [Troubleshooting Google](https://aistudio.google.com/docs/troubleshooting): metadata endpoints không xác nhận tier eligibility/billing; lỗi 503 cần phân biệt với metadata success. Task này không tự retry do giới hạn một lượt.
- [Structured REST output contract](https://aistudio.google.com/docs/structured-output?generate-content=true).
- [picomatch advisory](https://github.com/advisories/GHSA-fc5q-39xc-vjpf): affected ≤4.0.7, patched versions chưa có tại thời điểm kiểm tra.
