using Ash.AspNetCore.Http;
using Ash.Modules.Cases.Application.GetCase;
using Ash.Modules.Cases.Application.GetHistory;
using Ash.SharedKernel.Paging;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Ash.Modules.Cases.Endpoints;

/// <summary>The case page (task 020). Access is checked per case in the handlers: no view → 404.</summary>
public static class CaseDetailEndpoints
{
    public static IEndpointRouteBuilder MapCaseDetail(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/cases/{id:guid}", async (Guid id, GetCaseHandler handler, CancellationToken ct) =>
                {
                    var result = await handler.Handle(id, ct);
                    return result.IsSuccess ? Results.Ok(result.Value) : result.Error!.ToProblem();
                })
            .RequireAuthorization()
            .Produces<GetCaseResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithName("GetCase");

        endpoints.MapGet("/api/cases/{id:guid}/history",
                async (Guid id, string? after, int? limit, GetHistoryHandler handler, CancellationToken ct) =>
                {
                    var result = await handler.Handle(id, after, limit, ct);
                    return result.IsSuccess ? Results.Ok(result.Value) : result.Error!.ToProblem();
                })
            .RequireAuthorization()
            .Produces<KeysetPage<CaseHistoryItem>>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithName("GetCaseHistory");

        return endpoints;
    }
}
