using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Borrowing.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;
using Xunit.Abstractions;

namespace LabLend.Sprint1.IntegrationTests;

[Collection("postgres")]
public sealed class Sprint2LifecycleTests(PostgresFixture postgres, ITestOutputHelper output)
{
    [PostgresFact]
    public async Task GateA_real_JWT_REST_to_HTTP2_grpc_full_lifecycle_atomicity_and_competition()
    {
        await using var stack = new Sprint2Stack(postgres);
        await stack.Start();
        var (type, assets) = await stack.Stock(3);
        var loan = await stack.Submit(type, type, type);
        var id = loan["id"]!.GetValue<Guid>();
        Assert.Equal(HttpStatusCode.Forbidden, (await stack.Student.PostAsync($"api/loans/{id}/approve", null)).StatusCode);
        var approvals = await Task.WhenAll(stack.Staff.PostAsync($"api/loans/{id}/approve", null), stack.Staff.PostAsync($"api/loans/{id}/approve", null));
        Assert.Single(approvals, r => r.StatusCode == HttpStatusCode.OK);
        Assert.Single(approvals, r => r.StatusCode == HttpStatusCode.Conflict);
        loan = await stack.Loan(id);
        Assert.Equal("APPROVED", (string?)loan["status"]);
        Assert.All(loan["items"]!.AsArray(), i => { Assert.Equal("RESERVED", (string?)i!["itemStatus"]); Assert.Null(i["reservationId"]); });
        Assert.Equal(3, loan["items"]!.AsArray().Select(i => (string?)i!["assetUnitId"]).Distinct().Count());
        foreach (var asset in assets) Assert.Equal("RESERVED", (string?)(await stack.Asset(asset))["status"]);
        await stack.Command(id, "handover"); await stack.Command(id, "handover");
        loan = await stack.Loan(id);
        Assert.Equal("BORROWED", (string?)loan["status"]);
        Assert.Single(loan["history"]!.AsArray(), h => (string?)h!["action"] == "HANDED_OVER");
        var items = loan["items"]!.AsArray();
        var damaged = items[0]!["id"]!.GetValue<Guid>(); var damagedAsset = items[0]!["assetUnitId"]!.GetValue<Guid>();
        await stack.Return(id, (damaged, "DAMAGED"));
        Assert.Equal("BORROWED", (string?)(await stack.Loan(id))["status"]);
        Assert.Equal("MAINTENANCE", (string?)(await stack.Asset(damagedAsset))["status"]);
        var maintenance = await Sprint2Stack.Body(await stack.InventoryStaff.GetAsync($"api/inventory/assets/{damagedAsset}/maintenance"));
        Assert.Single(maintenance.AsArray());
        await stack.Return(id, (damaged, "DAMAGED"));
        Assert.Equal(HttpStatusCode.Conflict, (await stack.Staff.PostAsJsonAsync($"api/loans/{id}/returns", new { items = new[] { new { loanItemId = damaged, condition = "NORMAL" } } })).StatusCode);
        await stack.Return(id, (items[1]!["id"]!.GetValue<Guid>(), "NORMAL"), (items[2]!["id"]!.GetValue<Guid>(), "LOST"));
        Assert.Equal("RETURNED", (string?)(await stack.Loan(id))["status"]);
        Assert.Equal("AVAILABLE", (string?)(await stack.Asset(items[1]!["assetUnitId"]!.GetValue<Guid>()))["status"]);
        Assert.Equal("LOST", (string?)(await stack.Asset(items[2]!["assetUnitId"]!.GetValue<Guid>()))["status"]);

        var (lastType, lastAssets) = await stack.Stock(1);
        var first = (await stack.Submit(lastType))["id"]!.GetValue<Guid>();
        var second = (await stack.Submit(lastType))["id"]!.GetValue<Guid>();
        var contenders = await Task.WhenAll(stack.Staff.PostAsync($"api/loans/{first}/approve", null), stack.Staff.PostAsync($"api/loans/{second}/approve", null));
        Assert.Single(contenders, r => r.StatusCode == HttpStatusCode.OK);
        Assert.Single(contenders, r => r.StatusCode == HttpStatusCode.Conflict);
        var (availableType, availableAssets) = await stack.Stock(1);
        var (emptyType, _) = await stack.Stock(0);
        var shortage = (await stack.Submit(availableType, emptyType))["id"]!.GetValue<Guid>();
        Assert.Equal(HttpStatusCode.Conflict, (await stack.Staff.PostAsync($"api/loans/{shortage}/approve", null)).StatusCode);
        Assert.Equal("APPROVAL_FAILED", (string?)(await stack.Loan(shortage))["status"]);
        Assert.Equal("AVAILABLE", (string?)(await stack.Asset(availableAssets[0]))["status"]);
        var options = new DbContextOptionsBuilder<BorrowingDbContext>().UseNpgsql(postgres.BorrowingConnection).Options;
        await using var db = new BorrowingDbContext(options);
        Assert.Equal(0, await db.InventoryOperations.CountAsync(o => o.LoanId == id && o.State != "COMPLETED"));
        Assert.Equal(3, await db.LoanItems.CountAsync(i => i.LoanRequestId == id && i.ReturnCondition != null && i.ReturnedAt != null));
        output.WriteLine("PASS T06/T07/T08/T10/T11: real Identity JWT, Catalog REST, Borrowing REST, Inventory HTTP/2 gRPC and isolated PostgreSQL migrations. No business mocks.");
    }
}

internal sealed class Sprint2Stack(PostgresFixture postgres) : IAsyncDisposable
{
    private readonly LiveApi identity = new(), catalog = new(), inventory = new(), borrowing = new();
    public HttpClient Staff { get; private set; } = null!;
    public HttpClient Student { get; private set; } = null!;
    public HttpClient InventoryStaff { get; private set; } = null!;
    public HttpClient CatalogStaff { get; private set; } = null!;
    public string InventoryGrpc => inventory.GrpcAddress;
    public string CatalogAddress => catalog.Address;
    public string IdentityAddress => identity.Address;
    public string InventoryAddress => inventory.Address;
    public string BorrowingAddress => borrowing.Address;
    public string StudentEmail { get; private set; } = "";
    public string StaffEmail { get; private set; } = "";
    public string TestPassword { get; private set; } = "";
    private string staffToken = "";
    public Guid StudentId { get; private set; }
    public string StudentToken { get; private set; } = "";
    public async Task Start(IReadOnlyDictionary<string, string>? settings = null, string? webOrigin = null)
    {
        await identity.Start("Identity", postgres.IdentityConnection, postgres, settings: webOrigin == null ? null : new Dictionary<string, string> { ["SessionCookie__AllowedOrigins__1"] = webOrigin });
        await catalog.Start("Catalog", postgres.CatalogConnection, postgres);
        await inventory.Start("Inventory", postgres.InventoryConnection, postgres);
        var config = new Dictionary<string, string> { ["Inventory__GrpcAddress"] = inventory.GrpcAddress, ["Inventory__ServiceToken"] = postgres.SigningKey };
        if (settings != null) foreach (var (key, value) in settings) config[key] = value;
        await borrowing.Start("Borrowing", postgres.BorrowingConnection, postgres, catalog.Address, config);
        using var auth = new HttpClient { BaseAddress = new Uri(identity.Address) };
        auth.DefaultRequestHeaders.Add("X-LabLend-CSRF", "1");
        auth.DefaultRequestHeaders.Add("Origin", "http://localhost:5173");
        var admin = await Body(await auth.PostAsJsonAsync("api/auth/login", new { email = "admin@sprint1.test", password = postgres.AdminPassword }));
        auth.DefaultRequestHeaders.Authorization = new("Bearer", (string?)admin["accessToken"]);
        var suffix = Guid.NewGuid().ToString("N"); var password = "Aa1!" + suffix;
        var email = suffix + "@sprint2.test";
        StaffEmail = email; TestPassword = password;
        var user = await Body(await auth.PostAsJsonAsync("api/auth/register", new { email, password, confirmPassword = password, displayName = "Sprint 2 Staff" }));
        await Body(await auth.PutAsJsonAsync($"api/users/{user["id"]}/role", new { role = "LAB_STAFF" }));
        var staff = await Body(await auth.PostAsJsonAsync("api/auth/login", new { email, password }));
        var token = (string)staff["accessToken"]!;
        staffToken = token;
        Staff = Client(borrowing.Address, token); InventoryStaff = Client(inventory.Address, token); CatalogStaff = Client(catalog.Address, token);
        var studentEmail = "student-" + email;
        StudentEmail = studentEmail;
        var student = await Body(await auth.PostAsJsonAsync("api/auth/register", new { email = studentEmail, password, confirmPassword = password, displayName = "Sprint 2 Student" }));
        StudentId = student["id"]!.GetValue<Guid>();
        var login = await Body(await auth.PostAsJsonAsync("api/auth/login", new { email = studentEmail, password }));
        StudentToken = (string)login["accessToken"]!; Student = Client(borrowing.Address, StudentToken);
        Staff.DefaultRequestHeaders.Add("X-Correlation-ID", "sprint2-real-lifecycle");
    }
    public Task CrashBorrowing() => borrowing.DisposeAsync().AsTask();
    public async Task RestartBorrowing()
    {
        await borrowing.Start("Borrowing", postgres.BorrowingConnection, postgres, catalog.Address,
            new Dictionary<string, string> { ["Inventory__GrpcAddress"] = InventoryGrpc, ["Inventory__ServiceToken"] = postgres.SigningKey });
        Staff.Dispose(); Student.Dispose();
        Staff = Client(borrowing.Address, staffToken); Student = Client(borrowing.Address, StudentToken);
    }
    public async Task<(Guid Type, Guid[] Assets)> Stock(int count)
    {
        var category = await Body(await CatalogStaff.PostAsJsonAsync("api/categories", new { name = "S2-" + Guid.NewGuid().ToString("N"), description = "isolated test" }));
        var type = await Body(await CatalogStaff.PostAsJsonAsync("api/equipment-types", new { categoryId = category["id"]!.GetValue<Guid>(), name = "S2-" + Guid.NewGuid().ToString("N"), description = "integration", specifications = "test", tags = Array.Empty<string>(), imageUrl = (string?)null }));
        var id = type["id"]!.GetValue<Guid>(); var assets = new List<Guid>();
        for (var i = 0; i < count; i++)
        {
            var asset = await Body(await InventoryStaff.PostAsJsonAsync("api/inventory/assets", new { equipmentTypeId = id, serialNumber = "S2-" + Guid.NewGuid().ToString("N"), location = "test", notes = (string?)null }));
            assets.Add(asset["id"]!.GetValue<Guid>());
        }
        return (id, assets.ToArray());
    }
    public Task<JsonNode> Submit(params Guid[] types) => SubmitAt(DateTimeOffset.UtcNow.AddDays(2), types);
    public async Task<JsonNode> SubmitAt(DateTimeOffset due, params Guid[] types) => await Body(await Student.PostAsJsonAsync("api/loans", new { purpose = "Sprint 2 real integration test", dueAt = due, items = types.Select(t => new { equipmentTypeId = t }) }));
    public async Task<JsonNode> Loan(Guid id) => await Body(await Staff.GetAsync($"api/loans/{id}"));
    public async Task<JsonNode> Asset(Guid id) => await Body(await InventoryStaff.GetAsync($"api/inventory/assets/{id}"));
    public async Task Command(Guid id, string action) => await Body(await Staff.PostAsync($"api/loans/{id}/{action}", null));
    public async Task Return(Guid id, params (Guid Id, string Condition)[] items) => await Body(await Staff.PostAsJsonAsync($"api/loans/{id}/returns", new { items = items.Select(i => new { loanItemId = i.Id, condition = i.Condition }) }));
    public static HttpClient Client(string address, string token)
    {
        var client = new HttpClient { BaseAddress = new Uri(address), Timeout = TimeSpan.FromSeconds(30) };
        client.DefaultRequestHeaders.Authorization = new("Bearer", token); return client;
    }
    public static async Task<JsonNode> Body(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"{response.RequestMessage?.RequestUri}: {(int)response.StatusCode} {text}");
        return JsonNode.Parse(text)!;
    }
    public async ValueTask DisposeAsync()
    {
        Staff?.Dispose(); Student?.Dispose(); InventoryStaff?.Dispose(); CatalogStaff?.Dispose();
        await borrowing.DisposeAsync(); await inventory.DisposeAsync(); await catalog.DisposeAsync(); await identity.DisposeAsync();
    }
}
