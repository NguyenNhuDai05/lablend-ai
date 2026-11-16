# Final defense checklist

Use this with the [live runbook](FINAL_DEMO_RUNBOOK.md), [architecture and diagrams](../architecture/FINAL_ARCHITECTURE.md), and the latest [acceptance](../testing/FINAL_PRODUCT_ACCEPTANCE.md). The existing editable deck remains a draft until every slide can be rendered and inspected. A source/package check cannot replace that visual review or a live demonstration.

## Presenter allocation

| Presenter | Topics | Operator action and evidence |
|---|---|---|
| M1 Core | Architecture, authentication, jobs, broker, recovery | Explain C4 and ownership. Show real four-job pass logs and due/overdue notifications. Show outage/replay tests and correlation IDs. |
| M2 Catalog and Inventory | REST CRUD/search, independent gRPC, physical lifecycle | Create a type/unit. Approve, hand over, receive DAMAGED/NORMAL/LOST. Open maintenance and StockMovements; show append-only history. |
| M3 Web and Notification | Student flow, inbox, Profile/Admin, deployment/E2E | Find a type, review draft, submit once, follow history and loan-filtered notifications. Show global Admin filters, role protection and mobile layout. Show T20 artifact and actual Compose health. |

These are proposed ownership-based speaking assignments. Names, final speaking order and rehearsal signoff require the actual members.

## Before presenting

1. Run `scripts/Test-FinalPreflight.ps1`; inspect actual daemon, worker state/health, images and jobs.
2. Run the full build/test commands in README with both integration/browser flags enabled. Any skipped required test leaves the gate open.
3. Run `scripts/Test-CleanInstall.ps1 -Revision <reviewed branch or commit>` on a fresh clone/project. Inspect its `t20-result.json`, `docker-e2e.json` and screenshots, including the tested commit. Do not substitute shared-stack health for T20.
4. Prepare reusable accounts/equipment with `scripts/Initialize-DemoData.ps1` after a locally configured admin bootstrap. Passwords come from local process variables, never slides or Git. Re-running preserves existing users, passwords and equipment state.
5. Verify Student/Staff/Admin sessions in separate browser contexts; available demo units; screen scaling; and all seven required topics below.
6. Open the deck in PowerPoint and inspect each slide. Current draft has eight slides with architecture/REST/jobs/broker/gRPC/deployment/E2E. It has no current test count; do not add a count without the tested SHA. Show the C4/ERD companion when discussing data ownership. Screenshots in the repository are historical unless explicitly labeled final.

## Seven official demonstration topics

| Topic | Live expected outcome | Evidence to retain | Signoff |
|---|---|---|---|
| Architecture | Correct service boundaries and owned databases | C4 container view + ownership/ERD | PENDING HUMAN REVIEW |
| REST | Authorized CRUD and real server search/filter/sort/pagination | Swagger request/response + resulting UI | PENDING HUMAN REVIEW |
| Background job | Real due-soon/overdue event and notification | Worker log + fixture loan due date + notification | PENDING HUMAN REVIEW |
| Message broker | Real XADD/delivery, Inbox commit, XACK, replay dedupe | Redis metadata + integration result | PENDING HUMAN REVIEW |
| gRPC | Borrowing REST causes Inventory reservation/handover/return | API result + owning Inventory state/history | PENDING HUMAN REVIEW |
| Docker deployment | Healthy core from README/fresh clone | T20 JSON + Compose health + commit | PENDING HUMAN REVIEW |
| End-to-end | Student request, Staff fulfill, return, maintenance, Student timeline/inbox | Browser screenshots + DB/API assertions | PENDING HUMAN REVIEW |

## Accurate limitations to explain

- OD-04/05 and retention/replay policy need owner approval. At-least-once transport with deduplicated notification effects is the implemented guarantee.
- Inventory v1 omits reservation ID and historical return condition/time. Borrowing persists its own immutable return intent and supports its documented single-writer workflow. Do not infer historical outcomes from the current physical status.
- Access JWTs at another service can remain valid until their short expiry after password/role changes. Refresh sessions are revoked.
- A successful Docker recovery does not prove the root cause of an earlier native exit 139. Keep the captured diagnostic facts separate.
- Gemini is optional, disabled and not a completed live bonus. No billing is needed for core. Dashboard/QR/telemetry remain optional.
- Automated evidence cannot certify team rehearsal, projector readability, final submission approval or a video that nobody recorded.
