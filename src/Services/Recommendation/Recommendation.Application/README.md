# Recommendation Application

`RecommendationService` orchestrates input/budget validation, daily attempt admission, real Catalog candidates, Gemini reasoning, output validation, current Catalog verification, read-only Inventory enrichment and successful-session persistence. Ports and transport DTOs are in `Contracts.cs`. This layer references Domain and technical PagedResponse contracts only; provider/network/EF implementations stay in Infrastructure.
