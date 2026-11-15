using Catalog.Application;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Infrastructure;

namespace Catalog.Api;

// MVC filter instead of IExceptionHandler: expected 4xx business failures should not be logged as unhandled errors.
// ProblemDetailsFactory applies ServiceDefaults' customization (code + correlationId).
internal sealed class CatalogExceptionFilter(ProblemDetailsFactory problemDetails) : IExceptionFilter
{
    public void OnException(ExceptionContext context)
    {
        if (context.Exception is not CatalogException error)
            return;
        var problem = problemDetails.CreateProblemDetails(context.HttpContext, error.StatusCode, detail: error.Message);
        context.Result = new ObjectResult(problem) { StatusCode = error.StatusCode, ContentTypes = { "application/problem+json" } };
        context.ExceptionHandled = true;
    }
}
