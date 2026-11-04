namespace Ash.Modules.Cases.Application.Dashboard;

/// <summary>GET /api/dashboard (task 046): the caller's widgets, in display order.</summary>
public sealed record DashboardResponse(IReadOnlyList<DashboardWidget> Widgets, DateTime GeneratedAt);

/// <summary>
/// One widget. Kind: "count" (Value; LinkPath opens exactly the list it counts), "list" (Items, at most 8; Value =
/// how many the full list has), "chart" (at most 8 bars, the largest, with Value / Value2 and their links; Value =
/// the total over all of them, not only the bars shown). Secondary: one extra number,
/// meaning per code (unassigned: days the oldest case has waited). Error: set when this widget failed — the other
/// widgets are still there. Title is a fallback; the app shows its own text for the code. No trend is sent: none is
/// computed yet.
/// </summary>
public sealed record DashboardWidget(
    string Code,
    string Kind,
    string Title,
    long? Value = null,
    long? Secondary = null,
    IReadOnlyList<DashboardItem>? Items = null,
    string? LinkPath = null,
    string? Error = null)
{
    public const string Count = "count";
    public const string List = "list";
    public const string Chart = "chart";
}

/// <summary>
/// A row of a list or a bar of a chart. Public ids only (in the links). Charts: Label + Value (+ Value2) with LinkPath
/// (+ Link2Path). Lists of cases or hearings: CaseCode, CourtName, At (UTC), Status; activity: Label = the history
/// summary. Admin rows: Label = a job or table, Detail = its error or problem code.
/// </summary>
public sealed record DashboardItem(
    string Label,
    string? Detail = null,
    DateTime? At = null,
    long? Value = null,
    long? Value2 = null,
    string? LinkPath = null,
    string? Link2Path = null,
    string? CaseCode = null,
    string? CourtName = null,
    string? Status = null);
