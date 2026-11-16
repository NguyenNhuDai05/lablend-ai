# M3 P0 handover — 2026-11-16

The repository owner requested taking over Member 3's missing work and explicitly selected **P0 first; bonus later**. The gap-analysis DOCX is a scope reference. Its suggestions are not team approvals or permission to change M2 backend contracts. Baseline: `origin/main` at `06778313b4c9e9de61fd6122343ddea0a3f11a80`, after M1 PRs #10–13 merged. Branch: `feature/m3-p0-user-workflows`.

## Delivered implementation

| Area | Files / behavior |
|---|---|
| Public Catalog | `apps/web/src/features/catalog`: real categories/types/availability; keyword/category/sort/page; details/specifications; login return path preserves selected equipment |
| Student Loans | `features/loans`: create 1–20 physical units, purpose/date validation, local→UTC; My Loans/filter/page/detail/history/reviewer/handover; confirmation for pending owner cancellation |
| Staff integration | Shared loan pages expose approve/reject/handover and per-item NORMAL/DAMAGED/LOST returns through existing M1 endpoints. `/staff/loans` is connected to the router. This fills the missing integration point without changing M2 Staff Catalog/Inventory source or any Inventory implementation/proto. |
| Profile/Admin | `features/account`: current profile GET/PUT, password change and re-login, Admin paged users/detail/role/active, server last-active-admin 409. Admin text filter is explicitly current-page only because Identity has no global search endpoint. |
| Notifications | Owner list/read/unread/loan link; visible-page polling and immediate badge refresh after read. UI consumes actual REST data; no simulated messages. |
| Auth/races | Memory JWT, existing cookie/CSRF/single-flight refresh retained; safe internal return paths; read cancellation and session-state reload; mutation latch prevents double clicks; no automatic network retry of mutations. |
| Evidence | Existing 11 auth tests retained; added workflow contract/unit tests, seven producer→mapper compatibility cases and a headless Playwright test against real APIs/worker/PostgreSQL/Redis/gateway. |
| Defense | [Seven-section runbook](../demo/FINAL_DEMO_RUNBOOK.md), editable slide deck and [acceptance report](../testing/M3_P0_ACCEPTANCE.md). |

M1/M2 production business code and migrations are unchanged. Shared integration fixtures gained dynamic browser Origin, gateway launch and account/address accessors. Fixture teardown preserves database volumes. CI installs Chromium and explicitly enables the real-service browser test; a skipped browser test without flags is never counted as a passed E2E gate.

## Boundaries and open work

- **OD-04/05 remain OPEN**. Technical [consumer review](../events/M3_CONTRACT_REVIEW.md) and [v1 producer schema](../events/loan-event.v1.schema.json) do not represent absent members' signatures. Retention/archive/replay horizon and team approval are pending.
- Inventory v1 reservation ID/historical return condition/time gap remains in [OPEN_DECISIONS](../architecture/OPEN_DECISIONS.md). No proto expansion or invented reservation IDs.
- Global Admin search requires an agreed Identity API enhancement. Current UI honestly filters the loaded page and supports paging.
- A JWT already issued to another service may remain valid until expiry after password/role/active changes; refresh families are revoked. UI makes this limitation explicit.
- Gemini and dashboard remain deferred; no billing or paid service activation. M2 fresh-install T20 and human defense rehearsal/final approval are separate outstanding activities.
- Existing local stack upgrade verification does not prove a clean-machine installation. Shared DB volumes and other members' data are preserved.
- Final shared-stack runtime is pending: worker exited 139 at restart and Docker Desktop subsequently became unavailable. Web image build and private real-service regression passed. Restart/verify the local stack when Docker is available.
- The editable eight-slide deck passed structural validation; PNG rendering/native PowerPoint visual review could not complete. It is a draft requiring visual review, not a visually approved final submission.

## Suggested review order

Review real DTOs and authorization routes; then shared hooks/session races; Catalog/Student pages; Staff integration; Profile/Admin; Notifications; unit/contract tests and real-browser harness; CI; evidence and demo material. Confirm on the exact branch HEAD before merging. No merge or protection bypass is part of this P0 implementation request.
