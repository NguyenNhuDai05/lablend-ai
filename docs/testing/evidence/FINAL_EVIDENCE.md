# Final executed evidence

Code checkpoint: `430ff1b15ce7441a3862ad247316943e9a91acd2`. Fresh-clone Docker test source: `ea868e4f22ebc13fbc2371ca1bfb73ef99435179`; runtime/web/scripts/infrastructure/Compose content is identical. The evidence-only followup does not change those artifacts. Main remains `68933a1` and no PR was merged automatically.

| Evidence | Executed result and scope |
|---|---|
| [Exact merged-source CI](final-ci-43967053632.json) | Success; 71 backend, 25 frontend; 0 failed/skipped; real PostgreSQL/Redis/gRPC/browser flags enabled |
| [Exact Docker-source CI](final-ci-43966081127.json) | Same counts at the fresh-clone test source |
| [Local backend outcomes](final-test-results.json) | Per-test names, outcomes and durations for the 71-test Windows rerun; installed Edge used for browser |
| [Local frontend output](final-frontend-tests.txt) | 25 pass, 0 fail/skip; production TypeScript/Vite build also passed |
| [Final fresh-clone T20](final-t20/t20-result.json) | All 13 stages PASS, isolated random project, generated credentials, fresh owned volumes, cleanup exit 0 |
| [Production E2E expected/actual](final-t20/docker-e2e.json) | Ten scenario stages PASS: all three flows, RBAC, no stock, actual reminders/broker, three return conditions, append-only maintenance history, Redis reconnect, worker restart and mobile |
| [T20 stages and ownership checks](final-t20/stages.txt) | All six service roles connect only to their own database; demo preparation twice preserves existing users/equipment |
| [T20 container health](final-t20/containers.json) | Ten core containers healthy after sequential startup; business assertions run afterward |
| [Final demo runtime](final-deployed-runtime.json), [read-only checks](final-deployed-runtime.txt) | Exact tested images deployed to existing `lablend-ai`; ten healthy core services, five applied Borrowing migrations, four completed jobs, compatible OpenAPI parameters. Original credentials/data volumes preserved; rollback image tags retained |
| [Reproduced startup race](final-startup-race.json) | Five exit-139 states with OOMKilled=false, Npgsql failures and PostgreSQL bootstrap timeline; [diagnosis](../FINAL_DOCKER_DIAGNOSIS.md) explains TCP correction |
| [Historical recovered baseline](final-baseline-runtime.json), [baseline verification](final-shared-runtime.txt) | Existing shared-stack health before the final deployment; separate from final T20 evidence |
| [Changed-file manifest](final-files.txt) | Actual delta from verified main; no old migration or proto deletion |

Final screenshots are captured from the production web after real business commands. Their disposable fixture names and free-text purposes are test inputs; lifecycle dates/statuses and notifications come from the services.

- [Student persisted request](final-t20/t20-student-created.png)
- [Student timeline](final-t20/t20-timeline.png)
- [Real due/overdue and lifecycle notifications](final-t20/t20-notifications.png)
- [Maintenance and movement history](final-t20/t20-maintenance-history.png)
- [Mobile loans with real names/quantities](final-t20/t20-mobile-loans.png), [mobile notifications](final-t20/t20-mobile-notifications.png)
- [Settled mobile Catalog on the final deployed stack](final-demo-mobile-catalog.png): read-only followup with existing real equipment; no alert or horizontal overflow. The T20 [Catalog screenshot](final-t20/t20-mobile-catalog.png) captured its loading state and is not evidence of loaded cards

Raw local logs, database/Redis volumes, private `.env` files and credential-bearing process state stay outside Git. Curated evidence contains no passwords/JWTs. Linux Docker builds passed; local restore emitted 16 NU1900 vulnerability-feed network warnings, so a completed dependency vulnerability audit is not claimed. Earlier failed attempts were corrected and retained in the diagnostic/acceptance history. Deck visual QA, actual team rehearsal and OD owner approvals remain unresolved human gates.
