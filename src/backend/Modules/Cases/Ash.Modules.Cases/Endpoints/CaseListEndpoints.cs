using Ash.AspNetCore.Http;
using Ash.Modules.Cases.Application.ListCases;
using Ash.SharedKernel.Paging;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Ash.Modules.Cases.Endpoints;

/// <summary>The case list (task 019). Which tabs a user gets is decided by the handler (403 for another tab).</summary>
public static class CaseListEndpoints
{
    public static IEndpointRouteBuilder MapCaseList(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/cases",
                async (string? tab, string? status, int? courtId, string? categoryCode, string? priority, bool? suspended,
                    DateOnly? hearingFrom, DateOnly? hearingTo, Guid? lawyerId, string? q, string? after, int? limit,
                    ListCasesHandler handler, CancellationToken ct) =>
                {
                    var result = await handler.Handle(new ListCasesQuery(tab, status, courtId, categoryCode, priority, suspended,
                        hearingFrom, hearingTo, lawyerId, q, after, limit), ct);
                    return result.IsSuccess ? Results.Ok(result.Value) : result.Error!.ToProblem();
                })
            .RequireAuthorization()
            .Produces<KeysetPage<CaseListItem>>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .WithName("ListCases");

        endpoints.MapGet("/api/cases/tabs", async (CaseTabsHandler handler, CancellationToken ct) =>
                Results.Ok(await handler.Handle(ct)))
            .RequireAuthorization()
            .Produces<CaseTabsResponse>()
            .WithName("ListCaseTabs");

        return endpoints;
    }
}
