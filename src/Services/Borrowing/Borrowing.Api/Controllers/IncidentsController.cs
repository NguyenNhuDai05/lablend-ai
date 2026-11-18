using Borrowing.Application;
using Borrowing.Infrastructure;
using LabLend.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Borrowing.Api.Controllers;

[ApiController, Route("api/incidents"), Authorize, Produces("application/json")]
public sealed class IncidentsController(IncidentService service, IncidentWorkflow workflow) : ControllerBase
{
    private Guid Actor => Guid.Parse(User.FindFirst("sub")!.Value);
    private bool Staff => User.IsInRole(Roles.Admin) || User.IsInRole(Roles.LabStaff);
    private Guid Key(string? value) => Guid.TryParseExact(value, "D", out var key) && key != Guid.Empty ? key
        : throw new LoanFailure(400, "Idempotency-Key phải là UUID hợp lệ và không rỗng.", "IDEMPOTENCY_KEY_REQUIRED");
    [HttpPost, Authorize(Roles = Roles.Student), ProducesResponseType<IncidentDto>(201), ProducesResponseType<ProblemDetails>(409)]
    public async Task<IActionResult> Report(ReportIncidentRequest request, CancellationToken ct, [FromHeader(Name = "Idempotency-Key")] string? key)
    {
        var incident = await service.Report(request, Actor, Key(key), HttpContext.TraceIdentifier, ct);
        return Created($"/api/incidents/{incident.Id}", incident);
    }
    [HttpGet("my"), Authorize(Roles = Roles.Student)]
    public Task<PagedResponse<IncidentDto>> My([FromQuery] IncidentQuery query, CancellationToken ct) => service.List(Actor, query, ct);
    [HttpGet, Authorize(Policy = "StaffOrAdmin")]
    public Task<PagedResponse<IncidentDto>> List([FromQuery] IncidentQuery query, CancellationToken ct) => service.List(null, query, ct);
    [HttpGet("stats"), Authorize(Roles = Roles.Admin)]
    public Task<IncidentStatistics> Stats([FromQuery] IncidentQuery query, CancellationToken ct) => service.Stats(query, ct);
    [HttpGet("{id:guid}")]
    public Task<IncidentDto> Detail(Guid id, CancellationToken ct) => service.Detail(id, Actor, Staff, ct);
    [HttpPost("{id:guid}/cancel"), Authorize(Roles = Roles.Student)]
    public Task<IncidentDto> Cancel(Guid id, CancellationToken ct) => Change(id, "cancel", null, null, ct);
    [HttpPost("{id:guid}/wait"), Authorize(Roles = Roles.Student)]
    public Task<IncidentDto> Wait(Guid id, CancellationToken ct) => Change(id, "wait", null, null, ct);
    [HttpPost("{id:guid}/start-inspection"), Authorize(Policy = "StaffOrAdmin")]
    public Task<IncidentDto> Start(Guid id, CancellationToken ct) => Change(id, "start", null, null, ct);
    [HttpPost("{id:guid}/inspection"), Authorize(Policy = "StaffOrAdmin")]
    public Task<IncidentDto> Inspect(Guid id, InspectIncidentRequest request, CancellationToken ct) => Change(id, "inspect", request, null, ct);
    [HttpPost("{id:guid}/resolve"), Authorize(Policy = "StaffOrAdmin")]
    public Task<IncidentDto> Resolve(Guid id, ResolveIncidentRequest request, CancellationToken ct) => Change(id, "resolve", null, request.Notes, ct);
    [HttpPost("{id:guid}/replace"), Authorize(Policy = "StaffOrAdmin"), ProducesResponseType<IncidentDto>(200), ProducesResponseType<ProblemDetails>(409), ProducesResponseType<ProblemDetails>(503)]
    public Task<IncidentDto> Replace(Guid id, ReplaceIncidentRequest request, CancellationToken ct, [FromHeader(Name = "Idempotency-Key")] string? key) =>
        workflow.Execute(id, "INC_REPLACE", Key(key), Actor, request.ConfirmHandover, HttpContext.TraceIdentifier, ct);
    [HttpPost("{id:guid}/defective-return"), Authorize(Policy = "StaffOrAdmin")]
    public Task<IncidentDto> Return(Guid id, CancellationToken ct, [FromHeader(Name = "Idempotency-Key")] string? key) =>
        workflow.Execute(id, "INC_RETURN", Key(key), Actor, false, HttpContext.TraceIdentifier, ct);
    [HttpPost("{id:guid}/retry-operation"), Authorize(Policy = "StaffOrAdmin")]
    public Task<IncidentDto> Retry(Guid id, CancellationToken ct) => workflow.Retry(id, Actor, ct);
    [HttpPost("{id:guid}/evidence"), Authorize(Roles = Roles.Student), Consumes("multipart/form-data")]
    [RequestSizeLimit(6 * 1024 * 1024), RequestFormLimits(MultipartBodyLengthLimit = 6 * 1024 * 1024)]
    public async Task<IncidentEvidenceDto> Upload(Guid id, [FromForm] IFormFile file, CancellationToken ct)
    {
        if (file.Length is < 1 or > IncidentImageValidator.MaximumBytes) throw new LoanFailure(400, "Ảnh tối đa 5 MB.", "INVALID_IMAGE");
        await using var stream = file.OpenReadStream(); using var data = new MemoryStream();
        var buffer = new byte[81920]; int read;
        while ((read = await stream.ReadAsync(buffer, ct)) > 0)
        {
            if (data.Length + read > IncidentImageValidator.MaximumBytes) throw new LoanFailure(400, "Ảnh tối đa 5 MB.", "INVALID_IMAGE");
            await data.WriteAsync(buffer.AsMemory(0, read), ct);
        }
        return await service.AddEvidence(id, Actor, data.ToArray(), ct);
    }
    [HttpGet("{id:guid}/evidence/{evidenceId:guid}"), Produces("image/jpeg", "image/png", "image/webp")]
    public async Task<IActionResult> Download(Guid id, Guid evidenceId, CancellationToken ct)
    {
        var evidence = await service.Evidence(id, evidenceId, Actor, Staff, ct);
        Response.Headers.CacheControl = "private, no-store"; Response.Headers.XContentTypeOptions = "nosniff";
        Response.Headers.ContentSecurityPolicy = "sandbox; default-src 'none'";
        return File(evidence.Content, evidence.ContentType);
    }
    private Task<IncidentDto> Change(Guid id, string action, InspectIncidentRequest? request, string? notes, CancellationToken ct) =>
        service.Change(id, Actor, Staff, action, request, notes, HttpContext.TraceIdentifier, ct);
}
