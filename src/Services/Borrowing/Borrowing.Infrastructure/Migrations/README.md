# Borrowing migrations

InitialBorrowing is the real Sprint 1 EF Core 8 migration for loans, items and histories. Development startup runs MigrateAsync. Production: load borrowing environment, then dotnet ef database update --project src/Services/Borrowing/Borrowing.Infrastructure --startup-project src/Services/Borrowing/Borrowing.Api. No physical FK to Identity/Catalog; no Inventory or Outbox schema in this sprint.
