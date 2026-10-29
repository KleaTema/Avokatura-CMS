using Ash.AspNetCore.Authorization;
using Ash.AspNetCore.Http;
using Ash.Modules.Identity.Application.GetMe;
using Ash.Modules.Identity.Application.Users;
using Ash.Modules.Identity.Infrastructure;
using Ash.SharedKernel.Paging;
using Ash.SharedKernel.Results;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Ash.Modules.Identity.Endpoints;

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
            .RequirePermission(Permission)
            .Produces<KeysetPage<UserResponse>>()
            .WithName("ListAdminUsers");

        group.MapPost("", async (CreateUserRequest req, CreateUserHandler handler, CancellationToken ct) =>
            {
                var result = await handler.Handle(req, ct);
                return result.IsSuccess
                    ? Results.Created($"/api/identity/admin/users/{result.Value.PublicId}", result.Value)
                    : result.Error!.ToProblem();
            })
            .RequirePermission(Permission)
            .Produces<UserResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithName("CreateAdminUser");

        group.MapPut("/{id:guid}", async (Guid id, UpdateUserRequest req, UpdateUserHandler handler, CancellationToken ct) =>
                ToResult(await handler.Handle(id, req, ct)))
            .RequirePermission(Permission)
            .Produces<UserResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithName("UpdateAdminUser");

        group.MapPost("/{id:guid}/deactivate", async (Guid id, SetUserActiveHandler handler, CancellationToken ct) =>
                ToResult(await handler.Handle(id, false, ct)))
            .RequirePermission(Permission)
            .Produces<UserResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .WithName("DeactivateAdminUser");

        group.MapPost("/{id:guid}/activate", async (Guid id, SetUserActiveHandler handler, CancellationToken ct) =>
                ToResult(await handler.Handle(id, true, ct)))
            .RequirePermission(Permission)
            .Produces<UserResponse>()
            .WithName("ActivateAdminUser");

        group.MapPost("/{id:guid}/roles", async (Guid id, RoleRequest req, GrantRoleHandler handler, CancellationToken ct) =>
                ToResult(await handler.Handle(id, req.Role, ct)))
            .RequirePermission(Permission)
            .Produces<UserResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .WithName("GrantAdminUserRole");

        group.MapDelete("/{id:guid}/roles/{role}", async (Guid id, string role, RevokeRoleHandler handler, CancellationToken ct) =>
                ToResult(await handler.Handle(id, role, ct)))
            .RequirePermission(Permission)
            .Produces<UserResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .WithName("RevokeAdminUserRole");

        endpoints.MapGet("/api/identity/admin/roles", async (ListRolesHandler handler, CancellationToken ct) =>
                Results.Ok(await handler.Handle(ct)))
            .RequirePermission(Permission)
            .Produces<IReadOnlyList<RoleResponse>>()
            .WithName("ListAdminRoles");

        return endpoints;
    }

    private static IResult ToResult(Result<UserResponse> result) =>
        result.IsSuccess ? Results.Ok(result.Value) : result.Error!.ToProblem();
}
