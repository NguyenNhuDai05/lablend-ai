using System.Net;
using System.Net.Http.Json;
using Borrowing.Api;
using Borrowing.Application;
using Borrowing.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace LabLend.Sprint1.IntegrationTests;

public sealed class CatalogTestHandler : HttpMessageHandler
{
    public Guid EquipmentId { get; } = Guid.NewGuid();
    public bool Unavailable { get; set; }
    public bool Malformed { get; set; }
    public bool Inactive { get; set; }
    public bool Timeout { get; set; }
    public string? CorrelationId { get; private set; }
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        CorrelationId = request.Headers.GetValues("X-Correlation-ID").Single();
        if (Timeout) throw new TaskCanceledException();
        return Task.FromResult(new HttpResponseMessage(Unavailable ? HttpStatusCode.ServiceUnavailable :
            request.RequestUri!.AbsolutePath.EndsWith(EquipmentId.ToString()) ? HttpStatusCode.OK : HttpStatusCode.NotFound)
        { Content = Malformed ? new StringContent("invalid json") : JsonContent.Create(new { id = EquipmentId, isActive = !Inactive }) });
    }
}

public sealed class BorrowingFactory(PostgresFixture fixture) : WebApplicationFactory<BorrowingMarker>
{
    public CatalogTestHandler Catalog { get; } = new();
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:Database", fixture.BorrowingConnection);
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Database"] = fixture.BorrowingConnection, ["Jwt:SigningKey"] = fixture.SigningKey,
            ["Database:ApplyMigrations"] = "true", ["Logging:LogLevel:Default"] = "Warning"
        }));
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<ICatalogClient>();
            services.AddScoped<ICatalogClient>(_ => new CatalogClient(new HttpClient(Catalog, false) { BaseAddress = new Uri("http://catalog.test/") }));
        });
    }
    public HttpClient Student(string token)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", token);
        return client;
    }
}
