using Microsoft.EntityFrameworkCore;
using Notification.Domain;

namespace Notification.Infrastructure;

public sealed class NotificationDbContext(DbContextOptions<NotificationDbContext> options) : DbContext(options)
{
    public DbSet<NotificationItem> Notifications => Set<NotificationItem>();
    public DbSet<InboxMessage> InboxMessages => Set<InboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var notification = modelBuilder.Entity<NotificationItem>();
        notification.ToTable("notifications");
        notification.HasKey(x => x.Id);
        notification.Property(x => x.EventType).HasMaxLength(100).IsRequired();
        notification.Property(x => x.AggregateId).HasMaxLength(150).IsRequired();
        notification.Property(x => x.Title).HasMaxLength(200).IsRequired();
        notification.Property(x => x.Message).HasMaxLength(2000).IsRequired();
        notification.Ignore(x => x.IsRead);
        notification.HasIndex(x => x.EventId).IsUnique();
        notification.HasIndex(x => new { x.UserId, x.CreatedAt });
        notification.HasIndex(x => new { x.UserId, x.ReadAt });

        var inbox = modelBuilder.Entity<InboxMessage>();
        inbox.ToTable("inbox_messages");
        inbox.HasKey(x => x.EventId);
        inbox.Property(x => x.StreamMessageId).HasMaxLength(100).IsRequired();
        inbox.Property(x => x.EventType).HasMaxLength(100).IsRequired();
        inbox.Property(x => x.Status).HasConversion<string>().HasMaxLength(30).IsRequired();
        inbox.HasIndex(x => x.StreamMessageId);
        inbox.HasIndex(x => x.ProcessedAt);
    }
}
