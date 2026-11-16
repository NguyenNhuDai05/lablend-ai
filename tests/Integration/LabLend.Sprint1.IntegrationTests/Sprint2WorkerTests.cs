using Borrowing.Domain;
using Borrowing.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace LabLend.Sprint1.IntegrationTests;

[Collection("postgres")]
public sealed class Sprint2WorkerTests(PostgresFixture postgres, ITestOutputHelper output)
{
    [PostgresFact]
    public async Task GateD_E_real_scheduled_jobs_two_instances_restart_no_spam_and_JWT_to_notification_to_overdue_to_return()
    {
        await using var stack = new Sprint2Stack(postgres); await stack.Start();
        await using var notification = new LiveApi();
        await notification.Start("Notification", postgres.NotificationConnection, postgres, settings: new Dictionary<string, string>
        { ["ConnectionStrings__Redis"] = postgres.RedisConnection, ["NotificationStream__PendingIdleMilliseconds"] = "1000" });
        var (type, assets) = await stack.Stock(1);
        var loan = await stack.SubmitAt(DateTimeOffset.UtcNow.AddSeconds(30), type); var id = loan["id"]!.GetValue<Guid>();
        await stack.Command(id, "approve"); await stack.Command(id, "handover");
        await using var workerA = new LiveBorrowingWorker(); await using var workerB = new LiveBorrowingWorker();
        await workerA.Start(postgres, stack); await workerB.Start(postgres, stack);
        using var inbox = Sprint2Stack.Client(notification.Address, stack.StudentToken);
        await Sprint2RecoveryTests.Wait(() => HasEvent(inbox, id, "LoanDueSoon"));
        await Sprint2RecoveryTests.Wait(() => HasEvent(inbox, id, "LoanOverdue"), seconds: 45);
        loan = await stack.Loan(id);
        Assert.Equal("BORROWED", (string?)loan["status"]); Assert.True(loan["isOverdue"]!.GetValue<bool>());
        await using (var db = Db())
        {
            Assert.Equal(2, await db.ReminderDispatches.CountAsync(d => d.LoanId == id));
            Assert.Equal(2, await db.OutboxMessages.CountAsync(m => m.AggregateId == id && (m.EventType == "LoanDueSoon" || m.EventType == "LoanOverdue")));
        }
        // Repeated passes and a real worker process restart must retain the same durable reminder keys.
        await workerA.DisposeAsync(); await workerA.Start(postgres, stack);
        await using (var db = Db()) Assert.Equal(2, await db.ReminderDispatches.CountAsync(d => d.LoanId == id));
        var item = loan["items"]![0]!["id"]!.GetValue<Guid>();
        await stack.Return(id, (item, "NORMAL"));
        await Sprint2RecoveryTests.Wait(() => HasEvent(inbox, id, "LoanReturned"));
        Assert.Equal("RETURNED", (string?)(await stack.Loan(id))["status"]);
        Assert.Equal("AVAILABLE", (string?)(await stack.Asset(assets[0]))["status"]);
        Assert.True(workerA.Healthy); Assert.True(workerB.Healthy);
        await workerA.DisposeAsync(); await workerB.DisposeAsync();
        foreach (var job in new[] { "DueReminderJob", "OverdueDetectionJob", "ReservationReconciliationJob", "OutboxPublisherJob" })
            Assert.Contains(job, workerB.Logs);
        output.WriteLine("PASS T17/T18 + Gate E: actual JWT -> Student REST submit -> Staff REST approve -> Inventory HTTP/2 reserve/handover -> scheduled Outbox XADD -> actual M3 consumer/REST -> naturally elapsed DueAt -> scheduled overdue -> real return. Two worker processes, heartbeat dependencies, restart and persistent no-spam keys verified.");
    }

    [PostgresFact]
    public async Task Reminder_jobs_use_real_PostgreSQL_time_thresholds_and_concurrent_unique_dispatches()
    {
        await using var stack = new Sprint2Stack(postgres); await stack.Start();
        var (type, _) = await stack.Stock(1); var id = (await stack.Submit(type))["id"]!.GetValue<Guid>();
        await stack.Command(id, "approve"); await stack.Command(id, "handover");
        var time = new WorkflowTestClock(DateTimeOffset.UtcNow.AddDays(1).AddHours(12));
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:Database"] = postgres.BorrowingConnection }).Build();
        await using var services = new ServiceCollection().AddLogging().AddBorrowingInfrastructure(config).AddSingleton<TimeProvider>(time).BuildServiceProvider();
        async Task Run(bool overdue) { await using var scope = services.CreateAsyncScope(); await scope.ServiceProvider.GetRequiredService<LoanReminderJobs>().Run(overdue, default); }
        await Task.WhenAll(Run(false), Run(false)); await Run(false);
        await using (var db = Db()) Assert.Single(await db.ReminderDispatches.Where(d => d.LoanId == id).ToListAsync());
        time.Now = time.Now.AddDays(2); await Task.WhenAll(Run(true), Run(true)); await Run(true);
        await using (var db = Db())
        {
            Assert.Equal(2, await db.ReminderDispatches.CountAsync(d => d.LoanId == id));
            Assert.Equal(LoanStatus.BORROWED, (await db.LoanRequests.SingleAsync(l => l.Id == id)).Status);
        }
    }
    private static async Task<bool> HasEvent(HttpClient client, Guid id, string type)
    {
        var result = await Sprint2Stack.Body(await client.GetAsync("api/notifications?pageSize=100"));
        return result["items"]!.AsArray().Any(n => (string?)n!["aggregateId"] == id.ToString() && (string?)n["eventType"] == type);
    }
    private BorrowingDbContext Db() => new(new DbContextOptionsBuilder<BorrowingDbContext>().UseNpgsql(postgres.BorrowingConnection).Options);
}
