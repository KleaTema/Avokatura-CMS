using Ash.Modules.Identity.Application.ResolveUser;

namespace Ash.Api.Auth;

/// <summary>Maps the authenticated principal to a users row and fills ICurrentUser; unknown or inactive user gives 401.</summary>
public sealed class CurrentUserMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, ResolveUserHandler handler, CurrentUser currentUser)
    {
        if (context.User.Identity?.IsAuthenticated != true)
        {
            await next(context);
            return;
        }

        // TODO(client-q1): confirm
        var subject = context.User.FindFirst("sub")?.Value;
        var devEmail = context.User.FindFirst(DevAuthHandler.EmailClaim)?.Value;

        var result = await handler.Handle(new ResolveUserRequest(subject, subject is null ? devEmail : null), context.RequestAborted);
        if (result.IsFailure)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        currentUser.Set(result.Value);
        await next(context);
    }
}
