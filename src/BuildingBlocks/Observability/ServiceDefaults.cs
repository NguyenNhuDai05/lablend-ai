using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;

namespace LabLend.Observability;

public static class ServiceDefaults
{
    public static WebApplicationBuilder AddLabLendApi(this WebApplicationBuilder builder, string service)
    {
        builder.Services.AddOptions<JwtSettings>().BindConfiguration("Jwt")
            .Validate(settings => !string.IsNullOrWhiteSpace(settings.SigningKey)
                && Encoding.UTF8.GetByteCount(settings.SigningKey) >= 32
                && !string.IsNullOrWhiteSpace(settings.Issuer) && !string.IsNullOrWhiteSpace(settings.Audience),
                "Jwt__SigningKey must be at least 32 bytes. Run scripts/Initialize-Environment.ps1.")
            .ValidateOnStart();
        builder.Logging.ClearProviders();
        builder.Logging.AddJsonConsole(options => options.IncludeScopes = true);
        builder.Services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
        {
            context.ProblemDetails.Extensions["correlationId"] = context.HttpContext.TraceIdentifier;
            context.ProblemDetails.Extensions["code"] = context.ProblemDetails.Status switch
            {
                400 => "VALIDATION_ERROR", 401 => "UNAUTHENTICATED", 403 => "FORBIDDEN",
                404 => "NOT_FOUND", 409 => "CONFLICT", 429 => "RATE_LIMITED", 503 => "SERVICE_UNAVAILABLE", _ => "HTTP_ERROR"
            };
            if (context.ProblemDetails.Status >= 500)
            {
                context.ProblemDetails.Title = "The service could not complete the request.";
                context.ProblemDetails.Detail = null;
                context.ProblemDetails.Extensions["code"] = context.ProblemDetails.Status == 503 ? "SERVICE_UNAVAILABLE" : "SERVICE_ERROR";
            }
        });
        builder.Services.AddControllers().ConfigureApiBehaviorOptions(options =>
        {
            options.InvalidModelStateResponseFactory = context =>
            {
                var problem = new ValidationProblemDetails(context.ModelState)
                {
                    Status = StatusCodes.Status400BadRequest, Title = "Request validation failed."
                };
                problem.Extensions["code"] = "VALIDATION_ERROR";
                problem.Extensions["correlationId"] = context.HttpContext.TraceIdentifier;
                var result = new BadRequestObjectResult(problem);
                result.ContentTypes.Add("application/problem+json");
                return result;
            };
        });
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen(options =>
        {
            options.SwaggerDoc("v1", new OpenApiInfo { Title = $"LabLend {service}", Version = "v1" });
            options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.Http, Scheme = "bearer", BearerFormat = "JWT",
                Description = "Use the access token issued by POST /api/auth/login."
            });
            // Without a requirement Swagger UI shows Authorize but never sends the Bearer header.
            options.AddSecurityRequirement(new OpenApiSecurityRequirement
            {
                [new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" } }] = []
            });
        });
        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
        {
            var signingKey = builder.Configuration["Jwt:SigningKey"]!;
            options.MapInboundClaims = false;
            options.IncludeErrorDetails = false;
            options.Events = new JwtBearerEvents
            {
                OnTokenValidated = context =>
                {
                    if (!Guid.TryParse(context.Principal?.FindFirst("sub")?.Value, out var id) || id == Guid.Empty)
                        context.Fail("A user UUID subject is required.");
                    return Task.CompletedTask;
                }
            };
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true, ValidateAudience = true, ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ValidIssuer = builder.Configuration["Jwt:Issuer"] ?? "lablend-ai",
                ValidAudience = builder.Configuration["Jwt:Audience"] ?? "lablend-web",
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)),
                NameClaimType = "sub", RoleClaimType = "role", ClockSkew = TimeSpan.FromSeconds(30)
            };
        });
        builder.Services.AddAuthorization(options =>
        {
            options.AddPolicy("StaffOrAdmin", policy => policy.RequireRole("LAB_STAFF", "ADMIN"));
            options.AddPolicy("Admin", policy => policy.RequireRole("ADMIN"));
        });
        builder.Services.AddHealthChecks().AddCheck("process", () => HealthCheckResult.Healthy(), tags: ["live"]);
        return builder;
    }

    public static IHealthChecksBuilder AddLabLendDependencies(this IServiceCollection services, IConfiguration configuration, bool redis = false)
    {
        var database = configuration.GetConnectionString("Database");
        if (string.IsNullOrWhiteSpace(database))
            throw new InvalidOperationException("ConnectionStrings__Database is required.");
        var checks = services.AddHealthChecks().AddCheck("postgres",
            new PostgresReadinessCheck(database), tags: ["ready"], timeout: TimeSpan.FromSeconds(5));
        if (redis)
        {
            var redisConnection = configuration.GetConnectionString("Redis");
            if (string.IsNullOrWhiteSpace(redisConnection))
                throw new InvalidOperationException("ConnectionStrings__Redis is required.");
            checks.AddCheck("redis", new RedisReadinessCheck(redisConnection), tags: ["ready"], timeout: TimeSpan.FromSeconds(5));
        }
        return checks;
    }

    public static WebApplication UseLabLendApi(this WebApplication app, string service)
    {
        app.Use(async (context, next) =>
        {
            var correlationId = CorrelationId.Resolve(context.Request.Headers[CorrelationId.Header].ToString());
            context.TraceIdentifier = correlationId;
            context.Request.Headers[CorrelationId.Header] = correlationId;
            context.Response.Headers[CorrelationId.Header] = correlationId;
            using var scope = app.Logger.BeginScope(new Dictionary<string, object>
            {
                ["CorrelationId"] = correlationId, ["Service"] = service
            });
            await next(context);
        });
        // Use the same sanitized ProblemDetails even during Development; never DeveloperExceptionPage.
        app.UseExceptionHandler();
        app.UseStatusCodePages();
        app.UseSwagger();
        app.UseSwaggerUI(options => options.SwaggerEndpoint("v1/swagger.json", $"{service} v1"));
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapControllers();
        app.MapHealthChecks("/health/live", HealthOptions(liveOnly: true)).AllowAnonymous();
        app.MapHealthChecks("/health/ready", HealthOptions(liveOnly: false)).AllowAnonymous();
        return app;
    }

    private static HealthCheckOptions HealthOptions(bool liveOnly) => new()
    {
        Predicate = registration => !liveOnly || registration.Tags.Contains("live"),
        ResponseWriter = (context, report) => context.Response.WriteAsJsonAsync(new
        {
            status = report.Status.ToString(),
            checks = report.Entries.ToDictionary(entry => entry.Key, entry => entry.Value.Status.ToString()),
            correlationId = context.TraceIdentifier
        })
    };
}
