using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Catalog.Api.Controllers;
using Catalog.Application;
using LabLend.Contracts;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Testcontainers.PostgreSql;
using Xunit;

namespace Catalog.IntegrationTests;

// T04: real PostgreSQL (Testcontainers, needs Docker) + real migrations + HTTP pipeline with JWT.
public sealed class CatalogApiFactory : WebApplicationFactory<CategoriesController>, IAsyncLifetime
{
    private readonly PostgreSqlContainer postgres = new PostgreSqlBuilder().WithImage("postgres:16-alpine").Build();
    private readonly string signingKey = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));

    // UseSetting (not ConfigureAppConfiguration) so Program.cs sees the values while registering services.
    protected override void ConfigureWebHost(IWebHostBuilder builder) => builder
        .UseSetting("Jwt:SigningKey", signingKey)
        .UseSetting("ConnectionStrings:Database", postgres.GetConnectionString());

    public HttpClient ClientAs(string? role)
    {
        var client = CreateClient();
        if (role is null) return client;
        var token = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = "lablend-ai", Audience = "lablend-web", Expires = DateTime.UtcNow.AddMinutes(10),
            Claims = new Dictionary<string, object> { ["sub"] = Guid.NewGuid().ToString(), ["role"] = role },
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)),
                SecurityAlgorithms.HmacSha256)
        });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    public Task InitializeAsync() => postgres.StartAsync();
    public new Task DisposeAsync() => postgres.DisposeAsync().AsTask();
}

public sealed class CatalogApiTests(CatalogApiFactory factory) : IClassFixture<CatalogApiFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task Seeded_catalog_is_public_and_mutations_require_staff()
    {
        var anonymous = factory.ClientAs(null);
        var seeded = await anonymous.GetFromJsonAsync<PagedResponse<EquipmentTypeResponse>>("/api/equipment-types?pageSize=100", Json);
        Assert.True(seeded!.Total >= 5);

        var body = new CreateCategoryRequest("Không được tạo", null);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync("/api/categories", body)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await factory.ClientAs(Roles.Student).PostAsJsonAsync("/api/categories", body)).StatusCode);
    }

    [Fact]
    public async Task Staff_crud_with_search_filter_sort_and_pagination()
    {
        var staff = factory.ClientAs(Roles.LabStaff);
        var category = await CreateAsync<CategoryResponse>(staff, "/api/categories", new CreateCategoryRequest($"Quang học {Guid.NewGuid():N}", "Thử nghiệm"));
        Task<EquipmentTypeResponse> Type(string name, params string[] tags) =>
            CreateAsync<EquipmentTypeResponse>(staff, "/api/equipment-types",
                new CreateEquipmentTypeRequest(category.Id, name, $"{name} mô tả", "spec", tags, null));
        var alpha = await Type("Alpha laser", "Laser", "optics");
        var beta = await Type("Beta lens", "optics");
        await Type("Gamma prism", "optics");
        Assert.Equal(["laser", "optics"], alpha.Tags); // normalized

        var url = $"/api/equipment-types?categoryId={category.Id}";
        Assert.Equal(3, (await SearchAsync(staff, url)).Total);
        Assert.Equal("Beta lens", (await SearchAsync(staff, $"{url}&keyword=LENS")).Items.Single().Name);
        Assert.Equal("Alpha laser", (await SearchAsync(staff, $"{url}&tag=laser")).Items.Single().Name);
        Assert.Equal("Gamma prism", (await SearchAsync(staff, $"{url}&sortBy=name&direction=desc")).Items[0].Name);
        var secondPage = await SearchAsync(staff, $"{url}&sortBy=name&page=2&pageSize=2");
        Assert.Equal((3, 2, 2), (secondPage.Total, secondPage.Page, secondPage.PageSize));
        Assert.Equal("Gamma prism", secondPage.Items.Single().Name);

        var update = new UpdateEquipmentTypeRequest(category.Id, "Beta lens 50mm", null, "f/1.8", ["optics"], "https://example.com/b.png", true);
        var updated = await (await staff.PutAsJsonAsync($"/api/equipment-types/{beta.Id}", update)).Content.ReadFromJsonAsync<EquipmentTypeResponse>(Json);
        Assert.Equal(("Beta lens 50mm", "f/1.8"), (updated!.Name, updated.Specifications));
        var bundled = await (await staff.PutAsJsonAsync($"/api/equipment-types/{beta.Id}", update with { ImageUrl = "/equipment/beta-lens.webp" })).Content.ReadFromJsonAsync<EquipmentTypeResponse>(Json);
        Assert.Equal("/equipment/beta-lens.webp", bundled!.ImageUrl); // image shipped in apps/web/public/equipment

        Assert.Equal(HttpStatusCode.NoContent, (await staff.DeleteAsync($"/api/equipment-types/{alpha.Id}")).StatusCode);
        Assert.Equal(2, (await SearchAsync(factory.ClientAs(null), $"{url}&includeInactive=true")).Total); // ignored for anonymous
        Assert.Equal(3, (await SearchAsync(staff, $"{url}&includeInactive=true")).Total);
        var deactivated = await staff.GetFromJsonAsync<EquipmentTypeResponse>($"/api/equipment-types/{alpha.Id}", Json);
        Assert.False(deactivated!.IsActive); // ID stays resolvable for existing loans
    }

    [Fact]
    public async Task Invalid_queries_duplicates_and_unsafe_deletes_are_rejected()
    {
        var staff = factory.ClientAs(Roles.Admin);
        await AssertProblemAsync(await staff.GetAsync("/api/equipment-types?pageSize=1000"), HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        await AssertProblemAsync(await staff.GetAsync("/api/equipment-types?sortBy=password"), HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        await AssertProblemAsync(await staff.GetAsync($"/api/equipment-types/{Guid.NewGuid()}"), HttpStatusCode.NotFound, "NOT_FOUND");

        var name = $"Trùng {Guid.NewGuid():N}";
        var category = await CreateAsync<CategoryResponse>(staff, "/api/categories", new CreateCategoryRequest(name, null));
        await AssertProblemAsync(await staff.PostAsJsonAsync("/api/categories", new CreateCategoryRequest(name, null)), HttpStatusCode.Conflict, "CONFLICT");

        await CreateAsync<EquipmentTypeResponse>(staff, "/api/equipment-types", new CreateEquipmentTypeRequest(category.Id, "Thiết bị A", null, null, null, null));
        await AssertProblemAsync(await staff.DeleteAsync($"/api/categories/{category.Id}"), HttpStatusCode.Conflict, "CONFLICT");
        await AssertProblemAsync(await staff.PostAsJsonAsync("/api/equipment-types",
            new CreateEquipmentTypeRequest(Guid.NewGuid(), "Không có loại", null, null, null, null)), HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        foreach (var bad in new[] { "javascript:alert(1)", "ftp://example.com/a.png", "/etc/passwd", "/equipment/../x.png", "/equipment/A.webp", "equipment/a.webp" })
            await AssertProblemAsync(await staff.PostAsJsonAsync("/api/equipment-types",
                new CreateEquipmentTypeRequest(category.Id, $"Ảnh {Guid.NewGuid():N}", null, null, null, bad)), HttpStatusCode.BadRequest, "VALIDATION_ERROR");
    }

    private static async Task<T> CreateAsync<T>(HttpClient client, string url, object body)
    {
        var response = await client.PostAsJsonAsync(url, body);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(response.Headers.Location);
        return (await response.Content.ReadFromJsonAsync<T>(Json))!;
    }

    private static async Task<PagedResponse<EquipmentTypeResponse>> SearchAsync(HttpClient client, string url) =>
        (await client.GetFromJsonAsync<PagedResponse<EquipmentTypeResponse>>(url, Json))!;

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(code, body.RootElement.GetProperty("code").GetString());
        Assert.True(body.RootElement.TryGetProperty("correlationId", out _));
    }
}
