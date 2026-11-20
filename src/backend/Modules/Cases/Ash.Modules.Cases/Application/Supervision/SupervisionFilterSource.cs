using Ash.SharedKernel.Filters;

namespace Ash.Modules.Cases.Application.Supervision;

/// <summary>
/// Task 053m — GET /api/filters/supervisions (supervision.manage): the Monitor's supervisions, held whole and filtered
/// in the browser. Defaults: search (Kryetari, the lawyer or the case code), kind, status — none = every supervision,
/// "Aktive" or "Të përfunduara"; "+ Shto filtër": the start date.
/// </summary>
internal sealed class SupervisionFilterSource : ITableFilterSource
{
    public string Table => "supervisions";

    public string? Permission => "supervision.manage";

    public Task<IReadOnlyList<TableFilter>> DescribeAsync(IReadOnlyDictionary<string, string> context, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<TableFilter>>(
        [
            TableFilter.Of("q", "Kërko (Kryetari, avokati, kodi)", FilterKinds.Text, isDefault: true),
            TableFilter.Of("kind", "Lloji", FilterKinds.Select, isDefault: true,
                options: [new("LAWYER", "Avokat"), new("CASE", "Çështje")]),
            // task 054c: the empty choice "Të gjitha" is every supervision; no "ALL" option beside it
            TableFilter.Of("status", "Gjendja", FilterKinds.Select, isDefault: true,
                options: [new("ACTIVE", "Aktive"), new("ENDED", "Të përfunduara")]),
            TableFilter.Range("since", "Nga data", FilterKinds.DateRange, "sinceFrom", "sinceTo"),
        ]);
}
