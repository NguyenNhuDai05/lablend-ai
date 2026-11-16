using Identity.Infrastructure;
using LabLend.Observability;
using Identity.Api;
using Identity.Application;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);
builder.AddLabLendApi("Identity");
builder.Services.AddIdentityInfrastructure(builder.Configuration);
builder.Services.AddLabLendDependencies(builder.Configuration);
builder.Services.AddControllers().AddJsonOptions(o => o.JsonSerializerOptions.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow);
builder.Services.AddExceptionHandler<IdentityExceptionHandler>();
builder.Services.AddScoped<BrowserSessionFilter>();
builder.Services.AddOptions<CookieSettings>().BindConfiguration("SessionCookie")
    .Validate(x => x.AllowedOrigins.Length > 0 && x.AllowedOrigins.All(origin =>
        Uri.TryCreate(origin, UriKind.Absolute, out var uri) && (uri.Scheme == "https" ||
            (builder.Environment.IsDevelopment() && uri.IsLoopback && uri.Scheme == "http"))), "Explicit secure or local origins are required.")
    .Validate(x => x.Secure || builder.Environment.IsDevelopment(), "Insecure cookies are allowed only in Development.").ValidateOnStart();
builder.Services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
{
    options.Events ??= new JwtBearerEvents();
    options.Events.OnTokenValidated = async context =>
    {
        var service = context.HttpContext.RequestServices.GetRequiredService<IdentityService>();
        if (!Guid.TryParse(context.Principal?.FindFirst("sub")?.Value, out var id) || id == Guid.Empty || !await service.IsActive(id))
        {
            context.Fail("Account unavailable.");
            return;
        }
        var current = await service.Get(id);
        if (!current.Roles.Order().SequenceEqual(context.Principal!.FindAll("role").Select(c => c.Value).Order()))
            context.Fail("Account role changed; obtain a new token.");
    };
});
var app = builder.Build();
// Controlled, idempotent schema bootstrap. Production uses a separate migration invocation.
if (builder.Configuration.GetValue<bool>("Database:ApplyMigrations"))
{
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<IdentityDbContext>().Database.MigrateAsync();
    await IdentityBootstrap.Seed(scope.ServiceProvider, builder.Configuration, app.Environment.IsDevelopment(), default);
}
app.UseLabLendApi("Identity");
app.Use(async (context, next) => { context.Response.Headers.CacheControl = "no-store"; await next(); });
app.Run();

namespace Identity.Api { public sealed class IdentityMarker; }
