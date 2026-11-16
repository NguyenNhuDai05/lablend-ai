using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using LabLend.Gateway;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace LabLend.Sprint1.IntegrationTests;

public sealed class LiveGatewayFactory(PostgresFixture fixture, string identity, string borrowing) : WebApplicationFactory<GatewayMarker>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder) => builder.ConfigureAppConfiguration((_, config) =>
        config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:SigningKey"] = fixture.SigningKey,
            ["ReverseProxy:Clusters:identity:Destinations:primary:Address"] = identity,
            ["ReverseProxy:Clusters:borrowing:Destinations:primary:Address"] = borrowing,
            ["Logging:LogLevel:Default"] = "Warning"
        }));
}

[Collection("postgres")]
public sealed class GatewayFlowTests(PostgresFixture postgres, ITestOutputHelper output)
{
    [PostgresFact]
    public async Task YARP_register_login_me_admin_denial_and_cross_student_loan_access_with_real_hosts()
    {
        // Explicit Catalog HTTP test double. It is not the Member 2 Catalog service and is never shipped in Compose core.
        var equipment = Guid.NewGuid();
        var builder = WebApplication.CreateBuilder(); builder.WebHost.UseUrls("http://127.0.0.1:0");
        await using var catalog = builder.Build();
        catalog.MapGet("/api/equipment-types/{id:guid}", (Guid id) => id == equipment ? Results.Ok(new { id, isActive = true }) : Results.NotFound());
        await catalog.StartAsync();
        var catalogAddress = catalog.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        await using var identity = new LiveApi(); await identity.Start("Identity", postgres.IdentityConnection, postgres);
        await using var borrowing = new LiveApi(); await borrowing.Start("Borrowing", postgres.BorrowingConnection, postgres, catalogAddress + "/");
        await using var gateway = new LiveGatewayFactory(postgres, identity.Address, borrowing.Address);
        using var a = gateway.CreateClient(new() { HandleCookies = false });
        using var b = gateway.CreateClient(new() { HandleCookies = false });
        foreach (var client in new[] { a, b })
        { client.DefaultRequestHeaders.Add("Origin", "http://localhost:5173"); client.DefaultRequestHeaders.Add("X-LabLend-CSRF", "1"); }
        Assert.Equal(HttpStatusCode.Unauthorized, (await a.GetAsync("/api/auth/me")).StatusCode);
        await Evidence("GET /api/loans/my anonymous", await a.GetAsync("/api/loans/my"), HttpStatusCode.Unauthorized);
        var studentA = await RegisterLogin(a, "Student A");
        var studentB = await RegisterLogin(b, "Student B");
        a.DefaultRequestHeaders.Authorization = new("Bearer", studentA.Token);
        b.DefaultRequestHeaders.Authorization = new("Bearer", studentB.Token);
        await Evidence("GET /api/auth/me", await a.GetAsync("/api/auth/me"), HttpStatusCode.OK);
        await Evidence("GET /api/users STUDENT", await a.GetAsync("/api/users"), HttpStatusCode.Forbidden);
        await Evidence("PUT /api/users/{B} by Student A", await a.PutAsJsonAsync($"/api/users/{studentB.Id}", new { displayName = "Unauthorized change" }), HttpStatusCode.Forbidden);
        var response = await b.PostAsJsonAsync("/api/loans", new { purpose = "Gateway test laboratory measurement", dueAt = DateTimeOffset.UtcNow.AddDays(3), items = new[] { new { equipmentTypeId = equipment } } });
        var body = await Evidence("POST /api/loans Student B (Catalog test double)", response, HttpStatusCode.Created);
        var loanId = body!["id"]!.GetValue<Guid>();
        Assert.Equal($"/api/loans/{loanId}", response.Headers.Location!.ToString());
        await Evidence("GET /api/loans/{B} by Student A", await a.GetAsync($"/api/loans/{loanId}"), HttpStatusCode.NotFound);
        await Evidence("GET /api/loans/{B} by owner", await b.GetAsync($"/api/loans/{loanId}"), HttpStatusCode.OK);
        a.DefaultRequestHeaders.Add("Cookie", studentA.Cookie);
        await Evidence("POST /api/auth/refresh through YARP", await a.PostAsync("/api/auth/refresh", null), HttpStatusCode.OK);
        using var admin = gateway.CreateClient(new() { HandleCookies = false });
        admin.DefaultRequestHeaders.Add("Origin", "http://localhost:5173"); admin.DefaultRequestHeaders.Add("X-LabLend-CSRF", "1");
        var login = await admin.PostAsJsonAsync("/api/auth/login", new { email = "admin@sprint1.test", password = postgres.AdminPassword });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        admin.DefaultRequestHeaders.Authorization = new("Bearer", (await IdentityTests.Json(login)).GetProperty("accessToken").GetString());
        Assert.Equal(HttpStatusCode.OK, (await admin.PutAsJsonAsync($"/api/users/{studentA.Id}/role", new { role = "LAB_STAFF" })).StatusCode);
        var staffLogin = await a.PostAsJsonAsync("/api/auth/login", new { email = studentA.Email, password = studentA.Password });
        Assert.Equal(HttpStatusCode.OK, staffLogin.StatusCode);
        a.DefaultRequestHeaders.Authorization = new("Bearer", (await IdentityTests.Json(staffLogin)).GetProperty("accessToken").GetString());
        await Evidence("GET /api/users LAB_STAFF", await a.GetAsync("/api/users"), HttpStatusCode.Forbidden);
        await Evidence("GET /api/loans LAB_STAFF", await a.GetAsync("/api/loans?pageSize=1"), HttpStatusCode.OK);
        // Real HTTP adapter failure: unavailable Catalog must not persist a loan.
        await catalog.StopAsync();
        await Evidence("POST /api/loans Catalog unavailable", await b.PostAsJsonAsync("/api/loans", new { purpose = "Dependency failure laboratory test", dueAt = DateTimeOffset.UtcNow.AddDays(1), items = new[] { new { equipmentTypeId = equipment } } }), HttpStatusCode.ServiceUnavailable);
    }
    private async Task<(string Token, string Cookie, Guid Id, string Email, string Password)> RegisterLogin(HttpClient client, string name)
    {
        var email = Guid.NewGuid().ToString("N") + "@gateway.test";
        var password = "Aa1!" + Guid.NewGuid().ToString("N");
        var registered = await Evidence("POST /api/auth/register " + name, await client.PostAsJsonAsync("/api/auth/register", new { displayName = name, email, password, confirmPassword = password }), HttpStatusCode.Created);
        var response = await client.PostAsJsonAsync("/api/auth/login", new { email, password });
        var cookie = IdentityTests.Cookie(response);
        var body = await Evidence("POST /api/auth/login " + name, response, HttpStatusCode.OK);
        // Evidence redacts only its clone. Use the real issued token for subsequent calls.
        var token = (await IdentityTests.Json(response)).GetProperty("accessToken").GetString()!;
        return (token, cookie, registered!["id"]!.GetValue<Guid>(), email, password);
    }
    private async Task<JsonNode?> Evidence(string request, HttpResponseMessage response, HttpStatusCode expected)
    {
        var raw = await response.Content.ReadAsStringAsync();
        var body = string.IsNullOrWhiteSpace(raw) ? null : JsonNode.Parse(raw);
        if (body is JsonObject obj && obj.ContainsKey("accessToken")) obj["accessToken"] = "[redacted actual JWT]";
        output.WriteLine("EVIDENCE " + request + " => " + (int)response.StatusCode + " " + body?.ToJsonString());
        Assert.Equal(expected, response.StatusCode);
        return body;
    }
}
