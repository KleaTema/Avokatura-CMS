using System.Text.Json;

namespace Ash.Modules.Reports.Application.Builder;

/// <summary>The four tables of the report builder (task 053h): the client's table numbers.</summary>
public static class ReportTables
{
    public const int Active = 1;
    public const int Closed = 3;
    public const int Statistics = 5;
    public const int Institutional = 6;

    public static bool IsKnown(int table) => table is Active or Closed or Statistics or Institutional;

    public const string StatisticsTitle = "Tabela 3 — Çështje aktive";
    public const string InstitutionalTitle = "Tabela 4 — Institucionale";

    public static string Name(int table) => table switch
    {
        Active => ReportColumns.ActiveReportName,
        Closed => ReportColumns.ClosedReportName,
        // task 053o: the user-facing numbering (internal table numbers 5 / 6 stay)
        Statistics => StatisticsTitle,
        _ => InstitutionalTitle,
    };
}

/// <summary>How a filter is entered: one choice, a contained text, a date range, a number range, yes / no.</summary>
public static class ReportFilterKinds
{
    public const string Select = "select";
    public const string Text = "text";
    public const string DateRange = "dateRange";
    public const string NumberRange = "numberRange";
    public const string YesNo = "yesNo";
}

/// <summary>One choice of a select filter. Value: a public id, a reference id or a code — never an internal id.</summary>
public sealed record ReportFilterOption(string Value, string Label);

/// <summary>
/// One filter of a table, as the UI builds it. Parent: the filter whose choice limits this one's options (shkalla →
/// gjykata → gjyqtari, zyra → avokati, ministria → institucioni, kategoria → lloji). IsDefault: shown on top before
/// "+ Shto filtër". Locked: fixed by the user's scope (an institution's own institution), read-only in the UI.
/// Options: for a select, only the values that exist in the user's scope (and under the parent's choice).
/// </summary>
public sealed record ReportFilterMeta(
    string Key, string Label, string Kind, string? Parent, bool IsDefault, bool Required, bool Locked,
    IReadOnlyList<ReportFilterOption>? Options);

/// <summary>
/// One column of a table for the column chooser; Numeric columns get a total. Task 053o: Group = the header above it
/// (merged over the columns of the same group), DefaultVisible = in the client's table (the default view),
/// CurrencyKey = a money column's currency (a row key).
/// </summary>
public sealed record ReportColumnMeta(string Key, string Header, string Kind, bool Numeric)
{
    public string? Group { get; init; }
    public bool DefaultVisible { get; init; } = true;
    public string? CurrencyKey { get; init; }
}

/// <summary>GET /api/reports/tables/{table}/filters: what the filter builder and the column chooser show.</summary>
public sealed record ReportFiltersResponse(
    int Table, string Name, IReadOnlyList<ReportFilterMeta> Filters, IReadOnlyList<ReportColumnMeta> Columns,
    IReadOnlyDictionary<string, string> Fixed);

/// <summary>
/// The chosen filter values, as the UI sends them (?filters= a JSON object): select / text → a string, yes / no → a
/// boolean, a range → {"from":…,"to":…} (dates yyyy-MM-dd, Tirane) or {"min":…,"max":…}.
/// </summary>
public sealed class ReportFilterValues
{
    private readonly Dictionary<string, JsonElement> _values;

    private ReportFilterValues(Dictionary<string, JsonElement> values) => _values = values;

    public static readonly ReportFilterValues Empty = new([]);

    public IEnumerable<string> Keys => _values.Keys;

    /// <summary>Null when the text is not a JSON object.</summary>
    public static ReportFilterValues? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return Empty;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var values = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (property.Value.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined))
                {
                    values[property.Name] = property.Value.Clone();
                }
            }

            return new ReportFilterValues(values);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public bool Has(string key) => _values.ContainsKey(key);

    public string? Text(string key) =>
        _values.TryGetValue(key, out var v) && v.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(v.GetString())
            ? v.GetString()!.Trim()
            : null;

    public bool? Bool(string key) =>
        _values.TryGetValue(key, out var v) && v.ValueKind is JsonValueKind.True or JsonValueKind.False ? v.GetBoolean() : null;

    public (string? From, string? To) Range(string key, string fromName, string toName)
    {
        if (!_values.TryGetValue(key, out var v) || v.ValueKind != JsonValueKind.Object)
        {
            return (null, null);
        }

        static string? Read(JsonElement o, string name) =>
            o.TryGetProperty(name, out var x) ? x.ValueKind switch
            {
                JsonValueKind.String when !string.IsNullOrWhiteSpace(x.GetString()) => x.GetString()!.Trim(),
                JsonValueKind.Number => x.GetRawText(),
                _ => null,
            } : null;

        return (Read(v, fromName), Read(v, toName));
    }

    /// <summary>A copy without <paramref name="key"/> (the options of a filter never depend on its own value).</summary>
    public ReportFilterValues Without(string key)
    {
        var copy = new Dictionary<string, JsonElement>(_values, StringComparer.Ordinal);
        copy.Remove(key);
        return new ReportFilterValues(copy);
    }

    /// <summary>A copy with <paramref name="key"/> fixed to a text value (a locked filter).</summary>
    public ReportFilterValues With(string key, string value)
    {
        var copy = new Dictionary<string, JsonElement>(_values, StringComparer.Ordinal)
        {
            [key] = JsonSerializer.SerializeToElement(value),
        };
        return new ReportFilterValues(copy);
    }

    public string ToJson() => JsonSerializer.Serialize(_values);
}
