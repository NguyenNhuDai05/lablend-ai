using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using Grpc.Core;
using Grpc.Net.Client;
using Inventory.Application;
using Inventory.Host.Controllers;
using LabLend.Inventory.Contracts.V1;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace Inventory.IntegrationTests;

// Real PostgreSQL (Testcontainers, needs Docker) + real migrations; REST and gRPC through the in-memory TestServer.
public class InventoryApiFactory : WebApplicationFactory<AssetsController>, IAsyncLifetime
{
    private readonly PostgreSqlContainer postgres = new PostgreSqlBuilder().WithImage("postgres:16-alpine").Build();
    private readonly string signingKey = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    public string ServiceToken { get; } = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));

    protected override void ConfigureWebHost(IWebHostBuilder builder) => builder
        .UseSetting("Jwt:SigningKey", signingKey)
        .UseSetting("Inventory:ServiceToken", ServiceToken)
        .UseSetting("ConnectionStrings:Database", postgres.GetConnectionString());

    protected string ConnectionString => postgres.GetConnectionString();

    /// <summary>Direct connection to the service database, for tests that hold locks or inspect pg_locks.</summary>
    public async Task<NpgsqlConnection> OpenDatabaseAsync()
    {
        var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        return connection;
    }

    public HttpClient ClientAs(string? role)
    {
        var client = CreateClient();
        if (role is null) return client;
        var token = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = "lablend-ai", Audience = "lablend-web", Expires = DateTime.UtcNow.AddMinutes(10),
            Claims = new Dictionary<string, object> { ["sub"] = Guid.NewGuid().ToString(), ["role"] = role },
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)), SecurityAlgorithms.HmacSha256)
        });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    public InventoryAllocation.InventoryAllocationClient Grpc() =>
        new(GrpcChannel.ForAddress(Server.BaseAddress, new GrpcChannelOptions { HttpHandler = Server.CreateHandler() }));

    public Metadata ServiceAuth => new() { { "authorization", $"Bearer {ServiceToken}" } };

    /// <summary>Creates N units of a fresh equipment type so tests never share stock.</summary>
    public async Task<(Guid TypeId, List<AssetResponse> Units)> NewStockAsync(int units)
    {
        var staff = ClientAs("LAB_STAFF");
        var typeId = Guid.NewGuid();
        var created = new List<AssetResponse>();
        for (var i = 0; i < units; i++)
        {
            var response = await staff.PostAsJsonAsync("/api/inventory/assets",
                new CreateAssetRequest(typeId, $"T-{Guid.NewGuid():N}"[..20], "Lab test", null));
            response.EnsureSuccessStatusCode();
            created.Add((await response.Content.ReadFromJsonAsync<AssetResponse>())!);
        }
        return (typeId, created);
    }

    public virtual Task InitializeAsync() => postgres.StartAsync();
    public new Task DisposeAsync() => postgres.DisposeAsync().AsTask();
}
