using Borrowing.Application;
using Borrowing.Domain;
using Microsoft.AspNetCore.Diagnostics;

namespace Borrowing.Api;

public sealed class LoanExceptionHandler : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken ct)
    {
        var status = exception switch { LoanFailure f => f.Status, LoanStateException => 409, ArgumentException => 400,
            System.Data.Common.DbException => 503, _ => 500 };
        context.Response.StatusCode = status;
        await context.RequestServices.GetRequiredService<IProblemDetailsService>().WriteAsync(new()
        { HttpContext = context, ProblemDetails = new() { Status = status, Title = status < 500 ? exception.Message : "Dependency unavailable." } });
        return true;
    }
}
