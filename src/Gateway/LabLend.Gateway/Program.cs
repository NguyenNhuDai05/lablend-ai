using System.Threading.RateLimiting;
using LabLend.Observability;

var builder = WebApplication.CreateBuilder(args);
builder.AddLabLendApi("Gateway");
var origins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
if (origins.Length == 0 || origins.Contains("*"))
    throw new InvalidOperationException("Configure explicit Cors:AllowedOrigins; wildcard origins are not allowed.");
builder.Services.AddCors(options => options.AddPolicy("web", policy => policy.WithOrigins(origins)
    .WithMethods("GET", "POST", "PUT", "PATCH", "DELETE", "OPTIONS")
    .WithHeaders("Authorization", "Content-Type", "X-Correlation-ID", "Idempotency-Key", "X-LabLend-CSRF")
    .AllowCredentials()
    .WithExposedHeaders("X-Correlation-ID")));
builder.Services.AddReverseProxy().LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("api", context => RateLimitPartition.GetFixedWindowLimiter(
        // Shared laboratory networks and the web reverse proxy must not put all signed-in roles
        // in one 120-request bucket. Identity comes only from the validated JWT, never a client header.
        context.User.Identity?.IsAuthenticated == true && context.User.FindFirst("sub") is { } subject
            ? "user:" + subject.Value : "ip:" + (context.Connection.RemoteIpAddress?.ToString() ?? "unknown"), _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 120, Window = TimeSpan.FromMinutes(1), QueueLimit = 0
        }));
});
var app = builder.Build();
app.UseCors("web");
app.UseLabLendApi("Gateway");
app.UseRateLimiter();
app.MapReverseProxy().RequireRateLimiting("api");
app.Run();

namespace LabLend.Gateway { public sealed class GatewayMarker; }
