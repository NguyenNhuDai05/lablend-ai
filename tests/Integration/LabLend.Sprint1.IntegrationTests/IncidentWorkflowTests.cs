using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Borrowing.Application;
using Borrowing.Domain;
using Borrowing.Infrastructure;
using Inventory.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace LabLend.Sprint1.IntegrationTests;

[Collection("postgres")]
public sealed class IncidentWorkflowTests(PostgresFixture postgres, ITestOutputHelper output)
{
    private BorrowingDbContext Db() => new(new DbContextOptionsBuilder<BorrowingDbContext>().UseNpgsql(postgres.BorrowingConnection).Options);
    private InventoryDbContext InventoryDb() => new(new DbContextOptionsBuilder<InventoryDbContext>().UseNpgsql(postgres.InventoryConnection).Options);
    private static async Task<T> Body<T>(HttpResponseMessage response, HttpStatusCode expected = HttpStatusCode.OK)
    {
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == expected, $"Expected {(int)expected}, got {(int)response.StatusCode}: {text}");
        return JsonSerializer.Deserialize<T>(text, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
    }
    private static Task<HttpResponseMessage> Post(HttpClient client, string path, object? body = null, Guid? key = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path);
        if (body != null) request.Content = JsonContent.Create(body);
        if (key.HasValue) request.Headers.Add("Idempotency-Key", key.ToString());
        return client.SendAsync(request);
    }
    private static async Task<LoanDto> Borrow(Sprint2Stack stack, Guid type)
    {
        var id = (await stack.Submit(type))["id"]!.GetValue<Guid>();
        await stack.Command(id, "approve"); await stack.Command(id, "handover");
        return await Body<LoanDto>(await stack.Student.GetAsync($"api/loans/{id}"));
    }
    private static ReportIncidentRequest ReportBody(LoanDto loan) => new(loan.Id, loan.Items.Single(i => i.ItemStatus == "BORROWED").Id, "POWER_FAILURE", "Thiết bị không lên nguồn trong lúc làm thí nghiệm.", true);
    private static async Task<IncidentDto> Report(Sprint2Stack stack, LoanDto loan, Guid? key = null) =>
        await Body<IncidentDto>(await Post(stack.Student, "api/incidents", ReportBody(loan), key ?? Guid.NewGuid()), HttpStatusCode.Created);
    private static async Task<IncidentDto> Inspect(Sprint2Stack stack, IncidentDto incident, string outcome = "DEFECTIVE")
    {
        await Body<IncidentDto>(await Post(stack.Staff, $"api/incidents/{incident.Id}/start-inspection"));
        return await Body<IncidentDto>(await Post(stack.Staff, $"api/incidents/{incident.Id}/inspection", new InspectIncidentRequest(outcome,
            "Đã kiểm tra trực tiếp nguồn và chức năng thiết bị.", outcome == "FIXED" ? "Đã thay cáp nguồn và kiểm tra hoạt động bình thường." : null,
            outcome != "LOST", outcome == "LOST" ? null : incident.SerialNumber)));
    }
    private async Task<HttpClient> Admin(Sprint2Stack stack)
    {
        using var auth = new HttpClient { BaseAddress = new Uri(stack.IdentityAddress) };
        auth.DefaultRequestHeaders.Add("X-LabLend-CSRF", "1"); auth.DefaultRequestHeaders.Add("Origin", "http://localhost:5173");
        var session = await Sprint2Stack.Body(await auth.PostAsJsonAsync("api/auth/login", new { email = "admin@sprint1.test", password = postgres.AdminPassword }));
        return Sprint2Stack.Client(stack.BorrowingAddress, session["accessToken"]!.GetValue<string>());
    }
    private async Task<HttpClient> OtherStudent(Sprint2Stack stack)
    {
        using var auth = new HttpClient { BaseAddress = new Uri(stack.IdentityAddress) };
        auth.DefaultRequestHeaders.Add("X-LabLend-CSRF", "1"); auth.DefaultRequestHeaders.Add("Origin", "http://localhost:5173");
        var email = Guid.NewGuid().ToString("N") + "@incident.test";
        await Sprint2Stack.Body(await auth.PostAsJsonAsync("api/auth/register", new { email, password = stack.TestPassword, confirmPassword = stack.TestPassword, displayName = "Other incident student" }));
        var session = await Sprint2Stack.Body(await auth.PostAsJsonAsync("api/auth/login", new { email, password = stack.TestPassword }));
        return Sprint2Stack.Client(stack.BorrowingAddress, session["accessToken"]!.GetValue<string>());
    }

    [PostgresFact]
    public async Task Ownership_roles_duplicate_reports_private_evidence_no_fault_and_minor_fix_are_enforced()
    {
        await using var stack = new Sprint2Stack(postgres); await stack.Start();
        var (type, _) = await stack.Stock(2); var loan = await Borrow(stack, type); var key = Guid.NewGuid();
        var incident = await Report(stack, loan, key);
        Assert.StartsWith("INC-", incident.Code);
        Assert.Equal(incident.Id, (await Report(stack, loan, key)).Id);
        Assert.Equal(HttpStatusCode.Conflict, (await Post(stack.Student, "api/incidents", ReportBody(loan), Guid.NewGuid())).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Post(stack.Student, "api/incidents", ReportBody(loan) with { Description = "Different purpose for the same durable key" }, key)).StatusCode);
        using var other = await OtherStudent(stack); using var admin = await Admin(stack);
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"api/incidents/{incident.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Post(other, "api/incidents", ReportBody(loan), Guid.NewGuid())).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Post(stack.Staff, "api/incidents", ReportBody(loan), Guid.NewGuid())).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Post(admin, "api/incidents", ReportBody(loan), Guid.NewGuid())).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Post(stack.Student, $"api/incidents/{incident.Id}/start-inspection")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await stack.Student.GetAsync("api/incidents/stats")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await stack.Staff.GetAsync("api/incidents/stats")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await stack.Student.GetAsync("api/incidents")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"api/loans/{loan.Id}/asset-identities")).StatusCode);
        using (var form = new MultipartFormDataContent())
        {
            form.Add(new ByteArrayContent(IncidentImageTests.Png), "file", "evidence.jpg");
            var evidence = await Body<IncidentEvidenceDto>(await stack.Student.PostAsync($"api/incidents/{incident.Id}/evidence", form));
            Assert.Equal("image/png", evidence.ContentType);
            Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"api/incidents/{incident.Id}/evidence/{evidence.Id}")).StatusCode);
            var image = await stack.Staff.GetAsync($"api/incidents/{incident.Id}/evidence/{evidence.Id}");
            Assert.Equal(HttpStatusCode.OK, image.StatusCode); Assert.Equal(IncidentImageTests.Png, await image.Content.ReadAsByteArrayAsync());
        }
        var additionalPng = new[] {
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR4nGNg+M/wHwAEAQH/cetH5QAAAABJRU5ErkJggg==",
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR4nGNgYPj/HwADAgH/5ncLrgAAAABJRU5ErkJggg==",
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR4nGP4////fwAJ+wP9KobjigAAAABJRU5ErkJggg==" };
        for (var index = 0; index < additionalPng.Length; index++)
        {
            using var form = new MultipartFormDataContent(); form.Add(new ByteArrayContent(Convert.FromBase64String(additionalPng[index])), "file", "support.png");
            var upload = await stack.Student.PostAsync($"api/incidents/{incident.Id}/evidence", form);
            Assert.Equal(index < 2 ? HttpStatusCode.OK : HttpStatusCode.BadRequest, upload.StatusCode);
        }
        Assert.Equal(3, (await Body<IncidentDto>(await stack.Student.GetAsync($"api/incidents/{incident.Id}"))).Evidence.Count);
        await Body<IncidentDto>(await Post(stack.Staff, $"api/incidents/{incident.Id}/start-inspection"));
        Assert.Equal(HttpStatusCode.Conflict, (await Post(stack.Staff, $"api/incidents/{incident.Id}/inspection", new InspectIncidentRequest("DEFECTIVE", "Physical inspection findings recorded", null, false, incident.SerialNumber))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Post(stack.Staff, $"api/incidents/{incident.Id}/inspection", new InspectIncidentRequest("DEFECTIVE", "Physical inspection findings recorded", null, true, "WRONG-SERIAL"))).StatusCode);
        incident = await Body<IncidentDto>(await Post(stack.Staff, $"api/incidents/{incident.Id}/inspection", new InspectIncidentRequest("NO_FAULT", "Device works normally after physical inspection", null, true, incident.SerialNumber)));
        Assert.Equal("RESOLVED_NO_FAULT", incident.Status);
        var repaired = await Inspect(stack, await Report(stack, loan), "FIXED"); Assert.Equal("RESOLVED_REPAIRED", repaired.Status);
        Assert.Equal("ON_LOAN", (string?)(await stack.Asset(loan.Items[0].AssetUnitId!.Value))["status"]);
        await using (var inventory = InventoryDb()) Assert.Equal(0, await inventory.MaintenanceRecords.CountAsync(m => m.AssetUnitId == loan.Items[0].AssetUnitId));
        var stats = await Body<IncidentStatistics>(await admin.GetAsync($"api/incidents/stats?equipmentTypeId={type}"));
        Assert.Equal(2, stats.Total); Assert.Equal(2, stats.ClosedWithoutReplacement); Assert.Empty(stats.VerifiedFailures); Assert.NotNull(stats.AverageResolutionHours);
        var empty = await Body<IncidentStatistics>(await admin.GetAsync($"api/incidents/stats?equipmentTypeId={Guid.NewGuid()}"));
        Assert.Equal(0, empty.Total); Assert.Null(empty.AverageResolutionHours); Assert.Empty(empty.VerifiedFailures);
        await stack.Return(loan.Id, (loan.Items[0].Id, "NORMAL"));
        Assert.Equal(HttpStatusCode.Conflict, (await Post(stack.Student, "api/incidents", ReportBody(loan), Guid.NewGuid())).StatusCode);
        output.WriteLine("PASS: ownership, role guards, durable report replay, duplicate open incident, private real image, physical serial check, no-fault/minor-fix, real statistics.");
    }

    [PostgresFact]
    public async Task Immediate_exchange_is_atomic_auditable_idempotent_and_normal_return_still_works()
    {
        await using var stack = new Sprint2Stack(postgres); await stack.Start();
        var (type, _) = await stack.Stock(2); var loan = await Borrow(stack, type); var incident = await Inspect(stack, await Report(stack, loan));
        var key = Guid.NewGuid(); var path = $"api/incidents/{incident.Id}/replace";
        Assert.Equal(HttpStatusCode.Conflict, (await Post(stack.Staff, path, new ReplaceIncidentRequest(false), Guid.NewGuid())).StatusCode);
        var attempts = await Task.WhenAll(Post(stack.Staff, path, new ReplaceIncidentRequest(true), key), Post(stack.Staff, path, new ReplaceIncidentRequest(true), key));
        foreach (var attempt in attempts.Where(a => a.StatusCode != HttpStatusCode.OK)) output.WriteLine(await attempt.Content.ReadAsStringAsync());
        Assert.Contains(attempts, a => a.StatusCode == HttpStatusCode.OK);
        Assert.All(attempts, a => Assert.Contains(a.StatusCode, new[] { HttpStatusCode.OK, HttpStatusCode.Conflict }));
        incident = await Body<IncidentDto>(await Post(stack.Staff, path, new ReplaceIncidentRequest(true), key));
        Assert.Equal("RESOLVED_REPLACED", incident.Status);
        Assert.Equal(HttpStatusCode.Conflict, (await Post(stack.Staff, path, new ReplaceIncidentRequest(true), Guid.NewGuid())).StatusCode);
        var updated = await Body<LoanDto>(await stack.Student.GetAsync($"api/loans/{loan.Id}"));
        Assert.Equal("BORROWED", updated.Status); Assert.Equal(loan.DueAt, updated.DueAt); Assert.Equal(2, updated.Items.Count);
        var old = updated.Items.Single(i => i.Id == loan.Items[0].Id); var replacement = updated.Items.Single(i => i.Id != old.Id);
        Assert.Equal("DAMAGED", old.ItemStatus); Assert.Equal(loan.Items[0].AssetUnitId, old.AssetUnitId); Assert.NotNull(old.ReturnedAt);
        Assert.Equal(old.Id, replacement.ReplacesLoanItemId); Assert.Equal(incident.Id, replacement.IncidentId); Assert.NotEqual(old.AssetUnitId, replacement.AssetUnitId);
        Assert.Equal("MAINTENANCE", (string?)(await stack.Asset(old.AssetUnitId!.Value))["status"]);
        Assert.Equal("ON_LOAN", (string?)(await stack.Asset(replacement.AssetUnitId!.Value))["status"]);
        await using (var inventory = InventoryDb())
        {
            Assert.Equal(1, await inventory.ReplacementOperations.CountAsync(o => o.OperationId == key));
            Assert.Equal(1, await inventory.MaintenanceRecords.CountAsync(m => m.AssetUnitId == old.AssetUnitId));
            Assert.Equal(1, await inventory.Reservations.CountAsync(r => r.LoanItemId == replacement.Id));
            Assert.True(await inventory.StockMovements.AnyAsync(m => m.AssetUnitId == old.AssetUnitId && m.FromStatus == "ON_LOAN" && m.ToStatus == "MAINTENANCE"));
            Assert.True(await inventory.StockMovements.AnyAsync(m => m.AssetUnitId == replacement.AssetUnitId && m.FromStatus == "RESERVED" && m.ToStatus == "ON_LOAN"));
        }
        await using (var db = Db())
        {
            Assert.Equal(1, await db.OutboxMessages.CountAsync(m => m.AggregateId == incident.Id && m.EventType == "EquipmentReplaced"));
            Assert.Equal(1, await db.OutboxMessages.CountAsync(m => m.AggregateId == incident.Id && m.EventType == "IncidentResolved"));
            var events = await db.OutboxMessages.Where(m => m.AggregateId == incident.Id).ToListAsync();
            var mapper = new Notification.Application.ProposedLoanEventMapper();
            Assert.All(events, e => Assert.Equal(e.EventId, mapper.Map("1-0", LoanEvents.Envelope(e)).EventId));
            var reported = events.Single(e => e.EventType == "IncidentReported");
            Assert.Equal(loan.HandedOverBy, mapper.Map("2-0", LoanEvents.Envelope(reported)).RecipientUserId);
        }
        await stack.Return(loan.Id, (replacement.Id, "NORMAL"));
        Assert.Equal("RETURNED", (await Body<LoanDto>(await stack.Student.GetAsync($"api/loans/{loan.Id}"))).Status);
        Assert.All(incident.History, h => { Assert.NotEqual(Guid.Empty, h.ActorUserId); Assert.NotEqual(default, h.OccurredAt); });
        output.WriteLine("PASS: immediate same-loan exchange, old/new identities, unchanged due date, maintenance and movements, double click/replay, one notification event, subsequent normal return.");
    }

    [PostgresFact]
    public async Task Shortage_waiting_consent_followup_cancellation_and_lost_return_use_real_allocations()
    {
        await using var stack = new Sprint2Stack(postgres); await stack.Start();
        var (type, _) = await stack.Stock(1); var loan = await Borrow(stack, type); var incident = await Inspect(stack, await Report(stack, loan));
        var shortageKey = Guid.NewGuid();
        var shortage = await Post(stack.Staff, $"api/incidents/{incident.Id}/replace", new ReplaceIncidentRequest(true), shortageKey);
        Assert.Equal(HttpStatusCode.Conflict, shortage.StatusCode); Assert.Contains("OUT_OF_STOCK", await shortage.Content.ReadAsStringAsync());
        Assert.Equal("ON_LOAN", (string?)(await stack.Asset(loan.Items[0].AssetUnitId!.Value))["status"]);
        await using (var db = Db()) Assert.False(await db.OutboxMessages.AnyAsync(m => m.AggregateId == incident.Id && m.EventType == "EquipmentReplaced"));
        incident = await Body<IncidentDto>(await Post(stack.Staff, $"api/incidents/{incident.Id}/defective-return", key: Guid.NewGuid()));
        Assert.Equal("RETURNED", (await Body<LoanDto>(await stack.Student.GetAsync($"api/loans/{loan.Id}"))).Status);
        Assert.Equal(HttpStatusCode.Conflict, (await Post(stack.Staff, $"api/incidents/{incident.Id}/replace", new ReplaceIncidentRequest(true), Guid.NewGuid())).StatusCode);
        incident = await Body<IncidentDto>(await Post(stack.Student, $"api/incidents/{incident.Id}/wait")); Assert.True(incident.WaitingConsented);
        await Sprint2Stack.Body(await stack.InventoryStaff.PostAsJsonAsync("api/inventory/assets", new { equipmentTypeId = type, serialNumber = "DELAYED-" + Guid.NewGuid().ToString("N"), location = "test", notes = (string?)null }));
        await using (var db = Db())
        {
            var rejected = await db.InventoryOperations.SingleAsync(o => o.OperationId == shortageKey);
            var intent = JsonSerializer.Deserialize<IncidentIntent>(rejected.RequestJson)!;
            await using var provider = Provider(stack, new WorkflowTestClock(DateTimeOffset.UtcNow)); await using var scope = provider.CreateAsyncScope();
            var client = scope.ServiceProvider.GetRequiredService<IInventoryIncidentClient>();
            Assert.Equal("OUT_OF_STOCK", (await Assert.ThrowsAsync<InventoryFailure>(() => client.ReplacementStatus(shortageKey, "shortage-receipt", default))).Message);
            Assert.Equal("OUT_OF_STOCK", (await Assert.ThrowsAsync<InventoryFailure>(() => client.Replace(intent.Replacement!, "shortage-replay-after-restock", default))).Message);
            Assert.Equal("OPERATION_ID_REUSED", (await Assert.ThrowsAsync<InventoryFailure>(() => client.Replace(intent.Replacement! with { ActorId = Guid.NewGuid() }, "shortage-hash", default))).Message);
        }
        incident = await Body<IncidentDto>(await Post(stack.Staff, $"api/incidents/{incident.Id}/replace", new ReplaceIncidentRequest(true), Guid.NewGuid()));
        var followUpId = incident.Replacements.Single(r => r.ReplacedAt != null).ReplacementLoanId; Assert.NotEqual(loan.Id, followUpId);
        var followUp = await Body<LoanDto>(await stack.Student.GetAsync($"api/loans/{followUpId}"));
        Assert.Equal("BORROWED", followUp.Status); Assert.Equal(loan.DueAt, followUp.DueAt); Assert.Equal(loan.StudentId, followUp.StudentId);
        Assert.Equal("RETURNED", (await Body<LoanDto>(await stack.Student.GetAsync($"api/loans/{loan.Id}"))).Status);
        await stack.Return(followUpId, (followUp.Items[0].Id, "NORMAL"));
        foreach (var lost in new[] { false, true })
        {
            var (nextType, _) = await stack.Stock(1); var nextLoan = await Borrow(stack, nextType);
            var next = await Inspect(stack, await Report(stack, nextLoan), lost ? "LOST" : "DEFECTIVE");
            next = await Body<IncidentDto>(await Post(stack.Staff, $"api/incidents/{next.Id}/defective-return", key: Guid.NewGuid()));
            if (lost) { Assert.Equal("RESOLVED_NO_REPLACEMENT", next.Status); Assert.Equal("LOST", (string?)(await stack.Asset(nextLoan.Items[0].AssetUnitId!.Value))["status"]); }
            else
            {
                await Body<IncidentDto>(await Post(stack.Student, $"api/incidents/{next.Id}/wait"));
                next = await Body<IncidentDto>(await Post(stack.Student, $"api/incidents/{next.Id}/cancel")); Assert.Equal("CANCELLED", next.Status);
                Assert.Equal(HttpStatusCode.Conflict, (await Post(stack.Staff, $"api/incidents/{next.Id}/replace", new ReplaceIncidentRequest(true), Guid.NewGuid())).StatusCode);
            }
        }
        output.WriteLine("PASS: shortage is 409 with no allocation/event, physical damaged return, genuine wait consent, linked follow-up after terminal return, cancellation and LOST without exchange.");
    }

    internal ServiceProvider Provider(Sprint2Stack stack, WorkflowTestClock time, bool dropReply = false, int maxAttempts = 8)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
            ["ConnectionStrings:Database"] = postgres.BorrowingConnection, ["Inventory:GrpcAddress"] = stack.InventoryGrpc,
            ["Inventory:ServiceToken"] = postgres.SigningKey, ["BorrowingWorkflow:MaxAttempts"] = maxAttempts.ToString() }).Build();
        var services = new ServiceCollection().AddLogging().AddBorrowingInfrastructure(configuration).AddSingleton<TimeProvider>(time);
        if (dropReply) services.AddScoped<IInventoryIncidentClient>(sp => new DropReplacementReply(new InventoryGrpcClient(
            sp.GetRequiredService<Grpc.Net.Client.GrpcChannel>(), sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<InventoryOptions>>())));
        return services.BuildServiceProvider();
    }

    [PostgresFact]
    public async Task Concurrent_students_cannot_exchange_into_the_same_last_available_serial()
    {
        await using var stack = new Sprint2Stack(postgres); await stack.Start();
        var (type, _) = await stack.Stock(3); var firstLoan = await Borrow(stack, type);
        using var other = await OtherStudent(stack);
        var submitted = await Body<LoanDto>(await Post(other, "api/loans", new CreateLoanRequest("Second student laboratory replacement scenario", DateTimeOffset.UtcNow.AddDays(2), [new(type)])), HttpStatusCode.Created);
        await stack.Command(submitted.Id, "approve"); await stack.Command(submitted.Id, "handover");
        var secondLoan = await Body<LoanDto>(await other.GetAsync($"api/loans/{submitted.Id}"));
        var first = await Inspect(stack, await Report(stack, firstLoan));
        var second = await Inspect(stack, await Body<IncidentDto>(await Post(other, "api/incidents", ReportBody(secondLoan), Guid.NewGuid()), HttpStatusCode.Created));
        var results = await Task.WhenAll(Post(stack.Staff, $"api/incidents/{first.Id}/replace", new ReplaceIncidentRequest(true), Guid.NewGuid()),
            Post(stack.Staff, $"api/incidents/{second.Id}/replace", new ReplaceIncidentRequest(true), Guid.NewGuid()));
        Assert.Single(results, r => r.StatusCode == HttpStatusCode.OK); Assert.Single(results, r => r.StatusCode == HttpStatusCode.Conflict);
        var winner = await Body<IncidentDto>(results.Single(r => r.StatusCode == HttpStatusCode.OK));
        var issued = winner.Replacements.Single(r => r.ReplacedAt != null);
        await using var inventory = InventoryDb();
        Assert.Equal(1, await inventory.Reservations.CountAsync(r => r.AssetUnitId == issued.ReplacementAssetUnitId && r.Status == "HANDED_OVER"));
        Assert.Equal(0, await inventory.AssetUnits.CountAsync(a => a.EquipmentTypeId == type && a.Status == "AVAILABLE"));
        await using var db = Db(); var operation = await db.InventoryOperations.SingleAsync(o => o.OperationId == issued.OperationId);
        var intent = JsonSerializer.Deserialize<IncidentIntent>(operation.RequestJson)!;
        await using var provider = Provider(stack, new WorkflowTestClock(DateTimeOffset.UtcNow)); await using var scope = provider.CreateAsyncScope();
        var client = scope.ServiceProvider.GetRequiredService<IInventoryIncidentClient>();
        var original = await client.ReplacementStatus(operation.OperationId, "receipt-replay-test", default);
        Assert.Equal(original, await client.Replace(intent.Replacement!, "receipt-replay-test", default));
        var invalid = await Assert.ThrowsAsync<InventoryFailure>(() => client.Replace(intent.Replacement! with { ActorId = Guid.NewGuid() }, "receipt-hash-test", default));
        Assert.Equal("OPERATION_ID_REUSED", invalid.Message);
        output.WriteLine("PASS: two real Students race for one AVAILABLE unit; exactly one exchange, one 409, one active reservation; immutable operation hash rejects changed actor.");
    }

    [PostgresFact]
    public async Task Inventory_transaction_persists_a_stable_receipt_and_rejects_payload_reuse()
    {
        await using var stack = new Sprint2Stack(postgres); await stack.Start();
        var (type, _) = await stack.Stock(2); var loan = await Borrow(stack, type); var old = loan.Items[0];
        var command = new global::Inventory.Application.ReplaceAssetCommand(Guid.NewGuid(), Guid.NewGuid(), loan.Id, old.Id, old.AssetUnitId!.Value,
            type, loan.Id, Guid.NewGuid(), loan.HandedOverBy!.Value, false, loan.DueAt);
        await using var inventory = InventoryDb(); var repository = new ReplacementRepository(inventory);
        var result = await repository.Replace(command, default);
        Assert.True(result.Found); Assert.NotEqual(old.AssetUnitId, result.ReplacementAssetUnitId);
        Assert.Equal(result, await repository.Replace(command, default));
        Assert.Equal("OPERATION_ID_REUSED", (await Assert.ThrowsAsync<global::Inventory.Application.InventoryException>(() =>
            repository.Replace(command with { ActorId = Guid.NewGuid() }, default))).Code);
    }

    [PostgresFact]
    public async Task Lost_committed_reply_and_failed_borrowing_commit_reconcile_without_duplicate_exchange_or_false_events()
    {
        await using var stack = new Sprint2Stack(postgres); await stack.Start();
        foreach (var failureMode in new[] { "REPLY_LOSS", "DATABASE_FAILURE", "MANUAL_REVIEW" })
        {
            var databaseFailure = failureMode == "DATABASE_FAILURE"; var manual = failureMode == "MANUAL_REVIEW";
            var (type, _) = await stack.Stock(2); var loan = await Borrow(stack, type); var incident = await Inspect(stack, await Report(stack, loan));
            var key = Guid.NewGuid(); var clock = new WorkflowTestClock(DateTimeOffset.UtcNow);
            if (databaseFailure) await InstallFailure(incident.Id);
            try
            {
                await using var provider = Provider(stack, clock, !databaseFailure, manual ? 1 : 8);
                await using var scope = provider.CreateAsyncScope();
                Assert.Equal(503, (await Assert.ThrowsAsync<LoanFailure>(() => scope.ServiceProvider.GetRequiredService<IncidentWorkflow>()
                    .Execute(incident.Id, "INC_REPLACE", key, loan.HandedOverBy!.Value, true, "incident-fault-injection", default))).Status);
            }
            finally { if (databaseFailure) await RemoveFailure(); }
            await using (var inventory = InventoryDb()) Assert.Equal(1, await inventory.ReplacementOperations.CountAsync(o => o.OperationId == key));
            await using (var db = Db())
            {
                Assert.False(await db.OutboxMessages.AnyAsync(e => e.AggregateId == incident.Id && e.EventType == "EquipmentReplaced"));
                Assert.Equal(manual ? IncidentStatus.MANUAL_REVIEW : IncidentStatus.REPLACEMENT_IN_PROGRESS, (await db.EquipmentIncidents.SingleAsync(i => i.Id == incident.Id)).Status);
            }
            // Fresh provider/context represents a restarted worker; query the real Inventory receipt, never create a second intent.
            // A committed receipt remains finalizable even if its unchanged original due date passes during the outage.
            clock.Now = databaseFailure ? loan.DueAt.AddMinutes(1) : clock.Now.AddMinutes(5); await using var restarted = Provider(stack, clock);
            await Sprint2RecoveryTests.Reconcile(restarted); await Sprint2RecoveryTests.Reconcile(restarted);
            if (manual)
            {
                Assert.Equal("MANUAL_REVIEW", (await Body<IncidentDto>(await stack.Student.GetAsync($"api/incidents/{incident.Id}"))).Status);
                Assert.Equal(HttpStatusCode.Forbidden, (await Post(stack.Student, $"api/incidents/{incident.Id}/retry-operation")).StatusCode);
                await Body<IncidentDto>(await Post(stack.Staff, $"api/incidents/{incident.Id}/retry-operation"));
            }
            incident = await Body<IncidentDto>(await stack.Student.GetAsync($"api/incidents/{incident.Id}")); Assert.Equal("RESOLVED_REPLACED", incident.Status);
            await using (var db = Db())
            {
                Assert.Equal(1, await db.OutboxMessages.CountAsync(e => e.AggregateId == incident.Id && e.EventType == "EquipmentReplaced"));
                Assert.Equal("COMPLETED", (await db.InventoryOperations.SingleAsync(o => o.OperationId == key)).State);
                Assert.Equal(2, await db.LoanItems.CountAsync(i => i.LoanRequestId == loan.Id));
            }
            await using (var inventory = InventoryDb()) Assert.Equal(1, await inventory.ReplacementOperations.CountAsync(o => o.OperationId == key));
        }
        output.WriteLine("PASS: real committed gRPC reply dropped and real PostgreSQL finalize failure; durable worker recovery finalizes one exchange and publishes success once.");
    }
    private async Task InstallFailure(Guid incidentId)
    {
        await using var db = Db();
        // Test-generated UUID only; trigger is scoped to this fixture's own incident and is always removed in finally.
#pragma warning disable EF1002
        await db.Database.ExecuteSqlRawAsync($"""
            CREATE FUNCTION incident_commit_fault() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN IF NEW."Id" = '{incidentId:D}'::uuid AND NEW."Status" = 'RESOLVED_REPLACED' THEN
            RAISE EXCEPTION 'test incident finalize failure'; END IF; RETURN NEW; END $$;
            CREATE TRIGGER incident_commit_fault BEFORE UPDATE ON "EquipmentIncidents" FOR EACH ROW EXECUTE FUNCTION incident_commit_fault();
            """);
#pragma warning restore EF1002
    }
    private async Task RemoveFailure()
    {
        await using var db = Db(); await db.Database.ExecuteSqlRawAsync("DROP TRIGGER incident_commit_fault ON \"EquipmentIncidents\"; DROP FUNCTION incident_commit_fault()");
    }
}

internal sealed class DropReplacementReply(IInventoryIncidentClient inner) : IInventoryIncidentClient
{
    public Task<InventorySnapshot> LoanAssets(Guid loan, string correlation, CancellationToken ct) => inner.LoanAssets(loan, correlation, ct);
    public Task<ReplacementSnapshot> ReplacementStatus(Guid operation, string correlation, CancellationToken ct) => inner.ReplacementStatus(operation, correlation, ct);
    public async Task<ReplacementSnapshot> Replace(ReplacementCommand command, string correlation, CancellationToken ct)
    {
        await inner.Replace(command, correlation, ct);
        throw new InventoryFailure(503, "TEST_LOST_COMMITTED_REPLACEMENT_REPLY", true);
    }
}
