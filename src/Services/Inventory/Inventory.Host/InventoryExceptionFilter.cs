using Inventory.Application;
using Inventory.Domain;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Infrastructure;

namespace Inventory.Host;

// Same approach as Catalog: expected 4xx failures become ProblemDetails without "unhandled exception" error logs.
internal sealed class InventoryExceptionFilter(ProblemDetailsFactory problemDetails) : IExceptionFilter
{
    public void OnException(ExceptionContext context)
    {
        var (status, code) = context.Exception switch
        {
            InventoryException e => (e.StatusCode, e.Code),
            DomainRuleException => (StatusCodes.Status409Conflict, "CONFLICT"),
            _ => (0, "")
        };
        if (status == 0)
            return;
        var problem = problemDetails.CreateProblemDetails(context.HttpContext, status, detail: context.Exception.Message);
        problem.Extensions["code"] = code;
        context.Result = new ObjectResult(problem) { StatusCode = status, ContentTypes = { "application/problem+json" } };
        context.ExceptionHandled = true;
    }
}
