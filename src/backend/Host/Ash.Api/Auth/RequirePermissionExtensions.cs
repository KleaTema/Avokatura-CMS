using Ash.Modules.Identity.Contracts;

namespace Ash.Api.Auth;

public static class RequirePermissionExtensions
{
    /// <summary>Requires an authenticated user holding the given system permission; otherwise 401 / 403.</summary>
    public static TBuilder RequirePermission<TBuilder>(this TBuilder builder, string permissionCode)
        where TBuilder : IEndpointConventionBuilder
    {
        builder.RequireAuthorization();
        builder.AddEndpointFilter(async (context, next) =>
        {
            var permissions = context.HttpContext.RequestServices.GetRequiredService<IPermissionService>();
            if (!permissions.Has(permissionCode))
            {
                return Results.Problem(
                    title: "Nuk keni leje për këtë veprim.",
                    statusCode: StatusCodes.Status403Forbidden,
                    extensions: new Dictionary<string, object?> { ["code"] = "permission.denied" });
            }

            return await next(context);
        });
        return builder;
    }
}
