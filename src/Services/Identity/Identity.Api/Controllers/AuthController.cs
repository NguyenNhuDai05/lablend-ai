using Identity.Application;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Identity.Api.Controllers;

[ApiController, Route("api/auth"), Produces("application/json")]
public sealed class AuthController(IdentityService service, IOptions<CookieSettings> settings) : ControllerBase
{
    [HttpPost("register"), AllowAnonymous, ServiceFilter(typeof(BrowserSessionFilter))]
    [ProducesResponseType<CurrentUser>(201), ProducesResponseType<ProblemDetails>(400), ProducesResponseType<ProblemDetails>(409)]
    public async Task<IActionResult> Register(RegisterRequest request, CancellationToken ct) =>
        Created("/api/users/me", await service.Register(request, ct));

    [HttpPost("login"), AllowAnonymous, ServiceFilter(typeof(BrowserSessionFilter))]
    [ProducesResponseType<SessionResponse>(200), ProducesResponseType<ProblemDetails>(401)]
    public async Task<IActionResult> Login(LoginRequest request, CancellationToken ct) => Session(await service.Login(request, ct));

    [HttpPost("refresh"), AllowAnonymous, ServiceFilter(typeof(BrowserSessionFilter))]
    [ProducesResponseType<SessionResponse>(200), ProducesResponseType<ProblemDetails>(401)]
    public async Task<IActionResult> Refresh(CancellationToken ct)
    {
        try { return Session(await service.Refresh(Request.Cookies[CookieSettings.Name], ct)); }
        catch (IdentityFailure) { Response.Cookies.Delete(CookieSettings.Name, settings.Value.Options()); throw; }
    }

    [HttpPost("logout"), AllowAnonymous, ServiceFilter(typeof(BrowserSessionFilter)), ProducesResponseType(204)]
    public async Task<IActionResult> Logout(CancellationToken ct)
    {
        await service.Logout(Request.Cookies[CookieSettings.Name], ct);
        Response.Cookies.Delete(CookieSettings.Name, settings.Value.Options());
        return NoContent();
    }

    [HttpGet("me"), Authorize, ProducesResponseType<CurrentUser>(200)]
    public Task<CurrentUser> Me() => service.Get(Guid.Parse(User.FindFirst("sub")!.Value));

    private IActionResult Session(SessionResult session)
    {
        Response.Headers.CacheControl = "no-store";
        Response.Headers.Pragma = "no-cache";
        Response.Cookies.Append(CookieSettings.Name, session.RefreshToken, settings.Value.Options(session.RefreshExpiresAt));
        return Ok(new SessionResponse(session.AccessToken.Value, "Bearer", session.AccessToken.ExpiresAt, session.User));
    }
}
