using Notification.Application;
using Notification.Domain;
using Xunit;

namespace LabLend.Platform.UnitTests;

public sealed class NotificationServiceTests
{
    [Fact]
    public async Task MarkRead_ReturnsNull_WhenRepositoryCannotFindOwnedNotification()
    {
        var repository = new FakeRepository();
        var service = new NotificationService(repository, TimeProvider.System);

        var result = await service.MarkReadAsync(Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None);

        Assert.Null(result);
        Assert.Equal(0, repository.SaveCount);
    }

    [Fact]
    public async Task MarkRead_PersistsOnlyFirstTransition()
    {
        var userId = Guid.NewGuid();
        var item = NotificationItem.Create(userId, Guid.NewGuid(), "LoanApproved", "loan-1", "Approved", "Ready.", DateTimeOffset.UtcNow);
        var repository = new FakeRepository(item);
        var service = new NotificationService(repository, TimeProvider.System);

        var first = await service.MarkReadAsync(item.Id, userId, CancellationToken.None);
        var second = await service.MarkReadAsync(item.Id, userId, CancellationToken.None);

        Assert.NotNull(first?.ReadAt);
        Assert.Equal(first?.ReadAt, second?.ReadAt);
        Assert.Equal(1, repository.SaveCount);
    }

    private sealed class FakeRepository(NotificationItem? item = null) : INotificationRepository
    {
        public int SaveCount { get; private set; }
        public Task<NotificationItem?> FindOwnedAsync(Guid notificationId, Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult(item is not null && item.Id == notificationId && item.UserId == userId ? item : null);
        public Task<(IReadOnlyList<NotificationItem> Items, long Total)> ListAsync(Guid userId, int page, int pageSize, CancellationToken cancellationToken) =>
            Task.FromResult(((IReadOnlyList<NotificationItem>)Array.Empty<NotificationItem>(), 0L));
        public Task<long> UnreadCountAsync(Guid userId, CancellationToken cancellationToken) => Task.FromResult(0L);
        public Task SaveChangesAsync(CancellationToken cancellationToken) { SaveCount++; return Task.CompletedTask; }
    }
}
