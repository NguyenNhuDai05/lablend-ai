using System.Net;
using System.Net.Sockets;
using Inventory.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Inventory.IntegrationTests;

/// <summary>
/// The same Inventory app on real Kestrel sockets (HTTP/1 REST endpoint + HTTP/2-only gRPC endpoint, as in Docker),
/// so gRPC tests cross a real HTTP/2 connection like Borrowing will, instead of the in-memory TestServer.
/// </summary>
public sealed class KestrelInventoryFactory : InventoryApiFactory
{
    private readonly int grpcPort = FreePort(), restPort = FreePort();
    private IHost? kestrel;

    /// <summary>Starts the hosts on first use (WebApplicationFactory is lazy) and returns the HTTP/2 endpoint.</summary>
    public Uri GrpcAddress
    {
        get
        {
            _ = Services;
            return new($"http://127.0.0.1:{grpcPort}");
        }
    }

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        // Both hosts migrate on startup; on an empty database they would race creating the same tables.
        // Migrating once here turns their startup Migrate() into a no-op.
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:Database"] = ConnectionString }).Build();
        await using var services = new ServiceCollection().AddInventoryInfrastructure(configuration).BuildServiceProvider();
        services.MigrateInventoryDatabase();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        // Same endpoint names/protocols as appsettings.json, but on free loopback ports instead of 8080/8081.
        builder.UseSetting("Kestrel:Endpoints:Rest:Url", $"http://127.0.0.1:{restPort}")
            .UseSetting("Kestrel:Endpoints:Grpc:Url", $"http://127.0.0.1:{grpcPort}");
    }

    // ponytail: .NET 8 WebApplicationFactory can only host TestServer, so the app is built a second time on Kestrel
    // (factory.UseKestrel() exists from .NET 10). The TestServer host still serves CreateClient() for REST setup.
    protected override IHost CreateHost(IHostBuilder builder)
    {
        var testHost = builder.Build();
        builder.ConfigureWebHost(web => web.UseKestrel());
        kestrel = builder.Build();
        kestrel.Start();
        testHost.Start();
        return testHost;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) kestrel?.Dispose();
        base.Dispose(disposing);
    }

    private static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try { return ((IPEndPoint)listener.LocalEndpoint).Port; } finally { listener.Stop(); }
    }
}

/// <summary>Records the HTTP version of every response the gRPC client receives.</summary>
public sealed class HttpVersionRecorder() : DelegatingHandler(new SocketsHttpHandler())
{
    private readonly List<Version> versions = [];
    public IReadOnlyList<Version> Versions { get { lock (versions) return versions.ToList(); } }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = await base.SendAsync(request, cancellationToken);
        lock (versions) versions.Add(response.Version);
        return response;
    }
}
