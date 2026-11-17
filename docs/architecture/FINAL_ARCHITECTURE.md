# Final architecture checkpoint

Implemented core: React, YARP, five business REST hosts, independently deployed Inventory gRPC, Borrowing Worker, Redis Streams and PostgreSQL. Recommendation remains an optional skeleton with Gemini disabled. Every production UI mutation uses an authorized API. A browser does not allocate physical assets or write service databases.

## C4 context

```mermaid
flowchart LR
  Student[Student] -->|Find, request, track| System[LabLend AI]
  Staff[Lab Staff] -->|Review, hand over, receive, maintain| System
  Admin[Administrator] -->|Accounts and roles| System
  System -. optional and disabled .-> Gemini[Google Gemini Developer API]
```

## C4 containers

```mermaid
flowchart TB
  Web[React production web / nginx] --> Gateway[YARP Gateway]
  Gateway --> Identity[Identity REST]
  Gateway --> Catalog[Catalog REST]
  Gateway --> Borrowing[Borrowing REST]
  Gateway --> Inventory[Inventory REST and HTTP/2 gRPC]
  Gateway --> Notification[Notification REST and consumer]
  Borrowing -->|Catalog REST validation| Catalog
  Borrowing -->|Service credential and deadline| Inventory
  Worker[Borrowing Worker] -->|Reconcile and reserve expiry| Inventory
  Worker -->|XADD stable eventId| Redis[Redis Streams]
  Redis -->|XREADGROUP and XAUTOCLAIM| Notification
  Identity --> IDB[(identity_db)]
  Catalog --> CDB[(catalog_db)]
  Inventory --> VDB[(inventory_db)]
  Borrowing --> BDB[(borrowing_db)]
  Worker --> BDB
  Notification --> NDB[(notification_db)]
```

API/controller calls Application services and ports. Infrastructure implements repositories, EF contexts and network adapters. Only API/gateway/web ports intended for local access are exposed. Core service traffic stays in the Compose backend network. Worker belongs to Borrowing and may use its database; another service may not.

## Database ownership and ERD

| Database / login | Owned persisted data |
|---|---|
| identity_db / identity_user | Identity users/roles, refresh-token hashes, audit events |
| catalog_db / catalog_user | Categories, EquipmentTypes |
| inventory_db / inventory_user | AssetUnits, Reservations, ReserveOperations, MaintenanceRecords, StockMovements |
| borrowing_db / borrowing_user | LoanRequests, LoanItems, LoanHistories, LoanSubmissions, InventoryOperations, OutboxMessages, ReminderDispatches |
| notification_db / notification_user | notifications, inbox_messages |
| recommendation_db / recommendation_user | Reserved optional context; no implemented AI history migration |

All lines below are local foreign keys. EquipmentTypeId, StudentId and cross-service loan/asset IDs elsewhere are logical references with no cross-database FK or SQL join.

```mermaid
erDiagram
  Categories ||--o{ EquipmentTypes : catalog
  AssetUnits ||--o{ Reservations : inventory
  AssetUnits ||--o{ MaintenanceRecords : inventory
  AssetUnits ||--o{ StockMovements : inventory
  LoanRequests ||--o{ LoanItems : borrowing
  LoanRequests ||--o{ LoanHistories : borrowing
  LoanRequests ||--o{ LoanSubmissions : borrowing
  LoanRequests ||--o{ InventoryOperations : borrowing
  LoanRequests ||--o{ ReminderDispatches : borrowing
```

Outbox AggregateId/EventId and Inbox EventId connect distributed effects by logical IDs. A local Borrowing SaveChanges captures history and outbox in the same transaction. Notification commits its inbox and notification before acknowledgment. No distributed transaction or exactly-once transport is claimed.

## Loan state machine

```mermaid
stateDiagram-v2
  [*] --> PENDING: Student submits
  PENDING --> CANCELLED: Owner cancels
  PENDING --> REJECTED: Staff rejects with reason
  PENDING --> APPROVING: Persist stable reserve operation
  APPROVING --> APPROVED: All allocations verified
  APPROVING --> APPROVAL_FAILED: Definitive reserve failure
  APPROVING --> EXPIRED: Reconciled safe release
  APPROVED --> EXPIRED: Pickup hold expires and release confirmed
  APPROVED --> BORROWED: Handover confirmed
  BORROWED --> BORROWED: Partial return
  BORROWED --> RETURNED: Every item returned/damaged/lost
```

OVERDUE is derived from DueAt and outstanding borrowed items. An uncertain RPC does not imply approval, handover or return success. Persisted operations block conflicting commands until reconciliation; UI shows the actual backend status and a correlation ID on errors. An APPROVING history entry is a real internal transition, displayed as work in progress.

## Interactions and recovery

```mermaid
sequenceDiagram
  participant S as Student
  participant B as Borrowing REST
  participant I as Inventory gRPC
  participant W as Borrowing Worker
  participant R as Redis
  participant N as Notification
  S->>B: Submit reviewed draft + Idempotency-Key
  B->>B: Commit loan/items/history/outbox/key
  B-->>S: 201 with persisted loan
  B->>B: Staff approval persists stable operation
  B->>I: ReserveAssets (all or nothing)
  I-->>B: Verified unit allocations
  B->>B: Commit APPROVED and Outbox
  W->>R: XADD event (same eventId on retry)
  R->>N: Consumer group delivery
  N->>N: Commit Notification + Inbox dedupe
  N->>R: XACK after commit
  S->>N: Read own notifications through Gateway
```

Lost replies are reconciled using GetOperationStatus and immutable command intent. v1 has no reservation ID or historical return time/condition in its response. Borrowing preserves its own return intent and only supports the documented single-writer workflow. It never interprets current AssetUnit status as historical return evidence. Inventory owner review remains PR #15; no proto modification is included.

## Delivery contract and retention

Stream `lablend.events.v1`, field `event`, version 1, group `notification-consumers`; seven event types are in the schema and executable producer/consumer tests. XAUTOCLAIM reclaims idle pending entries. Inbox EventId deduplicates notification effects. Repeated non-Redis failures park after the configured attempt limit; Redis failures remain pending.

OD-04/05, retention/archive horizon, replay and parking policy remain **PENDING HUMAN REVIEW**. No stream trimming, inbox deletion, outbox dropping, group resetting or fabricated owner signoff was introduced. The tradeoff is unbounded storage until an approved retention policy exists. See [event contract](../events/EVENT_CONTRACT_GUIDE.md) and [technical review](../events/M3_CONTRACT_REVIEW.md).
