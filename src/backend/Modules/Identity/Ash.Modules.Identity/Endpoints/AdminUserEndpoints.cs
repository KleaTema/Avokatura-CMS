using Ash.Modules.Identity.Application.GetMe;
using Ash.Modules.Identity.Application.Users;
using Ash.Modules.Identity.Infrastructure;
using Ash.SharedKernel.Paging;
using Ash.SharedKernel.Results;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Ash.Modules.Identity.Contracts;

namespace Ash.Modules.Identity.Endpoints;

internal static class PermissionFilterExtensions
{
    /// <summary>Same behaviour as the host's RequirePermission (401 / 403), usable inside the module.</summary>
    public static RouteHandlerBuilder RequirePermissionFilter(this RouteHandlerBuilder builder, string permissionCode)
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

public static class AdminUserEndpoints
{
    private const string Permission = "admin.users";

    public static IEndpointRouteBuilder MapAdminUsers(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/identity/admin/users");

        group.MapGet("", async (
                string? q, string? role, Guid? officeId, Guid? institutionId, bool? active, string? after, int? limit,
                ListUsersHandler handler, CancellationToken ct) =>
                Results.Ok(await handler.Handle(new ListUsersQuery(q, role, officeId, institutionId, active, after, limit), ct)))
            .RequirePermissionFilter(Permission)
            .Produces<KeysetPage<UserResponse>>()
            .WithName("ListAdminUsers");

        group.MapPost("", async (CreateUserRequest req, CreateUserHandler handler, CancellationToken ct) =>
            {
                var result = await handler.Handle(req, ct);
                return result.IsSuccess
                    ? Results.Created($"/api/identity/admin/users/{result.Value.PublicId}", result.Value)
                    : Problem(result.Error!);
            })
            .RequirePermissionFilter(Permission)
            .Produces<UserResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithName("CreateAdminUser");

        group.MapPut("/{id:guid}", async (Guid id, UpdateUserRequest req, UpdateUserHandler handler, CancellationToken ct) =>
                ToResult(await handler.Handle(id, req, ct)))
            .RequirePermissionFilter(Permission)
            .Produces<UserResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithName("UpdateAdminUser");

        group.MapPost("/{id:guid}/deactivate", async (Guid id, SetUserActiveHandler handler, CancellationToken ct) =>
                ToResult(await handler.Handle(id, false, ct)))
            .RequirePermissionFilter(Permission)
            .Produces<UserResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .WithName("DeactivateAdminUser");

        group.MapPost("/{id:guid}/activate", async (Guid id, SetUserActiveHandler handler, CancellationToken ct) =>
                ToResult(await handler.Handle(id, true, ct)))
            .RequirePermissionFilter(Permission)
            .Produces<UserResponse>()
            .WithName("ActivateAdminUser");

        group.MapPost("/{id:guid}/roles", async (Guid id, RoleRequest req, GrantRoleHandler handler, CancellationToken ct) =>
                ToResult(await handler.Handle(id, req.Role, ct)))
            .RequirePermissionFilter(Permission)
            .Produces<UserResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .WithName("GrantAdminUserRole");

        group.MapDelete("/{id:guid}/roles/{role}", async (Guid id, string role, RevokeRoleHandler handler, CancellationToken ct) =>
                ToResult(await handler.Handle(id, role, ct)))
            .RequirePermissionFilter(Permission)
            .Produces<UserResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .WithName("RevokeAdminUserRole");

        endpoints.MapGet("/api/identity/admin/offices", async (IdentityDbContext db, CancellationToken ct) =>
                Results.Ok(await db.Offices.AsNoTracking().OrderBy(o => o.Name)
                    .Select(o => new NamedRef(o.PublicId, o.Name)).ToListAsync(ct)))
            .RequirePermissionFilter(Permission)
            .Produces<IReadOnlyList<NamedRef>>()
            .WithName("ListAdminOffices");

        endpoints.MapGet("/api/identity/admin/institutions", async (IdentityDbContext db, CancellationToken ct) =>
                Results.Ok(await db.Institutions.AsNoTracking().OrderBy(i => i.Name)
                    .Select(i => new NamedRef(i.PublicId, i.Name)).ToListAsync(ct)))
            .RequirePermissionFilter(Permission)
            .Produces<IReadOnlyList<NamedRef>>()
            .WithName("ListAdminInstitutions");

        return endpoints;
    }

    private static IResult ToResult(Result<UserResponse> result) =>
        result.IsSuccess ? Results.Ok(result.Value) : Problem(result.Error!);

    private static IResult Problem(Error error) => Results.Problem(
        title: error.Message,
        statusCode: error.Type switch
        {
            ErrorType.NotFound => StatusCodes.Status404NotFound,
            ErrorType.Conflict => StatusCodes.Status409Conflict,
            ErrorType.Forbidden => StatusCodes.Status403Forbidden,
            _ => StatusCodes.Status400BadRequest
        },
        extensions: new Dictionary<string, object?> { ["code"] = error.Code });
}
