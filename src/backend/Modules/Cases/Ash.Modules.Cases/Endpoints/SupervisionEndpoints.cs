using Ash.AspNetCore.Authorization;
using Ash.AspNetCore.Http;
using Ash.Modules.Cases.Application.Supervision;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Ash.Modules.Cases.Endpoints;

/// <summary>"Mbikëqyrja" (task 053d): the Monitor's screen — supervision.manage.</summary>
public static class SupervisionEndpoints
{
    public const string Permission = "supervision.manage";

    public static IEndpointRouteBuilder MapSupervisions(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/supervisions");

        group.MapGet("", async (bool? includeEnded, SupervisionHandlers handler, CancellationToken ct) =>
                Results.Ok(await handler.ListAsync(includeEnded == true, ct)))
            .RequirePermission(Permission)
            .Produces<IReadOnlyList<SupervisionItem>>()
            .WithName("ListSupervisions");

        group.MapGet("/people", async (SupervisionHandlers handler, CancellationToken ct) => Results.Ok(await handler.PeopleAsync(ct)))
            .RequirePermission(Permission)
            .Produces<SupervisionPeople>()
            .WithName("ListSupervisionPeople");

        group.MapPost("", async (CreateSupervisionRequest req, SupervisionHandlers handler, CancellationToken ct) =>
                {
                    var result = await handler.CreateAsync(req, ct);
                    return result.IsSuccess
                        ? Results.Created($"/api/supervisions/{result.Value.PublicId}", result.Value)
                        : result.Error!.ToProblem();
                })
            .RequirePermission(Permission)
            .Produces<SupervisionItem>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithName("CreateSupervision");

        group.MapPost("/{publicId:guid}/end", async (Guid publicId, SupervisionHandlers handler, CancellationToken ct) =>
                {
                    var result = await handler.EndAsync(publicId, ct);
                    return result.IsSuccess ? Results.Ok(result.Value) : result.Error!.ToProblem();
                })
            .RequirePermission(Permission)
            .Produces<SupervisionItem>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithName("EndSupervision");

        return endpoints;
    }
}
