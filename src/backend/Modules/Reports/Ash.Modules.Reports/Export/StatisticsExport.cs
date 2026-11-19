using System.Runtime.CompilerServices;
using Ash.AspNetCore.Http;
using Ash.Infrastructure.Excel;
using Ash.Modules.Audit.Contracts;
using Ash.Modules.Identity.Contracts;
using Ash.Modules.Reports.Application;
using Ash.Modules.Reports.Application.Statistics;
using Ash.SharedKernel.Identity;
using Ash.SharedKernel.Time;
using Microsoft.AspNetCore.Http;

namespace Ash.Modules.Reports.Export;

/// <summary>
/// GET /api/reports/statistics/export (tasks 044, 053o): the same figures as the JSON. The .xlsx draws the client's table
/// (<see cref="StatisticsGrid"/>: the title block, the merged header rows, the letter and formula rows, the court rows and
/// the TOTALI row, damages per currency — never summed across currencies), then "Rezultati", "Në dorë" and "Filtrat", in
/// the shared styled template. CSV / PDF: the same leaf columns, flat. Audited (REPORT_EXPORT).
/// </summary>
public sealed class StatisticsExport(StatisticsQueries statistics, IAuditLog audit, ICurrentUser user, IUserDirectory users, IClock clock)
{
    public const string Organisation = "AVOKATURA E SHTETIT";
    public const string Heading = "STATISTIKA";

    /// <param name="format">xlsx, csv or pdf.</param>
    /// <param name="columnKeys">Task 053h: kept for the API; the client's table is exported whole (task 053o).</param>
    public async Task<IResult> RunAsync(StatisticsQuery q, CancellationToken ct, string format = ExportFormats.Xlsx,
        IReadOnlyList<string>? columnKeys = null)
    {
        if (!ExportFormats.IsKnown(format))
        {
            return ExportFormats.Unknown.ToProblem();
        }

        var result = await statistics.GetAsync(q, ct, export: true);
        if (result.IsFailure)
        {
            return result.Error!.ToProblem();
        }

        var s = result.Value;
        var grid = s.Grid;
        audit.Record(AuditActions.ReportExport, "REPORT", details: new
        {
            report = grid.Title,
            format,
            columns = columnKeys,
            filters = s.Filters.ToDictionary(f => f.Label, f => f.Value),
            rows = grid.Rows.Count,
            frozen = s.Frozen,
        });

        var columns = Columns(grid);
        var info = new ExportInfo(grid.Title, clock.UtcNow, await ReportNames.UserAsync(users, user.PublicId, ct),
            s.Warning is null ? s.Filters : [.. s.Filters, new ReportFilterText("Kujdes", s.Warning)]);
        var fileName = $"statistikat-tabela-{(s.Table == 5 ? 3 : 4)}-{s.Year}-{TiraneTime.DateOf(clock.UtcNow):yyyy-MM-dd}.{format}";
        var file = new FileStream(Path.Combine(Path.GetTempPath(), $"ash-statistics-{Guid.NewGuid():N}.{format}"), FileMode.CreateNew,
            FileAccess.ReadWrite, FileShare.None, 81920, FileOptions.DeleteOnClose | FileOptions.Asynchronous);
        try
        {
            // csv / pdf (flat): the API's column choice (task 053h: measure codes) still narrows them; the .xlsx is the
            // client's whole table (its merged header cannot lose columns)
            var flat = Flat(grid, columns);
            if (columnKeys is { Count: > 0 })
            {
                var leafOf = grid.Columns.ToDictionary(l => l.Key);
                flat = flat.Where((c, i) => i == 0
                    || columnKeys.Contains(c.Key)
                    || (leafOf.TryGetValue(c.Key, out var leaf) && leaf.ResultColumn is null && leaf.Measure is { } m && columnKeys.Contains(m)))
                    .ToList();
            }

            if (format == ExportFormats.Csv)
            {
                await CsvReportWriter.WriteAsync(file, flat, Rows(GridRows(grid)), ct);
            }
            else if (format == ExportFormats.Pdf)
            {
                await PdfReportWriter.WriteAsync(file, flat, Rows(GridRows(grid)), info, null, ct);
            }
            else
            {
                await XlsxReportWriter.WriteAsync(file,
                [
                    new ExportSheet(s.Table == 5 ? "Tabela 3" : "Tabela 4", columns, Rows(GridRows(grid)))
                    {
                        Title = [Organisation, Heading, grid.Title],
                        HeaderRows = HeaderRows(grid),
                    },
                    new ExportSheet("Rezultati",
                    [
                        new("result", "Rezultati", ReportColumnKind.Text, 34),
                        new("ash", "Ndjekur nga ASH", ReportColumnKind.Integer, 16),
                        new("deleguar", "Deleguar / relatuar", ReportColumnKind.Integer, 18),
                    ], Rows(s.Results.Select(r => new Dictionary<string, object?> { ["result"] = r.Result, ["ash"] = r.Ash, ["deleguar"] = r.Deleguar }))),
                    new ExportSheet("Në dorë",
                    [
                        new("lawyer", "Avokati", ReportColumnKind.Text, 30),
                        new("count", "Në dorë", ReportColumnKind.Integer, 12),
                    ], Rows(s.InHand.Select(l => new Dictionary<string, object?> { ["lawyer"] = l.Lawyer, ["count"] = l.Count }))),
                ], info, ct);
            }

            file.Position = 0;
        }
        catch
        {
            await file.DisposeAsync();
            throw;
        }

        return Results.File(file, ExportFormats.ContentType(format), fileName);
    }

    /// <summary>The court column + one column per leaf; money cells carry their currency (the leaf's) for the format.</summary>
    public static IReadOnlyList<ReportColumn> Columns(StatisticsGrid grid)
    {
        var columns = new List<ReportColumn> { new("court", StatisticsLayout.CourtHeader, ReportColumnKind.Text, 22) };
        columns.AddRange(grid.Columns.Select(l => l.Kind switch
        {
            "money" => new ReportColumn(l.Key, l.Header, ReportColumnKind.Money, 16) { CurrencyKey = $"cur:{l.Key}" },
            "m2" => new ReportColumn(l.Key, l.Header, ReportColumnKind.Decimal, 12),
            _ => new ReportColumn(l.Key, l.Header, ReportColumnKind.Integer, 11),
        }));
        return columns;
    }

    /// <summary>
    /// The header rows exactly as the grid draws them, then the client's letter row and formula row (one cell per
    /// column; the court column's cell is merged over the grid's rows already).
    /// </summary>
    public static IReadOnlyList<IReadOnlyList<XlsxHeaderCell>> HeaderRows(StatisticsGrid grid)
    {
        var rows = grid.HeaderRows
            .Select(r => (IReadOnlyList<XlsxHeaderCell>)r.Select(c => new XlsxHeaderCell(c.Text, c.ColSpan, c.RowSpan)).ToList())
            .ToList();
        rows.Add([new XlsxHeaderCell(string.Empty), .. grid.Columns.Select(l => new XlsxHeaderCell(l.Letter ?? string.Empty))]);
        rows.Add([new XlsxHeaderCell(string.Empty), .. grid.Columns.Select(l => new XlsxHeaderCell(l.Formula ?? string.Empty))]);
        return rows;
    }

    /// <summary>CSV / PDF: one header row — each leaf named with the groups above it ("Pezulluar — Avokat").</summary>
    private static IReadOnlyList<ReportColumn> Flat(StatisticsGrid grid, IReadOnlyList<ReportColumn> columns)
    {
        var names = FlatNames(grid);
        return columns.Select((c, i) => i == 0 ? c : c with { Header = names[i - 1] }).ToList();
    }

    public static IReadOnlyList<string> FlatNames(StatisticsGrid grid)
    {
        // walk the header rows: each leaf's path of group texts
        var paths = Enumerable.Range(0, grid.Columns.Count).Select(_ => new List<string>()).ToList();
        var occupied = new HashSet<(int Row, int Column)>();
        for (var r = 0; r < grid.HeaderRows.Count; r++)
        {
            var column = 0;
            foreach (var cell in grid.HeaderRows[r])
            {
                while (occupied.Contains((r, column)))
                {
                    column++;
                }

                for (var rr = 0; rr < cell.RowSpan; rr++)
                {
                    for (var cc = 0; cc < cell.ColSpan; cc++)
                    {
                        occupied.Add((r + rr, column + cc));
                    }
                }

                // column 0 is the court
                for (var cc = 0; cc < cell.ColSpan; cc++)
                {
                    var leaf = column + cc - 1;
                    if (leaf >= 0 && leaf < paths.Count && cell.Text.Length > 0)
                    {
                        paths[leaf].Add(cell.Text);
                    }
                }

                column += cell.ColSpan;
            }
        }

        return paths.Select(p => string.Join(" — ", p)).ToList();
    }

    private static IEnumerable<Dictionary<string, object?>> GridRows(StatisticsGrid grid) =>
        grid.Rows.Select(row =>
        {
            var values = new Dictionary<string, object?> { ["court"] = row.Court, [TotalFlag] = row.CourtId is null };
            foreach (var leaf in grid.Columns)
            {
                var value = row.Values.GetValueOrDefault(leaf.Key);
                values[leaf.Key] = leaf.Kind == "count" ? (object)(int)value : value;
                if (leaf.Currency is { } currency)
                {
                    values[$"cur:{leaf.Key}"] = currency;
                }
            }

            return values;
        });

    private const string TotalFlag = "__total";

    private static async IAsyncEnumerable<IReportRow> Rows(
        IEnumerable<Dictionary<string, object?>> rows, [EnumeratorCancellation] CancellationToken ct = default)
    {
        foreach (var row in rows)
        {
            ct.ThrowIfCancellationRequested();
            yield return new ValuesRow(row, row.GetValueOrDefault(TotalFlag) is true);
        }

        await Task.CompletedTask;
    }
}
