using Ash.AspNetCore.Authorization;
using Ash.AspNetCore.Http;
using Ash.Modules.Identity.Application.Organika;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Ash.Modules.Identity.Endpoints;

/// <summary>"Organika" (task 053g, Administrimi): template, upload → preview, the kept imports, "Apliko". admin.org.</summary>
public static class OrganikaEndpoints
{
    private const string Permission = "admin.org";

    public static IEndpointRouteBuilder MapOrganika(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/identity/admin/organika");

        group.MapGet("/template", () => Results.File(OrgSheet.Template(), OrgSheet.XlsxContentType, OrgSheet.TemplateFileName))
            .RequirePermission(Permission)
            .Produces(StatusCodes.Status200OK, contentType: OrgSheet.XlsxContentType)
            .WithName("GetOrganikaTemplate");

        // the form is read by the handler (a sheet of at most 2 MB): no IFormFile binding
        group.MapPost("/imports", async (HttpRequest request, OrgImportHandlers handler, CancellationToken ct) =>
                {
                    var result = await handler.UploadAsync(request, ct);
                    return result.IsSuccess ? Results.Json(result.Value, statusCode: StatusCodes.Status201Created) : result.Error!.ToProblem();
                })
            .RequirePermission(Permission)
            .Accepts<IFormFile>("multipart/form-data")
            .Produces<OrgImportView>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .WithName("UploadOrganikaImport");

        group.MapGet("/imports", async (OrgImportHandlers handler, CancellationToken ct) => Results.Ok(await handler.ListAsync(ct)))
            .RequirePermission(Permission)
            .Produces<IReadOnlyList<OrgImportListItem>>()
            .WithName("ListOrganikaImports");

        group.MapGet("/imports/{publicId:guid}", async (Guid publicId, OrgImportHandlers handler, CancellationToken ct) =>
                {
                    var result = await handler.GetAsync(publicId, ct);
                    return result.IsSuccess ? Results.Ok(result.Value) : result.Error!.ToProblem();
                })
            .RequirePermission(Permission)
            .Produces<OrgImportView>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithName("GetOrganikaImport");

        group.MapPost("/imports/{publicId:guid}/apply", async (Guid publicId, OrgImportHandlers handler, CancellationToken ct) =>
                {
                    var result = await handler.ApplyAsync(publicId, ct);
                    return result.IsSuccess ? Results.Ok(result.Value) : result.Error!.ToProblem();
                })
            .RequirePermission(Permission)
            .Produces<OrgImportView>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithName("ApplyOrganikaImport");

        return endpoints;
    }
}
