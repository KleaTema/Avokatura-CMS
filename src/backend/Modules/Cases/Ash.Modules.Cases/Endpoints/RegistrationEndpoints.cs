using Ash.AspNetCore.Authorization;
using Ash.AspNetCore.Http;
using Ash.Modules.Cases.Application.RegisterCase;
using Ash.Modules.Cases.Application.RegisterNextLevel;
using Ash.Modules.Cases.Application.RegistrationSearch;
using Ash.Modules.Cases.Application.RegistrationSearch.Filters;
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

        // "Zyra që e ndjek" (task 053j): the offices, and the one covering the summons court
        endpoints.MapGet("/api/cases/registration/offices",
                async (int? courtId, RegistrationOfficesHandler handler, CancellationToken ct) =>
                {
                    var result = await handler.Handle(courtId, ct);
                    return result.IsSuccess ? Results.Ok(result.Value) : result.Error!.ToProblem();
                })
            .RequireAuthorization()
            .Produces<RegistrationOfficesResponse>()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .WithName("ListRegistrationOffices");

        // Find the existing dispute a new summons belongs to (task 018): a registration lookup, not case.view.
        // The filter parameters come from the registry (task 053b), read from the query string and added to OpenAPI.
        endpoints.MapGet("/api/cases/registration-search",
                async (HttpRequest request, string? after, int? limit, string? status, string? more, RegistrationSearchHandler handler,
                    CancellationToken ct) =>
                {
                    var filters = RegistrationSearchFilters.FromQuery(request.Query)
                        .Select(x => KeyValuePair.Create(x.Filter.Name, x.Value))
                        .ToList();
                    var result = await handler.Handle(new RegistrationSearchQuery(filters, after, limit, status, more), ct);
                    return result.IsSuccess ? Results.Ok(result.Value) : result.Error!.ToProblem();
                })
            .RequirePermission("case.link_previous")
            .AddOpenApiOperationTransformer(RegistrationSearchFilters.AddToOpenApi)
            .Produces<KeysetPage<RegistrationSearchItem>>()
            .ProducesValidationProblem()
            .WithName("SearchCasesForRegistration");

        // The fields of the search form, in order, with the control each one needs (the page renders them).
        endpoints.MapGet("/api/cases/registration-search/filters", () => Results.Ok(RegistrationSearchFilters.Describe()))
            .RequirePermission("case.link_previous")
            .Produces<IReadOnlyList<RegistrationFilterResponse>>()
            .WithName("ListRegistrationSearchFilters");

        // task 054d: the registration data of a case the Protokoll registered and can no longer open (its own tabs)
        endpoints.MapGet("/api/cases/{id:guid}/registration-summary",
                async (Guid id, Application.RegistrationSearch.RegistrationSummaryHandler handler, CancellationToken ct) =>
                {
                    var result = await handler.Handle(id, ct);
                    return result.IsSuccess ? Results.Ok(result.Value) : result.Error!.ToProblem();
                })
            .RequirePermission("case.create")
            .Produces<Application.RegistrationSearch.RegistrationSummaryResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithName("GetRegistrationSummary");

        // task 054c: the status choice of the search, with its names (the page has no list of its own)
        endpoints.MapGet("/api/cases/registration-search/statuses", () => Results.Ok(RegistrationSearchStatus.Options))
            .RequirePermission("case.link_previous")
            .Produces<IReadOnlyList<Ash.SharedKernel.Filters.FilterOption>>()
            .WithName("ListRegistrationSearchStatuses");

        // task 053j: "+ Shto filtër" — the report builder's registry (task 053h) over the country's latest levels; the
        // options of a select under the choices already made (more = the chosen values, the report builder's JSON)
        endpoints.MapGet("/api/cases/registration-search/more-filters",
                async (string? more, Ash.Modules.Reports.Contracts.ICaseFilterCatalog catalog, CancellationToken ct) =>
                {
                    var result = await catalog.DescribeLookupAsync(more, ct);
                    return result.IsSuccess ? Results.Ok(result.Value) : result.Error!.ToProblem();
                })
            .RequirePermission("case.link_previous")
            .Produces<IReadOnlyList<Ash.Modules.Reports.Contracts.CaseFilterField>>()
            .ProducesValidationProblem()
            .WithName("ListRegistrationSearchMoreFilters");

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

        // who the notification above would reach — shown in its dialog before sending
        endpoints.MapGet("/api/cases/{previousCasePublicId:guid}/notify-not-closed/recipients",
                async (Guid previousCasePublicId, NotifyNotClosedHandler handler, CancellationToken ct) =>
                {
                    var result = await handler.Recipients(previousCasePublicId, ct);
                    return result.IsSuccess ? Results.Ok(result.Value) : result.Error!.ToProblem();
                })
            .RequirePermission("case.link_previous")
            .Produces<NotifyRecipientsResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithName("NotifyPreviousLevelNotClosedRecipients");

        return endpoints;
    }
}
