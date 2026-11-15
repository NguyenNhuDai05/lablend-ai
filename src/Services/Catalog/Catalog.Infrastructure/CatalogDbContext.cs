using Microsoft.EntityFrameworkCore;

namespace Catalog.Infrastructure;

// Service-local context. Owner adds the actual model, constraints and migrations in Sprint 1.
// No DbSets or EnsureCreated until a real model has been reviewed.
public sealed class CatalogDbContext(DbContextOptions<CatalogDbContext> options) : DbContext(options);
