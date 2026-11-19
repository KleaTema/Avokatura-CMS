using Ash.AspNetCore.Authorization;
using Ash.AspNetCore.Http;
using Ash.Modules.Reports.Application.Statistics;
using Ash.SharedKernel.Identity;
using Ash.SharedKernel.Time;
using Ash.Modules.Reports.Application;
using Ash.Modules.Reports.Application.ActiveCases;
using Ash.Modules.Reports.Application.ClosedCases;
using Ash.Modules.Reports.Export;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Ash.Modules.Reports.Endpoints;

/// <summary>GET /api/reports/active-cases?officeId=&amp;lawyerId=&amp;courtId=&amp;categoryCode=&amp;courtLevelCode=&amp;priority=&amp;suspended=&amp;q=&amp;after=&amp;limit=</summary>
public sealed record ActiveCasesQuery(
    Guid? OfficeId, Guid? LawyerId, int? CourtId, string? CategoryCode, string? CourtLevelCode, string? Priority, bool? Suspended,
    string? Q, string? After, int? Limit)
{
    public ActiveCasesFilter Filter => new(OfficeId, LawyerId, CourtId, CategoryCode, CourtLevelCode, Priority, Suspended, Q);
}

/// <summary>GET /api/reports/closed-cases?closedFrom=&amp;closedTo= (Tirane dates)&amp;resultCode=&amp;outcomeGroup=&amp;courtId=&amp;lawyerId=&amp;officeId=&amp;after=&amp;limit=</summary>
public sealed record ClosedCasesQuery(
    DateOnly? ClosedFrom, DateOnly? ClosedTo, string? ResultCode, string? OutcomeGroup, int? CourtId, Guid? LawyerId, Guid? OfficeId,
    string? After, int? Limit)
{
    public ClosedCasesFilter Filter => new(ClosedFrom, ClosedTo, ResultCode, OutcomeGroup, CourtId, LawyerId, OfficeId);
}

/// <summary>POST /api/admin/statistics/snapshot/{year}: why the year is (re-)frozen — at least 10 characters, audited.</summary>
public sealed record SnapshotRequest(string? Reason);

/// <summary>Tasks 042/043: the two reports as JSON pages and as .xlsx exports (same filters, same rows).</summary>
public static class ReportEndpoints
{
    public static IEndpointRouteBuilder MapReports(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/reports").RequireAuthorization();

        group.MapGet("/active-cases", async ([AsParameters] ActiveCasesQuery q, ActiveCasesReport report, CancellationToken ct) =>
            {
                var r = await report.PageAsync(q.Filter, q.After, q.Limit, ct);
                return r.IsSuccess ? Results.Ok(r.Value) : Fail(r.Error!);
            })
            .Produces<ReportPage<ActiveCaseRow>>()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .WithName("ReportActiveCases");

        group.MapGet("/active-cases/export", async ([AsParameters] ActiveCasesQuery q, ActiveCasesReport report, ReportExports exports, CancellationToken ct) =>
                await exports.RunAsync(new ExportRequest(
                    // the API's whole export (every column); the page exports through the builder (its visible columns)
                    ReportColumns.ActiveReportName, "Çështjet aktive", "raporti-ceshtjet-aktive", ReportColumns.Active,
                    token => report.CountAsync(q.Filter, token),
                    token => report.AllAsync(q.Filter, token),
                    token => report.DescribeAsync(q.Filter, token)), ct))
            .Produces(StatusCodes.Status200OK, contentType: ReportExports.ContentType)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .WithName("ExportReportActiveCases");

        group.MapGet("/closed-cases", async ([AsParameters] ClosedCasesQuery q, ClosedCasesReport report, CancellationToken ct) =>
            {
                var r = await report.PageAsync(q.Filter, q.After, q.Limit, ct);
                return r.IsSuccess ? Results.Ok(r.Value) : Fail(r.Error!);
            })
            .Produces<ReportPage<ClosedCaseRow>>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .WithName("ReportClosedCases");

        group.MapGet("/closed-cases/export", async ([AsParameters] ClosedCasesQuery q, ClosedCasesReport report, ReportExports exports, CancellationToken ct) =>
            {
                if (q.ClosedFrom is { } from && q.ClosedTo is { } to && from > to)
                {
                    return Fail(Ash.SharedKernel.Results.Error.Validation("report.dates_invalid", "Data \"nga\" duhet të jetë para datës \"deri\"."));
                }

                return await exports.RunAsync(new ExportRequest(
                    ReportColumns.ClosedReportName, "Çështjet e mbyllura", "raporti-ceshtjet-mbyllura", ReportColumns.Closed,
                    token => report.CountAsync(q.Filter, token),
                    token => report.AllAsync(q.Filter, token),
                    token => report.DescribeAsync(q.Filter, token)), ct);
            })
            .Produces(StatusCodes.Status200OK, contentType: ReportExports.ContentType)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .WithName("ExportReportClosedCases");

        // task 044: Tabela 5 / 6, the drill-down, the export
        group.MapGet("/statistics", async ([AsParameters] StatisticsQuery q, StatisticsQueries statistics, CancellationToken ct) =>
            {
                var r = await statistics.GetAsync(q, ct);
                return r.IsSuccess ? Results.Ok(r.Value) : Fail(r.Error!);
            })
            .Produces<StatisticsResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .WithName("ReportStatistics");

        group.MapGet("/statistics/cases", async ([AsParameters] StatisticsCasesQuery q, StatisticsQueries statistics, CancellationToken ct) =>
            {
                var r = await statistics.CasesAsync(q, ct);
                return r.IsSuccess ? Results.Ok(r.Value) : Fail(r.Error!);
            })
            .Produces<StatisticsCasesPage>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .WithName("ReportStatisticsCases");

        group.MapGet("/statistics/export", async ([AsParameters] StatisticsQuery q, StatisticsExport export, CancellationToken ct) =>
                await export.RunAsync(q, ct))
            .Produces(StatusCodes.Status200OK, contentType: ReportExports.ContentType)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .WithName("ExportReportStatistics");

        // the admin's re-run of a closed year's snapshot (the yearly job takes it on 1 January)
        endpoints.MapPost("/api/admin/statistics/snapshot/{year:int}",
                async (int year, SnapshotRequest req, StatisticsSnapshots snapshots, ICurrentUser user, IClock clock, CancellationToken ct) =>
                {
                    var r = await snapshots.TakeByAdminAsync(year, req.Reason, user, clock, ct);
                    return r.IsSuccess ? Results.Ok(new { year, cases = r.Value }) : Fail(r.Error!);
                })
            .RequirePermission("menu.admin")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .WithName("TakeStatisticsSnapshot");

        return endpoints;
    }

    private static IResult Fail(Ash.SharedKernel.Results.Error error) => error.ToProblem();
}
