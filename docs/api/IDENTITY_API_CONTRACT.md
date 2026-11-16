# Identity API contract

Implemented for Sprint 1 on .NET 8. OD-06 defaults accepted by the repository owner in this session; M3 consumer review remains pending. Public base URL is Gateway `http://localhost:5080` (Vite) or same-origin `/api` through web nginx on `http://localhost:3000`. No frontend implementation is included.

## Browser session

Access JWT lives in React memory, expires in 10 minutes (configuration `Session:AccessMinutes`), and uses `Authorization: Bearer <accessToken>`. Refresh is a 64-byte CSPRNG opaque token with a 7-day lifetime. Only SHA-256 hashes are stored. The raw token is returned exclusively as `Set-Cookie: lablend.refresh=...; HttpOnly; SameSite=Strict; Path=/api/auth`, with Secure enabled outside Development. Use `credentials: 'include'` for login, refresh and logout. Never store refresh tokens in localStorage. A successful refresh rotates the cookie; serialize refresh calls in one SPA session. Concurrent refresh yields one success and one 401; reuse revokes the entire family, including the winning replacement. A fresh login is then required.

All four POST `/api/auth/{register,login,refresh,logout}` require `Origin` matching `SessionCookie:AllowedOrigins` **and** `X-LabLend-CSRF: 1`. This custom-header + exact-Origin policy also prevents login CSRF; no double-submit cookie is needed. Gateway CORS uses explicit origins with credentials and includes the CSRF header. Browsers generate Origin automatically; CLI clients must supply it explicitly. Defaults allow localhost ports 5173 and 3000. Browser Swagger auth calls require adding that UI's exact origin to Identity's allowlist first (a browser cannot override its Origin header); send the CSRF header as well. Swagger schemas remain readable without a session. For a custom web port configure both Gateway CORS and Identity origins (Compose does this). CORS is only at Gateway; direct service ports are internal.

Development uses plain HTTP on localhost and `Secure=false` cookies; use the same hostname `localhost` everywhere (do not mix 127.0.0.1). HTTPS deployments must use Secure cookies and configure HTTPS frontend origins; cross-site deployments require a reviewed SameSite/CSRF redesign, not a wildcard allowlist. Responses set Cache-Control no-store. Refresh/auth failures return ProblemDetails and do not expose session material.

```typescript
const response = await fetch(`${gateway}/api/auth/login`, {
  method: 'POST', credentials: 'include',
  headers: { 'Content-Type': 'application/json', 'X-LabLend-CSRF': '1' },
  body: JSON.stringify({ email, password })
});
const session = await response.json(); // accessToken stays in memory
```

## Endpoints

| Method | Path | Authorization | Successful result |
|---|---|---|---|
| POST | /api/auth/register | Origin + CSRF | 201 CurrentUser; Location /api/users/me |
| POST | /api/auth/login | Origin + CSRF | 200 session + refresh cookie |
| POST | /api/auth/refresh | Origin + CSRF + cookie | 200 session + rotated cookie |
| POST | /api/auth/logout | Origin + CSRF; cookie optional | 204, family revoked, cookie deleted; repeated calls 204 |
| GET | /api/auth/me | Bearer, active account | 200 CurrentUser |
| GET | /api/users/me | Bearer, active account | 200 CurrentUser |
| PUT | /api/users/me | Bearer, active account | 200 CurrentUser |
| POST | /api/users/me/change-password | Bearer, active account | 204; all refresh sessions revoked |
| GET | /api/users?page=1&pageSize=10 | ADMIN | 200 PagedResponse<CurrentUser> |
| GET | /api/users/{id} | ADMIN | 200 CurrentUser |
| PUT | /api/users/{id} | ADMIN | 200 CurrentUser; displayName only |
| PATCH | /api/users/{id}/active | ADMIN | 200 CurrentUser |
| PUT | /api/users/{id}/role | ADMIN | 200 CurrentUser; replaces single role |
| PATCH | /api/users/{id}/role | ADMIN | 200 CurrentUser; same controlled role assignment |

Register: `{"displayName":"Student Name","email":"student@example.test","password":"<12–128 chars>","confirmPassword":"<same password>"}`. Password needs upper/lowercase, digit and symbol. Backend always assigns STUDENT. Unknown JSON properties (including role, userId, IsActive) are rejected with 400. Email normalized by ASP.NET Core Identity, unique at DB level. Duplicate email returns 409, including concurrent registration. Register does not create a session; call login next.

Login: `{"email":"student@example.test","password":"<password>"}`. Five failed attempts lock out for 15 minutes; all unsuccessful account/password/lockout/inactive cases use the same 401 message. Identity's built-in hasher validates passwords. No public admin creation endpoint exists.

Refresh/logout have no body. Profile update: `{"displayName":"Updated Name"}` (trimmed 2–100 chars). Change password: `{"currentPassword":"<current>","newPassword":"<new>"}`. Activate: `{"isActive":false}`. Assign role: `{"role":"LAB_STAFF"}`; allowed STUDENT/LAB_STAFF/ADMIN. Last active admin cannot be deactivated or demoted (409), even across concurrent requests. No hard delete.

Session response shape (illustrative, actual execution evidence is separate):

```json
{"accessToken":"<JWT>","tokenType":"Bearer","expiresAt":"<UTC ISO-8601>","user":{"id":"<UUID>","email":"student@example.test","displayName":"Student Name","isActive":true,"roles":["STUDENT"],"createdAt":"<UTC ISO-8601>","updatedAt":"<UTC ISO-8601>"}}
```

CurrentUser is exactly the nested user object above. Pagination is `{"items":[...],"total":1,"page":1,"pageSize":10}`; page 1–1,000,000, size 1–100, ordered CreatedAt descending then Id. Password hash, security stamp, token hashes and audit records are never serialized in user DTOs.

JWT claims: `sub` UUID, `role` one role, `iss` configured `Jwt:Issuer` (lablend-ai), `aud` configured `Jwt:Audience` (lablend-web), `iat`, `nbf`, `exp`, `jti`. HS256 uses only the configured signing key; the existing Gateway/REST validation checks signature/issuer/audience/lifetime, allowing 30 seconds clock skew.

## Errors and revocation limits

400 VALIDATION_ERROR; 401 UNAUTHENTICATED; 403 FORBIDDEN; 404 NOT_FOUND; 409 CONFLICT; 429 RATE_LIMITED at Gateway; 503 SERVICE_UNAVAILABLE for DB unavailability. Errors use application/problem+json with status/title/code/correlationId and validation errors when available. Protected auth/me is authorized at Identity even though Gateway's `/api/auth/*` prefix is anonymous.

Identity checks current activity and compares JWT roles with current stored roles for every protected request. Deactivation, role changes and password changes revoke all refresh families. Login/refresh reject inactive accounts. **Stateless JWTs already issued remain valid at Borrowing/other services until exp + skew**, including stale roles; logout is not an access-token denylist. Immediate cross-service revocation/introspection is deferred; maximum default exposure is 10 minutes + 30 seconds. Identity itself rejects inactive accounts and stale-role tokens immediately. This behavior must be explained in UI rather than claiming immediate global JWT revocation.

Password changes and logout also leave already-issued access JWTs valid at Identity until expiry; only its current-activity/current-role checks add immediate rejection there. Refresh sessions are revoked as described above.

Account mutations use one PostgreSQL advisory transaction lock across service replicas, plus Identity concurrency stamps, unique normalized indexes and refresh xmin. This deliberately simple coursework implementation serializes password verification and writes; larger deployments should use per-account/family locking with a separate last-admin guard after measuring contention. Failed login/reuse audit and revocation commit before the application returns 401. Audit events store action/user IDs/time, never passwords or raw tokens.

## Bootstrap and schema

Development startup applies EF migrations and seeds the three roles. Admin bootstrap is disabled unless `Bootstrap__Enabled=true`, `Bootstrap__AdminEmail`, `Bootstrap__AdminPassword` are supplied through local environment. It is Development-only; re-running does not reset/promote an existing user and does not add another bootstrap admin when an active one already exists. Never commit credentials. Production sets Database__ApplyMigrations=false and uses `dotnet ef database update` with Identity's own DB credentials, then runs a controlled role/bootstrap process in a development seed invocation. No EnsureCreated, external auth provider or paid dependency is used.
