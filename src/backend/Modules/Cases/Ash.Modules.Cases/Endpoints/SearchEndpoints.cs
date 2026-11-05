using Ash.AspNetCore.Http;
using Ash.Modules.Cases.Application.Search;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Ash.Modules.Cases.Endpoints;

/// <summary>Task 047: the header search. Any authenticated user; the results follow the user's case access.</summary>
public static class SearchEndpoints
{
    public static IEndpointRouteBuilder MapSearch(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/search",
                async (string? q, int? limit, GlobalSearchHandler handler, CancellationToken ct) =>
                {
                    var result = await handler.Handle(q, limit, ct);
                    return result.IsSuccess ? Results.Ok(result.Value) : result.Error!.ToProblem();
                })
            .RequireAuthorization()
            .Produces<SearchResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .WithName("SearchCases");
        return endpoints;
    }
}
