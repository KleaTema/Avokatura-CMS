using Ash.Modules.Identity.Contracts;
using Ash.Modules.Reference.Application.Admin;
using Ash.SharedKernel.Results;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Ash.Modules.Reference.Endpoints;

public static class AdminReferenceEndpoints
{
    private const string Permission = "admin.lookups";

    public static IEndpointRouteBuilder MapAdminReference(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/reference/admin");

        group.MapGet("/hearing-types", async (ListHearingTypesAdminHandler handler, CancellationToken ct) =>
                Results.Ok(await handler.Handle(ct)))
            .RequireAuthorization()
            .AddEndpointFilter(PermissionFilter)
            .Produces<IReadOnlyList<HearingTypeAdminResponse>>()
            .WithName("ListAdminHearingTypes");

        group.MapPost("/hearing-types", async (CreateHearingTypeRequest req, CreateHearingTypeHandler handler, CancellationToken ct) =>
                {
                    var result = await handler.Handle(req, ct);
                    return result.IsSuccess ? Results.Created($"/api/reference/admin/hearing-types/{result.Value.Code}", result.Value)
                        : Problem(result.Error!);
                })
            .RequireAuthorization()
            .AddEndpointFilter(PermissionFilter)
            .Produces<HearingTypeAdminResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .WithName("CreateAdminHearingType");

        group.MapPut("/hearing-types/{code}", async (string code, UpdateHearingTypeRequest req, UpdateHearingTypeHandler handler, CancellationToken ct) =>
                {
                    var result = await handler.Handle(code, req, ct);
                    return result.IsSuccess ? Results.Ok(result.Value) : Problem(result.Error!);
                })
            .RequireAuthorization()
            .AddEndpointFilter(PermissionFilter)
            .Produces<HearingTypeAdminResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithName("UpdateAdminHearingType");

        group.MapPost("/hearing-types/{code}/deactivate", async (string code, SetHearingTypeActiveHandler handler, CancellationToken ct) =>
                {
                    var result = await handler.Handle(code, false, ct);
                    return result.IsSuccess ? Results.Ok(result.Value) : Problem(result.Error!);
                })
            .RequireAuthorization()
            .AddEndpointFilter(PermissionFilter)
            .Produces<HearingTypeAdminResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithName("DeactivateAdminHearingType");

        group.MapPost("/hearing-types/{code}/activate", async (string code, SetHearingTypeActiveHandler handler, CancellationToken ct) =>
                {
                    var result = await handler.Handle(code, true, ct);
                    return result.IsSuccess ? Results.Ok(result.Value) : Problem(result.Error!);
                })
            .RequireAuthorization()
            .AddEndpointFilter(PermissionFilter)
            .Produces<HearingTypeAdminResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithName("ActivateAdminHearingType");

        group.MapGet("/file-types", async (ListFileTypesAdminHandler handler, CancellationToken ct) =>
                Results.Ok(await handler.Handle(ct)))
            .RequireAuthorization()
            .AddEndpointFilter(PermissionFilter)
            .Produces<IReadOnlyList<FileTypeAdminResponse>>()
            .WithName("ListAdminFileTypes");

        group.MapPost("/file-types", async (CreateFileTypeRequest req, CreateFileTypeHandler handler, CancellationToken ct) =>
                {
                    var result = await handler.Handle(req, ct);
                    return result.IsSuccess ? Results.Created($"/api/reference/admin/file-types/{result.Value.Id}", result.Value)
                        : Problem(result.Error!);
                })
            .RequireAuthorization()
            .AddEndpointFilter(PermissionFilter)
            .Produces<FileTypeAdminResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .WithName("CreateAdminFileType");

        group.MapPut("/file-types/{id:int:range(1,255)}", async (int id, UpdateFileTypeRequest req, UpdateFileTypeHandler handler, CancellationToken ct) =>
                {
                    var result = await handler.Handle((byte)id, req, ct);
                    return result.IsSuccess ? Results.Ok(result.Value) : Problem(result.Error!);
                })
            .RequireAuthorization()
            .AddEndpointFilter(PermissionFilter)
            .Produces<FileTypeAdminResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithName("UpdateAdminFileType");

        group.MapGet("/dossier-code-formats", async (ListDossierCodeFormatsAdminHandler handler, CancellationToken ct) =>
                Results.Ok(await handler.Handle(ct)))
            .RequireAuthorization()
            .AddEndpointFilter(PermissionFilter)
            .Produces<IReadOnlyList<DossierCodeFormatAdminResponse>>()
            .WithName("ListAdminDossierCodeFormats");

        group.MapPost("/dossier-code-formats", async (CreateDossierCodeFormatRequest req, CreateDossierCodeFormatHandler handler, CancellationToken ct) =>
                {
                    var result = await handler.Handle(req, ct);
                    return result.IsSuccess ? Results.Created($"/api/reference/admin/dossier-code-formats/{result.Value.Id}", result.Value)
                        : Problem(result.Error!);
                })
            .RequireAuthorization()
            .AddEndpointFilter(PermissionFilter)
            .Produces<DossierCodeFormatAdminResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .WithName("CreateAdminDossierCodeFormat");

        group.MapPost("/dossier-code-formats/{id:int:range(1,255)}/activate", async (int id, ActivateDossierCodeFormatHandler handler, CancellationToken ct) =>
                {
                    var result = await handler.Handle((byte)id, ct);
                    return result.IsSuccess ? Results.Ok(result.Value) : Problem(result.Error!);
                })
            .RequireAuthorization()
            .AddEndpointFilter(PermissionFilter)
            .Produces<DossierCodeFormatAdminResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithName("ActivateAdminDossierCodeFormat");

        return endpoints;
    }

    private static async ValueTask<object?> PermissionFilter(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var permissions = context.HttpContext.RequestServices.GetRequiredService<IPermissionService>();
        if (!permissions.Has(Permission))
        {
            return Results.Problem(
                title: "Nuk keni leje për këtë veprim.",
                statusCode: StatusCodes.Status403Forbidden,
                extensions: new Dictionary<string, object?> { ["code"] = "permission.denied" });
        }

        return await next(context);
    }

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
