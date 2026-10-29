using Ash.SharedKernel.Results;
using Microsoft.AspNetCore.Http;

namespace Ash.AspNetCore.Http;

public static class ErrorResults
{
    public static int ToStatusCode(this ErrorType type) => type switch
    {
        ErrorType.Validation => StatusCodes.Status400BadRequest,
        ErrorType.NotFound => StatusCodes.Status404NotFound,
        ErrorType.Conflict => StatusCodes.Status409Conflict,
        ErrorType.Forbidden => StatusCodes.Status403Forbidden,
        _ => StatusCodes.Status500InternalServerError
    };

    public static IResult ToProblem(this Error error)
    {
        var extensions = new Dictionary<string, object?> { ["code"] = error.Code };
        foreach (var (key, value) in error.Extensions ?? new Dictionary<string, object?>())
        {
            extensions[key] = value;
        }

        return Results.Problem(title: error.Message, statusCode: error.Type.ToStatusCode(), extensions: extensions);
    }
}
