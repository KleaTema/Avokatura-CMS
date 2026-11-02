using Ash.AspNetCore.Authorization;
using Ash.AspNetCore.Http;
using Ash.Modules.Cases.Application.Agenda;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Ash.Modules.Cases.Endpoints;

/// <summary>The agenda (task 031): whose hearings follows from the user's permissions (handler); menu.agenda to enter.</summary>
public static class AgendaEndpoints
{
    public static IEndpointRouteBuilder MapAgenda(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/agenda",
                async (DateOnly? from, DateOnly? to, Guid? lawyerId, Guid? officeId, bool? includeCancelled,
                    GetAgendaHandler handler, CancellationToken ct) =>
                {
                    var result = await handler.Handle(new AgendaQuery(from, to, lawyerId, officeId, includeCancelled), ct);
                    return result.IsSuccess ? Results.Ok(result.Value) : result.Error!.ToProblem();
                })
            .RequirePermission("menu.agenda")
            .Produces<IReadOnlyList<AgendaItem>>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .WithName("GetAgenda");

        return endpoints;
    }
}
