using Ash.Modules.Reference.Contracts;
using Ash.SharedKernel.Filters;

namespace Ash.Modules.Reference.Application.Admin;

/// <summary>
/// Task 053m — the filters of the reference admin tables (admin.lookups). The keys are the pages' URL parameters: the
/// judges list sends them to its endpoint, the other lists (held whole) match them in the browser.
/// </summary>
internal static class ReferenceFilters
{
    public const string Permission = "admin.lookups";

    public static TableFilter Active => TableFilter.Of("active", "Aktive", FilterKinds.YesNo, isDefault: true);
}

/// <summary>GET /api/filters/courts: search, level, active by default; foreign.</summary>
internal sealed class CourtListFilterSource(IReferenceData reference) : ITableFilterSource
{
    public string Table => "courts";

    public string? Permission => ReferenceFilters.Permission;

    public async Task<IReadOnlyList<TableFilter>> DescribeAsync(IReadOnlyDictionary<string, string> context, CancellationToken ct)
    {
        var levels = await reference.ListCourtLevelsAsync(ct);
        return
        [
            TableFilter.Of("q", "Kërko (emri, qyteti, shkalla)", FilterKinds.Text, isDefault: true),
            TableFilter.Of("level", "Shkalla e gjykimit", FilterKinds.Select, isDefault: true,
                options: levels.OrderBy(l => l.SortOrder).Select(l => new FilterOption(l.Code, l.Name)).ToList()),
            ReferenceFilters.Active,
            TableFilter.Of("foreign", "E huaj", FilterKinds.YesNo),
        ];
    }
}

/// <summary>GET /api/filters/judges (a server-paged list): search, court, active by default; the court's level.</summary>
internal sealed class JudgeListFilterSource(IReferenceData reference) : ITableFilterSource
{
    public string Table => "judges";

    public string? Permission => ReferenceFilters.Permission;

    public async Task<IReadOnlyList<TableFilter>> DescribeAsync(IReadOnlyDictionary<string, string> context, CancellationToken ct)
    {
        var levels = (await reference.ListCourtLevelsAsync(ct)).ToDictionary(l => l.Id);
        var courts = (await reference.ListCourtsAsync(ct)).OrderBy(c => c.Name).ToList();
        return
        [
            TableFilter.Of("q", "Kërko (emri)", FilterKinds.Text, isDefault: true),
            TableFilter.Of("court", "Gjykata", FilterKinds.Select, isDefault: true, parent: "level",
                options: courts.Select(c => new FilterOption(c.Id.ToString(), c.Name,
                    levels.TryGetValue(c.CourtLevelId, out var level) ? level.Code : null)).ToList()),
            ReferenceFilters.Active,
            // narrows the courts only (the list endpoint filters by the court)
            TableFilter.Of("level", "Shkalla e gjykimit", FilterKinds.Select,
                options: levels.Values.OrderBy(l => l.SortOrder).Select(l => new FilterOption(l.Code, l.Name)).ToList()),
        ];
    }
}

/// <summary>
/// GET /api/filters/lists?list=… — the simple lists (hearing types, party roles …): search, active; the group where
/// the list has one (party roles: the report group, decision results: the outcome group).
/// </summary>
internal sealed class SimpleListFilterSource : ITableFilterSource
{
    public string Table => "lists";

    public string? Permission => ReferenceFilters.Permission;

    private static readonly IReadOnlyList<FilterOption> ReportGroups =
        [new("PLAINTIFF", "Paditës"), new("DEFENDANT_OR_THIRD", "I paditur / person i tretë")];

    private static readonly IReadOnlyList<FilterOption> OutcomeGroups =
    [
        new("WIN", "Fitim"), new("LOSS", "Humbje"), new("PARTIAL", "Fitim i pjesshëm"), new("PROCEDURAL", "Procedural"),
        new("OTHER", "Tjetër"),
    ];

    public Task<IReadOnlyList<TableFilter>> DescribeAsync(IReadOnlyDictionary<string, string> context, CancellationToken ct)
    {
        var list = context.GetValueOrDefault("list");
        List<TableFilter> filters =
        [
            TableFilter.Of("q", "Kërko (kodi, emri)", FilterKinds.Text, isDefault: true),
            ReferenceFilters.Active,
        ];
        if (list == "party-roles")
        {
            filters.Add(TableFilter.Of("group", "Grupi i raportit", FilterKinds.Select, isDefault: true, options: ReportGroups));
        }
        else if (list == "decision-results")
        {
            filters.Add(TableFilter.Of("group", "Grupi i rezultatit", FilterKinds.Select, isDefault: true, options: OutcomeGroups));
        }

        return Task.FromResult<IReadOnlyList<TableFilter>>(filters);
    }
}

/// <summary>GET /api/filters/file-types: search, applies to, active by default; the maximum size (MB).</summary>
internal sealed class FileTypeListFilterSource : ITableFilterSource
{
    public string Table => "file-types";

    public string? Permission => ReferenceFilters.Permission;

    public Task<IReadOnlyList<TableFilter>> DescribeAsync(IReadOnlyDictionary<string, string> context, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<TableFilter>>(
        [
            TableFilter.Of("q", "Kërko (shtesa, lloji MIME)", FilterKinds.Text, isDefault: true),
            TableFilter.Of("appliesTo", "Vlen për", FilterKinds.Select, isDefault: true,
                options: [new("DOCUMENT", "Dokumente"), new("CHAT", "Chat"), new("BOTH", "Të dyja")]),
            ReferenceFilters.Active,
            TableFilter.Range("maxSize", "Madhësia maksimale (MB)", FilterKinds.NumberRange, "sizeMin", "sizeMax"),
        ]);
}

/// <summary>GET /api/filters/code-formats: search (template, note), active; the creation date.</summary>
internal sealed class CodeFormatListFilterSource : ITableFilterSource
{
    public string Table => "code-formats";

    public string? Permission => ReferenceFilters.Permission;

    public Task<IReadOnlyList<TableFilter>> DescribeAsync(IReadOnlyDictionary<string, string> context, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<TableFilter>>(
        [
            TableFilter.Of("q", "Kërko (formati, shënimi)", FilterKinds.Text, isDefault: true),
            ReferenceFilters.Active,
            TableFilter.Range("created", "Krijuar më", FilterKinds.DateRange, "createdFrom", "createdTo"),
        ]);
}
