using System.Runtime.CompilerServices;
using Ash.AspNetCore.Http;
using Ash.Modules.Audit.Contracts;
using Ash.Modules.Identity.Contracts;
using Ash.Modules.Reports.Application;
using Ash.Modules.Reports.Application.Statistics;
using Ash.SharedKernel.Identity;
using Ash.SharedKernel.Time;
using Microsoft.AspNetCore.Http;

namespace Ash.Modules.Reports.Export;

/// <summary>
/// GET /api/reports/statistics/export (task 044): the same figures as the JSON, as .xlsx with the streaming writer of
/// tasks 042/043 — sheet "Tabela 5" (or 6) per court with the TOTAL row and the damages in ONE COLUMN PER CURRENCY (never
/// summed across currencies; m² in their own columns), "Rezultati", "Në dorë", "Filtrat". Audited (REPORT_EXPORT).
/// </summary>
public sealed class StatisticsExport(StatisticsQueries statistics, IAuditLog audit, ICurrentUser user, IUserDirectory users, IClock clock)
{
    public async Task<IResult> RunAsync(StatisticsQuery q, CancellationToken ct)
    {
        var result = await statistics.GetAsync(q, ct, export: true);
        if (result.IsFailure)
        {
            return result.Error!.ToProblem();
        }

        var s = result.Value;
        var name = $"Statistikat — Tabela {s.Table} — {s.Year}";
        audit.Record(AuditActions.ReportExport, "REPORT", details: new
        {
            report = name,
            filters = s.Filters.ToDictionary(f => f.Label, f => f.Value),
            rows = s.Courts.Count,
            frozen = s.Frozen,
        });

        var currencies = s.Money.Select(m => m.Currency).OfType<string>().Distinct().Order(StringComparer.Ordinal).ToList();
        var columns = new List<ReportColumn> { new("court", "Gjykata", ReportColumnKind.Text, 34) };
        columns.AddRange(s.Measures.Select(m => new ReportColumn(m.Code, m.Name, ReportColumnKind.Integer, 14)));
        foreach (var currency in currencies)
        {
            columns.Add(new($"prevented:{currency}", $"Dëm i parandaluar ({currency})", ReportColumnKind.Decimal, 18));
            columns.Add(new($"caused:{currency}", $"Dëm i shkaktuar ({currency})", ReportColumnKind.Decimal, 18));
        }

        columns.Add(new("prevented:m2", "Dëm i parandaluar (m²)", ReportColumnKind.Decimal, 16));
        columns.Add(new("caused:m2", "Dëm i shkaktuar (m²)", ReportColumnKind.Decimal, 16));

        var info = new ExportInfo(name, clock.UtcNow, await ReportNames.UserAsync(users, user.PublicId, ct),
            s.Warning is null ? s.Filters : [.. s.Filters, new ReportFilterText("Kujdes", s.Warning)]);
        var file = new FileStream(Path.Combine(Path.GetTempPath(), $"ash-statistics-{Guid.NewGuid():N}.xlsx"), FileMode.CreateNew,
            FileAccess.ReadWrite, FileShare.None, 81920, FileOptions.DeleteOnClose | FileOptions.Asynchronous);
        try
        {
            await XlsxReportWriter.WriteAsync(file,
            [
                new ExportSheet($"Tabela {s.Table}", columns, Rows(s.Courts.Select(c => CourtValues(c, s.Money, currencies)))),
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
            file.Position = 0;
        }
        catch
        {
            await file.DisposeAsync();
            throw;
        }

        return Results.File(file, ReportExports.ContentType, $"raporti-statistikat-{s.Year}-{TiraneTime.DateOf(clock.UtcNow):yyyy-MM-dd}.xlsx");
    }

    private static Dictionary<string, object?> CourtValues(StatisticsCourtRow court, IReadOnlyList<StatisticsMoneyRow> money, IReadOnlyList<string> currencies)
    {
        var values = new Dictionary<string, object?> { ["court"] = court.Court };
        foreach (var (code, value) in court.Values)
        {
            values[code] = value;
        }

        // the court's own rows, or (TOTAL row) each currency's total row
        var mine = money.Where(m => m.CourtId == court.CourtId).ToList();
        foreach (var currency in currencies)
        {
            var row = mine.FirstOrDefault(m => m.Currency == currency);
            values[$"prevented:{currency}"] = row?.PreventedAmount;
            values[$"caused:{currency}"] = row?.CausedAmount;
        }

        values["prevented:m2"] = mine.Count == 0 ? null : mine.Sum(m => m.PreventedM2);
        values["caused:m2"] = mine.Count == 0 ? null : mine.Sum(m => m.CausedM2);
        return values;
    }

    private static async IAsyncEnumerable<IReportRow> Rows(
        IEnumerable<Dictionary<string, object?>> rows, [EnumeratorCancellation] CancellationToken ct = default)
    {
        foreach (var row in rows)
        {
            ct.ThrowIfCancellationRequested();
            yield return new ValuesRow(row);
        }

        await Task.CompletedTask;
    }
}
