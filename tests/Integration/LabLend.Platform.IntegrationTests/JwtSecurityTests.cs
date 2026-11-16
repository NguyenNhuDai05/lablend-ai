using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace LabLend.Platform.IntegrationTests;

public sealed class JwtSecurityTests(GatewayFactory factory) : IClassFixture<GatewayFactory>
{
    [Theory]
    [InlineData("signature")]
    [InlineData("issuer")]
    [InlineData("audience")]
    [InlineData("expired")]
    [InlineData("subject")]
    public async Task Gateway_rejects_invalid_JWT_before_proxying(string defect)
    {
        using var client = factory.CreateClient();
        var key = defect == "signature" ? new string('x', 64) : factory.SigningKey;
        var expires = DateTime.UtcNow.AddMinutes(defect == "expired" ? -2 : 5);
        var token = new JwtSecurityToken(defect == "issuer" ? "wrong" : "lablend-ai",
            defect == "audience" ? "wrong" : "lablend-web",
            [new Claim("sub", defect == "subject" ? "invalid-user" : Guid.NewGuid().ToString()), new Claim("role", "STUDENT")],
            DateTime.UtcNow.AddHours(-1), expires,
            new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)), SecurityAlgorithms.HmacSha256));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", new JwtSecurityTokenHandler().WriteToken(token));
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/loans/my")).StatusCode);
    }
}
