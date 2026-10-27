using Ash.Infrastructure.Persistence;

namespace Ash.Api.Persistence;

/// <summary>
/// For POST/PUT/PATCH/DELETE: opens one transaction before the endpoint runs,
/// commits on success (status below 400) and rolls back on error status or exception.
/// </summary>
public sealed class UnitOfWorkMiddleware(RequestDelegate next)
{
    private static readonly HashSet<string> WriteMethods =
        [HttpMethods.Post, HttpMethods.Put, HttpMethods.Patch, HttpMethods.Delete];

    public async Task InvokeAsync(HttpContext context, IUnitOfWork unitOfWork)
    {
        if (!WriteMethods.Contains(context.Request.Method))
        {
            await next(context);
            return;
        }

        await unitOfWork.BeginAsync(context.RequestAborted);
        try
        {
            await next(context);

            if (context.Response.StatusCode < StatusCodes.Status400BadRequest)
            {
                await unitOfWork.CommitAsync(context.RequestAborted);
            }
            else
            {
                await unitOfWork.RollbackAsync(CancellationToken.None);
            }
        }
        catch
        {
            await unitOfWork.RollbackAsync(CancellationToken.None);
            throw;
        }
    }
}
