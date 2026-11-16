using Ash.AspNetCore.Http;
using Ash.Modules.Reports.Application;
using Ash.Modules.Reports.Application.ActiveCases;
using Ash.Modules.Reports.Application.Builder;
using Ash.Modules.Reports.Application.ClosedCases;
using Ash.Modules.Reports.Application.Statistics;
using Ash.Modules.Reports.Export;
using Ash.SharedKernel.Results;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Ash.Modules.Reports.Endpoints;

/// <summary>The builder's query: the filter values (a JSON object, see ReportFilterValues), the cursor and page size.</summary>
public sealed record BuilderQuery(string? Filters, string? After, int? Limit);

/// <summary>The builder's export: format xlsx | csv | pdf, the filter values, the chosen columns ("a,b,c", in order).</summary>
public sealed record BuilderExportQuery(string? Format, string? Filters, string? Columns);

/// <summary>A page of Tabela 1 / 3 and (first page only) the totals row over every row of the filters.</summary>
public sealed record BuilderPage<T>(ReportPage<T> Page, ReportTotals? Totals);

/// <summary>
/// Task 053h: the report builder — one filter metadata endpoint per table, the results (nothing runs before the UI's
/// "Gjenero"), the exports in three formats and "Raportet e mia". The tables: 1 (active), 3 (closed), 5 / 6
/// (statistics). Access: ReportAudience (the reports' 403, the statistics' 403 for a secondary-only lawyer).
/// </summary>
public static class ReportBuilderEndpoints
{
    public static IEndpointRouteBuilder MapReportBuilder(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/reports/builder").RequireAuthorization();

        group.MapGet("/{table:int}/filters", async (int table, string? filters, CaseReportBuilder cases, StatisticsBuilder statistics,
                CancellationToken ct) =>
            {
                if (!ReportTables.IsKnown(table))
                {
                    return UnknownTable();
                }

                if (ReportFilterValues.Parse(filters) is not { } values)
                {
                    return InvalidFilters();
                }

                var r = table is ReportTables.Active or ReportTables.Closed
                    ? await cases.MetaAsync(table, values, ct)
                    : await statistics.MetaAsync(table, values, ct);
                return r.IsSuccess ? Results.Ok(r.Value) : r.Error!.ToProblem();
            })
            .Produces<ReportFiltersResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .WithName("ReportBuilderFilters");

        group.MapGet("/active", async ([AsParameters] BuilderQuery q, ActiveCasesReport report, CancellationToken ct) =>
            {
                if (Criteria(ReportTables.Active, q.Filters) is not { IsSuccess: true } criteria)
                {
                    return Problem(ReportTables.Active, q.Filters);
                }

                var f = Empty.Active with { Criteria = criteria.Value };
                var page = await report.PageAsync(f, q.After, q.Limit, ct);
                if (page.IsFailure)
                {
                    return page.Error!.ToProblem();
                }

                var totals = q.After is null ? await report.TotalsAsync(f, ct) : null;
                return Results.Ok(new BuilderPage<ActiveCaseRow>(page.Value, totals));
            })
            .Produces<BuilderPage<ActiveCaseRow>>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .WithName("ReportBuilderActive");

        group.MapGet("/closed", async ([AsParameters] BuilderQuery q, ClosedCasesReport report, CancellationToken ct) =>
            {
                if (Criteria(ReportTables.Closed, q.Filters) is not { IsSuccess: true } criteria)
                {
                    return Problem(ReportTables.Closed, q.Filters);
                }

                var f = Empty.Closed with { Criteria = criteria.Value };
                var page = await report.PageAsync(f, q.After, q.Limit, ct);
                if (page.IsFailure)
                {
                    return page.Error!.ToProblem();
                }

                var totals = q.After is null ? await report.TotalsAsync(f, ct) : null;
                return Results.Ok(new BuilderPage<ClosedCaseRow>(page.Value, totals));
            })
            .Produces<BuilderPage<ClosedCaseRow>>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .WithName("ReportBuilderClosed");

        group.MapGet("/statistics/{table:int}", async (int table, string? filters, StatisticsQueries statistics, CancellationToken ct) =>
            {
                if (table is not (ReportTables.Statistics or ReportTables.Institutional))
                {
                    return UnknownTable();
                }

                if (ReportFilterValues.Parse(filters) is not { } values)
                {
                    return InvalidFilters();
                }

                var query = StatisticsBuilder.Query(table, values);
                if (query.IsFailure)
                {
                    return query.Error!.ToProblem();
                }

                var r = await statistics.GetAsync(query.Value, ct);
                return r.IsSuccess ? Results.Ok(r.Value) : r.Error!.ToProblem();
            })
            .Produces<StatisticsResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .WithName("ReportBuilderStatistics");

        group.MapGet("/{table:int}/export", async (int table, [AsParameters] BuilderExportQuery q, ActiveCasesReport active,
                ClosedCasesReport closed, ReportExports exports, StatisticsExport statisticsExport, CancellationToken ct) =>
            {
                if (!ReportTables.IsKnown(table))
                {
                    return UnknownTable();
                }

                var format = string.IsNullOrWhiteSpace(q.Format) ? ExportFormats.Xlsx : q.Format.Trim().ToLowerInvariant();
                if (!ExportFormats.IsKnown(format))
                {
                    return ExportFormats.Unknown.ToProblem();
                }

                if (ReportFilterValues.Parse(q.Filters) is not { } values)
                {
                    return InvalidFilters();
                }

                var keys = string.IsNullOrWhiteSpace(q.Columns)
                    ? null
                    : q.Columns.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

                if (table is ReportTables.Statistics or ReportTables.Institutional)
                {
                    var query = StatisticsBuilder.Query(table, values);
                    return query.IsFailure
                        ? query.Error!.ToProblem()
                        : await statisticsExport.RunAsync(query.Value, ct, format, keys?.Where(k => k != "court").ToList());
                }

                var criteria = CaseReportBuilder.Criteria(table, values);
                if (criteria.IsFailure)
                {
                    return criteria.Error!.ToProblem();
                }

                var all = table == ReportTables.Closed ? ReportColumns.Closed : ReportColumns.Active;
                var columns = Columns(all, keys);
                if (columns is null)
                {
                    return Error.Validation("report.columns_invalid", "Kolonat nuk janë të vlefshme.").ToProblem();
                }

                if (table == ReportTables.Closed)
                {
                    var f = Empty.Closed with { Criteria = criteria.Value };
                    return await exports.RunAsync(new ExportRequest(
                        ReportColumns.ClosedReportName, "Çështjet e mbyllura", "raporti-ceshtjet-mbyllura", columns,
                        token => closed.CountAsync(f, token),
                        token => closed.AllAsync(f, token),
                        token => closed.DescribeAsync(f, token)) { Format = format, Totals = token => closed.TotalsAsync(f, token) }, ct);
                }

                var a = Empty.Active with { Criteria = criteria.Value };
                return await exports.RunAsync(new ExportRequest(
                    ReportColumns.ActiveReportName, "Çështjet aktive", "raporti-ceshtjet-aktive", columns,
                    token => active.CountAsync(a, token),
                    token => active.AllAsync(a, token),
                    token => active.DescribeAsync(a, token)) { Format = format, Totals = token => active.TotalsAsync(a, token) }, ct);
            })
            .Produces(StatusCodes.Status200OK, contentType: ReportExports.ContentType)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .WithName("ExportReportBuilder");

        // "Raportet e mia"
        var saved = endpoints.MapGroup("/api/reports/saved").RequireAuthorization();
        saved.MapGet("/", async (SavedReports reports, ReportScope scope, CancellationToken ct) =>
                await scope.DeniedAsync(ct) is { } denied ? denied.ToProblem() : Results.Ok(await reports.ListAsync(ct)))
            .Produces<IReadOnlyList<SavedReportView>>()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .WithName("ListSavedReports");
        saved.MapPost("/", async (SaveReportRequest req, SavedReports reports, ReportScope scope, CancellationToken ct) =>
            {
                if (await scope.DeniedAsync(ct) is { } denied)
                {
                    return denied.ToProblem();
                }

                var r = await reports.CreateAsync(req, ct);
                return r.IsSuccess ? Results.Created($"/api/reports/saved/{r.Value.PublicId}", r.Value) : r.Error!.ToProblem();
            })
            .Produces<SavedReportView>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithName("CreateSavedReport");
        saved.MapPut("/{id:guid}", async (Guid id, SaveReportRequest req, SavedReports reports, CancellationToken ct) =>
            {
                var r = await reports.UpdateAsync(id, req, ct);
                return r.IsSuccess ? Results.Ok(r.Value) : r.Error!.ToProblem();
            })
            .Produces<SavedReportView>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithName("UpdateSavedReport");
        saved.MapDelete("/{id:guid}", async (Guid id, SavedReports reports, CancellationToken ct) =>
            {
                var r = await reports.DeleteAsync(id, ct);
                return r.IsSuccess ? Results.NoContent() : r.Error!.ToProblem();
            })
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithName("DeleteSavedReport");

        return endpoints;
    }

    /// <summary>The chosen columns in their order (every column when none is chosen); null for an unknown key.</summary>
    private static IReadOnlyList<ReportColumn>? Columns(IReadOnlyList<ReportColumn> all, IReadOnlyList<string>? keys)
    {
        if (keys is null || keys.Count == 0)
        {
            return all;
        }

        var chosen = keys.Distinct().Select(k => all.FirstOrDefault(c => c.Key == k)).ToList();
        return chosen.Any(c => c is null) ? null : chosen.OfType<ReportColumn>().ToList();
    }

    private static Result<ReportCriteria>? Criteria(int table, string? filters) =>
        ReportFilterValues.Parse(filters) is { } values ? CaseReportBuilder.Criteria(table, values) : null;

    private static IResult Problem(int table, string? filters) =>
        ReportFilterValues.Parse(filters) is { } values
            ? CaseReportBuilder.Criteria(table, values).Error!.ToProblem()
            : InvalidFilters();

    private static IResult InvalidFilters() =>
        Error.Validation("report.filter_invalid", "Filtrat nuk janë të vlefshëm.").ToProblem();

    private static IResult UnknownTable() =>
        Error.NotFound("report.table_not_found", "Tabela nuk ekziston.").ToProblem();

    private static class Empty
    {
        public static readonly ActiveCasesFilter Active = new(null, null, null, null, null, null, null, null);
        public static readonly ClosedCasesFilter Closed = new(null, null, null, null, null, null, null);
    }
}
