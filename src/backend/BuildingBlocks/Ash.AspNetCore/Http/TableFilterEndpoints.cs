using Ash.SharedKernel.Filters;
using Ash.SharedKernel.Identity;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Ash.AspNetCore.Http;

/// <summary>
/// Task 053m: GET /api/filters/{table} — the filters of a table (one <see cref="ITableFilterSource"/> per table, owned by
/// its module) for the shared FilterBar. 404 for an unknown table, 403 without the source's permission. The query
/// string is passed on as the context (e.g. ?tab=office for the case list).
/// </summary>
public static class TableFilterEndpoints
{
    public static IEndpointRouteBuilder MapTableFilters(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/filters/{table}",
                async (string table, HttpRequest request, IEnumerable<ITableFilterSource> sources, ICurrentUser user, CancellationToken ct) =>
                {
                    var source = sources.FirstOrDefault(s => string.Equals(s.Table, table, StringComparison.OrdinalIgnoreCase));
                    if (source is null)
                    {
                        return Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Tabela nuk ekziston.");
                    }

                    if (source.Permission is { } permission && !user.HasPermission(permission))
                    {
                        return Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Nuk keni leje për këtë veprim.");
                    }

                    var context = request.Query.ToDictionary(q => q.Key, q => q.Value.ToString(), StringComparer.OrdinalIgnoreCase);
                    return Results.Ok(await source.DescribeAsync(context, ct));
                })
            .RequireAuthorization()
            .Produces<IReadOnlyList<TableFilter>>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithName("ListTableFilters");
        return endpoints;
    }
}
