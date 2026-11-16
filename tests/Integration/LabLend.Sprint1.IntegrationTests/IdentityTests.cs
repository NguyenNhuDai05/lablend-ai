using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Identity.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace LabLend.Sprint1.IntegrationTests;

[Collection("postgres")]
public sealed class IdentityTests(PostgresFixture postgres)
{
    private static string Password => "Aa1!" + Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
    private static string Email => Guid.NewGuid().ToString("N") + "@sprint1.test";
    internal static string Cookie(HttpResponseMessage response) => response.Headers.GetValues("Set-Cookie").Single(h => h.StartsWith("lablend.refresh=")).Split(';')[0];
    internal static async Task<JsonElement> Json(HttpResponseMessage response) => JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
    internal static async Task<(string Token, string Cookie, Guid Id)> RegisterLogin(HttpClient client)
    {
        var email = Email; var password = Password;
        var register = await client.PostAsJsonAsync("/api/auth/register", new { displayName = "Student Test", email, password, confirmPassword = password });
        Assert.Equal(HttpStatusCode.Created, register.StatusCode);
        var id = (await Json(register)).GetProperty("id").GetGuid();
        var login = await client.PostAsJsonAsync("/api/auth/login", new { email, password });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        return ((await Json(login)).GetProperty("accessToken").GetString()!, Cookie(login), id);
    }

    [PostgresFact]
    public async Task T01_Register_Login_Profile_Password_and_lockout_use_real_identity_store()
    {
        await using var factory = new IdentityFactory(postgres);
        using var client = factory.Browser();
        var email = Email; var password = Password;
        var request = new { displayName = "Student Test", email, password, confirmPassword = password };
        var registers = await Task.WhenAll(client.PostAsJsonAsync("/api/auth/register", request), client.PostAsJsonAsync("/api/auth/register", request));
        Assert.Single(registers, r => r.StatusCode == HttpStatusCode.Created);
        Assert.Single(registers, r => r.StatusCode == HttpStatusCode.Conflict);
        var failed = await client.PostAsJsonAsync("/api/auth/login", new { email, password = Password });
        Assert.Equal(HttpStatusCode.Unauthorized, failed.StatusCode);
        var login = await client.PostAsJsonAsync("/api/auth/login", new { email = email.ToUpperInvariant(), password });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var body = await Json(login);
        Assert.Equal("STUDENT", body.GetProperty("user").GetProperty("roles")[0].GetString());
        var text = await login.Content.ReadAsStringAsync();
        Assert.DoesNotContain("passwordHash", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("securityStamp", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("refreshToken", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("httponly", login.Headers.GetValues("Set-Cookie").Single().ToLowerInvariant());
        client.DefaultRequestHeaders.Authorization = new("Bearer", body.GetProperty("accessToken").GetString());
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync("/api/users/me", new { displayName = "Updated Name" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync("/api/users/me", new { displayName = "Name", userId = Guid.NewGuid(), role = "ADMIN" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/users/me/change-password", new { currentPassword = Password, newPassword = Password })).StatusCode);
        var changed = Password;
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync("/api/users/me/change-password", new { currentPassword = password, newPassword = changed })).StatusCode);
        client.DefaultRequestHeaders.Add("Cookie", Cookie(login));
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsync("/api/auth/refresh", null)).StatusCode);
        for (var i = 0; i < 5; i++) Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/auth/login", new { email, password = Password })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/auth/login", new { email, password = changed })).StatusCode);
    }

    [PostgresFact]
    public async Task T22_Rotation_replay_concurrent_refresh_expiration_logout_and_hash_storage()
    {
        await using var factory = new IdentityFactory(postgres);
        using var client = factory.Browser();
        var session = await RegisterLogin(client);
        client.DefaultRequestHeaders.Add("Cookie", session.Cookie);
        var concurrent = await Task.WhenAll(client.PostAsync("/api/auth/refresh", null), client.PostAsync("/api/auth/refresh", null));
        Assert.Single(concurrent, r => r.StatusCode == HttpStatusCode.OK);
        Assert.Single(concurrent, r => r.StatusCode == HttpStatusCode.Unauthorized);
        var rotatedCookie = Cookie(concurrent.Single(r => r.IsSuccessStatusCode));
        client.DefaultRequestHeaders.Remove("Cookie"); client.DefaultRequestHeaders.Add("Cookie", rotatedCookie);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsync("/api/auth/refresh", null)).StatusCode);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var tokens = await db.RefreshTokens.Where(t => t.UserId == session.Id).ToListAsync();
        Assert.Equal(2, tokens.Count);
        Assert.All(tokens, t => { Assert.Equal(64, t.TokenHash.Length); Assert.NotNull(t.RevokedAt); });
        Assert.Contains(tokens, t => t.ReplacedByTokenId != null);
        Assert.Contains(await db.UserAuditEvents.Where(e => e.UserId == session.Id).ToListAsync(), e => e.Action == "RefreshReuseDetected");
        using var clean = factory.Browser();
        var fresh = await RegisterLogin(clean);
        clean.DefaultRequestHeaders.Add("Cookie", fresh.Cookie);
        Assert.Equal(HttpStatusCode.NoContent, (await clean.PostAsync("/api/auth/logout", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await clean.PostAsync("/api/auth/logout", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await clean.PostAsync("/api/auth/refresh", null)).StatusCode);
        using var expiry = factory.Browser();
        var exp = await RegisterLogin(expiry);
        var token = await db.RefreshTokens.SingleAsync(t => t.UserId == exp.Id);
        token.CreatedAt = DateTimeOffset.UtcNow.AddDays(-10); token.ExpiresAt = DateTimeOffset.UtcNow.AddDays(-1);
        await db.SaveChangesAsync();
        expiry.DefaultRequestHeaders.Add("Cookie", exp.Cookie);
        Assert.Equal(HttpStatusCode.Unauthorized, (await expiry.PostAsync("/api/auth/refresh", null)).StatusCode);
    }

    [PostgresFact]
    public async Task T02_Admin_RBAC_deactivation_last_admin_and_CSRF()
    {
        await using var factory = new IdentityFactory(postgres);
        using var student = factory.Browser();
        var session = await RegisterLogin(student);
        Assert.Equal(HttpStatusCode.Unauthorized, (await student.GetAsync("/api/auth/me")).StatusCode);
        student.DefaultRequestHeaders.Authorization = new("Bearer", session.Token);
        Assert.Equal(HttpStatusCode.Forbidden, (await student.GetAsync("/api/users")).StatusCode);
        using var admin = factory.Browser();
        var login = await admin.PostAsJsonAsync("/api/auth/login", new { email = "admin@sprint1.test", password = postgres.AdminPassword });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var body = await Json(login);
        admin.DefaultRequestHeaders.Authorization = new("Bearer", body.GetProperty("accessToken").GetString());
        var adminId = body.GetProperty("user").GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/users?page=1&pageSize=2")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.GetAsync("/api/users?page=0")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PatchAsJsonAsync($"/api/users/{adminId}/active", new { isActive = false })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PutAsJsonAsync($"/api/users/{adminId}/role", new { role = "STUDENT" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.PatchAsJsonAsync($"/api/users/{session.Id}/role", new { role = "LAB_STAFF" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await student.GetAsync("/api/users/me")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.PatchAsJsonAsync($"/api/users/{session.Id}/active", new { isActive = false })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await student.GetAsync("/api/users/me")).StatusCode);
        student.DefaultRequestHeaders.Add("Cookie", session.Cookie);
        Assert.Equal(HttpStatusCode.Unauthorized, (await student.PostAsync("/api/auth/refresh", null)).StatusCode);
        var inactiveEmail = Email; var inactivePassword = Password;
        var inactive = await student.PostAsJsonAsync("/api/auth/register", new { displayName = "Inactive Student", email = inactiveEmail,
            password = inactivePassword, confirmPassword = inactivePassword });
        Assert.Equal(HttpStatusCode.Created, inactive.StatusCode);
        var inactiveId = (await Json(inactive)).GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await admin.PatchAsJsonAsync($"/api/users/{inactiveId}/active", new { isActive = false })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await student.PostAsJsonAsync("/api/auth/login", new { email = inactiveEmail, password = inactivePassword })).StatusCode);
        using var attacker = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Forbidden, (await attacker.PostAsJsonAsync("/api/auth/login", new { email = "admin@sprint1.test", password = postgres.AdminPassword })).StatusCode);
        attacker.DefaultRequestHeaders.Add("Origin", "https://evil.example"); attacker.DefaultRequestHeaders.Add("X-LabLend-CSRF", "1");
        Assert.Equal(HttpStatusCode.Forbidden, (await attacker.PostAsync("/api/auth/refresh", null)).StatusCode);
        var attemptedPassword = Password;
        Assert.Equal(HttpStatusCode.BadRequest, (await student.PostAsJsonAsync("/api/auth/register", new { displayName = "Attacker", email = Email,
            password = attemptedPassword, confirmPassword = attemptedPassword, role = "ADMIN" })).StatusCode);
    }
}
