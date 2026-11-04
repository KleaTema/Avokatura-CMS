using Ash.AspNetCore.Http;
using Ash.Modules.Cases.Application.ArchiveDossier;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Ash.Modules.Cases.Endpoints;

/// <summary>
/// The dossier (task 038): archive it once its latest level has a final decision. Case permission decision.close_final
/// on the latest level, checked in the handler (no view → 404, wrong state → 409, missing → 403).
/// </summary>
public static class DossierEndpoints
{
    public static IEndpointRouteBuilder MapDossiers(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/dossiers/{dossierPublicId:guid}/archive",
                async (Guid dossierPublicId, ArchiveDossierRequest req, ArchiveDossierHandler handler, CancellationToken ct) =>
                {
                    var result = await handler.Handle(dossierPublicId, req, ct);
                    return result.IsSuccess ? Results.Ok(result.Value) : result.Error!.ToProblem();
                })
            .RequireAuthorization()
            .Produces<ArchiveDossierResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithName("ArchiveDossier");

        return endpoints;
    }
}
