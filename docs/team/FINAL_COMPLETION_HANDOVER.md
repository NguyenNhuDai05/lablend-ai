# Final completion handover 2026-11-17

Main baseline: `68933a1` including merged M3 PR #14. Final tested code: `ea868e4`; merged review checkpoint: `430ff1b`, with identical runtime/web/scripts/Compose content, on `feature/final-c-acceptance`. PR-A head `9608470`; PR-B head `7e850fc`. This followup packages evidence/documents. Initial working tree was clean; M1/M2/M3 source, tests, migrations and service ownership were preserved. No main merge was performed. **Automated P0 gates PASS; final submission remains PROVISIONAL** for the human gates in [acceptance](../testing/FINAL_PRODUCT_ACCEPTANCE.md).

## Delivered changes

| Package | Files and purpose |
|---|---|
| PR-A #16 | TCP PostgreSQL health, sequential `Start-Core.ps1`, bounded preflight, isolated fresh-clone T20/production browser scenario, preserved failure diagnostics and initial audit |
| PR-B #17 | Borrowing key transaction/entity/migration/tests; Identity global server filters; Notification owned aggregate filter; React review dialogs/serials/Admin filters/related inbox; focus and Staff submit latch; compatible API docs |
| PR-C #18 | Real bounded equipment previews and Vietnamese event copy; reusable REST demo data; final C4/ERD/states/interactions; README/runbook/test plan/traceability; exact CI, Docker T20, screenshots, acceptance and handover evidence |

Exact changed files are listed in [the final manifest](../testing/evidence/final-files.txt) and can be reviewed with `git diff --name-status 68933a1..HEAD` on this branch. The PR diffs are the reviewable source of truth; generated binaries, `.env`, private fixture credentials and raw local build directories are ignored. Only `20261116180731_AddLoanSubmissionIdempotency` is added in Borrowing. No protobuf change, event schema rewrite, shared database reset or new paid dependency exists.

## How to verify and rehearse

1. Follow README setup. Use the .NET SDK from global.json, Node 22.12+ and Docker Linux containers.
2. Run Release restore/build and the frontend build/tests. Enable both `LABLEND_POSTGRES_TESTS=1` and `LABLEND_BROWSER_TESTS=1` for the real-service gate. Windows can use installed Edge; CI installs Chromium. Skipped required tests are not PASS.
3. Run `Test-FinalPreflight.ps1` and inspect worker state/logs/dependencies. Desktop recovery restored baseline health. Fresh T20 then established a PostgreSQL bootstrap/Unix-socket health race, now corrected by a TCP health gate. See FINAL_DOCKER_DIAGNOSIS; the older unrecorded M3 failure remains historically uncertain.
4. Run `Test-CleanInstall.ps1 -Revision origin/feature/final-c-acceptance`. It clones into a new directory, generates credentials, isolates project/ports/volumes, builds sequentially, starts real containers, runs three flows through the production web/Gateway, verifies owning DB state, notification/reminder/reconnect/restart/role/error behavior and prepares reusable demo data twice. It never removes volumes. Inspect the recorded commit/result, not just the command exit.
5. Use `Initialize-DemoData.ps1` with local environment passwords and an authorized bootstrap admin to prepare the ordinary presentation stack. It preserves existing accounts/passwords and asset state. Perform all loans and state transitions through the UI/API.
6. Rehearse the [seven-topic runbook](../demo/FINAL_DEMO_RUNBOOK.md) and [presenter checklist](../demo/FINAL_DEFENSE_CHECKLIST.md). Do not project passwords/JWTs/.env or claim mock AI as a live bonus.

## Git and review sequence

PR-A [#16](https://github.com/NguyenNhuDai05/lablend-ai/pull/16) targets main. PR-B [#17](https://github.com/NguyenNhuDai05/lablend-ai/pull/17) targets A. PR-C [#18](https://github.com/NguyenNhuDai05/lablend-ai/pull/18) targets B. Review and merge A, retarget B to main and merge after gates, then retarget C. No force push or automatic merge. Independent M2 gRPC owner review remains [PR #15](https://github.com/NguyenNhuDai05/lablend-ai/pull/15); it was not silently integrated into these branches.

## Human-owned decisions and limitations

- Approve OD-04/05, retention/replay/parking and privacy/storage horizon. Implementation compatibility tests do not sign for owners.
- Review Inventory v1 gaps and the single-writer recovery assumption; no backward-incompatible change is proposed.
- Inspect every deck slide in PowerPoint and approve visual output. Bundled renderer stalled and native automation discovery timed out; existing eight-slide editable deck is preserved as a draft. Current C4/ERD and defense script are available separately.
- Confirm actual speakers, run rehearsal on the presentation machine and record duration/issues. No team signoff/video was fabricated.
- Gemini/dashboard remain optional. Gemini is disabled and no live provider call/key/billing is used. Begin bonus work only after P0 acceptance is stable.

Final test counts, Docker/T20 status, CI links and residual failures belong to the acceptance report and evidence manifest. Historical M1/M2/M3 reports are preserved and do not supersede the final checkpoint.

The local [demo web](http://localhost:3000) is running the same eight application image IDs that passed the final T20. Existing `.env` and named data volumes were retained; older images have `pre-final-20261117` rollback tags. Ten core containers are healthy and read-only runtime checks, the additive migration, current OpenAPI parameters and all four actual Worker passes are verified. [Final runtime evidence](../testing/evidence/final-deployed-runtime.json). Existing demo data/users were preserved; this does not reset anyone's password or reassign roles.

## Executed regression checkpoint

Local Release build: 0 errors/16 NU1900 network warnings; EF model aligned; frontend production build and **25/25 tests**; backend real-service rerun **71/71 tests with 0 skipped**, including browser and the three new PostgreSQL cases. Final exact-code CI runs `43966081127` (`ea868e4`) and `43967053632` (`430ff1b`) both confirm 71 backend/25 frontend, zero failures/skips. The isolated final fresh clone `lablend-t20-98c82cde5abe` at `ea868e4` passed all **13 T20 stages**, ten production browser stages, six database isolation checks, and reusable demo preparation twice. Teardown removed only its own containers/networks and preserved all volumes. See [curated evidence](../testing/evidence/FINAL_EVIDENCE.md) and [actual T20 result](../testing/evidence/final-t20/t20-result.json). These automated results do not certify deck readability, actual speakers, team rehearsal or owner signoff.
