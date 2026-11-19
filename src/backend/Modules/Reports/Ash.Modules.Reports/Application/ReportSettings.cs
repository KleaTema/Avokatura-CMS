using Ash.Modules.Reports.Application.Statistics;
using Dapper;
using MySql.Data.MySqlClient;

namespace Ash.Modules.Reports.Application;

/// <summary>
/// Task 053o — the rules the client has not confirmed yet, as DATA (script 034): ash_core.report_settings (one value per
/// open point, checked against its allowed values — anything else falls back to the first allowed one) and the result
/// columns of the statistics (ash_core.statistics_result_columns; the mapping itself is read by the SQL). A client
/// answer changes a seed row or a setting, never this code. Read per request (a few rows; no cache to go stale).
/// </summary>
public static class ReportSettings
{
    public const string AppealColumns = "statistics.appeal_columns";
    public const string CarriedOrphan = "statistics.carried_orphan";
    public const string ClosedValue = "reports.closed_value";

    private sealed record SettingRow(string SettingKey, string SettingValue, string AllowedValues);

    public static async Task<IReadOnlyDictionary<string, string>> ReadAsync(MySqlConnection connection, CancellationToken ct)
    {
        var rows = await connection.QueryAsync<SettingRow>(new CommandDefinition(
            "SELECT setting_key AS SettingKey, setting_value AS SettingValue, allowed_values AS AllowedValues FROM report_settings",
            cancellationToken: ct));
        return rows.ToDictionary(r => r.SettingKey, r =>
        {
            var allowed = r.AllowedValues.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            return allowed.Contains(r.SettingValue, StringComparer.Ordinal) ? r.SettingValue : allowed.FirstOrDefault() ?? r.SettingValue;
        });
    }

    public static async Task<StatisticsLayoutSettings> LayoutAsync(MySqlConnection connection, CancellationToken ct)
    {
        var s = await ReadAsync(connection, ct);
        return new StatisticsLayoutSettings(
            SplitAppeal: s.GetValueOrDefault(AppealColumns, "SPLIT") == "SPLIT",
            ShowCarriedOrphan: s.GetValueOrDefault(CarriedOrphan, "HIDE") == "SHOW");
    }

    /// <summary>The active result columns in their order.</summary>
    public static async Task<IReadOnlyList<StatisticsResultColumn>> ResultColumnsAsync(MySqlConnection connection, CancellationToken ct) =>
        (await connection.QueryAsync<StatisticsResultColumn>(new CommandDefinition(
            "SELECT code AS Code, label AS Label FROM statistics_result_columns WHERE is_active = 1 ORDER BY sort_order, code",
            cancellationToken: ct))).ToList();
}
