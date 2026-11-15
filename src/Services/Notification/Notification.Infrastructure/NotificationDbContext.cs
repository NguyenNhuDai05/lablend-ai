using Microsoft.EntityFrameworkCore;

namespace Notification.Infrastructure;

// Service-local context. Owner adds the actual model, constraints and migrations in Sprint 1.
// No DbSets or EnsureCreated until a real model has been reviewed.
public sealed class NotificationDbContext(DbContextOptions<NotificationDbContext> options) : DbContext(options);
