# Gemini equipment recommendation v1

Recommendation is an optional .NET 8 service. Only JWT `role=STUDENT` can use its business endpoints. Ownership comes from validated `sub`; request bodies never accept a user ID. Staff/Admin receive 403, anonymous callers 401. Unknown and other-owner sessions both return 404.

| Method | Route | Result |
|---|---|---|
| POST | `/api/recommendations` | 201 with Location and enriched RecommendationResponse |
| GET | `/api/recommendations/history?page=1&pageSize=20` | `{items,total,page,pageSize}`; page 1–100000, pageSize 1–100 |
| GET | `/api/recommendations/{id}` | Owner detail; current Catalog metadata and freshly checked Inventory availability |

POST body:

```json
{
  "purpose": "Tôi muốn đo tín hiệu của một mạch điện tử cơ bản",
  "constraints": ["Thiết bị phù hợp cho người mới bắt đầu"],
  "categoryId": null
}
```

Purpose is trimmed, 10–2000 characters. Constraints are optional, at most 8 nonblank strings of at most 300 characters each. Optional `categoryId` is a nonempty UUID and an explicit Catalog filter. Maximum request body: 16 KiB.

Response fields: `id`, `purpose`, `constraints`, `categoryId`, `createdAt` (UTC), `modelUsed`, `availabilityCheckedAt` (UTC), `recommendations[]`. Each item has `equipmentTypeId`, `equipmentName`, `categoryName`, `description`, `specifications`, `imageUrl`, `isActive`, `rank` (1–6), `reason`, `usageNotes`, `available`, `total`. Name/specifications/image/category come from Catalog, counts exclusively from Inventory. Rank preserves Gemini order, without confidence scores. History rows contain `id,purpose,createdAt,modelUsed,itemCount`; they deliberately contain no stock snapshots.

An empty Gemini array is a successful saved session, different from provider failure. An empty active Catalog skips the provider and saves an empty session with the explicit marker `modelUsed=catalog-empty`; this marker does not claim a Gemini call. Initial creation rejects selected equipment deactivated or removed during inference. Historical detail shows currently inactive equipment as `isActive=false` so the UI disables selection; missing historical Catalog records return a dependency failure without inventing metadata.

| Status | code | Meaning |
|---|---|---|
| 400 | `VALIDATION_ERROR` | Invalid input/pagination, or candidate/context budget exceeded; explicitly narrow category |
| 401 / 403 | `UNAUTHENTICATED` / `FORBIDDEN` | JWT / Student role required |
| 404 | `NOT_FOUND` | Missing or other-owner session |
| 429 | `RATE_LIMITED` | 3 create attempts/minute/user/process, or persistent daily limit (default 20 UTC day) |
| 502 | `AI_INVALID_RESPONSE` | Invalid/truncated/blocked output, malformed JSON/DTO, unknown IDs, inactive selected records, excessive items/text |
| 503 | `AI_UNAVAILABLE` | Disabled, missing/invalid Gemini settings/key, network/timeout/quota/provider failure |
| 503 | `CATALOG_UNAVAILABLE` | Catalog network/contract failure or unstable pagination |
| 503 | `INVENTORY_UNAVAILABLE` | Inventory credential/gRPC/deadline/contract failure |

Errors are sanitized `application/problem+json` with `status,title,code,correlationId`. Per-minute rejection has `Retry-After: 60`; daily rejection explains UTC reset. No raw provider bodies, keys, user tokens or stack traces are returned. Client cancellation propagates and does not become a fictitious successful session.

Catalog uses all pages at pageSize 100 within the explicit candidate scope, up to 200 candidates by default; changing totals, duplicate IDs or partial empty pages fail explicitly. Above the candidate limit, reject before calling Gemini, rather than take the first N results. The serialized context has a separate conservative 100000-byte budget (full fields, no silent truncation). Choose a narrower category; if a single category is too large, its owner must refine it or an operator must deliberately adjust the documented budget. Neither keyword scoring nor a hidden fallback chooses recommendations.

Google HTTPS `v1beta/models/{configured-model}:generateContent` is called with `x-goog-api-key`, separate system instruction, `generationConfig.responseFormat.text` JSON Schema, enum of supplied IDs, maxItems 6, and capped output tokens. User input and Catalog strings are untrusted data. Backend validates again, deduplicates in first-occurrence order, re-reads selected Catalog records, then calls only Inventory v1 `CheckAvailability` over HTTP/2 with service Bearer credential, deadline and correlation metadata. No allocation RPC is invoked. Inventory failure aborts creation before save; no stock=0 fallback.

PostgreSQL `recommendation_db` stores session ownership/purpose/constraints/model/time and ordered item references/reasons/usage notes. No Catalog copies, cross-database FK, raw provider output or secrets. `RecommendationDailyUsage` stores atomic per-user/day attempt counts including failed provider calls, preventing quota bypass after restart or across replicas. Session + items save in one EF transaction only after successful validation and availability enrichment. Daily attempt admission is separate from successful-session persistence. Minute limiting is process local; daily limiting is shared in PostgreSQL. Daily usage rows currently require operator retention housekeeping if deployed long term; no automatic deletion of history is introduced.

Borrowing integration uses React Router navigation state `{recommendationPrefill:{purpose,selections:[{equipmentTypeId,quantity}]}}`, default quantity 1. The receiving form validates UUIDs, duplicates, quantities and total ≤20, fetches Catalog and current availability again, retains due-date selection, review dialog, existing idempotency key and `loanApi.create`. AI-prefilled selections with insufficient current stock are blocked with a clear adjustment/removal message. Ordinary Catalog/query-based loan creation retains the existing pending-request semantics: low stock is advisory and Staff checks stock at approval. Query-based prefill is not labelled as AI. The user must explicitly confirm. Recommendation never reserves or approves a loan.
