# Identity migrations

InitialIdentity is the real Sprint 1 EF Core 8 migration for ASP.NET Core Identity, refresh tokens and audit events. Development startup runs MigrateAsync and idempotent role seed. Production: load identity environment, then dotnet ef database update --project src/Services/Identity/Identity.Infrastructure --startup-project src/Services/Identity/Identity.Api. Never use EnsureCreated or another service's DB credentials.
