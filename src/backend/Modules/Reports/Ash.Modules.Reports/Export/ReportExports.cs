using Ash.AspNetCore.Http;
using Ash.Modules.Audit.Contracts;
using Ash.Modules.Identity.Contracts;
using Ash.Modules.Reports.Application;
using Ash.Modules.Reports.Infrastructure;
using Ash.SharedKernel.Identity;
using Ash.SharedKernel.Results;
using Ash.SharedKernel.Time;
using Microsoft.AspNetCore.Http;

namespace Ash.Modules.Reports.Export;

/// <summary>One export request: what to count, read and describe, and how to name it.</summary>
public sealed record ExportRequest(
    string ReportName,
    string SheetName,
    string FilePrefix,
    IReadOnlyList<ReportColumn> Columns,
    Func<CancellationToken, Task<int>> Count,
    Func<CancellationToken, IAsyncEnumerable<IReportRow>> Rows,
    Func<CancellationToken, Task<IReadOnlyList<ReportFilterText>>> Describe);

/// <summary>
/// The export flow of both reports (tasks 042/043): access → row count (more than Reports:MaxExportRows → 400
/// "Ngushtoni filtrat.") → the REPORT_EXPORT audit row (report, filters, row count — before any byte is sent) → the
/// workbook, written row by row into a temporary file that is deleted when the response is done. The package format
/// (zip) needs a seekable stream, so it cannot go straight into the response body; the file keeps memory flat whatever
/// the size, and an error while reading still becomes a proper ProblemDetails instead of a half-sent file.
/// </summary>
public sealed class ReportExports(ReportScope scope, ReportingDb db, IAuditLog audit, ICurrentUser user, IUserDirectory users, IClock clock)
{
    public const string ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    public async Task<IResult> RunAsync(ExportRequest request, CancellationToken ct)
    {
        if (scope.Denied() is { } denied)
        {
            return denied.ToProblem();
        }

        var count = await request.Count(ct);
        if (count > db.Options.MaxExportRows)
        {
            return Error.Validation("report.too_many_rows",
                $"Raporti ka {count:N0} rreshta, më shumë se {db.Options.MaxExportRows:N0} që mund të eksportohen. Ngushtoni filtrat.")
                .ToProblem();
        }

        var filters = await request.Describe(ct);
        audit.Record(AuditActions.ReportExport, "REPORT", details: new
        {
            report = request.ReportName,
            filters = filters.ToDictionary(f => f.Label, f => f.Value),
            rows = count,
        });

        var now = clock.UtcNow;
        var file = new FileStream(Path.Combine(Path.GetTempPath(), $"ash-report-{Guid.NewGuid():N}.xlsx"), FileMode.CreateNew,
            FileAccess.ReadWrite, FileShare.None, 81920, FileOptions.DeleteOnClose | FileOptions.Asynchronous);
        try
        {
            await XlsxReportWriter.WriteAsync(file, request.SheetName, request.Columns, request.Rows(ct),
                new ExportInfo(request.ReportName, now, await ReportNames.UserAsync(users, user.PublicId, ct), filters), ct);
            file.Position = 0;
        }
        catch
        {
            await file.DisposeAsync();
            throw;
        }

        // Results.File writes Content-Disposition with filename and filename* (RFC 5987) and disposes the stream
        var name = $"{request.FilePrefix}-{TiraneTime.DateOf(now):yyyy-MM-dd}.xlsx";
        return Results.File(file, ContentType, name);
    }
}
