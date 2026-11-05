using Ash.AspNetCore.Http;
using Ash.Infrastructure.Persistence;
using Ash.Modules.Cases.Contracts;
using Ash.SharedKernel.Concurrency;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Ash.Api.Errors;

public sealed class ApiExceptionHandler(IProblemDetailsService problemDetails, ILogger<ApiExceptionHandler> logger)
    : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var problem = exception switch
        {
            RowVersionMismatchException => new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Regjistrimi u ndryshua nga dikush tjetër. Rifreskoni dhe provoni përsëri."
            },
            CaseAccessException ex => new ProblemDetails
            {
                Status = ex.Error.Type.ToStatusCode(),
                Title = ex.Error.Message
            },
            _ => null
        };

        // a read query stopped by MySQL's time limit (or the client backstop): a clear 503, never a hang (task 046)
        if (problem is null && QueryTimeLimitInterceptor.IsTimeout(exception))
        {
            logger.LogWarning(exception, "A query exceeded the time limit: {Path}", httpContext.Request.Path.Value);
            problem = new ProblemDetails
            {
                Status = StatusCodes.Status503ServiceUnavailable,
                Title = "Kërkimi zgjati shumë. Provoni me më shumë filtra.",
                Extensions = { ["code"] = "query.timeout" },
            };
        }

        if (problem is null)
        {
            logger.LogError(exception, "Unhandled exception");
            problem = new ProblemDetails
            {
                Status = StatusCodes.Status500InternalServerError,
                Title = "Ndodhi një gabim i papritur."
            };
        }

        httpContext.Response.StatusCode = problem.Status!.Value;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problem,
            Exception = exception
        });
    }
}
