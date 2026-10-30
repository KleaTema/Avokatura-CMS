using Ash.AspNetCore.Authorization;
using Ash.AspNetCore.Http;
using Ash.Modules.Cases.Application.RegisterCase;
using Ash.Modules.Cases.Application.RegisterNextLevel;
using Ash.Modules.Cases.Application.RegistrationSearch;
using Ash.SharedKernel.Paging;
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

        // Find the existing dispute a new summons belongs to (task 018): a registration lookup, not case.view.
        endpoints.MapGet("/api/cases/registration-search",
                async (string? q, int? courtId, string? after, int? limit, RegistrationSearchHandler handler, CancellationToken ct) =>
                {
                    var result = await handler.Handle(new RegistrationSearchQuery(q, courtId, after, limit), ct);
                    return result.IsSuccess ? Results.Ok(result.Value) : result.Error!.ToProblem();
                })
            .RequirePermission("case.link_previous")
            .Produces<KeysetPage<RegistrationSearchItem>>()
            .ProducesValidationProblem()
            .WithName("SearchCasesForRegistration");

        endpoints.MapPost("/api/cases/{previousCasePublicId:guid}/next-level",
                async (Guid previousCasePublicId, RegisterNextLevelRequest req, RegisterNextLevelHandler handler, CancellationToken ct) =>
                {
                    var result = await handler.Handle(previousCasePublicId, req, ct);
                    return result.IsSuccess
                        ? Results.Created($"/api/cases/{result.Value.CasePublicId}", result.Value)
                        : result.Error!.ToProblem();
                })
            .RequirePermission("case.link_previous")
            .Produces<RegisterCaseResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithName("RegisterNextLevel");

        // What continuing will copy (object, parties, institution links), shown read-only above the next-level form.
        endpoints.MapGet("/api/cases/{previousCasePublicId:guid}/next-level/preview",
                async (Guid previousCasePublicId, NextLevelPreviewHandler handler, CancellationToken ct) =>
                {
                    var result = await handler.Handle(previousCasePublicId, ct);
                    return result.IsSuccess ? Results.Ok(result.Value) : result.Error!.ToProblem();
                })
            .RequirePermission("case.link_previous")
            .Produces<NextLevelPreviewResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithName("PreviewNextLevel");

        // A next-level summons for a level not closed in the system: tell the lawyer and the Kryetar (at most once a day).
        endpoints.MapPost("/api/cases/{previousCasePublicId:guid}/notify-not-closed",
                async (Guid previousCasePublicId, NotifyNotClosedRequest req, NotifyNotClosedHandler handler, CancellationToken ct) =>
                {
                    var result = await handler.Handle(previousCasePublicId, req, ct);
                    return result.IsSuccess ? Results.Ok(result.Value) : result.Error!.ToProblem();
                })
            .RequirePermission("case.link_previous")
            .Produces<NotifyNotClosedResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithName("NotifyPreviousLevelNotClosed");

        return endpoints;
    }
}
