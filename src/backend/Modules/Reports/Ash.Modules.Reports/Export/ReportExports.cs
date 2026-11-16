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
    Func<CancellationToken, Task<IReadOnlyList<ReportFilterText>>> Describe)
{
    /// <summary>Task 053h: xlsx (default), csv or pdf.</summary>
    public string Format { get; init; } = ExportFormats.Xlsx;

    /// <summary>Task 053h: the totals row of the pdf (null = none).</summary>
    public Func<CancellationToken, Task<ReportTotals>>? Totals { get; init; }
}

/// <summary>Task 053h: the export formats.</summary>
public static class ExportFormats
{
    public const string Xlsx = "xlsx";
    public const string Csv = "csv";
    public const string Pdf = "pdf";

    public static bool IsKnown(string? format) => format is Xlsx or Csv or Pdf;

    public static string ContentType(string format) => format switch
    {
        Csv => CsvReportWriter.ContentType,
        Pdf => PdfReportWriter.ContentType,
        _ => ReportExports.ContentType,
    };

    public static Error Unknown => Error.Validation("report.format_invalid", "Formati duhet të jetë xlsx, csv ose pdf.");
}

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
        if (await scope.DeniedAsync(ct) is { } denied)
        {
            return denied.ToProblem();
        }

        if (!ExportFormats.IsKnown(request.Format))
        {
            return ExportFormats.Unknown.ToProblem();
        }

        var count = await request.Count(ct);
        if (count > db.Options.MaxExportRows)
        {
            return Error.Validation("report.too_many_rows",
                $"Raporti ka {count:N0} rreshta, më shumë se {db.Options.MaxExportRows:N0} që mund të eksportohen. Ngushtoni filtrat.")
                .ToProblem();
        }

        if (request.Format == ExportFormats.Pdf && count > PdfReportWriter.MaxRows)
        {
            return Error.Validation("report.too_many_rows_pdf",
                $"Raporti ka {count:N0} rreshta; PDF-ja lejon deri në {PdfReportWriter.MaxRows:N0}. Ngushtoni filtrat ose përdorni Excel/CSV.")
                .ToProblem();
        }

        var filters = await request.Describe(ct);
        // every export is audited, whatever its format (task 053h), before any byte is sent
        audit.Record(AuditActions.ReportExport, "REPORT", details: new
        {
            report = request.ReportName,
            format = request.Format,
            columns = request.Columns.Select(c => c.Key).ToList(),
            filters = filters.ToDictionary(f => f.Label, f => f.Value),
            rows = count,
        });

        var now = clock.UtcNow;
        var file = new FileStream(Path.Combine(Path.GetTempPath(), $"ash-report-{Guid.NewGuid():N}.{request.Format}"), FileMode.CreateNew,
            FileAccess.ReadWrite, FileShare.None, 81920, FileOptions.DeleteOnClose | FileOptions.Asynchronous);
        try
        {
            var info = new ExportInfo(request.ReportName, now, await ReportNames.UserAsync(users, user.PublicId, ct), filters);
            switch (request.Format)
            {
                case ExportFormats.Csv:
                    await CsvReportWriter.WriteAsync(file, request.Columns, request.Rows(ct), ct);
                    break;
                case ExportFormats.Pdf:
                    var totals = request.Totals is null ? null : (await request.Totals(ct)).Sums;
                    await PdfReportWriter.WriteAsync(file, request.Columns, request.Rows(ct), info, totals, ct);
                    break;
                default:
                    await XlsxReportWriter.WriteAsync(file, request.SheetName, request.Columns, request.Rows(ct), info, ct);
                    break;
            }

            file.Position = 0;
        }
        catch
        {
            await file.DisposeAsync();
            throw;
        }

        // Results.File writes Content-Disposition with filename and filename* (RFC 5987) and disposes the stream
        var name = $"{request.FilePrefix}-{TiraneTime.DateOf(now):yyyy-MM-dd}.{request.Format}";
        return Results.File(file, ExportFormats.ContentType(request.Format), name);
    }
}
