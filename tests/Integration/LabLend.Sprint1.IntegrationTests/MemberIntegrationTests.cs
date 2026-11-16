using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Xunit;
using Xunit.Abstractions;

namespace LabLend.Sprint1.IntegrationTests;

[Collection("postgres")]
public sealed class MemberIntegrationTests(PostgresFixture postgres, ITestOutputHelper output)
{
    [PostgresFact]
    public async Task Real_identity_staff_catalog_inventory_and_borrowing_interoperate_through_gateway()
    {
        // Every upstream is the actual service executable with its own database/user and real migrations.
        // No Catalog mock, EF in-memory provider, dev JWT, or shared environment credentials.
        await using var identity = new LiveApi(); await identity.Start("Identity", postgres.IdentityConnection, postgres);
        await using var catalog = new LiveApi(); await catalog.Start("Catalog", postgres.CatalogConnection, postgres);
        await using var inventory = new LiveApi(); await inventory.Start("Inventory", postgres.InventoryConnection, postgres);
        await using var borrowing = new LiveApi(); await borrowing.Start("Borrowing", postgres.BorrowingConnection, postgres, catalog.Address);
        await using var gateway = new LiveGatewayFactory(postgres, identity.Address, borrowing.Address, catalog.Address, inventory.Address);
        using var admin = Client(); using var staff = Client(); using var student = Client();
        var suffix = Guid.NewGuid().ToString("N");
        var password = "Aa1!" + suffix;
        var email = suffix + "@pr8.test";
        var user = await Body(await staff.PostAsJsonAsync("/api/auth/register", new { email, password, confirmPassword = password, displayName = "PR8 staff" }), HttpStatusCode.Created);
        await Login(admin, "admin@sprint1.test", postgres.AdminPassword);
        await Body(await admin.PutAsJsonAsync($"/api/users/{user["id"]}/role", new { role = "LAB_STAFF" }), HttpStatusCode.OK);
        await Login(staff, email, password);
        await Body(await student.PostAsJsonAsync("/api/auth/register", new { email = "student-" + email, password, confirmPassword = password, displayName = "PR8 student" }), HttpStatusCode.Created);
        await Login(student, "student-" + email, password);
        foreach (var path in new[] { "/api/categories", "/api/equipment-types", "/api/inventory/assets" })
            Assert.Equal(HttpStatusCode.Forbidden, (await student.PostAsJsonAsync(path, new { })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await student.GetAsync("/api/inventory/assets")).StatusCode);

        var category = await Body(await staff.PostAsJsonAsync("/api/categories", new { name = "PR8 " + suffix, description = "real integration" }), HttpStatusCode.Created);
        var categoryId = category["id"]!.GetValue<Guid>();
        await Body(await staff.PutAsJsonAsync($"/api/categories/{categoryId}", new { name = "PR8 updated " + suffix, description = "updated", isActive = true }), HttpStatusCode.OK);
        var type = await Body(await staff.PostAsJsonAsync("/api/equipment-types", new { categoryId, name = "PR8 optics", tags = new[] { "PR8" } }), HttpStatusCode.Created);
        var typeId = type["id"]!.GetValue<Guid>();
        var search = await Body(await staff.GetAsync($"/api/equipment-types?categoryId={categoryId}&keyword=optics&tag=pr8&sortBy=name&direction=desc&page=1&pageSize=1"), HttpStatusCode.OK);
        Assert.Equal(1, search["total"]!.GetValue<int>());
        Assert.Equal(typeId, search["items"]![0]!["id"]!.GetValue<Guid>());

        object Loan(Guid equipmentTypeId) => new { purpose = "PR8 real Catalog integration", dueAt = DateTimeOffset.UtcNow.AddDays(2), items = new[] { new { equipmentTypeId } } };
        var loan = await Body(await student.PostAsJsonAsync("/api/loans", Loan(typeId)), HttpStatusCode.Created);
        await Body(await student.GetAsync($"/api/loans/{loan["id"]}"), HttpStatusCode.OK);
        await Body(await student.PostAsJsonAsync("/api/loans", Loan(Guid.NewGuid())), HttpStatusCode.BadRequest);
        await Body(await staff.PutAsJsonAsync($"/api/equipment-types/{typeId}", new { categoryId, name = "PR8 optics updated", tags = new[] { "pr8" }, isActive = true }), HttpStatusCode.OK);

        var asset = await Body(await staff.PostAsJsonAsync("/api/inventory/assets", new { equipmentTypeId = typeId, serialNumber = "PR8-" + suffix, location = "PR8 lab" }), HttpStatusCode.Created);
        var assetId = asset["id"]!.GetValue<Guid>();
        await Body(await staff.PutAsJsonAsync($"/api/inventory/assets/{assetId}", new { equipmentTypeId = typeId, serialNumber = "PR8-" + suffix, location = "PR8 updated lab" }), HttpStatusCode.OK);
        await Body(await staff.PostAsJsonAsync($"/api/inventory/assets/{assetId}/maintenance", new { reason = "PR8 preventive check" }), HttpStatusCode.Created);
        await Body(await staff.PostAsJsonAsync($"/api/inventory/assets/{assetId}/maintenance/complete", new { notes = "PR8 completed" }), HttpStatusCode.OK);
        Assert.Equal(HttpStatusCode.NoContent, (await staff.DeleteAsync($"/api/inventory/assets/{assetId}")).StatusCode);
        var movements = await Body(await staff.GetAsync($"/api/inventory/assets/{assetId}/movements"), HttpStatusCode.OK);
        Assert.Equal(new[] { "RETIRED", "AVAILABLE", "MAINTENANCE", "AVAILABLE" }, movements.AsArray().Select(x => x!["toStatus"]!.GetValue<string>()));
        Assert.All(movements.AsArray(), movement => Assert.Equal(user["id"]!.GetValue<Guid>(), movement!["changedBy"]!.GetValue<Guid>()));
        Assert.Equal(HttpStatusCode.NoContent, (await staff.DeleteAsync($"/api/equipment-types/{typeId}")).StatusCode);
        await Body(await student.PostAsJsonAsync("/api/loans", Loan(typeId)), HttpStatusCode.BadRequest);
        Assert.Equal(HttpStatusCode.NoContent, (await staff.DeleteAsync($"/api/categories/{categoryId}")).StatusCode);

        HttpClient Client()
        {
            var client = gateway.CreateClient(new() { HandleCookies = false });
            client.DefaultRequestHeaders.Add("Origin", "http://localhost:5173");
            client.DefaultRequestHeaders.Add("X-LabLend-CSRF", "1");
            return client;
        }
        async Task Login(HttpClient client, string address, string secret)
        {
            var response = await client.PostAsJsonAsync("/api/auth/login", new { email = address, password = secret });
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var session = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
            client.DefaultRequestHeaders.Authorization = new("Bearer", session["accessToken"]!.GetValue<string>());
        }
    }

    private async Task<JsonNode> Body(HttpResponseMessage response, HttpStatusCode expected)
    {
        var body = await response.Content.ReadAsStringAsync();
        // Login responses (tokens/cookies) are intentionally never passed here.
        output.WriteLine($"{response.RequestMessage?.Method} {response.RequestMessage?.RequestUri?.AbsolutePath} => {(int)response.StatusCode} {body}");
        Assert.Equal(expected, response.StatusCode);
        return JsonNode.Parse(body)!;
    }
}
