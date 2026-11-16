using System.Net;
using System.Net.Http.Json;
using Borrowing.Domain;
using Borrowing.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace LabLend.Sprint1.IntegrationTests;

[Collection("postgres")]
public sealed class BorrowingTests(PostgresFixture postgres)
{
    private static object Request(Guid id) => new { purpose = "Measurement for laboratory assignment", dueAt = DateTimeOffset.UtcNow.AddDays(7), items = new[] { new { equipmentTypeId = id } } };
    private static async Task<Guid> Create(HttpClient client, Guid type)
    {
        var response = await client.PostAsJsonAsync("/api/loans", Request(type));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var loan = await IdentityTests.Json(response);
        Assert.Equal("PENDING", loan.GetProperty("status").GetString());
        Assert.Equal("SUBMITTED", loan.GetProperty("history")[0].GetProperty("action").GetString());
        Assert.Contains("/api/loans/", response.Headers.Location!.ToString());
        return loan.GetProperty("id").GetGuid();
    }
    private async Task<string> Admin(IdentityFactory factory)
    {
        using var client = factory.Browser();
        var response = await client.PostAsJsonAsync("/api/auth/login", new { email = "admin@sprint1.test", password = postgres.AdminPassword });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await IdentityTests.Json(response)).GetProperty("accessToken").GetString()!;
    }
    [PostgresFact]
    public async Task T03_T05_Real_Student_JWT_create_list_ownership_cancel_and_staff_reject()
    {
        await using var identity = new IdentityFactory(postgres);
        using var identityA = identity.Browser(); using var identityB = identity.Browser();
        var a = await IdentityTests.RegisterLogin(identityA); var b = await IdentityTests.RegisterLogin(identityB);
        await using var factory = new BorrowingFactory(postgres);
        using var clientA = factory.Student(a.Token); using var clientB = factory.Student(b.Token);
        using var anonymous = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/loans/my")).StatusCode);
        clientA.DefaultRequestHeaders.Add("X-Correlation-ID", "borrowing-sprint1");
        var loanA = await Create(clientA, factory.Catalog.EquipmentId);
        Assert.Equal("borrowing-sprint1", factory.Catalog.CorrelationId);
        var loanB = await Create(clientB, factory.Catalog.EquipmentId);
        Assert.Equal(HttpStatusCode.NotFound, (await clientA.GetAsync($"/api/loans/{loanB}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await clientA.PostAsync($"/api/loans/{loanB}/cancel", null)).StatusCode);
        var my = await IdentityTests.Json(await clientA.GetAsync("/api/loans/my?status=PENDING&pageSize=1"));
        Assert.Equal(1, my.GetProperty("total").GetInt32());
        Assert.Equal(a.Id, my.GetProperty("items")[0].GetProperty("studentId").GetGuid());
        Assert.Equal(HttpStatusCode.Forbidden, (await clientA.GetAsync("/api/loans")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await clientA.PostAsJsonAsync($"/api/loans/{loanA}/reject", new { rejectionReason = "Student cannot reject" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await clientA.PostAsJsonAsync("/api/loans", new { purpose = "Valid loan purpose", dueAt = DateTimeOffset.UtcNow.AddDays(1), studentId = b.Id, items = new[] { new { equipmentTypeId = factory.Catalog.EquipmentId } } })).StatusCode);
        var cancellations = await Task.WhenAll(clientA.PostAsync($"/api/loans/{loanA}/cancel", null), clientA.PostAsync($"/api/loans/{loanA}/cancel", null));
        Assert.All(cancellations, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        using var admin = factory.Student(await Admin(identity));
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/loans?status=PENDING")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync($"/api/loans/{loanB}")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync($"/api/loans/{loanB}/reject", new { rejectionReason = " " })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsJsonAsync($"/api/loans/{loanB}/reject", new { rejectionReason = "Equipment requires calibration" })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await clientB.PostAsync($"/api/loans/{loanB}/cancel", null)).StatusCode);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BorrowingDbContext>();
        Assert.Equal(2, await db.LoanHistories.CountAsync(h => h.LoanRequestId == loanA));
        Assert.Equal(1, await db.LoanItems.CountAsync(i => i.LoanRequestId == loanA));
        var rejected = await db.LoanRequests.SingleAsync(l => l.Id == loanB);
        Assert.Equal(LoanStatus.REJECTED, rejected.Status); Assert.NotNull(rejected.ReviewedBy); Assert.NotNull(rejected.ReviewedAt);
        Assert.Equal(2, await db.LoanHistories.CountAsync(h => h.LoanRequestId == loanB));
    }

    [PostgresFact]
    public async Task Invalid_inputs_Catalog_failure_and_real_DB_constraints_do_not_leave_partial_loans()
    {
        await using var identity = new IdentityFactory(postgres); using var auth = identity.Browser();
        var user = await IdentityTests.RegisterLogin(auth);
        await using var factory = new BorrowingFactory(postgres); using var client = factory.Student(user.Token);
        var id = factory.Catalog.EquipmentId;
        foreach (var due in new[] { DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(31) })
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/loans", new { purpose = "Valid loan purpose", dueAt = due, items = new[] { new { equipmentTypeId = id } } })).StatusCode);
        foreach (var items in new object?[] { Array.Empty<object>(), new object?[] { null }, new[] { new { equipmentTypeId = Guid.Empty } }, Enumerable.Repeat(new { equipmentTypeId = id }, 21).ToArray() })
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/loans", new { purpose = "Valid loan purpose", dueAt = DateTimeOffset.UtcNow.AddDays(1), items })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/loans", Request(Guid.NewGuid()))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/loans", new { purpose = "Valid loan purpose", dueAt = DateTimeOffset.UtcNow.AddDays(1), items = new[] { new { equipmentTypeId = id, assetUnitId = Guid.NewGuid() } } })).StatusCode);
        factory.Catalog.Unavailable = true;
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.PostAsJsonAsync("/api/loans", Request(id))).StatusCode);
        factory.Catalog.Unavailable = false; factory.Catalog.Malformed = true;
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.PostAsJsonAsync("/api/loans", Request(id))).StatusCode);
        factory.Catalog.Malformed = false; factory.Catalog.Inactive = true;
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/loans", Request(id))).StatusCode);
        factory.Catalog.Inactive = false; factory.Catalog.Timeout = true;
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.PostAsJsonAsync("/api/loans", Request(id))).StatusCode);
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<BorrowingDbContext>();
        Assert.Equal(0, await db.LoanRequests.CountAsync(l => l.StudentId == user.Id));
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/loans/my?status=OVERDUE")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/loans/my?pageSize=101")).StatusCode);
        // Direct invalid persistence must rollback aggregate, items and history in the same PostgreSQL transaction.
        var invalid = LoanRequest.Create(user.Id, "Valid test purpose", DateTimeOffset.UtcNow.AddDays(1), [id], DateTimeOffset.UtcNow);
        db.LoanRequests.Add(invalid);
        db.Entry(invalid).Property(l => l.Purpose).CurrentValue = "bad";
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear();
        Assert.False(await db.LoanRequests.AnyAsync(l => l.Id == invalid.Id));
        Assert.False(await db.LoanItems.AnyAsync(l => l.LoanRequestId == invalid.Id));
        Assert.False(await db.LoanHistories.AnyAsync(l => l.LoanRequestId == invalid.Id));
    }

    [PostgresFact]
    public async Task Concurrent_reject_and_cancel_commit_only_one_terminal_history()
    {
        await using var identity = new IdentityFactory(postgres); using var auth = identity.Browser();
        var user = await IdentityTests.RegisterLogin(auth);
        await using var factory = new BorrowingFactory(postgres);
        using var student = factory.Student(user.Token); using var staff = factory.Student(await Admin(identity));
        var id = await Create(student, factory.Catalog.EquipmentId);
        var changes = await Task.WhenAll(student.PostAsync($"/api/loans/{id}/cancel", null),
            staff.PostAsJsonAsync($"/api/loans/{id}/reject", new { rejectionReason = "Concurrency test review" }));
        Assert.Single(changes, r => r.StatusCode == HttpStatusCode.OK);
        Assert.Single(changes, r => r.StatusCode == HttpStatusCode.Conflict);
        var another = await Create(student, factory.Catalog.EquipmentId);
        var rejects = await Task.WhenAll(staff.PostAsJsonAsync($"/api/loans/{another}/reject", new { rejectionReason = "First reviewer" }),
            staff.PostAsJsonAsync($"/api/loans/{another}/reject", new { rejectionReason = "Second reviewer" }));
        Assert.Single(rejects, r => r.StatusCode == HttpStatusCode.OK); Assert.Single(rejects, r => r.StatusCode == HttpStatusCode.Conflict);
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<BorrowingDbContext>();
        Assert.Equal(2, await db.LoanHistories.CountAsync(h => h.LoanRequestId == id));
        Assert.Equal(2, await db.LoanHistories.CountAsync(h => h.LoanRequestId == another));
        Assert.DoesNotContain(await db.LoanRequests.Where(l => l.StudentId == user.Id).Select(l => l.Status).ToListAsync(), s => s is LoanStatus.APPROVED or LoanStatus.APPROVING);
    }
}
