using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;
using Recommendation.Application;
using Xunit;

namespace Recommendation.Tests;

public sealed class RecommendationFactory : WebApplicationFactory<RecommendationProgram>
{
    private const string Key = "recommendation-test-signing-key-only-64-characters-long-never-real-key";
    internal MemoryRepository Repository { get; } = new();
    internal FakeGemini Gemini { get; } = new();
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("Jwt:SigningKey", Key);
        builder.UseSetting("ConnectionStrings:Database", "Host=localhost;Database=unused;Username=unused");
        builder.UseSetting("Database:AutoMigrate", "false");
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?> {
            ["Jwt:SigningKey"] = Key, ["ConnectionStrings:Database"] = "Host=localhost;Database=unused;Username=unused", ["Database:AutoMigrate"] = "false" }));
        builder.ConfigureServices(services => {
            services.RemoveAll<IGeminiClient>(); services.AddSingleton<IGeminiClient>(Gemini);
            services.RemoveAll<ICatalogClient>(); services.AddSingleton<ICatalogClient>(new FakeCatalog());
            services.RemoveAll<IInventoryAvailabilityClient>(); services.AddSingleton<IInventoryAvailabilityClient>(new FakeInventory());
            services.RemoveAll<IRecommendationRepository>(); services.AddSingleton<IRecommendationRepository>(Repository);
        });
    }
    public HttpClient Client(string? role, Guid? user = null)
    {
        var client = CreateClient(); if (role is null) return client;
        var token = new JwtSecurityToken("lablend-ai", "lablend-web", [new Claim("sub", (user ?? Guid.NewGuid()).ToString()), new Claim("role", role)],
            expires: DateTime.UtcNow.AddMinutes(5), signingCredentials: new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Key)), SecurityAlgorithms.HmacSha256));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", new JwtSecurityTokenHandler().WriteToken(token));
        return client;
    }
}
public sealed class RecommendationApiTests
{
    [Theory] [InlineData(null, 401)] [InlineData("LAB_STAFF", 403)] [InlineData("ADMIN", 403)]
    public async Task JWT_and_student_role_are_enforced_at_service(string? role, int status)
    {
        using var factory = new RecommendationFactory(); using var client = factory.Client(role);
        Assert.Equal(status, (int)(await client.PostAsJsonAsync("/api/recommendations", Data.Request)).StatusCode);
        Assert.Equal(status, (int)(await client.GetAsync("/api/recommendations/history")).StatusCode);
        Assert.Equal(status, (int)(await client.GetAsync($"/api/recommendations/{Guid.NewGuid()}")).StatusCode);
    }
    [Fact] public async Task Student_create_history_owner_detail_and_cross_account_404()
    {
        using var factory = new RecommendationFactory(); using var client = factory.Client("STUDENT", Data.User);
        var response = await client.PostAsJsonAsync("/api/recommendations", Data.Request); Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<RecommendationResponse>(); Assert.NotNull(result);
        Assert.Equal($"/api/recommendations/{result.Id}", response.Headers.Location!.ToString());
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(response.Headers.Location)).StatusCode);
        using var other = factory.Client("STUDENT"); Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync(response.Headers.Location)).StatusCode);
        using var history = JsonDocument.Parse(await (await client.GetAsync("/api/recommendations/history?page=1&pageSize=1")).Content.ReadAsStringAsync());
        Assert.Equal(1, history.RootElement.GetProperty("total").GetInt32()); Assert.Equal(1, history.RootElement.GetProperty("pageSize").GetInt32());
    }
    [Fact] public async Task Feature_disabled_returns_sanitized_problem_with_code_and_correlation()
    {
        using var factory = new RecommendationFactory(); factory.Gemini.Disabled = true; using var client = factory.Client("STUDENT");
        client.DefaultRequestHeaders.Add("X-Correlation-ID", "recommendation-disabled-test");
        var response = await client.PostAsJsonAsync("/api/recommendations", Data.Request); Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType!.MediaType);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("AI_UNAVAILABLE", json.RootElement.GetProperty("code").GetString());
        Assert.Equal("recommendation-disabled-test", json.RootElement.GetProperty("correlationId").GetString()); Assert.Empty(factory.Repository.Sessions);
    }
    [Fact] public async Task Authenticated_rate_limit_is_per_user_and_returns_429()
    {
        using var factory = new RecommendationFactory(); using var client = factory.Client("STUDENT", Data.User);
        for (var i = 0; i < 3; i++) Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/recommendations", Data.Request)).StatusCode);
        var denied = await client.PostAsJsonAsync("/api/recommendations", Data.Request); Assert.Equal(HttpStatusCode.TooManyRequests, denied.StatusCode);
        Assert.Equal("60", denied.Headers.GetValues("Retry-After").Single()); Assert.Contains("RATE_LIMITED", await denied.Content.ReadAsStringAsync());
        using var other = factory.Client("STUDENT"); Assert.Equal(HttpStatusCode.Created, (await other.PostAsJsonAsync("/api/recommendations", Data.Request)).StatusCode);
    }
    [Fact] public async Task Validation_returns_problem_without_provider_call()
    {
        using var factory = new RecommendationFactory(); using var client = factory.Client("STUDENT");
        var response = await client.PostAsJsonAsync("/api/recommendations", new { purpose = "short" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode); Assert.Contains("VALIDATION_ERROR", await response.Content.ReadAsStringAsync()); Assert.Equal(0, factory.Gemini.Calls);
    }
}
