using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Ash.Modules.Cases.Application.RegistrationSearch.Filters;

/// <summary>What the page needs to render one filter (GET /api/cases/registration-search/filters).</summary>
public sealed record RegistrationFilterResponse(string Name, string Input, string? DependsOn);

/// <summary>
/// The registry of the registration-search filters: every non-abstract <see cref="RegistrationSearchFilter"/> of this
/// assembly, in <see cref="RegistrationSearchFilter.Order"/>. The endpoint reads them from the query string, the
/// OpenAPI document lists them as query parameters (so the generated client is typed), and the page renders them.
/// </summary>
internal static class RegistrationSearchFilters
{
    public static IReadOnlyList<RegistrationSearchFilter> All { get; } = typeof(RegistrationSearchFilter).Assembly.GetTypes()
        .Where(t => t is { IsAbstract: false, IsClass: true } && t.IsSubclassOf(typeof(RegistrationSearchFilter)))
        .Select(t => (RegistrationSearchFilter)Activator.CreateInstance(t, nonPublic: true)!)
        .OrderBy(f => f.Order)
        .ToList();

    public static IReadOnlyList<RegistrationFilterResponse> Describe() =>
        All.Select(f => new RegistrationFilterResponse(f.Name, f.Input.ToString(), f.DependsOn)).ToList();

    /// <summary>The non-empty values of the known filters (case-insensitive names); other parameters are ignored.</summary>
    public static IReadOnlyList<(RegistrationSearchFilter Filter, string Value)> FromQuery(IQueryCollection query) =>
        All.Select(f => (Filter: f, Value: query.FirstOrDefault(p => string.Equals(p.Key, f.Name, StringComparison.OrdinalIgnoreCase)).Value.ToString()))
            .Where(x => !string.IsNullOrWhiteSpace(x.Value))
            .ToList();

    /// <summary>Adds every filter to the operation as an optional query parameter.</summary>
    public static Task AddToOpenApi(OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken ct)
    {
        operation.Parameters ??= [];
        foreach (var filter in All)
        {
            operation.Parameters.Add(new OpenApiParameter
            {
                Name = filter.Name,
                In = ParameterLocation.Query,
                Required = false,
                Schema = filter.IsInteger
                    ? new OpenApiSchema { Type = JsonSchemaType.Integer, Format = "int32" }
                    : new OpenApiSchema { Type = JsonSchemaType.String }
            });
        }

        return Task.CompletedTask;
    }
}
