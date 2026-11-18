# Recommendation migrations

`InitialRecommendations` creates sessions, items and atomic daily-attempt usage. No database is recreated or shared with Catalog/Inventory. Development/Compose applies migrations at startup through `Database:AutoMigrate`; other environments default to manual deployment.

```powershell
dotnet ef database update --project src/Services/Recommendation/Recommendation.Infrastructure --startup-project src/Services/Recommendation/Recommendation.Api
```

Load the existing service-local connection via `scripts/Set-LocalEnvironment.ps1 -Service recommendation` first. The design-time factory reads `ConnectionStrings__Database`; its no-password fallback is only for generating migration source. Do not use EnsureCreated for this service.
