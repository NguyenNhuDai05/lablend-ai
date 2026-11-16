# Member 1 and Member 3 integration

Integration date: 16 November 2026, Asia/Bangkok. The repository owner explicitly authorized fetching, conflict resolution, committing, pushing and merging into main.

Bases: M1 Sprint 1 commit `eb6756c`; remote main `49aa318` containing M3 PR #9 (`e152e7a`). M3's routed frontend, staff UI, Notification implementation and tests are retained. The separate unmerged M2 backend branch is outside this merge; actual Catalog integration remains pending that work.

## Resolved conflicts

The textual conflict was in tests/Unit/LabLend.Platform.UnitTests/LabLend.Platform.UnitTests.csproj. Both sets of references are retained: Borrowing.Domain plus Notification.Application and Notification.Domain. No teammate tests were discarded.

Functional contract differences required small integration changes:

- Browser auth POSTs now include credentials and X-LabLend-CSRF:1; the browser supplies Origin. Login/refresh/logout cookie commands are serialized, with Web Locks support between same-origin tabs. Protected requests share one refresh operation and retry at most once after 401.
- Access JWT is held in memory. The old sessionStorage entry is removed; reload restores the session using the HttpOnly refresh cookie. Session generation guards reject stale responses after account changes/logout and avoid duplicate bootstrap refresh in React StrictMode.
- Identity CurrentUser roles[] is mapped to the routed UI's existing single-role model. Unknown/inactive roles fail closed; backend authorization remains authoritative.
- Registration sends confirmPassword and uses the backend's 12–128 character password limits. Frontend layout, routes and staff feature design are preserved.
- Notification Development startup applies M3's own migration before the hosted consumer starts. Production retains the default of no automatic migration unless explicitly configured. No producer, Borrowing Outbox, business gRPC approval or new event contract was added.

## Validation

- dotnet restore and Release build: PASS, zero warnings/errors.
- Full .NET test suite with LABLEND_POSTGRES_TESTS=1: PASS, 38 tests (20 unit including M3, ten platform, eight real PostgreSQL/YARP).
- npm ci: PASS, zero reported vulnerabilities at this install.
- npm run build: PASS.
- npm test: PASS, seven client contract/security tests covering cookie/CSRF transport, single refresh for concurrent 401s, refresh failure, invalid login, session changes, 403 handling and roles[] mapping.
- Docker Compose configuration: PASS.

The existing Sprint 1 acceptance/evidence files describe the earlier M1-only checkpoint. This document records the subsequent integration. Actual Catalog equipment verification and full producer→Redis→Notification delivery remain unverified; M3's consumer proposal still awaits OD-04/OD-05 review.
