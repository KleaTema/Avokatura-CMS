using System.Text.RegularExpressions;
using Dapper;
using MySql.Data.MySqlClient;

namespace Ash.Modules.Reports.Infrastructure;

/// <param name="ConnectionString">ConnectionStrings:Reporting (account ash_reporting: SELECT on ash_core.*, the read
/// replica in production); falls back to ConnectionStrings:App when not set (locally).</param>
/// <param name="QueryTimeLimit">A JSON page: the API's read limit (Database:QueryTimeLimitSeconds, 5 s).</param>
/// <param name="ExportTimeLimit">An export: Reports:ExportQueryTimeLimitSeconds (60 s) — a whole office is many rows.</param>
/// <param name="MaxExportRows">Above this many rows an export is refused (Reports:MaxExportRows, 200,000).</param>
public sealed record ReportingOptions(string ConnectionString, TimeSpan QueryTimeLimit, TimeSpan ExportTimeLimit, int MaxExportRows)
{
    public const int DefaultMaxExportRows = 200_000;
    public static readonly TimeSpan DefaultExportTimeLimit = TimeSpan.FromSeconds(60);
}

/// <summary>
/// The reports' own read-only connection (tasks 042/043): Dapper, plain SQL written for the purpose (ReportQueries) —
/// never the core unit of work. Every SELECT carries a MAX_EXECUTION_TIME hint so MySQL itself stops a slow report
/// (a timeout becomes the API's 503, like every other read); the command timeout is the backstop.
/// The session is READ ONLY, and GROUP_CONCAT may build long party lists (the 1,024-byte default would cut them).
/// </summary>
public sealed partial class ReportingDb(ReportingOptions options)
{
    /// <summary>
    /// The session settings below must never reach another user of the pool (locally the App connection string is the
    /// same): ConnectionReset clears the session when a pooled connection is handed out again, and the different string
    /// gives the reports their own pool anyway.
    /// </summary>
    private readonly string _connectionString = new MySqlConnectionStringBuilder(options.ConnectionString)
    {
        ConnectionReset = true,
    }.ConnectionString;

    public ReportingOptions Options => options;

    /// <summary>
    /// Opens a read-only session. Its max_execution_time is the limit of the kind of work (a JSON page or an export):
    /// it covers statements that start with WITH (the statistics, task 044), where the per-statement hint cannot go.
    /// </summary>
    public async Task<MySqlConnection> OpenAsync(CancellationToken ct, bool export = false)
    {
        var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync(ct);
        var limit = export ? options.ExportTimeLimit : options.QueryTimeLimit;
        await connection.ExecuteAsync(new CommandDefinition(
            "SET SESSION TRANSACTION READ ONLY; SET SESSION group_concat_max_len = 1048576; SET SESSION max_execution_time = @ms;",
            new { ms = (long)limit.TotalMilliseconds }, cancellationToken: ct));
        return connection;
    }

    public Task<IEnumerable<T>> QueryAsync<T>(MySqlConnection connection, string sql, object? param, bool export, CancellationToken ct)
    {
        var limit = export ? options.ExportTimeLimit : options.QueryTimeLimit;
        return connection.QueryAsync<T>(new CommandDefinition(
            WithTimeLimit(sql, limit), param, commandTimeout: (int)Math.Ceiling(limit.TotalSeconds) + 5, cancellationToken: ct));
    }

    public Task<T> ScalarAsync<T>(MySqlConnection connection, string sql, object? param, bool export, CancellationToken ct)
    {
        var limit = export ? options.ExportTimeLimit : options.QueryTimeLimit;
        return connection.ExecuteScalarAsync<T>(new CommandDefinition(
            WithTimeLimit(sql, limit), param, commandTimeout: (int)Math.Ceiling(limit.TotalSeconds) + 5, cancellationToken: ct))!;
    }

    [GeneratedRegex(@"^\s*SELECT\b", RegexOptions.IgnoreCase)]
    private static partial Regex LeadingSelect();

    /// <summary>The optimizer hint on the statement's first SELECT (MySQL honours it on the top-level SELECT only).</summary>
    public static string WithTimeLimit(string sql, TimeSpan limit) =>
        LeadingSelect().Replace(sql, $"SELECT /*+ MAX_EXECUTION_TIME({(long)limit.TotalMilliseconds}) */", 1);
}
