namespace Ash.SharedKernel.Filters;

/// <summary>How a filter is entered (task 053m): the FilterBar draws one control per kind.</summary>
public static class FilterKinds
{
    /// <summary>One value of <see cref="TableFilter.Options"/> (a code, a reference id or a public id).</summary>
    public const string Select = "select";
    /// <summary>A contained text (accent- and case-insensitive where the column's collation is).</summary>
    public const string Text = "text";
    /// <summary>From / to dates (yyyy-MM-dd, Tirane): two parameters, <see cref="TableFilter.Params"/>.</summary>
    public const string DateRange = "dateRange";
    /// <summary>Min / max numbers: two parameters.</summary>
    public const string NumberRange = "numberRange";
    /// <summary>"true" / "false".</summary>
    public const string YesNo = "yesNo";
}

/// <summary>One choice of a select; Parent = the value of the parent filter it belongs to (court → its level).</summary>
public sealed record FilterOption(string Value, string Label, string? Parent = null);

/// <summary>
/// One filter of a table (task 053m), as the shared FilterBar renders it:
/// <list type="bullet">
/// <item>Key: the filter (unique in the table); Label; Kind (<see cref="FilterKinds"/>);</item>
/// <item>Parent: the filter whose choice narrows this one's options (each option names its parent value);</item>
/// <item>IsDefault: shown on top (the page's main filters); every other one is offered by "+ Shto filtër";</item>
/// <item>Params: the page's URL parameters the filter writes — one (select, text, yes / no) or two (from / to, min /
/// max); a server table's page passes them to its list endpoint; a browser table matches its rows with them.</item>
/// </list>
/// </summary>
public sealed record TableFilter(
    string Key, string Label, string Kind, string? Parent, bool IsDefault, IReadOnlyList<string> Params,
    IReadOnlyList<FilterOption>? Options = null)
{
    public static TableFilter Of(string key, string label, string kind, bool isDefault = false, string? parent = null,
        IReadOnlyList<FilterOption>? options = null) =>
        new(key, label, kind, parent, isDefault, [key], options);

    public static TableFilter Range(string key, string label, string kind, string fromParam, string toParam, bool isDefault = false) =>
        new(key, label, kind, null, isDefault, [fromParam, toParam]);
}

/// <summary>
/// The ONE filter metadata source of a table (task 053m): GET /api/filters/{Table} returns its filters, the options
/// limited to what the user may see. A module registers one per table it owns.
/// </summary>
public interface ITableFilterSource
{
    /// <summary>The table's name in the URL (e.g. "cases", "users", "offices").</summary>
    string Table { get; }

    /// <summary>A system permission the user needs to read the metadata (null: any signed-in user).</summary>
    string? Permission { get; }

    /// <summary>The filters, in order; <paramref name="context"/> = the page's query (e.g. the case list's tab).</summary>
    Task<IReadOnlyList<TableFilter>> DescribeAsync(IReadOnlyDictionary<string, string> context, CancellationToken ct);
}
