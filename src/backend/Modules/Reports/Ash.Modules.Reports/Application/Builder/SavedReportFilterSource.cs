using Ash.SharedKernel.Filters;

namespace Ash.Modules.Reports.Application.Builder;

/// <summary>
/// Task 053m — GET /api/filters/saved-reports: "Raportet e mia" (the user's own, held whole: filtered in the browser).
/// Defaults: the name, the table; "+ Shto filtër": the last change. The URL parameters carry the tab's "mine" prefix
/// (the reports page holds the builder's parameters too).
/// </summary>
internal sealed class SavedReportFilterSource : ITableFilterSource
{
    public string Table => "saved-reports";

    public string? Permission => null;

    public Task<IReadOnlyList<TableFilter>> DescribeAsync(IReadOnlyDictionary<string, string> context, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<TableFilter>>(
        [
            new TableFilter("q", "Emri", FilterKinds.Text, null, true, ["mineQ"], null),
            new TableFilter("table", "Tabela", FilterKinds.Select, null, true, ["mineTable"],
                new[] { ReportTables.Active, ReportTables.Closed, ReportTables.Statistics, ReportTables.Institutional }
                    .Select(t => new FilterOption(t.ToString(System.Globalization.CultureInfo.InvariantCulture), ReportTables.Name(t)))
                    .ToList()),
            new TableFilter("updated", "Ndryshuar më", FilterKinds.DateRange, null, false, ["mineFrom", "mineTo"], null),
        ]);
}
