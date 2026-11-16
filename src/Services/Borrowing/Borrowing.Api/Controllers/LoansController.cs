using Borrowing.Application;
using LabLend.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Borrowing.Api.Controllers;

[ApiController, Route("api/loans"), Authorize, Produces("application/json")]
public sealed class LoansController(LoanService service) : ControllerBase
{
    private Guid Actor => Guid.Parse(User.FindFirst("sub")!.Value);
    private bool Staff => User.IsInRole(Roles.Admin) || User.IsInRole(Roles.LabStaff);
    [HttpPost, Authorize(Roles = Roles.Student), ProducesResponseType<LoanDto>(201), ProducesResponseType<ProblemDetails>(400), ProducesResponseType<ProblemDetails>(503)]
    public async Task<IActionResult> Create(CreateLoanRequest request, CancellationToken ct)
    {
        var loan = await service.Create(Actor, request, HttpContext.TraceIdentifier, ct);
        // A relative public resource path remains usable behind both YARP and the web nginx proxy.
        return Created($"/api/loans/{loan.Id}", loan);
    }
    [HttpGet("my"), ProducesResponseType<PagedResponse<LoanDto>>(200)]
    public Task<PagedResponse<LoanDto>> My(CancellationToken ct, string? status = null, int page = 1, int pageSize = 10) =>
        service.List(Actor, status, page, pageSize, ct);
    [HttpGet, Authorize(Policy = "StaffOrAdmin"), ProducesResponseType<PagedResponse<LoanDto>>(200)]
    public Task<PagedResponse<LoanDto>> List(CancellationToken ct, string? status = null, int page = 1, int pageSize = 10) =>
        service.List(null, status, page, pageSize, ct);
    [HttpGet("{id:guid}"), ProducesResponseType<LoanDto>(200), ProducesResponseType<ProblemDetails>(404)]
    public Task<LoanDto> Detail(Guid id, CancellationToken ct) => service.Detail(id, Actor, Staff, ct);
    [HttpPost("{id:guid}/cancel"), Authorize(Roles = Roles.Student), ProducesResponseType<LoanDto>(200), ProducesResponseType<ProblemDetails>(409)]
    public Task<LoanDto> Cancel(Guid id, CancellationToken ct) => service.Cancel(id, Actor, ct);
    [HttpPost("{id:guid}/reject"), Authorize(Policy = "StaffOrAdmin"), ProducesResponseType<LoanDto>(200), ProducesResponseType<ProblemDetails>(409)]
    public Task<LoanDto> Reject(Guid id, RejectLoanRequest request, CancellationToken ct) => service.Reject(id, Actor, request.RejectionReason, ct);
}
