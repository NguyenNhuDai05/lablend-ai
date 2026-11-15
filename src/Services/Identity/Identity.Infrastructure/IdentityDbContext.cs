using Microsoft.EntityFrameworkCore;

namespace Identity.Infrastructure;

// Service-local context. Owner adds the actual model, constraints and migrations in Sprint 1.
// No DbSets or EnsureCreated until a real model has been reviewed.
public sealed class IdentityDbContext(DbContextOptions<IdentityDbContext> options) : DbContext(options);
