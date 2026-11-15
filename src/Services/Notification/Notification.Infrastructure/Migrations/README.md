# Notification migrations

`InitialNotification` creates the service-local `notifications` and `inbox_messages` tables in
`notification_db`, including owner/read indexes and the unique event identifier used for deduplication.
Use the commands in `docs/architecture/DATABASE_CONVENTIONS.md`; do not use `EnsureCreated`.
