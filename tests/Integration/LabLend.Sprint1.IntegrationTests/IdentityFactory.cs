using Identity.Api;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace LabLend.Sprint1.IntegrationTests;

public sealed class IdentityFactory(PostgresFixture fixture) : WebApplicationFactory<IdentityMarker>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:Database", fixture.IdentityConnection);
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Database"] = fixture.IdentityConnection,
            ["Jwt:SigningKey"] = fixture.SigningKey,
            ["Database:ApplyMigrations"] = "true",
            ["SessionCookie:Secure"] = "false",
            ["SessionCookie:AllowedOrigins:0"] = "http://localhost:5173",
            ["Bootstrap:Enabled"] = "true",
            ["Bootstrap:AdminEmail"] = "admin@sprint1.test",
            ["Bootstrap:AdminPassword"] = fixture.AdminPassword,
            ["Logging:LogLevel:Default"] = "Warning"
        }));
    }
    public HttpClient Browser()
    {
        var client = CreateClient(new() { HandleCookies = false });
        client.DefaultRequestHeaders.Add("Origin", "http://localhost:5173");
        client.DefaultRequestHeaders.Add("X-LabLend-CSRF", "1");
        return client;
    }
}
