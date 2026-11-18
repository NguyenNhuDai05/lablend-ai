using System.Diagnostics;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using Recommendation.Application;
using Recommendation.Infrastructure;
using Testcontainers.PostgreSql;
using Xunit;

namespace Recommendation.Tests;

public sealed class RealDependencyFlowTests
{
    private const string Key = "isolated-recommendation-integration-test-only-signing-credential-64";
    [RecommendationPostgresFact]
    public async Task API_to_real_Catalog_HTTP_Inventory_HTTP2_and_own_PostgreSQL_with_mocked_Gemini_HTTP()
    {
        await using var postgres = new PostgreSqlBuilder().WithImage("postgres:16-alpine").Build(); await postgres.StartAsync();
        await using var admin = new NpgsqlConnection(postgres.GetConnectionString()); await admin.OpenAsync();
        var connections = new Dictionary<string, string>();
        foreach (var name in new[] { "catalog", "inventory", "recommendation" })
        {
            await using var command = new NpgsqlCommand($"CREATE DATABASE {name}_test", admin); await command.ExecuteNonQueryAsync();
            connections[name] = new NpgsqlConnectionStringBuilder(postgres.GetConnectionString()) { Database = name + "_test" }.ConnectionString;
        }
        await using var catalog = new LiveDependency(); await catalog.Start("Catalog", connections["catalog"]);
        await using var inventory = new LiveDependency(); await inventory.Start("Inventory", connections["inventory"]);
        await using var factory = new RealFactory(connections["recommendation"], catalog.Address, inventory.GrpcAddress);
        using var client = factory.CreateClient();
        var token = new JwtSecurityToken("lablend-ai", "lablend-web", [new Claim("sub", Data.User.ToString()), new Claim("role", "STUDENT")],
            expires: DateTime.UtcNow.AddMinutes(5), signingCredentials: new(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Key)), SecurityAlgorithms.HmacSha256));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", new JwtSecurityTokenHandler().WriteToken(token));
        var response = await client.PostAsJsonAsync("/api/recommendations", Data.Request);
        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        var result = await response.Content.ReadFromJsonAsync<RecommendationResponse>(); Assert.NotNull(result);
        using var http = new HttpClient();
        var realType = await http.GetFromJsonAsync<CatalogEquipment>(catalog.Address + "api/equipment-types/" + RealFactory.TypeId);
        var realStock = await http.GetFromJsonAsync<Availability[]>(inventory.Address + "api/inventory/availability?equipmentTypeIds=" + RealFactory.TypeId);
        var item = Assert.Single(result.Recommendations); Assert.Equal(realType!.Name, item.EquipmentName);
        Assert.Equal(realStock![0].Available, item.Available); Assert.Equal(realStock[0].Total, item.Total);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/recommendations/{result.Id}")).StatusCode);
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<RecommendationDbContext>();
        Assert.Equal(1, await db.Sessions.CountAsync()); Assert.Equal(1, await db.Items.CountAsync());
        await using var inventoryDb = new NpgsqlConnection(connections["inventory"]); await inventoryDb.OpenAsync();
        await using var reservations = new NpgsqlCommand("SELECT count(*) FROM \"Reservations\"", inventoryDb);
        Assert.Equal(0L, await reservations.ExecuteScalarAsync());
        await inventory.DisposeAsync();
        var unavailable = await client.GetAsync($"/api/recommendations/{result.Id}");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, unavailable.StatusCode); Assert.Contains("INVENTORY_UNAVAILABLE", await unavailable.Content.ReadAsStringAsync());
    }

    private sealed class RealFactory(string connection, string catalog, string inventory) : WebApplicationFactory<RecommendationProgram>
    {
        public const string TypeId = "e0000000-0000-0000-0000-000000000001";
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing").UseSetting("Jwt:SigningKey", Key).UseSetting("ConnectionStrings:Database", connection)
                .UseSetting("Database:AutoMigrate", "true").UseSetting("Catalog:BaseAddress", catalog)
                .UseSetting("Inventory:GrpcAddress", inventory).UseSetting("Inventory:ServiceToken", Key);
            builder.ConfigureServices(services => {
                services.RemoveAll<IGeminiClient>();
                services.AddSingleton<IGeminiClient>(new GeminiClient(new HttpClient(new Handler((_, _) => Task.FromResult(
                    Handler.Raw(Data.ProviderJson(new { recommendations = new[] { Data.Item(Guid.Parse(TypeId)) } }))))),
                    Options.Create(new GeminiOptions { Enabled = true, ApiKey = "mock-http-only-never-live", Model = "test-gemini" })));
            });
        }
    }
    private sealed class LiveDependency : IAsyncDisposable
    {
        private Process? process; private Task<string>? stdout, stderr;
        public string Address { get; private set; } = ""; public string GrpcAddress { get; private set; } = "";
        private static int Port() { var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start(); var port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop(); return port; }
        public async Task Start(string service, string connection)
        {
            var root = new DirectoryInfo(AppContext.BaseDirectory); while (root != null && !File.Exists(Path.Combine(root.FullName, "LabLendAI.sln"))) root = root.Parent;
            var host = service + (service == "Inventory" ? ".Host" : ".Api");
            var folder = Path.Combine(root!.FullName, "src", "Services", service, host);
            var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
            Address = $"http://127.0.0.1:{Port()}/"; GrpcAddress = $"http://127.0.0.1:{Port()}";
            var start = new ProcessStartInfo("dotnet") { WorkingDirectory = folder, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
            start.ArgumentList.Add(Path.Combine(folder, "bin", configuration, "net8.0", host + ".dll"));
            start.Environment["ASPNETCORE_URLS"] = Address; start.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";
            start.Environment["ConnectionStrings__Database"] = connection; start.Environment["Jwt__SigningKey"] = Key;
            start.Environment["Inventory__ServiceToken"] = Key; start.Environment["Logging__LogLevel__Default"] = "Warning";
            if (service == "Inventory") { start.Environment["Kestrel__Endpoints__Rest__Url"] = Address; start.Environment["Kestrel__Endpoints__Grpc__Url"] = GrpcAddress; }
            process = Process.Start(start)!; stdout = process.StandardOutput.ReadToEndAsync(); stderr = process.StandardError.ReadToEndAsync();
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(1) };
            for (var i = 0; i < 150; i++)
            {
                if (process.HasExited) throw new InvalidOperationException(service + " test host exited during startup.");
                try { if ((await http.GetAsync(Address + "health/ready")).IsSuccessStatusCode) return; } catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { }
                await Task.Delay(200);
            }
            throw new TimeoutException(service + " test host startup timed out.");
        }
        public async ValueTask DisposeAsync()
        {
            if (process is null) return;
            if (!process.HasExited) process.Kill(entireProcessTree: true); await process.WaitForExitAsync();
            if (stdout != null) await stdout; if (stderr != null) await stderr; process.Dispose(); process = null;
        }
    }
}
