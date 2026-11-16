using System.Net;
using Borrowing.Application;
using Borrowing.Domain;
using Borrowing.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;
using Xunit.Abstractions;

namespace LabLend.Sprint1.IntegrationTests;

[Collection("postgres")]
public sealed class Sprint2RecoveryTests(PostgresFixture postgres, ITestOutputHelper output)
{
    [PostgresFact]
    public async Task A_late_error_after_competing_recovery_commit_never_reopens_or_compensates_a_completed_operation()
    {
        await using var stack = new Sprint2Stack(postgres); await stack.Start();
        foreach (var loseReply in new[] { false, true })
        {
            var (type, assets) = await stack.Stock(1); var id = (await stack.Submit(type))["id"]!.GetValue<Guid>();
            await using var provider = Provider(stack, new WorkflowTestClock(DateTimeOffset.UtcNow), competingCommit: loseReply);
            await using (var scope = provider.CreateAsyncScope())
                await scope.ServiceProvider.GetRequiredService<ILoanLifecycle>().Execute(id, "APPROVE", Guid.NewGuid(), "late-completed-race", null, default);
            await using (var db = Db())
            {
                Assert.Equal("COMPLETED", (await db.InventoryOperations.SingleAsync(o => o.LoanId == id)).State);
                Assert.Equal(LoanStatus.APPROVED, (await db.LoanRequests.SingleAsync(l => l.Id == id)).Status);
                Assert.Equal(1, await db.LoanHistories.CountAsync(h => h.LoanRequestId == id && h.Action == "APPROVED"));
                Assert.Equal(1, await db.OutboxMessages.CountAsync(m => m.AggregateId == id && m.EventType == "LoanApproved"));
            }
            Assert.Equal("RESERVED", (string?)(await stack.Asset(assets[0]))["status"]);
            await stack.Command(id, "handover"); await stack.Return(id, ((await Item(id)).Id, "NORMAL"));
        }
    }

    [PostgresFact]
    public async Task GateB_real_RPC_reply_loss_process_crash_compensation_expiry_and_bounded_retry()
    {
        await using var stack = new Sprint2Stack(postgres); await stack.Start();
        var time = new WorkflowTestClock(DateTimeOffset.UtcNow.AddMinutes(2));
        await using var normal = Provider(stack, time);
        var (type, assets) = await stack.Stock(1);
        var id = (await stack.Submit(type))["id"]!.GetValue<Guid>();

        // Transport fault injection drops a REAL committed Inventory response; no business response is mocked.
        foreach (var action in new[] { "APPROVE", "HANDOVER", "RETURN" })
        {
            await using var dropping = Provider(stack, time, action);
            await using (var scope = dropping.CreateAsyncScope())
            {
                var request = action == "RETURN" ? new ReturnLoanRequest([new((await Item(id)).Id, "NORMAL")]) : null;
                var failure = await Assert.ThrowsAsync<LoanFailure>(() => scope.ServiceProvider.GetRequiredService<ILoanLifecycle>()
                    .Execute(id, action, Guid.NewGuid(), "reply-loss-" + action, request, default));
                Assert.Equal(503, failure.Status);
            }
            var before = await Read(id);
            Assert.Equal(action switch { "APPROVE" => LoanStatus.APPROVING, "HANDOVER" => LoanStatus.APPROVED, _ => LoanStatus.BORROWED }, before.Status);
            time.Now = time.Now.AddMinutes(10);
            await Reconcile(normal); await Reconcile(normal);
            var after = await Read(id);
            Assert.Equal(action switch { "APPROVE" => LoanStatus.APPROVED, "HANDOVER" => LoanStatus.BORROWED, _ => LoanStatus.RETURNED }, after.Status);
            Assert.Equal(assets[0], (await Item(id)).AssetUnitId);
        }

        // Local database failure AFTER the real reservation committed -> durable compensation -> HELD release.
        var commitFailure = (await stack.Submit(type))["id"]!.GetValue<Guid>();
        await InstallTrigger(commitFailure, sleep: false);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await stack.Staff.PostAsync($"api/loans/{commitFailure}/approve", null)).StatusCode);
        await using (var db = Db()) Assert.Equal("COMPENSATING", (await db.InventoryOperations.SingleAsync(o => o.LoanId == commitFailure)).State);
        Assert.Equal("RESERVED", (string?)(await stack.Asset(assets[0]))["status"]);
        await RemoveTrigger(); time.Now = time.Now.AddMinutes(10); await Reconcile(normal);
        Assert.Equal(LoanStatus.APPROVAL_FAILED, (await Read(commitFailure)).Status);
        Assert.Equal("AVAILABLE", (string?)(await stack.Asset(assets[0]))["status"]);

        // Real process termination while its local APPROVED transaction is sleeping after gRPC reserve.
        var crash = (await stack.Submit(type))["id"]!.GetValue<Guid>();
        await InstallTrigger(crash, sleep: true);
        var pending = stack.Staff.PostAsync($"api/loans/{crash}/approve", null);
        await Wait(async () =>
        {
            await using var connection = new NpgsqlConnection(postgres.BorrowingConnection); await connection.OpenAsync();
            await using var command = new NpgsqlCommand("SELECT EXISTS(SELECT 1 FROM pg_stat_activity WHERE datname=current_database() AND wait_event='PgSleep')", connection);
            return (bool)(await command.ExecuteScalarAsync())!;
        });
        await stack.CrashBorrowing();
        try { await pending; } catch (HttpRequestException) { } catch (TaskCanceledException) { }
        await RemoveTrigger();
        Assert.Equal(LoanStatus.APPROVING, (await Read(crash)).Status);
        time.Now = time.Now.AddMinutes(10); await Reconcile(normal); await Reconcile(normal);
        Assert.Equal(LoanStatus.APPROVED, (await Read(crash)).Status);
        Assert.Equal(assets[0], (await Item(crash)).AssetUnitId);
        await stack.RestartBorrowing();
        await stack.Command(crash, "handover"); await stack.Return(crash, ((await Item(crash)).Id, "NORMAL"));

        // Pickup expiry releases only HELD reservations. The loan remains auditable as EXPIRED.
        var expiry = (await stack.Submit(type))["id"]!.GetValue<Guid>(); await stack.Command(expiry, "approve");
        time.Now = DateTimeOffset.UtcNow.AddHours(25); await Reconcile(normal); await Reconcile(normal);
        Assert.Equal(LoanStatus.EXPIRED, (await Read(expiry)).Status);
        Assert.Equal("AVAILABLE", (string?)(await stack.Asset(assets[0]))["status"]);

        // Even an inconsistent persisted release intent may never free an already handed-over asset.
        var (safeType, safeAssets) = await stack.Stock(1);
        var handedOver = (await stack.Submit(safeType))["id"]!.GetValue<Guid>();
        await stack.Command(handedOver, "approve"); await stack.Command(handedOver, "handover");
        await using (var db = Db())
        {
            db.InventoryOperations.Add(new InventoryOperation { LoanId = handedOver, OperationType = "RELEASE", State = "COMPENSATING",
                StartedAt = time.Now, NextRetryAt = time.Now, CorrelationId = "safety-no-release-after-handover" });
            await db.SaveChangesAsync();
        }
        await Reconcile(normal);
        Assert.Equal("ON_LOAN", (string?)(await stack.Asset(safeAssets[0]))["status"]);

        // A wrong service credential is rejected by the real server; no inventory mutation occurs.
        var invalidAuth = (await stack.Submit(type))["id"]!.GetValue<Guid>();
        time.Now = DateTimeOffset.UtcNow;
        await using var bad = Provider(stack, time, token: new string('x', 64), maxAttempts: 2);
        await using (var scope = bad.CreateAsyncScope())
            Assert.Equal(503, (await Assert.ThrowsAsync<LoanFailure>(() => scope.ServiceProvider.GetRequiredService<ILoanLifecycle>()
                .Execute(invalidAuth, "APPROVE", Guid.NewGuid(), "bad-service-token", null, default))).Status);
        time.Now = time.Now.AddMinutes(10); await Reconcile(bad); await Reconcile(bad);
        await using (var db = Db())
        {
            var operation = await db.InventoryOperations.SingleAsync(o => o.LoanId == invalidAuth);
            Assert.Equal("MANUAL_REVIEW", operation.State); Assert.Equal(2, operation.RetryCount);
        }
        Assert.Equal("AVAILABLE", (string?)(await stack.Asset(assets[0]))["status"]);
        output.WriteLine("PASS T09/T27/T28: real committed RPC replies dropped for all three commands; real Borrowing process killed after reserve; failed commit compensated; pickup expiry released; unauthorized RPC bounded at manual review.");
    }

    internal ServiceProvider Provider(Sprint2Stack stack, WorkflowTestClock time, string? drop = null, string? token = null, int maxAttempts = 8, bool? competingCommit = null)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Database"] = postgres.BorrowingConnection, ["Inventory:GrpcAddress"] = stack.InventoryGrpc,
            ["Inventory:ServiceToken"] = token ?? postgres.SigningKey, ["BorrowingWorkflow:MaxAttempts"] = maxAttempts.ToString()
        }).Build();
        var services = new ServiceCollection().AddLogging().AddBorrowingInfrastructure(config);
        services.AddSingleton<TimeProvider>(time);
        if (drop != null) services.AddScoped<IInventoryClient>(sp => new DropCommittedReply(new InventoryGrpcClient(
            sp.GetRequiredService<Grpc.Net.Client.GrpcChannel>(), sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<InventoryOptions>>()), drop));
        if (competingCommit.HasValue) services.AddScoped<IInventoryClient>(sp => new CompetingRecoveryClient(new InventoryGrpcClient(
            sp.GetRequiredService<Grpc.Net.Client.GrpcChannel>(), sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<InventoryOptions>>()), postgres.BorrowingConnection, competingCommit.Value));
        return services.BuildServiceProvider();
    }
    internal static async Task Reconcile(ServiceProvider provider)
    {
        await using var scope = provider.CreateAsyncScope(); await scope.ServiceProvider.GetRequiredService<LoanReconciliation>().Run(default);
    }
    private BorrowingDbContext Db() => new(new DbContextOptionsBuilder<BorrowingDbContext>().UseNpgsql(postgres.BorrowingConnection).Options);
    private async Task<LoanRequest> Read(Guid id) { await using var db = Db(); return await db.LoanRequests.SingleAsync(l => l.Id == id); }
    private async Task<LoanItem> Item(Guid id) { await using var db = Db(); return await db.LoanItems.SingleAsync(i => i.LoanRequestId == id); }
    private async Task InstallTrigger(Guid id, bool sleep)
    {
        await using var db = Db();
        // Only a generated, private fixture database. Never runs against the shared Compose databases.
#pragma warning disable EF1002 // DDL cannot parameterize a function body; id is a generated Guid and body is two constant strings.
        await db.Database.ExecuteSqlRawAsync($"""
            CREATE FUNCTION s2_local_commit_fault() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN
              IF NEW."Id" = '{id}'::uuid AND NEW."Status" = 'APPROVED' THEN
                {(sleep ? "PERFORM pg_sleep(10);" : "RAISE EXCEPTION 'test-owned local commit failure';")}
              END IF;
              RETURN NEW;
            END $$;
            CREATE TRIGGER s2_local_commit_fault BEFORE UPDATE ON "LoanRequests" FOR EACH ROW EXECUTE FUNCTION s2_local_commit_fault();
            """);
#pragma warning restore EF1002
    }
    private async Task RemoveTrigger()
    {
        await using var db = Db();
        await db.Database.ExecuteSqlRawAsync("DROP TRIGGER s2_local_commit_fault ON \"LoanRequests\"; DROP FUNCTION s2_local_commit_fault()");
    }
    internal static async Task Wait(Func<Task<bool>> condition, int seconds = 30)
    {
        var deadline = DateTime.UtcNow.AddSeconds(seconds);
        while (!await condition()) { if (DateTime.UtcNow >= deadline) throw new TimeoutException("Real service condition not reached."); await Task.Delay(100); }
    }
}

internal sealed class WorkflowTestClock(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;
    public override DateTimeOffset GetUtcNow() => Now;
}

internal sealed class DropCommittedReply(IInventoryClient inner, string action) : IInventoryClient
{
    private async Task<InventorySnapshot> Reply(Task<InventorySnapshot> actual, string type)
    {
        var result = await actual;
        if (action == type) throw new InventoryFailure(503, "TEST_DROPPED_COMMITTED_REPLY", true);
        return result;
    }
    public Task<InventorySnapshot> Reserve(Guid op, Guid loan, IReadOnlyDictionary<Guid, Guid> items, string correlation, CancellationToken ct) => Reply(inner.Reserve(op, loan, items, correlation, ct), "APPROVE");
    public Task<InventorySnapshot> Handover(Guid loan, string correlation, CancellationToken ct) => Reply(inner.Handover(loan, correlation, ct), "HANDOVER");
    public Task<InventorySnapshot> Return(Guid loan, IReadOnlyDictionary<Guid, string> items, string correlation, CancellationToken ct) => Reply(inner.Return(loan, items, correlation, ct), "RETURN");
    public Task<InventorySnapshot> Release(Guid loan, string correlation, CancellationToken ct) => inner.Release(loan, correlation, ct);
    public Task<InventorySnapshot> Status(Guid op, string correlation, CancellationToken ct) => inner.Status(op, correlation, ct);
}

// Fault fixture: bypasses the first executor's lock to model a lost coordination session. Inventory is real;
// a separate real PostgreSQL transaction commits the competing recovery before the first executor resumes.
internal sealed class CompetingRecoveryClient(IInventoryClient inner, string connection, bool loseReply) : IInventoryClient
{
    public async Task<InventorySnapshot> Reserve(Guid op, Guid loanId, IReadOnlyDictionary<Guid, Guid> items, string correlation, CancellationToken ct)
    {
        var result = await inner.Reserve(op, loanId, items, correlation, ct);
        await using var db = new BorrowingDbContext(new DbContextOptionsBuilder<BorrowingDbContext>().UseNpgsql(connection).Options);
        var loan = await db.LoanRequests.Include(l => l.Items).Include(l => l.Histories).SingleAsync(l => l.Id == loanId, ct);
        var operation = await db.InventoryOperations.SingleAsync(o => o.OperationId == op, ct);
        foreach (var allocation in result.Allocations)
        {
            var item = loan.Items.Single(i => i.Id == allocation.LoanItemId); item.AssetUnitId = allocation.AssetUnitId; item.ItemStatus = ItemStatus.RESERVED;
        }
        loan.Approve(operation.ActorId, DateTimeOffset.UtcNow); operation.State = "COMPLETED"; operation.CompletedAt = DateTimeOffset.UtcNow;
        db.EventCorrelationId = correlation; await db.SaveChangesAsync(ct);
        if (loseReply) throw new InventoryFailure(503, "TEST_LATE_REPLY_LOSS", true);
        return result;
    }
    public Task<InventorySnapshot> Handover(Guid loan, string correlation, CancellationToken ct) => inner.Handover(loan, correlation, ct);
    public Task<InventorySnapshot> Return(Guid loan, IReadOnlyDictionary<Guid, string> items, string correlation, CancellationToken ct) => inner.Return(loan, items, correlation, ct);
    public Task<InventorySnapshot> Release(Guid loan, string correlation, CancellationToken ct) => inner.Release(loan, correlation, ct);
    public Task<InventorySnapshot> Status(Guid op, string correlation, CancellationToken ct) => inner.Status(op, correlation, ct);
}
