using Ash.Modules.Cases.Contracts;
using Ash.SharedKernel.Concurrency;
using Ash.SharedKernel.Results;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Ash.Api.Errors;

public static class ProblemDetailsMapping
{
    public static int ToStatusCode(this ErrorType type) => type switch
    {
        ErrorType.Validation => StatusCodes.Status400BadRequest,
        ErrorType.NotFound => StatusCodes.Status404NotFound,
        ErrorType.Conflict => StatusCodes.Status409Conflict,
        ErrorType.Forbidden => StatusCodes.Status403Forbidden,
        _ => StatusCodes.Status500InternalServerError
    };

    public static IResult ToProblem(this Error error) =>
        Results.Problem(
            title: error.Message,
            statusCode: error.Type.ToStatusCode(),
            extensions: new Dictionary<string, object?> { ["code"] = error.Code });
}

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
