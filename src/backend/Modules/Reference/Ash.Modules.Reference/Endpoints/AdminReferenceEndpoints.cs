using Ash.AspNetCore.Authorization;
using Ash.AspNetCore.Http;
using Ash.Modules.Reference.Application.Admin;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Ash.Modules.Reference.Endpoints;

public static class AdminReferenceEndpoints
{
    private const string Permission = "admin.lookups";

    public static IEndpointRouteBuilder MapAdminReference(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/reference/admin");

        group.MapGet("/hearing-types", async (ListHearingTypesAdminHandler handler, CancellationToken ct) =>
                Results.Ok(await handler.Handle(ct)))
            .RequirePermission(Permission)
            .Produces<IReadOnlyList<HearingTypeAdminResponse>>()
            .WithName("ListAdminHearingTypes");

        group.MapPost("/hearing-types", async (CreateHearingTypeRequest req, CreateHearingTypeHandler handler, CancellationToken ct) =>
                {
                    var result = await handler.Handle(req, ct);
                    return result.IsSuccess ? Results.Created($"/api/reference/admin/hearing-types/{result.Value.Code}", result.Value)
                        : result.Error!.ToProblem();
                })
            .RequirePermission(Permission)
            .Produces<HearingTypeAdminResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .WithName("CreateAdminHearingType");

        group.MapPut("/hearing-types/{code}", async (string code, UpdateHearingTypeRequest req, UpdateHearingTypeHandler handler, CancellationToken ct) =>
                {
                    var result = await handler.Handle(code, req, ct);
                    return result.IsSuccess ? Results.Ok(result.Value) : result.Error!.ToProblem();
                })
            .RequirePermission(Permission)
            .Produces<HearingTypeAdminResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithName("UpdateAdminHearingType");

        group.MapPost("/hearing-types/{code}/deactivate", async (string code, SetHearingTypeActiveHandler handler, CancellationToken ct) =>
                {
                    var result = await handler.Handle(code, false, ct);
                    return result.IsSuccess ? Results.Ok(result.Value) : result.Error!.ToProblem();
                })
            .RequirePermission(Permission)
            .Produces<HearingTypeAdminResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithName("DeactivateAdminHearingType");

        group.MapPost("/hearing-types/{code}/activate", async (string code, SetHearingTypeActiveHandler handler, CancellationToken ct) =>
                {
                    var result = await handler.Handle(code, true, ct);
                    return result.IsSuccess ? Results.Ok(result.Value) : result.Error!.ToProblem();
                })
            .RequirePermission(Permission)
            .Produces<HearingTypeAdminResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithName("ActivateAdminHearingType");

        group.MapGet("/file-types", async (ListFileTypesAdminHandler handler, CancellationToken ct) =>
                Results.Ok(await handler.Handle(ct)))
            .RequirePermission(Permission)
            .Produces<IReadOnlyList<FileTypeAdminResponse>>()
            .WithName("ListAdminFileTypes");

        group.MapPost("/file-types", async (CreateFileTypeRequest req, CreateFileTypeHandler handler, CancellationToken ct) =>
                {
                    var result = await handler.Handle(req, ct);
                    return result.IsSuccess ? Results.Created($"/api/reference/admin/file-types/{result.Value.Id}", result.Value)
                        : result.Error!.ToProblem();
                })
            .RequirePermission(Permission)
            .Produces<FileTypeAdminResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .WithName("CreateAdminFileType");

        group.MapPut("/file-types/{id:int:range(1,255)}", async (int id, UpdateFileTypeRequest req, UpdateFileTypeHandler handler, CancellationToken ct) =>
                {
                    var result = await handler.Handle((byte)id, req, ct);
                    return result.IsSuccess ? Results.Ok(result.Value) : result.Error!.ToProblem();
                })
            .RequirePermission(Permission)
            .Produces<FileTypeAdminResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithName("UpdateAdminFileType");

        group.MapGet("/dossier-code-formats", async (ListDossierCodeFormatsAdminHandler handler, CancellationToken ct) =>
                Results.Ok(await handler.Handle(ct)))
            .RequirePermission(Permission)
            .Produces<IReadOnlyList<DossierCodeFormatAdminResponse>>()
            .WithName("ListAdminDossierCodeFormats");

        group.MapPost("/dossier-code-formats", async (CreateDossierCodeFormatRequest req, CreateDossierCodeFormatHandler handler, CancellationToken ct) =>
                {
                    var result = await handler.Handle(req, ct);
                    return result.IsSuccess ? Results.Created($"/api/reference/admin/dossier-code-formats/{result.Value.Id}", result.Value)
                        : result.Error!.ToProblem();
                })
            .RequirePermission(Permission)
            .Produces<DossierCodeFormatAdminResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .WithName("CreateAdminDossierCodeFormat");

        group.MapPost("/dossier-code-formats/{id:int:range(1,255)}/activate", async (int id, ActivateDossierCodeFormatHandler handler, CancellationToken ct) =>
                {
                    var result = await handler.Handle((byte)id, ct);
                    return result.IsSuccess ? Results.Ok(result.Value) : result.Error!.ToProblem();
                })
            .RequirePermission(Permission)
            .Produces<DossierCodeFormatAdminResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithName("ActivateAdminDossierCodeFormat");

        group.MapGet("/party-roles", async (ListPartyRolesAdminHandler handler, CancellationToken ct) =>
                Results.Ok(await handler.Handle(ct)))
            .RequirePermission(Permission)
            .Produces<IReadOnlyList<PartyRoleAdminResponse>>()
            .WithName("ListAdminPartyRoles");

        group.MapPost("/party-roles", async (CreatePartyRoleRequest req, CreatePartyRoleHandler handler, CancellationToken ct) =>
                {
                    var result = await handler.Handle(req, ct);
                    return result.IsSuccess ? Results.Created($"/api/reference/admin/party-roles/{result.Value.Code}", result.Value)
                        : result.Error!.ToProblem();
                })
            .RequirePermission(Permission)
            .Produces<PartyRoleAdminResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .WithName("CreateAdminPartyRole");

        group.MapPut("/party-roles/{code}", async (string code, UpdatePartyRoleRequest req, UpdatePartyRoleHandler handler, CancellationToken ct) =>
                {
                    var result = await handler.Handle(code, req, ct);
                    return result.IsSuccess ? Results.Ok(result.Value) : result.Error!.ToProblem();
                })
            .RequirePermission(Permission)
            .Produces<PartyRoleAdminResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithName("UpdateAdminPartyRole");

        group.MapPost("/party-roles/{code}/deactivate", async (string code, SetPartyRoleActiveHandler handler, CancellationToken ct) =>
                {
                    var result = await handler.Handle(code, false, ct);
                    return result.IsSuccess ? Results.Ok(result.Value) : result.Error!.ToProblem();
                })
            .RequirePermission(Permission)
            .Produces<PartyRoleAdminResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithName("DeactivateAdminPartyRole");

        group.MapPost("/party-roles/{code}/activate", async (string code, SetPartyRoleActiveHandler handler, CancellationToken ct) =>
                {
                    var result = await handler.Handle(code, true, ct);
                    return result.IsSuccess ? Results.Ok(result.Value) : result.Error!.ToProblem();
                })
            .RequirePermission(Permission)
            .Produces<PartyRoleAdminResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithName("ActivateAdminPartyRole");

        group.MapGet("/summons-types", async (ListSummonsTypesAdminHandler handler, CancellationToken ct) =>
                Results.Ok(await handler.Handle(ct)))
            .RequirePermission(Permission)
            .Produces<IReadOnlyList<SummonsTypeAdminResponse>>()
            .WithName("ListAdminSummonsTypes");

        group.MapPost("/summons-types", async (CreateSummonsTypeRequest req, CreateSummonsTypeHandler handler, CancellationToken ct) =>
                {
                    var result = await handler.Handle(req, ct);
                    return result.IsSuccess ? Results.Created($"/api/reference/admin/summons-types/{result.Value.Code}", result.Value)
                        : result.Error!.ToProblem();
                })
            .RequirePermission(Permission)
            .Produces<SummonsTypeAdminResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .WithName("CreateAdminSummonsType");

        group.MapPut("/summons-types/{code}", async (string code, UpdateSummonsTypeRequest req, UpdateSummonsTypeHandler handler, CancellationToken ct) =>
                {
                    var result = await handler.Handle(code, req, ct);
                    return result.IsSuccess ? Results.Ok(result.Value) : result.Error!.ToProblem();
                })
            .RequirePermission(Permission)
            .Produces<SummonsTypeAdminResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithName("UpdateAdminSummonsType");

        group.MapPost("/summons-types/{code}/deactivate", async (string code, SetSummonsTypeActiveHandler handler, CancellationToken ct) =>
                {
                    var result = await handler.Handle(code, false, ct);
                    return result.IsSuccess ? Results.Ok(result.Value) : result.Error!.ToProblem();
                })
            .RequirePermission(Permission)
            .Produces<SummonsTypeAdminResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithName("DeactivateAdminSummonsType");

        group.MapPost("/summons-types/{code}/activate", async (string code, SetSummonsTypeActiveHandler handler, CancellationToken ct) =>
                {
                    var result = await handler.Handle(code, true, ct);
                    return result.IsSuccess ? Results.Ok(result.Value) : result.Error!.ToProblem();
                })
            .RequirePermission(Permission)
            .Produces<SummonsTypeAdminResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithName("ActivateAdminSummonsType");

        group.MapGet("/document-types", async (ListDocumentTypesAdminHandler handler, CancellationToken ct) =>
                Results.Ok(await handler.Handle(ct)))
            .RequirePermission(Permission)
            .Produces<IReadOnlyList<DocumentTypeAdminResponse>>()
            .WithName("ListAdminDocumentTypes");

        group.MapPost("/document-types", async (CreateDocumentTypeRequest req, CreateDocumentTypeHandler handler, CancellationToken ct) =>
                {
                    var result = await handler.Handle(req, ct);
                    return result.IsSuccess ? Results.Created($"/api/reference/admin/document-types/{result.Value.Code}", result.Value)
                        : result.Error!.ToProblem();
                })
            .RequirePermission(Permission)
            .Produces<DocumentTypeAdminResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .WithName("CreateAdminDocumentType");

        group.MapPut("/document-types/{code}", async (string code, UpdateDocumentTypeRequest req, UpdateDocumentTypeHandler handler, CancellationToken ct) =>
                {
                    var result = await handler.Handle(code, req, ct);
                    return result.IsSuccess ? Results.Ok(result.Value) : result.Error!.ToProblem();
                })
            .RequirePermission(Permission)
            .Produces<DocumentTypeAdminResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithName("UpdateAdminDocumentType");

        group.MapPost("/document-types/{code}/deactivate", async (string code, SetDocumentTypeActiveHandler handler, CancellationToken ct) =>
                {
                    var result = await handler.Handle(code, false, ct);
                    return result.IsSuccess ? Results.Ok(result.Value) : result.Error!.ToProblem();
                })
            .RequirePermission(Permission)
            .Produces<DocumentTypeAdminResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithName("DeactivateAdminDocumentType");

        group.MapPost("/document-types/{code}/activate", async (string code, SetDocumentTypeActiveHandler handler, CancellationToken ct) =>
                {
                    var result = await handler.Handle(code, true, ct);
                    return result.IsSuccess ? Results.Ok(result.Value) : result.Error!.ToProblem();
                })
            .RequirePermission(Permission)
            .Produces<DocumentTypeAdminResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithName("ActivateAdminDocumentType");

        group.MapGet("/decision-results", async (ListDecisionResultsAdminHandler handler, CancellationToken ct) =>
                Results.Ok(await handler.Handle(ct)))
            .RequirePermission(Permission)
            .Produces<IReadOnlyList<DecisionResultAdminResponse>>()
            .WithName("ListAdminDecisionResults");

        group.MapPost("/decision-results", async (CreateDecisionResultRequest req, CreateDecisionResultHandler handler, CancellationToken ct) =>
                {
                    var result = await handler.Handle(req, ct);
                    return result.IsSuccess ? Results.Created($"/api/reference/admin/decision-results/{result.Value.Code}", result.Value)
                        : result.Error!.ToProblem();
                })
            .RequirePermission(Permission)
            .Produces<DecisionResultAdminResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .WithName("CreateAdminDecisionResult");

        group.MapPut("/decision-results/{code}", async (string code, UpdateDecisionResultRequest req, UpdateDecisionResultHandler handler, CancellationToken ct) =>
                {
                    var result = await handler.Handle(code, req, ct);
                    return result.IsSuccess ? Results.Ok(result.Value) : result.Error!.ToProblem();
                })
            .RequirePermission(Permission)
            .Produces<DecisionResultAdminResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithName("UpdateAdminDecisionResult");

        group.MapPost("/decision-results/{code}/deactivate", async (string code, SetDecisionResultActiveHandler handler, CancellationToken ct) =>
                {
                    var result = await handler.Handle(code, false, ct);
                    return result.IsSuccess ? Results.Ok(result.Value) : result.Error!.ToProblem();
                })
            .RequirePermission(Permission)
            .Produces<DecisionResultAdminResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithName("DeactivateAdminDecisionResult");

        group.MapPost("/decision-results/{code}/activate", async (string code, SetDecisionResultActiveHandler handler, CancellationToken ct) =>
                {
                    var result = await handler.Handle(code, true, ct);
                    return result.IsSuccess ? Results.Ok(result.Value) : result.Error!.ToProblem();
                })
            .RequirePermission(Permission)
            .Produces<DecisionResultAdminResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithName("ActivateAdminDecisionResult");

        return endpoints;
    }
}
