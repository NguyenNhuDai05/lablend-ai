# Member 3 P0 acceptance — 2026-11-16

Scope authorized by repository owner: P0 first, Gemini/dashboard deferred. Reference document: the supplied M2/M3 gap analysis. Baseline main `06778313b4c9e9de61fd6122343ddea0a3f11a80`; feature branch `feature/m3-p0-user-workflows`. This report distinguishes implementation, automated evidence and pending human decisions.

## Gates

Executed local results: **68 backend PASS, 21 frontend PASS, 0 FAIL, 0 SKIPPED**. Full Release build has 0 errors and 16 NU1900 warnings because NuGet vulnerability-audit data could not be fetched on this machine. Frontend production build passed. All seven event cases and the real-service browser test passed in the full solution run. Evidence: [individual test outcomes](evidence/m3-local-gates.txt), [backend output](evidence/m3-backend.txt), [Release output](evidence/m3-release-build.txt). Branch CI is evaluated separately after push; no result from an older commit is substituted.

| Gate | What it proves |
|---|---|
| Release build | Entire solution compiles; report any restore/audit-network warnings separately |
| Frontend build + 21 tests | Existing 11 auth regressions plus validation boundaries, UTC, quantity expansion, no client ownership fields, no network mutation retry, single-flight double click, cancellation, safe return URL, Admin/Profile contract and recoverable errors |
| Seven event compatibility cases | Actual producer envelopes map through current consumer; recipient/loan/event IDs and minimized payload agree. In-process test, not Redis E2E. |
| Real-service browser | Private PostgreSQL/Redis, five APIs, gateway, worker and React. Guest catalog→login→Student create, double click, Staff approve/handover, actual Inventory ON_LOAN, Student route denial, actual due-soon/overdue/read notifications, pending cancel, damaged partial+normal full return, Admin last-admin conflict/role/active, profile/password re-login, mobile/no token persistence |
| Existing integration suite | M1 REST/security/lifecycle/recovery/concurrency/outbox/jobs, M2 Catalog/Inventory HTTP2 allocation and platform isolation remain green |
| Docker image/runtime | Updated web image **BUILD PASS**. Final shared-stack runtime recheck is **NOT PASSED**: worker exited 139 during restart, then the Docker Desktop daemon became unavailable after the pause. No healthy-container claim is made. Earlier private real-service browser/integration gates passed; they do not replace the current shared-stack recheck. |

Runbook: [commands and seven-section demo](../demo/FINAL_DEMO_RUNBOOK.md). `LABLEND_POSTGRES_TESTS=1` and `LABLEND_BROWSER_TESTS=1` are both required for the browser gate; otherwise xUnit reports SKIPPED. Windows may set `LABLEND_BROWSER_CHANNEL=msedge`; CI uses installed Playwright Chromium. No browser user profile is reused.

## Evidence handling

The browser test creates accounts and data only in its generated private fixture. Credentials/tokens are transient environment values and are not printed or persisted in the repository. Screenshots under `.tmp/m3-browser` contain only generated fixture data. Sanitized final artifacts may be copied into `docs/testing/evidence/m3-*`. Shared database volumes are preserved, including fixture volume cleanup; no `down --volumes` is used.

The initial browser attempts caught a fixture Origin mismatch and a public-read/session bootstrap race. These are corrected before acceptance. Frontend test doubles validate transport/logic contracts and are explicitly separate from the real-service browser path.

## Pending decisions / limits

The eight-slide editable deck passed package integrity, geometry/font policy and Artifact Tool import checks (0 findings). Automated PNG rendering stalled and native PowerPoint launch approval timed out; **visual slide review remains pending**. The deck is supplied as a draft for review, not certified as visually verified. Its seven-topic script and speaker notes are complete. The implementation and tests are committed/pushed on the feature branch; the user will create the PR and merge.

OD-04/05 stay provisional (human review and retention/replay policy); Inventory v1 historical condition/time/reservation ID gap stays open. No exactly-once delivery or global ordering claim. Admin search filters the currently loaded server page; global search API is absent. Gemini/dashboard deferred. M2 fresh-install T20 and human rehearsal/video/final submission approval are not certified by these tests. See [handover](../team/M3_P0_HANDOVER.md) and [technical event review](../events/M3_CONTRACT_REVIEW.md).
