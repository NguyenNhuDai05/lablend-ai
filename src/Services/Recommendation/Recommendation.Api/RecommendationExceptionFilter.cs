using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Recommendation.Application;

namespace Recommendation.Api;

public sealed class RecommendationExceptionFilter : IExceptionFilter
{
    public void OnException(ExceptionContext context)
    {
        if (context.Exception is not RecommendationFailure failure) return;
        var problem = new ProblemDetails { Status = failure.Status, Title = failure.Message };
        problem.Extensions["code"] = failure.Code;
        problem.Extensions["correlationId"] = context.HttpContext.TraceIdentifier;
        context.Result = new ObjectResult(problem) { StatusCode = failure.Status, ContentTypes = { "application/problem+json" } };
        context.ExceptionHandled = true;
    }
}
