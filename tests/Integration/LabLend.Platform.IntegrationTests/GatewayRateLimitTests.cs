using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using Yarp.ReverseProxy.Forwarder;
using Xunit;

namespace LabLend.Platform.IntegrationTests;

public sealed class GatewayRateLimitTests
{
    [Fact]
    public async Task Exhausting_one_authenticated_user_does_not_block_another_user_on_the_same_proxy_IP()
    {
        using var root = new GatewayFactory();
        using var factory = root.WithWebHostBuilder(builder => builder.ConfigureLogging(logging => logging.ClearProviders())
            .ConfigureTestServices(services => services.AddSingleton<IForwarderHttpClientFactory>(new ProbeHttpClientFactory()))
            .ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?> {
                // The transport probe is not a business mock: authentication, YARP routes and rate limiting all run in the real gateway.
                ["ReverseProxy:Clusters:borrowing:Destinations:primary:Address"] = "http://127.0.0.1:1/" })));
        using var first = factory.CreateClient(); using var second = factory.CreateClient();
        first.DefaultRequestHeaders.Authorization = Token(root.SigningKey, Guid.NewGuid());
        second.DefaultRequestHeaders.Authorization = Token(root.SigningKey, Guid.NewGuid());
        for (var n = 0; n < 120; n++)
        {
            using var allowed = await first.GetAsync("/api/loans/my");
            Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        }
        using var rejected = await first.GetAsync("/api/loans/my"); Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
        using var independent = await second.GetAsync("/api/loans/my"); Assert.Equal(HttpStatusCode.OK, independent.StatusCode);
    }
    private static AuthenticationHeaderValue Token(string key, Guid user)
    {
        var token = new JwtSecurityToken("lablend-ai", "lablend-web", [new Claim("sub", user.ToString()), new Claim("role", "STUDENT")],
            DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow.AddMinutes(10), new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)), SecurityAlgorithms.HmacSha256));
        return new("Bearer", new JwtSecurityTokenHandler().WriteToken(token));
    }
    private sealed class ProbeHttpClientFactory : IForwarderHttpClientFactory
    {
        public HttpMessageInvoker CreateClient(ForwarderHttpClientContext context) => new(new ProbeHandler());
    }
    private sealed class ProbeHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}"), RequestMessage = request });
    }
}
