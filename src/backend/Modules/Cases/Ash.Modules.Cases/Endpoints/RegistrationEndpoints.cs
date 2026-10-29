using Ash.AspNetCore.Authorization;
using Ash.AspNetCore.Http;
using Ash.Modules.Cases.Application.RegisterCase;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Ash.Modules.Cases.Endpoints;

public static class RegistrationEndpoints
{
    public static IEndpointRouteBuilder MapCaseRegistration(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/cases/register", async (RegisterCaseRequest req, RegisterCaseHandler handler, CancellationToken ct) =>
                {
                    var result = await handler.Handle(req, ct);
                    return result.IsSuccess
                        ? Results.Created($"/api/cases/{result.Value.CasePublicId}", result.Value)
                        : result.Error!.ToProblem();
                })
            .RequirePermission("case.create")
            .Produces<RegisterCaseResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithName("RegisterCase");

        return endpoints;
    }
}
