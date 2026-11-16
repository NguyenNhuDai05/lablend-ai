using System.Text.Json;
using Borrowing.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Notification.Application;
using Notification.Infrastructure;
using StackExchange.Redis;
using Xunit;
using Xunit.Abstractions;

namespace LabLend.Sprint1.IntegrationTests;

[Collection("postgres")]
public sealed class Sprint2MessagingTests(PostgresFixture postgres, ITestOutputHelper output)
{
    [PostgresFact]
    public async Task Outbox_failure_rolls_back_the_entire_real_PostgreSQL_business_transaction()
    {
        var loan = global::Borrowing.Domain.LoanRequest.Create(Guid.NewGuid(), "Outbox rollback invariant", DateTimeOffset.UtcNow.AddDays(2), [Guid.NewGuid()], DateTimeOffset.UtcNow);
        await using (var db = Borrowing())
        {
            await db.Database.MigrateAsync();
            db.EventCorrelationId = new string('x', 65); // Violates the real outbox column bound.
            db.LoanRequests.Add(loan);
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        }
        await using var verify = Borrowing();
        Assert.False(await verify.LoanRequests.AnyAsync(l => l.Id == loan.Id));
        Assert.False(await verify.OutboxMessages.AnyAsync(m => m.AggregateId == loan.Id));
        Assert.False(await verify.LoanHistories.AnyAsync(h => h.LoanRequestId == loan.Id));
    }

    [PostgresFact]
    public async Task GateC_real_Redis_down_outbox_concurrent_publish_consumer_REST_duplicate_and_pending_replay()
    {
        await using var stack = new Sprint2Stack(postgres); await stack.Start();
        var time = new WorkflowTestClock(DateTimeOffset.UtcNow);
        var config = Config(stack);
        await using var publisher = new ServiceCollection().AddLogging().AddBorrowingInfrastructure(config)
            .AddBorrowingPublisher(config).AddSingleton<TimeProvider>(time).BuildServiceProvider();
        await using var notification = new LiveApi();
        await notification.Start("Notification", postgres.NotificationConnection, postgres, settings: new Dictionary<string, string>
        { ["ConnectionStrings__Redis"] = postgres.RedisConnection, ["NotificationStream__PendingIdleMilliseconds"] = "1000" });
        using var inboxApi = Sprint2Stack.Client(notification.Address, stack.StudentToken);
        var (type, _) = await stack.Stock(1);

        // Redis is genuinely stopped in this fixture-owned project. API commits never connect to Redis.
        await postgres.StopRedis();
        var loan = await stack.Submit(type); var id = loan["id"]!.GetValue<Guid>();
        await stack.Command(id, "approve");
        time.Now = DateTimeOffset.UtcNow.AddMinutes(1);
        await Publish(publisher);
        await using (var db = Borrowing())
        {
            var messages = await db.OutboxMessages.Where(m => m.AggregateId == id).ToListAsync();
            Assert.Equal(2, messages.Count); Assert.All(messages, m => Assert.Null(m.PublishedAt));
            Assert.Contains(messages, m => m.Attempts > 0 && m.LastError != null);
            foreach (var message in messages)
            {
                var mapped = new ProposedLoanEventMapper().Map("compatibility-test", LoanEvents.Envelope(message));
                Assert.Equal(stack.StudentId, mapped.RecipientUserId); Assert.Equal(message.EventId, mapped.EventId);
                Assert.DoesNotContain("password", message.PayloadJson, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("token", message.PayloadJson, StringComparison.OrdinalIgnoreCase);
            }
            Assert.Equal("sprint2-real-lifecycle", messages.Single(m => m.EventType == "LoanApproved").CorrelationId);
        }
        await postgres.StartRedis(); time.Now = time.Now.AddMinutes(10);
        await Sprint2RecoveryTests.Wait(() => Task.FromResult(publisher.GetRequiredService<IConnectionMultiplexer>().IsConnected));
        // Each publisher owns a fresh scope/DbContext. SKIP LOCKED prevents concurrent row ownership.
        await Task.WhenAll(Publish(publisher), Publish(publisher));
        await Sprint2RecoveryTests.Wait(async () => (await Sprint2Stack.Body(await inboxApi.GetAsync("api/notifications")))["items"]!.AsArray().Count(i => (string?)i!["aggregateId"] == id.ToString()) == 2);
        using var redis = await ConnectionMultiplexer.ConnectAsync(postgres.RedisConnection);
        var stream = redis.GetDatabase();
        var entries = await stream.StreamRangeAsync("lablend.events.v1");
        var eventIds = entries.Select(e => JsonSerializer.Deserialize<JsonElement>((string)e.Values.Single(v => v.Name == "event").Value!))
            .Where(e => e.GetProperty("aggregateId").GetGuid() == id).Select(e => e.GetProperty("eventId").GetGuid()).ToArray();
        Assert.Equal(2, eventIds.Length);
        Assert.Equal(eventIds.Length, eventIds.Distinct().Count());
        await using (var db = Borrowing()) Assert.All(await db.OutboxMessages.Where(m => m.AggregateId == id).ToListAsync(), m => Assert.NotNull(m.PublishedAt));

        // Duplicate XADD and publisher crash after XADD/before marker: same EventId must still make one notification.
        Borrowing.Domain.OutboxMessage original;
        await using (var db = Borrowing()) original = await db.OutboxMessages.FirstAsync(m => m.AggregateId == id);
        await stream.StreamAddAsync("lablend.events.v1", [new NameValueEntry("event", LoanEvents.Envelope(original))]);
        await using (var db = Borrowing())
            await db.OutboxMessages.Where(m => m.EventId == original.EventId).ExecuteUpdateAsync(s => s.SetProperty(m => m.PublishedAt, (DateTimeOffset?)null));
        await Publish(publisher);
        await Sprint2RecoveryTests.Wait(async () => (await stream.StreamPendingAsync("lablend.events.v1", "notification-consumers")).PendingMessageCount == 0);
        await using (var db = Notification()) Assert.Equal(2, await db.Notifications.CountAsync(n => n.AggregateId == id.ToString()));

        // Consumer failure window: actual M3 inbox commits a message read by a dead consumer but not XACKed.
        await notification.DisposeAsync();
        var replay = LoanEvents.Create(await Loan(id), "LoanDueSoon", Guid.NewGuid(), "consumer-crash-before-xack", DateTimeOffset.UtcNow);
        var replayStreamId = await stream.StreamAddAsync("lablend.events.v1", [new NameValueEntry("event", LoanEvents.Envelope(replay))]);
        var pending = await stream.StreamReadGroupAsync("lablend.events.v1", "notification-consumers", "test-dead-consumer", ">");
        Assert.Contains(pending, e => e.Id == replayStreamId);
        await using (var db = Notification())
            Assert.Equal(ConsumeResult.Created, await new EfNotificationInbox(db, TimeProvider.System).PersistAsync(new ProposedLoanEventMapper().Map(replayStreamId!, LoanEvents.Envelope(replay)), default));
        // Start the unchanged real Notification process; XAUTOCLAIM must replay/dedupe/ack this pending entry.
        await notification.Start("Notification", postgres.NotificationConnection, postgres, settings: new Dictionary<string, string>
        { ["ConnectionStrings__Redis"] = postgres.RedisConnection, ["NotificationStream__PendingIdleMilliseconds"] = "1000" });
        await Sprint2RecoveryTests.Wait(async () => (await stream.StreamPendingAsync("lablend.events.v1", "notification-consumers")).PendingMessageCount == 0);
        await using (var db = Notification()) Assert.Equal(1, await db.Notifications.CountAsync(n => n.EventId == replay.EventId));
        output.WriteLine("PASS T12–T16: real Redis stopped/restored, atomic outbox retained, concurrent XADD publishing, actual M3 consumer/REST, EventId dedupe, post-inbox/pre-XACK pending recovery by XAUTOCLAIM. Crash window is deterministically staged using M3's actual inbox implementation.");
    }

    internal IConfiguration Config(Sprint2Stack stack) => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["ConnectionStrings:Database"] = postgres.BorrowingConnection, ["ConnectionStrings:Redis"] = postgres.RedisConnection,
        ["Inventory:GrpcAddress"] = stack.InventoryGrpc, ["Inventory:ServiceToken"] = postgres.SigningKey,
        ["Outbox:BatchSize"] = "100"
    }).Build();
    internal static async Task Publish(ServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope(); await scope.ServiceProvider.GetRequiredService<OutboxPublisher>().Run(default);
    }
    private BorrowingDbContext Borrowing() => new(new DbContextOptionsBuilder<BorrowingDbContext>().UseNpgsql(postgres.BorrowingConnection).Options);
    private NotificationDbContext Notification() => new(new DbContextOptionsBuilder<NotificationDbContext>().UseNpgsql(postgres.NotificationConnection).Options);
    private async Task<Borrowing.Domain.LoanRequest> Loan(Guid id) { await using var db = Borrowing(); return await db.LoanRequests.SingleAsync(l => l.Id == id); }
}
