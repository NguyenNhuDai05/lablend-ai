# Final Demo — M3 P0

Prepared 2026-11-16. Target: about 10 minutes plus Q&A. This is a runnable defense script and rehearsal checklist, not a statement that all members have rehearsed or approved the submission. Gemini and dashboard are deferred by the user. Do not turn on billing or use paid services for this demo.

## Preparation

Use the repository's existing local environment setup; do not paste `.env`, JWTs, service credentials or passwords into slides, terminal output or recordings. Open `http://localhost:3000` in separate Student, Staff and Admin browser profiles/private sessions. Use authorized local demo accounts; bootstrap secrets stay local. Create two physical units in Staff Inventory with a unique demo asset code, using existing Catalog types. Do not overwrite other members' data. A normal live request should have a comfortable future due date; the automated isolated test uses a short deadline only for job evidence.

Readiness commands from the repo root (PowerShell):

```powershell
docker compose ps
docker compose config --quiet
Invoke-RestMethod http://localhost:5080/health/live
dotnet build LabLendAI.sln -c Release --no-restore
$env:LABLEND_POSTGRES_TESTS='1'
$env:LABLEND_BROWSER_TESTS='1'
# Windows with installed Edge only; omit this on CI/Linux with Playwright Chromium.
$env:LABLEND_BROWSER_CHANNEL='msedge'
dotnet test LabLendAI.sln -c Release --no-build --no-restore
Push-Location apps/web
npm run build
npm test
Pop-Location
```

Install npm dependencies using `npm ci` and Playwright browser using `npx playwright install chromium` inside `apps/web` before the first E2E run. The browser test starts real APIs, worker, gateway, temporary Vite server and fixture-owned PostgreSQL/Redis. It preserves database volumes at teardown. Do not run `down -v`, volume prune, hard reset or force push. Test evidence is generated in ignored `.tmp/m3-browser`; only sanitized screenshots/logs are suitable for submission.

The example URLs use the repository defaults (`WEB_PORT=3000`, `GATEWAY_PORT=5080`); use the configured local ports if changed. Swagger through the Gateway is available at `/platform/{identity|catalog|inventory|borrowing|notification}/openapi.json`. `scripts/Verify-Sprint2.ps1 -IncludeBonus` reads configured ports and prints safe read-only runtime evidence, including Redis XINFO and worker job passes.

## Seven required defense sections

| Section | Presenter cue / time | Concrete demonstration and proof |
|---|---|---|
| 1. Architecture | 60s | React→YARP→five business REST hosts, independent Inventory gRPC, Worker, Redis Streams, isolated PostgreSQL databases. Explain local transactions and no cross-service SQL joins. Recommendation is deferred bonus. |
| 2. REST | 90s | Guest `/catalog`: search/filter/sort; detail availability. Student login returns to new request. Purpose 10–2000, 1–20 physical units, due ≤30 days sent UTC. Show validation, list and history. Staff Catalog/Inventory CRUD remains available. |
| 3. Background jobs | 60s | Four jobs: reserve-hold expiry, operation reconciliation, due-soon and overdue. Show actual test output and Notification entries from the private fixture; describe unique reminder keys. Do not edit the shared database clock or claim a 30-day timer fired live. |
| 4. Broker | 75s | Business+Outbox commit atomically; publisher XADD; consumer group persists Notification+Inbox before XACK; pending reclaim and parking. Show real Redis outage/replay test evidence. Say “at least once”; duplicates are deduplicated by eventId. OD-04/05 and retention remain provisional. |
| 5. gRPC | 75s | Staff approve→reserve all-or-nothing, handover→ON_LOAN, partial return DAMAGED→MAINTENANCE, remaining NORMAL→AVAILABLE. Show staff item IDs/history and real Inventory state. Stable operation ID, deadline/service credential, recovery on ambiguity. Explain the open historical-return/reservation-ID gap in v1. |
| 6. Deployment | 60s | Show `docker compose ps`, gateway readiness/Swagger and web. All shared volumes stay intact. Existing-stack upgrade verification is distinct from M2 fresh-install acceptance T20. |
| 7. End-to-end | 180s | Student request→Staff approval/handover→Student approved/due/overdue notifications→mark read→Staff partial/full return→Student return notice. Also show pending owner cancellation, Student denied Staff/Admin, profile edit/password re-login, Admin last-active-admin 409 and reversible activation/role controls using authorized demo accounts. |

## Click sequence

1. Guest: `/catalog` → search → detail → “Đăng nhập để mượn”. Login returns to `/loans/new?equipmentTypeId=...`.
2. Student: choose quantity 2, enter purpose and future due date, submit once. Record loan ID. Open history and `/loans` status filter. A separate pending request demonstrates cancel confirmation.
3. Staff: `/staff/loans` → exact loan → “Duyệt phiếu” → confirmation → “Bàn giao thiết bị” → confirmation. Check Inventory assets and movement history on the existing Staff pages.
4. Student: `/notifications`, verify title and linked loan, mark read and observe badge. Polling can take 15 seconds; use “Tải lại” if needed. No synthetic messages are inserted.
5. Staff: select only the first borrowed item as DAMAGED, submit return; confirm other item is still borrowed. Select remaining item NORMAL; loan becomes RETURNED. Student sees return notification.
6. Admin: `/admin/users` pages/filter (filter applies to current page), user detail, controlled role/active actions. Avoid locking the actual presenters out. Last active admin operation is rejected by server; the private browser test proves 409 safely.
7. Profile: edit display name. Password change ends the current local session and revokes refresh families; re-login. Previously issued access JWTs in other services can survive until their short expiry, so do not claim immediate global JWT invalidation.

## Failure handling and Q&A

- 401: sign in again; 403: correct account/role. Never bypass authorization for the presentation.
- 404: use the right owner and loan ID. UI does not leak another Student's loan.
- 409: reload state/stock and explain conflict handling. Do not resubmit a mutation blindly after a timeout; first inspect My Loans/history.
- 503: retain the business record/outbox, show real recovery test output and retry after the service is healthy. Do not manufacture a success screenshot.
- Long job wait: use recorded automated evidence, identify it as an isolated real-service run, and explain the configured threshold. No claim that a human rehearsal/video happened if it has not.

Likely questions: Why gRPC for allocation? Why no distributed transaction? What happens between XADD and published marker? Between Inbox commit and XACK? Why can one partial return trigger a notification while loan remains BORROWED? How do two workers avoid duplicate reminder effects? What is still provisional? Answers and code pointers: [architecture](../architecture/ARCHITECTURE_OVERVIEW.md), [Borrowing API](../api/BORROWING_API_CONTRACT.md), [M3 event review](../events/M3_CONTRACT_REVIEW.md), [open decisions](../architecture/OPEN_DECISIONS.md).

## Human rehearsal checklist (pending)

- Assign presenters for all seven sections; one operator and one fallback operator.
- Run the exact click sequence on the presentation machine; record actual duration and failures.
- Verify projection readability, local accounts, network-free fallback screenshots, and the current CI commit.
- Confirm team-owned contract/retention decisions separately; do not sign on absent members' behalf.
- Capture a video only if required by the course/team, without secrets or personal data. No video is fabricated here.
