using Ash.AspNetCore.Http;
using Ash.Modules.Documents.Application.Delete;
using Ash.Modules.Documents.Application.Download;
using Ash.Modules.Documents.Application.List;
using Ash.Modules.Documents.Application.Upload;
using Ash.SharedKernel.Paging;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Ash.Modules.Documents.Endpoints;

/// <summary>
/// Documents (tasks 033, 034). document.upload / document.delete are CASE permissions: checked in the handlers
/// (effective permissions); viewing follows the dossier rule (ICaseAccessService.CanViewDossierDocumentsAsync).
/// </summary>
public static class DocumentEndpoints
{
    public static IEndpointRouteBuilder MapDocuments(this IEndpointRouteBuilder endpoints)
    {
        // multipart read by the handler itself (MultipartReader): no form binding, no buffering, no temp files
        endpoints.MapPost("/api/cases/{id:guid}/documents",
                async (Guid id, HttpRequest request, UploadDocumentHandler handler, CancellationToken ct) =>
                {
                    var result = await handler.Handle(id, request, ct);
                    return result.IsSuccess
                        ? Results.Json(result.Value, statusCode: StatusCodes.Status201Created)
                        : result.Error!.ToProblem();
                })
            .RequireAuthorization()
            .Accepts<IFormFile>("multipart/form-data")
            .Produces<UploadDocumentResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status413PayloadTooLarge)
            .WithName("UploadDocument");

        endpoints.MapGet("/api/cases/{id:guid}/documents",
                async (Guid id, string? type, int? level, string? after, int? limit, string? status,
                    ListDocumentsHandler handler, CancellationToken ct) =>
                {
                    var result = await handler.Handle(id, new ListDocumentsQuery(type, level, after, limit, status), ct);
                    return result.IsSuccess ? Results.Ok(result.Value) : result.Error!.ToProblem();
                })
            .RequireAuthorization()
            .Produces<KeysetPage<DocumentListItem>>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithName("ListDocuments");

        endpoints.MapGet("/api/documents/{docId:guid}/download",
                (Guid docId, HttpRequest request, DownloadDocumentHandler handler, CancellationToken ct) =>
                    Serve(docId, DownloadMode.Download, request, handler, ct))
            .RequireAuthorization()
            .Produces(StatusCodes.Status200OK, contentType: "application/octet-stream")
            .Produces(StatusCodes.Status206PartialContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithName("DownloadDocument");

        endpoints.MapGet("/api/documents/{docId:guid}/preview",
                (Guid docId, HttpRequest request, DownloadDocumentHandler handler, CancellationToken ct) =>
                    Serve(docId, DownloadMode.Preview, request, handler, ct))
            .RequireAuthorization()
            .Produces(StatusCodes.Status200OK, contentType: "application/pdf")
            .Produces(StatusCodes.Status206PartialContent)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithName("PreviewDocument");

        endpoints.MapDelete("/api/documents/{docId:guid}",
                // DELETE with a body (the reason): minimal APIs infer bodies only for POST/PUT/PATCH
                async (Guid docId, [FromBody] DeleteDocumentRequest req, DeleteDocumentHandler handler, CancellationToken ct) =>
                {
                    var result = await handler.Handle(docId, req, ct);
                    return result.IsSuccess ? Results.Ok(result.Value) : result.Error!.ToProblem();
                })
            .RequireAuthorization()
            .Produces<DeleteDocumentResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithName("DeleteDocument");

        return endpoints;
    }

    private static async Task<IResult> Serve(Guid docId, DownloadMode mode, HttpRequest request, DownloadDocumentHandler handler, CancellationToken ct)
    {
        var range = request.Headers.Range.ToString();
        var result = await handler.Handle(docId, mode, string.IsNullOrEmpty(range) ? null : range, ct);
        return result.IsSuccess ? result.Value! : result.Error!.ToProblem();
    }
}
