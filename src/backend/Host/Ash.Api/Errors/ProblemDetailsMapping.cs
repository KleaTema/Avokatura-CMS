using Ash.AspNetCore.Http;
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
