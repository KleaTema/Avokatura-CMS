using Ash.AspNetCore.Http;
using Ash.Modules.Documents.Application.Upload;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Ash.Modules.Documents.Endpoints;

/// <summary>Documents (task 033). document.upload is a CASE permission: checked in the handler (effective permissions).</summary>
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

        return endpoints;
    }
}
