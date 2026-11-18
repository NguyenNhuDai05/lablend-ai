# Gemini recommendation on Windows PowerShell

The source is implemented; a live Gemini result requires your own eligible Free tier project, key, quota and internet. Mock tests are not evidence of a live provider call. Never paste the key into chat or put it in `VITE_*` settings.

## Provider choice and free configuration

Official documentation reviewed on 17–18 November 2026:

- [Gemini docs](https://ai.google.dev/gemini-api/docs)
- [Structured output, generateContent REST](https://aistudio.google.com/docs/structured-output?generate-content=true)
- [Interactions status](https://aistudio.google.com/docs/interactions-overview): GA and recommended for new projects; `generateContent` remains fully supported. This implementation follows Master Plan V4 using stateless `generateContent`, with no conversation storage or agent tools.
- [Pricing](https://aistudio.google.com/docs/pricing#gemini-3.5-flash-lite) and [model capabilities](https://aistudio.google.com/docs/models/gemini-3.5-flash-lite): `gemini-3.5-flash-lite` has free input/output in the Standard Free tier and structured output support at review time. Model/project eligibility can change.
- [Rate limits](https://aistudio.google.com/docs/rate-limits): actual limits are project/model specific; check AI Studio. Local limits do not replace Google's shared project quota.

1. Open [Google AI Studio API keys](https://aistudio.google.com/apikey). Select/create your own project in the Free tier without linking a billing account, card or paid tier. If free access is unavailable, leave AI disabled.
2. Confirm the chosen model's Free tier eligibility and your project's active limits in AI Studio before making any call. The application cannot infer a project's billing status from an API key. Rate/token limits alone cannot guarantee zero spending for a key attached to a paid project.
3. Create the key yourself, then edit the ignored repository `.env` locally. Free tier content may be used to improve Google products; submit only non-sensitive experiment descriptions and public Catalog data.

```dotenv
GEMINI_ENABLED=true
GEMINI_API_KEY=your_private_local_key
GEMINI_MODEL=gemini-3.5-flash-lite
GEMINI_TIMEOUT_SECONDS=30
GEMINI_MAX_OUTPUT_TOKENS=4096
RECOMMENDATION_DAILY_LIMIT=20
```

These map to backend `Gemini__Enabled`, `Gemini__ApiKey`, `Gemini__Model`, `Gemini__TimeoutSeconds`, `Gemini__MaxOutputTokens` and `Recommendation__DailyLimit`. `.env.example` contains no real key. Never print `docker compose config` without `--quiet` on a machine with real secrets.

## Build and startup

Open Docker Desktop in Linux container mode. In PowerShell 7:

```powershell
Set-Location D:\PRN232\Assigment\lablend-ai
./scripts/Initialize-Environment.ps1 # preserves an existing .env
docker compose --profile bonus config --quiet
./scripts/Build-Images.ps1 -IncludeBonus
./scripts/Start-Core.ps1 -IncludeBonus
docker compose --profile bonus ps
```

Core startup remains independent of the bonus host. Recommendation joins the private backend network for Catalog/Inventory/PostgreSQL and the edge network for outbound Google HTTPS; no additional public port is published. Gateway already routes the Recommendation prefix to this host. Gemini disabled/missing key does not fail service health or core startup.

Compose explicitly applies Recommendation's real EF migrations at startup. Tables: RecommendationSessions, RecommendationItems, RecommendationDailyUsage. There is no EnsureCreated or database reset. For controlled production rollout, set `Database__AutoMigrate=false` and apply migrations as a separate deployment operation. For local SDK development:

```powershell
docker compose -f docker-compose.yml -f infrastructure/docker/compose.dev.yml up -d --wait postgres redis
. ./scripts/Set-LocalEnvironment.ps1 -Service recommendation
dotnet tool restore
dotnet ef database update --project src/Services/Recommendation/Recommendation.Infrastructure --startup-project src/Services/Recommendation/Recommendation.Api
./scripts/Run-LocalHost.ps1 -Service recommendation
```

Catalog (5102), Inventory (REST 5103 / gRPC 5181), Identity and Gateway must also run in the chosen local mode. Service token is loaded from the existing local environment, never taken from the Student JWT. Startup migration defaults on only in Development; other environments need explicit configuration or the EF command.

## Four-part demonstration

1. Sign in as an existing Student at [local web](http://localhost:3000). Select **AI tư vấn thiết bị**, enter “Tôi muốn làm hệ thống tưới cây tự động có thể đo độ ẩm đất, điều khiển máy bơm và hiển thị dữ liệu.” Add a beginner constraint if desired. Submit. Results must reflect actual active Catalog records; no assumption that the lab owns Arduino, sensors or pumps. An empty result is legitimate.
2. Read reasons, usage notes and current availability. Select one or several available equipment types and press **Tiếp tục tạo phiếu mượn**. Availability is a read-only snapshot, not a reservation.
3. Confirm the prefilled purpose and selected types in `/loans/new`; choose a due date, adjust quantities (≤20 units), review and explicitly send. This calls Borrowing exactly as the existing flow does. Staff approval/handover/return continues unchanged. Open AI history and a previous session to refresh its stock values.
4. Set `GEMINI_ENABLED=false` locally and recreate only the optional host:

```powershell
docker compose --profile bonus up -d --no-build --wait recommendation
```

Submit another AI request. The page must show “AI tư vấn hiện tạm thời không khả dụng. Bạn vẫn có thể tìm và mượn thiết bị qua danh mục.” Follow **Xem danh mục** and continue the ordinary borrowing flow. This does not clear existing history.

## Troubleshooting and tests

`AI_UNAVAILABLE`: check enabled/key/model without printing secrets; check internet, AI Studio Free tier quota and timeout. Do not add billing to resolve quota. `AI_INVALID_RESPONSE`: retry manually; malformed/truncated/unknown IDs are rejected and not saved. `CATALOG_UNAVAILABLE`: check Catalog health. `INVENTORY_UNAVAILABLE`: check Inventory readiness, HTTP/2 internal address and service token; counts are never fabricated. `VALIDATION_ERROR` for oversized scope: choose a category. `RATE_LIMITED`: wait one minute, or until the next UTC day for the daily limit. Report the correlation ID when debugging.

```powershell
dotnet restore LabLendAI.sln
dotnet build LabLendAI.sln -c Release
$env:LABLEND_POSTGRES_TESTS = '1' # isolated test containers; never production/.env DB
dotnet test LabLendAI.sln -c Release --no-build --no-restore
Remove-Item Env:LABLEND_POSTGRES_TESTS
Push-Location apps/web
npm ci
npm test
npm run test:recommendations # Playwright with labelled mock API fixtures, no provider/network AI
npm run build
Pop-Location
docker compose --profile bonus config --quiet
./scripts/Test-RecommendationSmoke.ps1 # after building recommendation image; isolated DB, Gemini disabled
```

Playwright requires a browser installed by Playwright; if unavailable, install its Chromium browser or set `LABLEND_BROWSER_CHANNEL=msedge` for installed Edge. Browser screenshots are written under `.tmp/recommendation-browser`. Mock tests never contact Google. Full backend tests include real PostgreSQL/HTTP/HTTP2 dependencies when Docker is enabled; the new real-dependency test mocks only Gemini HTTP. Live provider and real Student demo remain separately reported in the implementation evidence.
