using LabLend.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Recommendation.Application;

namespace Recommendation.Api;

[ApiController, Route("api/recommendations"), Authorize(Roles = Roles.Student)]
public sealed class RecommendationsController(IRecommendationService service) : ControllerBase
{
    private Guid UserId => Guid.Parse(User.FindFirst("sub")!.Value);
    [HttpPost, EnableRateLimiting("recommendation-create"), RequestSizeLimit(16_384)]
    [ProducesResponseType<RecommendationResponse>(201)]
    [ProducesResponseType<ProblemDetails>(400)]
    [ProducesResponseType<ProblemDetails>(429)]
    [ProducesResponseType<ProblemDetails>(502)]
    [ProducesResponseType<ProblemDetails>(503)]
    public async Task<ActionResult<RecommendationResponse>> Create(CreateRecommendationRequest request, CancellationToken ct)
    {
        var result = await service.Create(UserId, request, HttpContext.TraceIdentifier, ct);
        return Created($"/api/recommendations/{result.Id}", result);
    }
    [HttpGet("history")]
    public Task<PagedResponse<RecommendationHistoryResponse>> History(int page = 1, int pageSize = 20, CancellationToken ct = default) =>
        service.History(UserId, page, pageSize, ct);
    [HttpGet("{id:guid}")]
    public Task<RecommendationResponse> Get(Guid id, CancellationToken ct) => service.Get(UserId, id, HttpContext.TraceIdentifier, ct);
}
