# Sprint 0 acceptance

**PASS — Sprint 0 foundation đã được kiểm chứng local ngày 15/11/2026 (Asia/Bangkok).** Đây chưa phải nghiệm thu cuối môn. Nhánh: `chore/sprint0-foundation`; không merge main.

## Kết quả đã chạy

| Check | Result | Evidence |
|---|---|---|
| Repository audit | PASS | Main ban đầu sạch, HEAD 196a09e, remote xác minh; chưa có source/build |
| .NET SDK | PASS | global.json chọn 8.0.423; backend target net8.0 |
| dotnet restore LabLendAI.sln | PASS | 31 projects restore; EF transitive packages đồng bộ 8.0.34 |
| dotnet build -c Release | PASS | 0 warnings, 0 errors sau fix EF package conflict |
| dotnet test | PASS | 6 Unit + 5 in-process Gateway integration, 11 passed / 0 failed |
| npm ci | PASS | package-lock.json cài sạch, 0 vulnerabilities tại lần install |
| npm run build | PASS | TypeScript + Vite production bundle thành công |
| Compose config core + bonus | PASS | docker compose config --quiet và --profile bonus config --quiet |
| PostgreSQL/Redis startup | PASS | docker compose up -d --wait postgres redis; cả hai healthy |
| Linux solution build | PASS | Docker shared build stage: 31 projects, 0 warnings / 0 errors, --maxcpucount:2 |
| Full core/bonus image build | PASS | Build-Images.ps1 -IncludeBonus; cả 9 application images build thành công trong Docker VM khoảng 3 GB RAM |
| Core Compose up --build | PASS | docker compose up -d --build --wait --wait-timeout 180; 10 containers healthy |
| Core runtime script | PASS | Verify-Sprint0.ps1: readiness/schema/correlation của 5 service, frontend/nginx, actual standard gRPC, 6 own-DB logins và 30 cross-DB denials |
| Bonus host | PASS | --profile bonus start Recommendation; Verify-Sprint0.ps1 -IncludeBonus: 11 containers healthy; không truyền key/không gọi Gemini |
| Dependency failure and recovery | PASS | Stop Redis: Notification /ready=503, /live=200 qua Gateway; restart Redis: /ready=200; tất cả 11 hosts/dependencies phục hồi healthy |
| Database bootstrap idempotency | PASS | Chạy lại 01-init-databases.sh, không DROP DB/volume; ownership/isolation checks pass |
| EF migration tooling | PASS | dotnet tool restore 8.0.34; dotnet ef dbcontext info --configuration Release nhận CatalogDbContext, Npgsql, catalog_db |
| Secret and ignore checks | PASS | .env/build outputs ignored; so khớp 10 secret values vừa sinh với source dự kiến commit, không thấy giá trị nào |
| GitHub Actions remote run | NOT RUN at local sign-off | Workflow sẵn sàng; hosted status xem tab Actions sau khi push, không suy diễn PASS từ local tests |

Lỗi phát hiện và đã sửa: EF Core transitive 8.0.11 vs 8.0.34 gây assembly warnings → central transitive pinning; test host đọc JWT key quá sớm → bound options ValidateOnStart; EF tools cần Design reference ở startup host → thêm PrivateAssets=all vào cả 6 host; Docker entrypoint JSON quoting sai → sửa và kiểm chứng process startup/health thực; PowerShell pipe thêm CR vào bash stdin → chuyển isolation check qua docker compose cp. Docker VM ít RAM → build tuần tự, NuGet cache và compilation stage chung, không tự thay resource settings.

Raw runtime output đã kiểm tra không chứa secret: [core](evidence/runtime-core.txt), [bonus](evidence/runtime-bonus.txt), [dependency failure](evidence/dependency-failure.txt). Các output này là foundation checks, không phải business acceptance T01–T28.

## Definition of done và phạm vi

- Implemented: cấu trúc repo, SDK/packages, host composition/DI/logging/errors, JWT validation/policies, Gateway/CORS/rate limit, environment scripts, technical tests, docs/Git workflow.
- Scaffolded: domain/application/infrastructure boundaries, empty EF contexts, standard gRPC health, worker heartbeat, React entry point.
- Planned: CRUD/search, Identity issuance/refresh, Borrowing lifecycle, allocation proto/handlers, service-to-service business auth, Outbox/Streams/Inbox, overdue/reconciliation jobs, migrations/seed, business UI và Gemini.
- Open review: OD-01–OD-10. Chưa chốt contract thuộc M2/M3; các đề xuất chưa phải schema phát hành.

## Definition of done

| Sprint 0 exit criterion | Status |
|---|---|
| Cấu trúc repo chuẩn và 31 projects không trùng lặp | PASS |
| .NET 8 restore/build | PASS |
| React clean install/build | PASS |
| PostgreSQL + Redis local actual health | PASS |
| Gateway routing và health | PASS |
| EF Core/database ownership standards | PASS (migrations nghiệp vụ Planned) |
| JWT/REST/error/logging standards | PASS foundation (issuance/business auth Planned) |
| gRPC folder/guide và proposal không chốt thay M2 | PASS |
| Redis event guide/proposal chờ M1/M3 review | PASS |
| .gitignore/.env.example/README | PASS |
| GitHub Issues/PR templates và CI source | PASS; hosted run chưa chạy tại local sign-off |
| Handover M2/M3 và next sprint ownership | PASS |
| Source không chứa 10 local generated secrets | PASS |
| Không tạo paid resource, không gọi AI/billing | PASS |

Không còn blocker local cho Sprint 0. Các business contracts vẫn Open theo chủ đích của scope, không phải implementation đã được nghiệm thu.

Business P0 tests T01–T18/T20–T24/T27–T28 chưa được báo PASS ở Sprint 0; platform checks chỉ kiểm tra nền tảng. AI bonus không gọi provider, không billing. Handover và suggested issues: docs/team/TEAM_RESPONSIBILITIES.md.
