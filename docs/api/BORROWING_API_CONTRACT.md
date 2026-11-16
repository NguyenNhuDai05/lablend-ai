# Borrowing Core API contract

Sprint 1 implements submission, owned reads, pending cancellation and staff rejection. Gateway base URL is localhost:5080 or the web nginx same-origin `/api`. Each service validates JWT independently. OD-08 defaults accepted by the repository owner: M2/M3 review remains pending. Approval, gRPC reservation, handover, returns, outbox and overdue worker remain PLANNED.

| Method | Path | Role / scope | Success |
|---|---|---|---|
| POST | /api/loans | STUDENT; StudentId from validated sub | 201 LoanDto + Location |
| GET | /api/loans/my?status=PENDING&page=1&pageSize=10 | Authenticated; current sub only | 200 PagedResponse<LoanDto> |
| GET | /api/loans?status=PENDING&page=1&pageSize=10 | LAB_STAFF or ADMIN | 200 PagedResponse<LoanDto> |
| GET | /api/loans/{id} | Owner or LAB_STAFF/ADMIN | 200 LoanDto |
| POST | /api/loans/{id}/cancel | STUDENT owner; PENDING | 200 LoanDto |
| POST | /api/loans/{id}/reject | LAB_STAFF/ADMIN; PENDING | 200 LoanDto |

Create body (illustrative):

```json
{"purpose":"Measure signals for laboratory assignment","dueAt":"<future UTC ISO-8601>","items":[{"equipmentTypeId":"<real Catalog UUID>"}]}
```

Purpose is trimmed, 10–2000 chars. DueAt must be strictly after current time and at most 30 days after submission. 1–20 items; each item represents one unit. Repeated equipmentTypeId values request multiple units, each with a distinct LoanItem Id. Empty UUIDs/null elements are rejected. Unknown JSON fields, including studentId, assetUnitId and reservationId, return 400. No client chooses owner, initial state, allocation, reviewer or timestamps.

Create first validates input, then calls Catalog outside any DB transaction. A single local PostgreSQL transaction writes LoanRequest + all LoanItems + SUBMITTED history. Initial state PENDING; ItemStatus REQUESTED; AssetUnitId/ReservationId null. Location is the relative public path `/api/loans/{id}` so YARP/nginx do not expose internal destination hosts. Failure writes no aggregate. Runtime has no permissive Catalog fallback.

## Catalog integration awaiting Member 2

Adapter uses `GET {Catalog:BaseAddress}/api/equipment-types/{uuid}` with X-Correlation-ID. Required response fields: `id` matching requested UUID and `isActive` boolean; extra fields ignored. Distinct IDs checked once each, with a total 5-second budget; no automatic retry. 404/inactive → 400 VALIDATION_ERROR; timeout/network/non-success/invalid JSON/mismatched or missing fields → 503 SERVICE_UNAVAILABLE. Missing endpoint in the current Catalog skeleton also returns 404/400; it cannot validate any positive ID. Compose uses http://catalog:8080/; local default http://localhost:5102/.

**BLOCKED:** positive verification against M2's actual Catalog, because no business Controller exists yet. Tests exercise the HTTP adapter with an explicitly labelled Catalog test double and real PostgreSQL persistence. They do not certify equipment existence in the real Catalog. M2 must review `id/isActive` before end-to-end create acceptance.

## Responses filters and state

LoanDto: `id,studentId,purpose,status,isOverdue,requestedAt,dueAt,reviewedBy,reviewedAt,rejectionReason,createdAt,updatedAt,items,history`. Items: `id,equipmentTypeId,assetUnitId,reservationId,itemStatus`. History: `id,actorUserId,action,fromStatus,toStatus,occurredAt,note`, ordered OccurredAt then Id. All IDs UUIDs and dates UTC ISO-8601; no EF entities returned.

Lifecycle: PENDING, APPROVING, APPROVED, BORROWED, RETURNED, REJECTED, CANCELLED, APPROVAL_FAILED, EXPIRED. Sprint 1 only performs PENDING→CANCELLED/REJECTED. isOverdue is derived when BORROWED, past dueAt and an item remains BORROWED; never stored as lifecycle or accepted as status filter. Item statuses: REQUESTED/RESERVED/BORROWED/RETURNED/DAMAGED/LOST/CANCELLED/REJECTED. Terminal actions update requested item statuses consistently.

Both lists return `items,total,page,pageSize`, CreatedAt descending then Id ascending; page 1–1,000,000; size 1–100. Status must be an exact uppercase lifecycle name; undefined/numeric/OVERDUE filters return 400. StudentId query parameters cannot change ownership of /my. Staff list has a separate route/policy.

Other students receive 404 for detail/cancel, identical to missing ID. Staff may read all but cannot use owner-only cancellation. Cancel body empty. Repeated cancellation of CANCELLED returns 200 with no additional history; any other non-PENDING state returns 409. Rejection body: `{"rejectionReason":"Equipment requires calibration"}` (trimmed 3–1000 chars); records reviewer/time/reason/history. Repeated reject returns 409. PostgreSQL row lock + xmin ensures one terminal transition commits in cancel/reject or two-reviewer races. No gRPC, Redis or fake APPROVED state is produced.

Errors: 400 VALIDATION_ERROR, 401 UNAUTHENTICATED, 403 FORBIDDEN, 404 NOT_FOUND, 409 CONFLICT, 429 RATE_LIMITED at Gateway, 503 SERVICE_UNAVAILABLE. Format application/problem+json with status/title/code/correlationId. JWT revocation limits: [Identity contract](IDENTITY_API_CONTRACT.md).

Schema only accesses borrowing_db: local FK LoanItems/LoanHistories→LoanRequests; logical UUID StudentId/EquipmentTypeId; due/purpose/status constraints; indexed owner/status/time; xmin. Development uses real migrations; production migration tools use borrowing_user. Existing aggregate transaction can include an Outbox record in Sprint 2 without changing DB ownership.
