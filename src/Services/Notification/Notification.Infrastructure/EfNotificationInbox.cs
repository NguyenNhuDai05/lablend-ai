using Microsoft.EntityFrameworkCore;
using Npgsql;
using Notification.Application;
using Notification.Domain;

namespace Notification.Infrastructure;

public sealed class EfNotificationInbox(NotificationDbContext dbContext, TimeProvider timeProvider) : INotificationInbox
{
    public async Task<ConsumeResult> PersistAsync(IncomingNotification incoming, CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            if (await dbContext.InboxMessages.AnyAsync(x => x.EventId == incoming.EventId, cancellationToken))
                return ConsumeResult.Duplicate;

            dbContext.Notifications.Add(NotificationItem.Create(incoming.RecipientUserId, incoming.EventId,
                incoming.EventType, incoming.AggregateId, incoming.Title, incoming.Message, incoming.OccurredAt));
            dbContext.InboxMessages.Add(InboxMessage.Processed(incoming.EventId, incoming.StreamMessageId,
                incoming.EventType, timeProvider.GetUtcNow()));
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return ConsumeResult.Created;
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            await transaction.RollbackAsync(cancellationToken);
            dbContext.ChangeTracker.Clear();
            return ConsumeResult.Duplicate;
        }
    }
}
