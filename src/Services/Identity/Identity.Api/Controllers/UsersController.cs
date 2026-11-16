using Identity.Application;
using LabLend.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Identity.Api.Controllers;

[ApiController, Route("api/users"), Authorize, Produces("application/json")]
public sealed class UsersController(IdentityService service) : ControllerBase
{
    private Guid Actor => Guid.Parse(User.FindFirst("sub")!.Value);
    [HttpGet("me"), ProducesResponseType<CurrentUser>(200)]
    public Task<CurrentUser> Me() => service.Get(Actor);
    [HttpPut("me"), ProducesResponseType<CurrentUser>(200)]
    public Task<CurrentUser> Profile(ProfileRequest request, CancellationToken ct) => service.Update(Actor, request, Actor, ct);
    [HttpPost("me/change-password"), ProducesResponseType(204)]
    public async Task<IActionResult> Password(PasswordRequest request, CancellationToken ct)
    { await service.ChangePassword(Actor, request, ct); return NoContent(); }
    [HttpGet, Authorize(Policy = "Admin"), ProducesResponseType<PagedResponse<CurrentUser>>(200)]
    public Task<PagedResponse<CurrentUser>> List(CancellationToken ct, int page = 1, int pageSize = 10) => service.List(page, pageSize, ct);
    [HttpGet("{id:guid}"), Authorize(Policy = "Admin"), ProducesResponseType<CurrentUser>(200)]
    public Task<CurrentUser> Detail(Guid id) => service.Get(id);
    [HttpPut("{id:guid}"), Authorize(Policy = "Admin"), ProducesResponseType<CurrentUser>(200)]
    public Task<CurrentUser> Update(Guid id, ProfileRequest request, CancellationToken ct) => service.Update(id, request, Actor, ct);
    [HttpPatch("{id:guid}/active"), Authorize(Policy = "Admin"), ProducesResponseType<CurrentUser>(200)]
    public Task<CurrentUser> Active(Guid id, ActiveRequest request, CancellationToken ct) => service.SetActive(id, request.IsActive, Actor, ct);
    [HttpPut("{id:guid}/role"), Authorize(Policy = "Admin"), ProducesResponseType<CurrentUser>(200)]
    [HttpPatch("{id:guid}/role")]
    public Task<CurrentUser> Role(Guid id, RoleRequest request, CancellationToken ct) => service.SetRole(id, request.Role, Actor, ct);
}
