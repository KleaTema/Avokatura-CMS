using Ash.AspNetCore.Authorization;
using Ash.AspNetCore.Http;
using Ash.Modules.Identity.Application.Institutions;
using Ash.Modules.Identity.Application.Offices;
using Ash.SharedKernel.Paging;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Ash.Modules.Identity.Endpoints;

/// <summary>Offices and institutions (task 015). Identified by public id; no delete, only deactivate.</summary>
public static class AdminOrgEndpoints
{
    private const string Permission = "admin.org";

    public static IEndpointRouteBuilder MapAdminOrg(this IEndpointRouteBuilder endpoints)
    {
        var offices = endpoints.MapGroup("/api/identity/admin/offices");

        offices.MapGet("", async (ListOfficesAdminHandler handler, CancellationToken ct) =>
                Results.Ok(await handler.Handle(ct)))
            .RequirePermission(Permission)
            .Produces<IReadOnlyList<OfficeAdminResponse>>()
            .WithName("ListAdminOffices");

        offices.MapPost("", async (CreateOfficeRequest req, CreateOfficeHandler handler, CancellationToken ct) =>
                {
                    var result = await handler.Handle(req, ct);
                    return result.IsSuccess
                        ? Results.Created($"/api/identity/admin/offices/{result.Value.PublicId}", result.Value)
                        : result.Error!.ToProblem();
                })
            .RequirePermission(Permission)
            .Produces<OfficeAdminResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .WithName("CreateAdminOffice");

        offices.MapPut("/{publicId:guid}", async (Guid publicId, UpdateOfficeRequest req, UpdateOfficeHandler handler, CancellationToken ct) =>
                {
                    var result = await handler.Handle(publicId, req, ct);
                    return result.IsSuccess ? Results.Ok(result.Value) : result.Error!.ToProblem();
                })
            .RequirePermission(Permission)
            .Produces<OfficeAdminResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithName("UpdateAdminOffice");

        offices.MapPost("/{publicId:guid}/deactivate", async (Guid publicId, SetOfficeActiveHandler handler, CancellationToken ct) =>
                {
                    var result = await handler.Handle(publicId, false, ct);
                    return result.IsSuccess ? Results.Ok(result.Value) : result.Error!.ToProblem();
                })
            .RequirePermission(Permission)
            .Produces<OfficeAdminResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithName("DeactivateAdminOffice");

        offices.MapPost("/{publicId:guid}/activate", async (Guid publicId, SetOfficeActiveHandler handler, CancellationToken ct) =>
                {
                    var result = await handler.Handle(publicId, true, ct);
                    return result.IsSuccess ? Results.Ok(result.Value) : result.Error!.ToProblem();
                })
            .RequirePermission(Permission)
            .Produces<OfficeAdminResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithName("ActivateAdminOffice");

        offices.MapPut("/{publicId:guid}/head", async (Guid publicId, SetOfficeHeadRequest req, SetOfficeHeadHandler handler, CancellationToken ct) =>
                {
                    var result = await handler.Handle(publicId, req, ct);
                    return result.IsSuccess ? Results.Ok(result.Value) : result.Error!.ToProblem();
                })
            .RequirePermission(Permission)
            .Produces<OfficeAdminResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithName("SetAdminOfficeHead");

        var institutions = endpoints.MapGroup("/api/identity/admin/institutions");

        institutions.MapGet("", async (string? q, string? after, int? limit, ListInstitutionsAdminHandler handler, CancellationToken ct) =>
                Results.Ok(await handler.Handle(new ListInstitutionsAdminQuery(q, after, limit), ct)))
            .RequirePermission(Permission)
            .Produces<KeysetPage<InstitutionAdminResponse>>()
            .WithName("ListAdminInstitutions");

        institutions.MapPost("", async (CreateInstitutionRequest req, CreateInstitutionHandler handler, CancellationToken ct) =>
                {
                    var result = await handler.Handle(req, ct);
                    return result.IsSuccess
                        ? Results.Created($"/api/identity/admin/institutions/{result.Value.PublicId}", result.Value)
                        : result.Error!.ToProblem();
                })
            .RequirePermission(Permission)
            .Produces<InstitutionAdminResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .WithName("CreateAdminInstitution");

        institutions.MapPut("/{publicId:guid}", async (Guid publicId, UpdateInstitutionRequest req, UpdateInstitutionHandler handler, CancellationToken ct) =>
                {
                    var result = await handler.Handle(publicId, req, ct);
                    return result.IsSuccess ? Results.Ok(result.Value) : result.Error!.ToProblem();
                })
            .RequirePermission(Permission)
            .Produces<InstitutionAdminResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithName("UpdateAdminInstitution");

        institutions.MapPost("/{publicId:guid}/deactivate", async (Guid publicId, SetInstitutionActiveHandler handler, CancellationToken ct) =>
                {
                    var result = await handler.Handle(publicId, false, ct);
                    return result.IsSuccess ? Results.Ok(result.Value) : result.Error!.ToProblem();
                })
            .RequirePermission(Permission)
            .Produces<InstitutionAdminResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithName("DeactivateAdminInstitution");

        institutions.MapPost("/{publicId:guid}/activate", async (Guid publicId, SetInstitutionActiveHandler handler, CancellationToken ct) =>
                {
                    var result = await handler.Handle(publicId, true, ct);
                    return result.IsSuccess ? Results.Ok(result.Value) : result.Error!.ToProblem();
                })
            .RequirePermission(Permission)
            .Produces<InstitutionAdminResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithName("ActivateAdminInstitution");

        // Public institution names only (no NIPT, internal ids or counts), so any authenticated, active user may search:
        // office users and institution users (delegated institutions add parties too). Widening or narrowing this is a
        // deliberate decision — see task 015. Inactive users never get here: CurrentUserMiddleware answers 401.
        endpoints.MapGet("/api/identity/institutions", async (string? q, int? limit, SearchInstitutionsHandler handler, CancellationToken ct) =>
                {
                    var result = await handler.Handle(q, limit, ct);
                    return result.IsSuccess ? Results.Ok(result.Value) : result.Error!.ToProblem();
                })
            .RequireAuthorization()
            .Produces<IReadOnlyList<InstitutionSearchResult>>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .WithName("SearchInstitutions");

        return endpoints;
    }
}
