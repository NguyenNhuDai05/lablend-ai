using System.Net;
using System.Net.Http.Json;
using Borrowing.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace LabLend.Sprint1.IntegrationTests;

[Collection("postgres")]
public sealed class FinalWorkflowTests(PostgresFixture postgres)
{
    [PostgresFact]
    public async Task Notification_loan_filter_is_applied_with_owner_scope_before_pagination()
    {
        await using var identity = new IdentityFactory(postgres); using var auth = identity.Browser();
        var user = await IdentityTests.RegisterLogin(auth); var other = await IdentityTests.RegisterLogin(auth);
        await using var notification = new LiveApi();
        await notification.Start("Notification", postgres.NotificationConnection, postgres,
            settings: new Dictionary<string, string> { ["ConnectionStrings__Redis"] = postgres.RedisConnection });
        await using var db = new Notification.Infrastructure.NotificationDbContext(new DbContextOptionsBuilder<Notification.Infrastructure.NotificationDbContext>()
            .UseNpgsql(postgres.NotificationConnection).Options);
        var loan = Guid.NewGuid();
        foreach (var (owner, aggregate) in new[] { (user.Id, loan), (user.Id, Guid.NewGuid()), (other.Id, loan) })
            db.Notifications.Add(Notification.Domain.NotificationItem.Create(owner, Guid.NewGuid(), "LoanApproved", aggregate.ToString(), "Approved", "Repository integration fixture", DateTimeOffset.UtcNow));
        await db.SaveChangesAsync();
        using var client = new HttpClient { BaseAddress = new Uri(notification.Address) };
        client.DefaultRequestHeaders.Authorization = new("Bearer", user.Token);
        var response = await client.GetAsync($"api/notifications?aggregateId={loan}&pageSize=1");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var page = await IdentityTests.Json(response);
        Assert.Equal(1, page.GetProperty("total").GetInt64());
        Assert.Equal(loan.ToString(), page.GetProperty("items")[0].GetProperty("aggregateId").GetString());
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("api/notifications?aggregateId=invalid")).StatusCode);
    }

    [PostgresFact]
    public async Task Create_idempotency_serializes_concurrent_submissions_and_replays_without_catalog_or_duplicate_events()
    {
        await using var identity = new IdentityFactory(postgres); using var auth = identity.Browser();
        var user = await IdentityTests.RegisterLogin(auth);
        await using var factory = new BorrowingFactory(postgres); using var client = factory.Student(user.Token);
        var key = Guid.NewGuid().ToString(); client.DefaultRequestHeaders.Add("Idempotency-Key", key);
        var due = DateTimeOffset.UtcNow.AddDays(1);
        var body = new { purpose = "Final concurrent submission measurement", dueAt = due, items = new[] { new { equipmentTypeId = factory.Catalog.EquipmentId } } };
        var responses = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => client.PostAsJsonAsync("/api/loans", body)));
        Assert.All(responses, r => Assert.Equal(HttpStatusCode.Created, r.StatusCode));
        var ids = new List<Guid>();
        foreach (var response in responses) ids.Add((await IdentityTests.Json(response)).GetProperty("id").GetGuid());
        var loanId = Assert.Single(ids.Distinct());
        factory.Catalog.Unavailable = true;
        var replay = await client.PostAsJsonAsync("/api/loans", body);
        Assert.Equal(HttpStatusCode.Created, replay.StatusCode);
        Assert.Equal(loanId, (await IdentityTests.Json(replay)).GetProperty("id").GetGuid());
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/loans", new { purpose = body.purpose + " changed", body.dueAt, body.items })).StatusCode);
        factory.Catalog.Unavailable = false;
        // The same key on another authenticated student cannot disclose/reuse the original student's loan.
        var other = await IdentityTests.RegisterLogin(auth); using var second = factory.Student(other.Token);
        second.DefaultRequestHeaders.Add("Idempotency-Key", key);
        var scoped = await second.PostAsJsonAsync("/api/loans", body);
        Assert.Equal(HttpStatusCode.Created, scoped.StatusCode);
        Assert.NotEqual(loanId, (await IdentityTests.Json(scoped)).GetProperty("id").GetGuid());
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<BorrowingDbContext>();
        Assert.Equal(1, await db.LoanRequests.CountAsync(l => l.StudentId == user.Id));
        Assert.Equal(1, await db.LoanSubmissions.CountAsync(s => s.StudentId == user.Id));
        Assert.Equal(1, await db.LoanHistories.CountAsync(h => h.LoanRequestId == loanId));
        Assert.Equal(1, await db.OutboxMessages.CountAsync(o => o.AggregateId == loanId));
        client.DefaultRequestHeaders.Remove("Idempotency-Key"); client.DefaultRequestHeaders.Add("Idempotency-Key", "not-a-uuid");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/loans", body)).StatusCode);
    }

    [PostgresFact]
    public async Task Admin_search_filters_all_accounts_before_pagination_and_escapes_wildcards()
    {
        await using var factory = new IdentityFactory(postgres); using var client = factory.Browser();
        var unique = Guid.NewGuid().ToString("N");
        var email = $"find-{unique}@final.test"; var password = "Aa1!" + unique;
        var target = await client.PostAsJsonAsync("/api/auth/register", new { displayName = "Search_100% " + unique, email, password, confirmPassword = password });
        Assert.Equal(HttpStatusCode.Created, target.StatusCode);
        var targetId = (await IdentityTests.Json(target)).GetProperty("id").GetGuid();
        await IdentityTests.RegisterLogin(client); // newer account pushes target off the first page of size one
        var admin = await client.PostAsJsonAsync("/api/auth/login", new { email = "admin@sprint1.test", password = postgres.AdminPassword });
        Assert.Equal(HttpStatusCode.OK, admin.StatusCode);
        client.DefaultRequestHeaders.Authorization = new("Bearer", (await IdentityTests.Json(admin)).GetProperty("accessToken").GetString());
        var first = await IdentityTests.Json(await client.GetAsync("/api/users?pageSize=1"));
        Assert.DoesNotContain(first.GetProperty("items").EnumerateArray(), u => u.GetProperty("id").GetGuid() == targetId);
        var found = await IdentityTests.Json(await client.GetAsync($"/api/users?keyword={email.ToUpperInvariant()}&role=STUDENT&isActive=true&pageSize=1"));
        Assert.Equal(1, found.GetProperty("total").GetInt64());
        Assert.Equal(targetId, found.GetProperty("items")[0].GetProperty("id").GetGuid());
        var literal = await IdentityTests.Json(await client.GetAsync("/api/users?keyword=" + Uri.EscapeDataString("Search_100% " + unique)));
        Assert.Equal(1, literal.GetProperty("total").GetInt64());
        Assert.Equal(HttpStatusCode.OK, (await client.PatchAsJsonAsync($"/api/users/{targetId}/active", new { isActive = false })).StatusCode);
        var disabled = await IdentityTests.Json(await client.GetAsync($"/api/users?keyword={unique}&isActive=false"));
        Assert.Equal(1, disabled.GetProperty("total").GetInt64());
        var active = await IdentityTests.Json(await client.GetAsync($"/api/users?keyword={unique}&isActive=true"));
        Assert.Equal(0, active.GetProperty("total").GetInt64());
        foreach (var query in new[] { "role=OWNER", "keyword=" + new string('x', 101), "isActive=maybe", "page=0", "pageSize=101" })
            Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/users?" + query)).StatusCode);
    }
}
