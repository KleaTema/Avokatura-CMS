using Ash.AspNetCore.Authorization;
using Ash.AspNetCore.Http;
using Ash.Modules.Identity.Application.Imports;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Ash.Modules.Identity.Endpoints;

/// <summary>
/// The Excel / CSV imports (task 053l, the shared pipeline): per kind — /organika (admin.org, task 053g) and /users
/// (admin.users) — the definition (the page's instructions), the template, upload → preview, the kept imports, "Apliko",
/// and the result report.
/// </summary>
public static class ImportEndpoints
{
    private static readonly (string Route, Type Kind, string Permission)[] Kinds =
    [
        ("organika", typeof(OrganikaImport), "admin.org"),
        ("users", typeof(UsersImport), "admin.users"),
    ];

    public static IEndpointRouteBuilder MapImports(this IEndpointRouteBuilder endpoints)
    {
        foreach (var (route, kindType, permission) in Kinds)
        {
            var group = endpoints.MapGroup($"/api/identity/admin/imports/{route}");
            var name = char.ToUpperInvariant(route[0]) + route[1..];
            IImportKind Kind(HttpContext context) => (IImportKind)context.RequestServices.GetRequiredService(kindType);

            group.MapGet("/definition", (HttpContext context) => Results.Ok(ImportPipeline.Describe(Kind(context))))
                .RequirePermission(permission)
                .Produces<ImportDefinitionView>()
                .WithName($"Get{name}ImportDefinition");

            group.MapGet("/template", (HttpContext context) =>
                {
                    var definition = Kind(context).Definition;
                    return Results.File(definition.Template(), ImportDefinition.XlsxContentType, definition.TemplateFileName);
                })
                .RequirePermission(permission)
                .Produces(StatusCodes.Status200OK, contentType: ImportDefinition.XlsxContentType)
                .WithName($"Get{name}ImportTemplate");

            // the form is read by the pipeline (a sheet of at most 5 MB): no IFormFile binding
            group.MapPost("/", async (HttpContext context, ImportPipeline pipeline, CancellationToken ct) =>
                {
                    var result = await pipeline.UploadAsync(Kind(context), context.Request, ct);
                    return result.IsSuccess ? Results.Json(result.Value, statusCode: StatusCodes.Status201Created) : result.Error!.ToProblem();
                })
                .RequirePermission(permission)
                .Accepts<IFormFile>("multipart/form-data")
                .Produces<ImportView>(StatusCodes.Status201Created)
                .ProducesValidationProblem()
                .WithName($"Upload{name}Import");

            group.MapGet("/", async (HttpContext context, ImportPipeline pipeline, CancellationToken ct) =>
                    Results.Ok(await pipeline.ListAsync(Kind(context), ct)))
                .RequirePermission(permission)
                .Produces<IReadOnlyList<ImportListItem>>()
                .WithName($"List{name}Imports");

            group.MapGet("/{publicId:guid}", async (Guid publicId, HttpContext context, ImportPipeline pipeline, CancellationToken ct) =>
                {
                    var result = await pipeline.GetAsync(Kind(context), publicId, ct);
                    return result.IsSuccess ? Results.Ok(result.Value) : result.Error!.ToProblem();
                })
                .RequirePermission(permission)
                .Produces<ImportView>()
                .ProducesProblem(StatusCodes.Status404NotFound)
                .WithName($"Get{name}Import");

            group.MapPost("/{publicId:guid}/apply", async (Guid publicId, HttpContext context, ImportPipeline pipeline, CancellationToken ct) =>
                {
                    var result = await pipeline.ApplyAsync(Kind(context), publicId, ct);
                    return result.IsSuccess ? Results.Ok(result.Value) : result.Error!.ToProblem();
                })
                .RequirePermission(permission)
                .Produces<ImportView>()
                .ProducesProblem(StatusCodes.Status404NotFound)
                .ProducesProblem(StatusCodes.Status409Conflict)
                .WithName($"Apply{name}Import");

            group.MapGet("/{publicId:guid}/report", async (Guid publicId, HttpContext context, ImportPipeline pipeline, CancellationToken ct) =>
                {
                    var result = await pipeline.ReportAsync(Kind(context), publicId, ct);
                    return result.IsSuccess
                        ? Results.File(result.Value.Content, ImportDefinition.XlsxContentType, result.Value.FileName)
                        : result.Error!.ToProblem();
                })
                .RequirePermission(permission)
                .Produces(StatusCodes.Status200OK, contentType: ImportDefinition.XlsxContentType)
                .ProducesProblem(StatusCodes.Status404NotFound)
                .WithName($"Get{name}ImportReport");
        }

        return endpoints;
    }
}
