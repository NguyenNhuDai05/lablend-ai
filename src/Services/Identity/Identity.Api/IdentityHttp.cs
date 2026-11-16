using Identity.Application;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;

namespace Identity.Api;

public sealed class CookieSettings
{
    public bool Secure { get; set; } = true;
    public string[] AllowedOrigins { get; set; } = [];
    public const string Name = "lablend.refresh";
    public CookieOptions Options(DateTimeOffset? expires = null) => new()
    { HttpOnly = true, Secure = Secure, SameSite = SameSiteMode.Strict, Path = "/api/auth", Expires = expires, IsEssential = true };
}

public sealed class BrowserSessionFilter(IOptions<CookieSettings> options) : IAuthorizationFilter
{
    public void OnAuthorization(AuthorizationFilterContext context)
    {
        var request = context.HttpContext.Request;
        if (request.Headers["X-LabLend-CSRF"] != "1" ||
            !options.Value.AllowedOrigins.Contains(request.Headers.Origin.ToString(), StringComparer.Ordinal))
        {
            context.Result = new ObjectResult(new ProblemDetails { Status = 403, Title = "Origin and CSRF header are required." }) { StatusCode = 403 };
        }
    }
}

public sealed class IdentityExceptionHandler : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken ct)
    {
        var status = exception is IdentityFailure failure ? failure.Status : exception is System.Data.Common.DbException ? 503 : 500;
        context.Response.StatusCode = status;
        await context.RequestServices.GetRequiredService<IProblemDetailsService>().WriteAsync(new()
        {
            HttpContext = context,
            ProblemDetails = new() { Status = status, Title = status < 500 ? exception.Message : "Service temporarily unavailable." }
        });
        return true;
    }
}
