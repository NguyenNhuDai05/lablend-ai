using Recommendation.Infrastructure;
using LabLend.Observability;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using System.Threading.RateLimiting;
using Recommendation.Api;

var builder = WebApplication.CreateBuilder(args);
builder.AddLabLendApi("Recommendation");
builder.Services.AddControllers(o => o.Filters.Add<RecommendationExceptionFilter>());
builder.Services.AddRateLimiter(o =>
{
    o.AddPolicy("recommendation-create", context => RateLimitPartition.GetFixedWindowLimiter(
        context.User.FindFirst("sub")?.Value ?? "anonymous", _ => new FixedWindowRateLimiterOptions
        { PermitLimit = 3, Window = TimeSpan.FromMinutes(1), QueueLimit = 0, AutoReplenishment = true }));
    o.OnRejected = async (context, ct) =>
    {
        context.HttpContext.Response.StatusCode = 429;
        context.HttpContext.Response.Headers.RetryAfter = "60";
        var problem = new ProblemDetails { Status = 429, Title = "Bạn gửi yêu cầu quá nhanh. Vui lòng chờ một phút." };
        problem.Extensions["code"] = "RATE_LIMITED"; problem.Extensions["correlationId"] = context.HttpContext.TraceIdentifier;
        await context.HttpContext.Response.WriteAsJsonAsync(problem, options: null, contentType: "application/problem+json", cancellationToken: ct);
    };
});
builder.Services.AddRecommendationInfrastructure(builder.Configuration);
builder.Services.AddLabLendDependencies(builder.Configuration);
var app = builder.Build();
if (builder.Configuration.GetValue("Database:AutoMigrate", app.Environment.IsDevelopment()))
{
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<RecommendationDbContext>().Database.MigrateAsync();
}
app.UseLabLendApi("Recommendation");
app.UseRateLimiter();
app.Run();

public partial class RecommendationProgram;
