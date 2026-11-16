# Sprint 1 file inventory

Final local changes on feature/m1-identity-jwt. No push or merge. Build outputs and local secrets are ignored.

| Change | File | Purpose |
|---|---|---|
| Modified | [.github/workflows/ci.yml](../../.github/workflows/ci.yml) | Enable isolated real PostgreSQL suites in CI |
| Modified | [Directory.Packages.props](../../Directory.Packages.props) | Pin compatible Identity 8.0.34 packages centrally |
| Modified | [docker-compose.yml](../../docker-compose.yml) | Identity web origin and internal Borrowing Catalog address |
| Created | [docs/api/BORROWING_API_CONTRACT.md](../../docs/api/BORROWING_API_CONTRACT.md) | Current API contracts, decisions, traceability and handover |
| Created | [docs/api/IDENTITY_API_CONTRACT.md](../../docs/api/IDENTITY_API_CONTRACT.md) | Current API contracts, decisions, traceability and handover |
| Modified | [docs/api/REST_CONTRACTS.md](../../docs/api/REST_CONTRACTS.md) | Current API contracts, decisions, traceability and handover |
| Modified | [docs/architecture/DATABASE_CONVENTIONS.md](../../docs/architecture/DATABASE_CONVENTIONS.md) | Current API contracts, decisions, traceability and handover |
| Modified | [docs/architecture/OPEN_DECISIONS.md](../../docs/architecture/OPEN_DECISIONS.md) | Current API contracts, decisions, traceability and handover |
| Modified | [docs/requirements/REQUIREMENTS_TRACEABILITY.md](../../docs/requirements/REQUIREMENTS_TRACEABILITY.md) | Current API contracts, decisions, traceability and handover |
| Created | [docs/team/SPRINT1_FILES.md](../../docs/team/SPRINT1_FILES.md) | This generated inventory |
| Created | [docs/team/SPRINT1_HANDOVER.md](../../docs/team/SPRINT1_HANDOVER.md) | Current API contracts, decisions, traceability and handover |
| Created | [docs/testing/evidence/sprint1-build.txt](../../docs/testing/evidence/sprint1-build.txt) | Sanitized output from actual verification commands |
| Created | [docs/testing/evidence/sprint1-compose-ps.txt](../../docs/testing/evidence/sprint1-compose-ps.txt) | Sanitized actual verification output |
| Created | [docs/testing/evidence/sprint1-compose-up.txt](../../docs/testing/evidence/sprint1-compose-up.txt) | Sanitized actual verification output |
| Created | [docs/testing/evidence/sprint1-gateway.txt](../../docs/testing/evidence/sprint1-gateway.txt) | Sanitized output from actual verification commands |
| Created | [docs/testing/evidence/sprint1-images.txt](../../docs/testing/evidence/sprint1-images.txt) | Sanitized actual verification output |
| Created | [docs/testing/evidence/sprint1-runtime.txt](../../docs/testing/evidence/sprint1-runtime.txt) | Sanitized actual verification output |
| Created | [docs/testing/evidence/sprint1-tests.txt](../../docs/testing/evidence/sprint1-tests.txt) | Sanitized output from actual verification commands |
| Created | [docs/testing/SPRINT1_ACCEPTANCE.md](../../docs/testing/SPRINT1_ACCEPTANCE.md) | Current API contracts, decisions, traceability and handover |
| Modified | [docs/testing/TEST_PLAN.md](../../docs/testing/TEST_PLAN.md) | Current API contracts, decisions, traceability and handover |
| Created | [infrastructure/docker/compose.tests.yml](../../infrastructure/docker/compose.tests.yml) | Private disposable PostgreSQL test environment |
| Modified | [LabLendAI.sln](../../LabLendAI.sln) | Include Sprint 1 integration test project |
| Modified | [README.md](../../README.md) | Sprint 1 status, setup, contracts and executable verification |
| Created | [scripts/Verify-Sprint1.ps1](../../scripts/Verify-Sprint1.ps1) | Read-only Compose, OpenAPI, authorization, schema and isolation verification |
| Modified | [src/BuildingBlocks/Observability/JwtSettings.cs](../../src/BuildingBlocks/Observability/JwtSettings.cs) | Shared JWT subject/options validation, Swagger and 503 semantics |
| Modified | [src/BuildingBlocks/Observability/ServiceDefaults.cs](../../src/BuildingBlocks/Observability/ServiceDefaults.cs) | Shared JWT subject/options validation, Swagger and 503 semantics |
| Modified | [src/Gateway/LabLend.Gateway/Program.cs](../../src/Gateway/LabLend.Gateway/Program.cs) | Explicit credentialed CORS and custom CSRF header allowlist |
| Created | [src/Services/Borrowing/Borrowing.Api/appsettings.Development.json](../../src/Services/Borrowing/Borrowing.Api/appsettings.Development.json) | Loan REST endpoints, authorization, HTTP errors and migrations startup |
| Modified | [src/Services/Borrowing/Borrowing.Api/appsettings.json](../../src/Services/Borrowing/Borrowing.Api/appsettings.json) | Loan REST endpoints, authorization, HTTP errors and migrations startup |
| Created | [src/Services/Borrowing/Borrowing.Api/Controllers/LoansController.cs](../../src/Services/Borrowing/Borrowing.Api/Controllers/LoansController.cs) | Loan REST endpoints, authorization, HTTP errors and migrations startup |
| Created | [src/Services/Borrowing/Borrowing.Api/LoanExceptionHandler.cs](../../src/Services/Borrowing/Borrowing.Api/LoanExceptionHandler.cs) | Loan REST endpoints, authorization, HTTP errors and migrations startup |
| Modified | [src/Services/Borrowing/Borrowing.Api/Program.cs](../../src/Services/Borrowing/Borrowing.Api/Program.cs) | Loan REST endpoints, authorization, HTTP errors and migrations startup |
| Created | [src/Services/Borrowing/Borrowing.Application/Contracts.cs](../../src/Services/Borrowing/Borrowing.Application/Contracts.cs) | Loan DTOs, ownership/validation use cases and persistence contracts |
| Created | [src/Services/Borrowing/Borrowing.Application/LoanService.cs](../../src/Services/Borrowing/Borrowing.Application/LoanService.cs) | Loan DTOs, ownership/validation use cases and persistence contracts |
| Created | [src/Services/Borrowing/Borrowing.Domain/Loans.cs](../../src/Services/Borrowing/Borrowing.Domain/Loans.cs) | Loan lifecycle and aggregate invariants |
| Modified | [src/Services/Borrowing/Borrowing.Infrastructure/Borrowing.Infrastructure.csproj](../../src/Services/Borrowing/Borrowing.Infrastructure/Borrowing.Infrastructure.csproj) | PostgreSQL aggregate persistence, locking and Catalog HTTP adapter |
| Modified | [src/Services/Borrowing/Borrowing.Infrastructure/BorrowingDbContext.cs](../../src/Services/Borrowing/Borrowing.Infrastructure/BorrowingDbContext.cs) | PostgreSQL aggregate persistence, locking and Catalog HTTP adapter |
| Created | [src/Services/Borrowing/Borrowing.Infrastructure/BorrowingDesignTimeFactory.cs](../../src/Services/Borrowing/Borrowing.Infrastructure/BorrowingDesignTimeFactory.cs) | PostgreSQL aggregate persistence, locking and Catalog HTTP adapter |
| Created | [src/Services/Borrowing/Borrowing.Infrastructure/CatalogClient.cs](../../src/Services/Borrowing/Borrowing.Infrastructure/CatalogClient.cs) | PostgreSQL aggregate persistence, locking and Catalog HTTP adapter |
| Modified | [src/Services/Borrowing/Borrowing.Infrastructure/DependencyInjection.cs](../../src/Services/Borrowing/Borrowing.Infrastructure/DependencyInjection.cs) | PostgreSQL aggregate persistence, locking and Catalog HTTP adapter |
| Created | [src/Services/Borrowing/Borrowing.Infrastructure/LoanRepository.cs](../../src/Services/Borrowing/Borrowing.Infrastructure/LoanRepository.cs) | PostgreSQL aggregate persistence, locking and Catalog HTTP adapter |
| Created | [src/Services/Borrowing/Borrowing.Infrastructure/Migrations/20261115163138_InitialBorrowing.cs](../../src/Services/Borrowing/Borrowing.Infrastructure/Migrations/20261115163138_InitialBorrowing.cs) | Borrowing EF Core 8 schema and snapshot |
| Created | [src/Services/Borrowing/Borrowing.Infrastructure/Migrations/20261115163138_InitialBorrowing.Designer.cs](../../src/Services/Borrowing/Borrowing.Infrastructure/Migrations/20261115163138_InitialBorrowing.Designer.cs) | Borrowing EF Core 8 schema and snapshot |
| Created | [src/Services/Borrowing/Borrowing.Infrastructure/Migrations/BorrowingDbContextModelSnapshot.cs](../../src/Services/Borrowing/Borrowing.Infrastructure/Migrations/BorrowingDbContextModelSnapshot.cs) | Borrowing EF Core 8 schema and snapshot |
| Modified | [src/Services/Borrowing/Borrowing.Infrastructure/Migrations/README.md](../../src/Services/Borrowing/Borrowing.Infrastructure/Migrations/README.md) | Borrowing EF Core 8 schema and snapshot |
| Created | [src/Services/Identity/Identity.Api/appsettings.Development.json](../../src/Services/Identity/Identity.Api/appsettings.Development.json) | Identity HTTP endpoints, auth/session policies and host configuration |
| Modified | [src/Services/Identity/Identity.Api/appsettings.json](../../src/Services/Identity/Identity.Api/appsettings.json) | Identity HTTP endpoints, auth/session policies and host configuration |
| Created | [src/Services/Identity/Identity.Api/Controllers/AuthController.cs](../../src/Services/Identity/Identity.Api/Controllers/AuthController.cs) | Identity HTTP endpoints, auth/session policies and host configuration |
| Created | [src/Services/Identity/Identity.Api/Controllers/UsersController.cs](../../src/Services/Identity/Identity.Api/Controllers/UsersController.cs) | Identity HTTP endpoints, auth/session policies and host configuration |
| Created | [src/Services/Identity/Identity.Api/IdentityHttp.cs](../../src/Services/Identity/Identity.Api/IdentityHttp.cs) | Identity HTTP endpoints, auth/session policies and host configuration |
| Modified | [src/Services/Identity/Identity.Api/Program.cs](../../src/Services/Identity/Identity.Api/Program.cs) | Identity HTTP endpoints, auth/session policies and host configuration |
| Created | [src/Services/Identity/Identity.Application/Contracts.cs](../../src/Services/Identity/Identity.Application/Contracts.cs) | Identity DTOs, use cases and repository/token abstractions |
| Modified | [src/Services/Identity/Identity.Application/Identity.Application.csproj](../../src/Services/Identity/Identity.Application/Identity.Application.csproj) | Identity DTOs, use cases and repository/token abstractions |
| Created | [src/Services/Identity/Identity.Application/IdentityService.cs](../../src/Services/Identity/Identity.Application/IdentityService.cs) | Identity DTOs, use cases and repository/token abstractions |
| Created | [src/Services/Identity/Identity.Domain/Accounts.cs](../../src/Services/Identity/Identity.Domain/Accounts.cs) | Identity user, role, refresh and audit entities |
| Modified | [src/Services/Identity/Identity.Domain/Identity.Domain.csproj](../../src/Services/Identity/Identity.Domain/Identity.Domain.csproj) | Identity user, role, refresh and audit entities |
| Modified | [src/Services/Identity/Identity.Infrastructure/DependencyInjection.cs](../../src/Services/Identity/Identity.Infrastructure/DependencyInjection.cs) | Identity EF stores, atomic persistence, JWT and controlled bootstrap |
| Modified | [src/Services/Identity/Identity.Infrastructure/Identity.Infrastructure.csproj](../../src/Services/Identity/Identity.Infrastructure/Identity.Infrastructure.csproj) | Identity EF stores, atomic persistence, JWT and controlled bootstrap |
| Created | [src/Services/Identity/Identity.Infrastructure/IdentityBootstrap.cs](../../src/Services/Identity/Identity.Infrastructure/IdentityBootstrap.cs) | Identity EF stores, atomic persistence, JWT and controlled bootstrap |
| Modified | [src/Services/Identity/Identity.Infrastructure/IdentityDbContext.cs](../../src/Services/Identity/Identity.Infrastructure/IdentityDbContext.cs) | Identity EF stores, atomic persistence, JWT and controlled bootstrap |
| Created | [src/Services/Identity/Identity.Infrastructure/IdentityDesignTimeFactory.cs](../../src/Services/Identity/Identity.Infrastructure/IdentityDesignTimeFactory.cs) | Identity EF stores, atomic persistence, JWT and controlled bootstrap |
| Created | [src/Services/Identity/Identity.Infrastructure/IdentityRepository.cs](../../src/Services/Identity/Identity.Infrastructure/IdentityRepository.cs) | Identity EF stores, atomic persistence, JWT and controlled bootstrap |
| Created | [src/Services/Identity/Identity.Infrastructure/JwtIssuer.cs](../../src/Services/Identity/Identity.Infrastructure/JwtIssuer.cs) | Identity EF stores, atomic persistence, JWT and controlled bootstrap |
| Created | [src/Services/Identity/Identity.Infrastructure/Migrations/20261115162335_InitialIdentity.cs](../../src/Services/Identity/Identity.Infrastructure/Migrations/20261115162335_InitialIdentity.cs) | Identity EF Core 8 schema and snapshot |
| Created | [src/Services/Identity/Identity.Infrastructure/Migrations/20261115162335_InitialIdentity.Designer.cs](../../src/Services/Identity/Identity.Infrastructure/Migrations/20261115162335_InitialIdentity.Designer.cs) | Identity EF Core 8 schema and snapshot |
| Created | [src/Services/Identity/Identity.Infrastructure/Migrations/IdentityDbContextModelSnapshot.cs](../../src/Services/Identity/Identity.Infrastructure/Migrations/IdentityDbContextModelSnapshot.cs) | Identity EF Core 8 schema and snapshot |
| Modified | [src/Services/Identity/Identity.Infrastructure/Migrations/README.md](../../src/Services/Identity/Identity.Infrastructure/Migrations/README.md) | Identity EF Core 8 schema and snapshot |
| Modified | [tests/Integration/LabLend.Platform.IntegrationTests/GatewayTests.cs](../../tests/Integration/LabLend.Platform.IntegrationTests/GatewayTests.cs) | Executable unit, security, PostgreSQL and YARP verification |
| Created | [tests/Integration/LabLend.Platform.IntegrationTests/JwtSecurityTests.cs](../../tests/Integration/LabLend.Platform.IntegrationTests/JwtSecurityTests.cs) | Executable unit, security, PostgreSQL and YARP verification |
| Created | [tests/Integration/LabLend.Sprint1.IntegrationTests/BorrowingFactory.cs](../../tests/Integration/LabLend.Sprint1.IntegrationTests/BorrowingFactory.cs) | Executable unit, security, PostgreSQL and YARP verification |
| Created | [tests/Integration/LabLend.Sprint1.IntegrationTests/BorrowingTests.cs](../../tests/Integration/LabLend.Sprint1.IntegrationTests/BorrowingTests.cs) | Executable unit, security, PostgreSQL and YARP verification |
| Created | [tests/Integration/LabLend.Sprint1.IntegrationTests/GatewayFlowTests.cs](../../tests/Integration/LabLend.Sprint1.IntegrationTests/GatewayFlowTests.cs) | Executable unit, security, PostgreSQL and YARP verification |
| Created | [tests/Integration/LabLend.Sprint1.IntegrationTests/IdentityFactory.cs](../../tests/Integration/LabLend.Sprint1.IntegrationTests/IdentityFactory.cs) | Executable unit, security, PostgreSQL and YARP verification |
| Created | [tests/Integration/LabLend.Sprint1.IntegrationTests/IdentityTests.cs](../../tests/Integration/LabLend.Sprint1.IntegrationTests/IdentityTests.cs) | Executable unit, security, PostgreSQL and YARP verification |
| Created | [tests/Integration/LabLend.Sprint1.IntegrationTests/LabLend.Sprint1.IntegrationTests.csproj](../../tests/Integration/LabLend.Sprint1.IntegrationTests/LabLend.Sprint1.IntegrationTests.csproj) | Executable unit, security, PostgreSQL and YARP verification |
| Created | [tests/Integration/LabLend.Sprint1.IntegrationTests/LiveApi.cs](../../tests/Integration/LabLend.Sprint1.IntegrationTests/LiveApi.cs) | Executable unit, security, PostgreSQL and YARP verification |
| Created | [tests/Integration/LabLend.Sprint1.IntegrationTests/PostgresFixture.cs](../../tests/Integration/LabLend.Sprint1.IntegrationTests/PostgresFixture.cs) | Executable unit, security, PostgreSQL and YARP verification |
| Created | [tests/Integration/LabLend.Sprint1.IntegrationTests/SchemaTests.cs](../../tests/Integration/LabLend.Sprint1.IntegrationTests/SchemaTests.cs) | Executable unit, security, PostgreSQL and YARP verification |
| Modified | [tests/Unit/LabLend.Platform.UnitTests/LabLend.Platform.UnitTests.csproj](../../tests/Unit/LabLend.Platform.UnitTests/LabLend.Platform.UnitTests.csproj) | Executable unit, security, PostgreSQL and YARP verification |
| Created | [tests/Unit/LabLend.Platform.UnitTests/LoanRulesTests.cs](../../tests/Unit/LabLend.Platform.UnitTests/LoanRulesTests.cs) | Executable unit, security, PostgreSQL and YARP verification |
