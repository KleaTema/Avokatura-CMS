using System.Text.Json;
using Ash.Infrastructure.Persistence;
using Ash.Modules.Audit.Contracts;
using Ash.SharedKernel.Identity;
using Ash.SharedKernel.Ids;
using Ash.SharedKernel.Results;
using Dapper;
using MySql.Data.MySqlClient;

namespace Ash.Modules.Reports.Application.Builder;

/// <summary>POST / PUT /api/reports/saved: a name, the table, the filter values (JSON object), the columns in order.</summary>
public sealed record SaveReportRequest(string? Name, int? Table, JsonElement? Filters, IReadOnlyList<string>? Columns);

/// <summary>One saved report of "Raportet e mia". Filters: the JSON object the builder takes as ?filters=.</summary>
public sealed record SavedReportView(
    Guid PublicId, string Name, int Table, string TableName, JsonElement Filters, IReadOnlyList<string> Columns, DateTime UpdatedAt);

/// <summary>
/// Task 053h: "Raportet e mia" — save the builder's table + filters + columns under a name, list them, rerun one (the
/// UI opens the builder with them; nothing runs before "Gjenero"), rename / overwrite, delete. Private to the owner:
/// someone else's id → 404. Running a saved report applies the reader's scope at that moment (the filters are only
/// filters). Written through the app connection — the reporting account is read-only.
/// </summary>
public sealed class SavedReports(PersistenceOptions persistence, IUnitOfWork unitOfWork, ICurrentUser user, IAuditLog audit)
{
    public const int MaxPerUser = 100;
    private static readonly Error NotFound = Error.NotFound("report.saved_not_found", "Raporti i ruajtur nuk u gjet.");

    public async Task<IReadOnlyList<SavedReportView>> ListAsync(CancellationToken ct)
    {
        await using var connection = await OpenAsync(ct);
        var rows = await connection.QueryAsync<Row>(new CommandDefinition(
            $"{Select} WHERE owner_user_id = @me ORDER BY name", new { me = user.UserId }, cancellationToken: ct));
        return rows.Select(View).ToList();
    }

    public async Task<Result<SavedReportView>> CreateAsync(SaveReportRequest req, CancellationToken ct)
    {
        var valid = Validate(req);
        if (valid.IsFailure)
        {
            return valid.Error!;
        }

        await using var connection = await OpenAsync(ct);
        var count = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT COUNT(*) FROM saved_reports WHERE owner_user_id = @me", new { me = user.UserId }, cancellationToken: ct));
        if (count >= MaxPerUser)
        {
            return Error.Validation("report.saved_limit", $"Mund të ruani deri në {MaxPerUser} raporte.");
        }

        var (name, table, filters, columns) = valid.Value;
        var publicId = Guid.CreateVersion7();
        try
        {
            await connection.ExecuteAsync(new CommandDefinition("""
                INSERT INTO saved_reports (public_id, owner_user_id, name, report_table, filters, columns)
                VALUES (@pid, @me, @name, @table, @filters, @columns)
                """, new { pid = PublicId.ToBytes(publicId), me = user.UserId, name, table, filters, columns }, cancellationToken: ct));
        }
        catch (MySqlException ex) when (ex.Number == 1062)
        {
            return NameTaken();
        }

        audit.Record(AuditActions.ReportSaved, "REPORT", details: new { action = "CREATE", name, table });
        return await GetAsync(publicId, ct);
    }

    public async Task<Result<SavedReportView>> UpdateAsync(Guid publicId, SaveReportRequest req, CancellationToken ct)
    {
        var valid = Validate(req);
        if (valid.IsFailure)
        {
            return valid.Error!;
        }

        var (name, table, filters, columns) = valid.Value;
        await using var connection = await OpenAsync(ct);
        int changed;
        try
        {
            changed = await connection.ExecuteAsync(new CommandDefinition("""
                UPDATE saved_reports SET name = @name, report_table = @table, filters = @filters, columns = @columns
                 WHERE public_id = @pid AND owner_user_id = @me
                """, new { pid = PublicId.ToBytes(publicId), me = user.UserId, name, table, filters, columns }, cancellationToken: ct));
        }
        catch (MySqlException ex) when (ex.Number == 1062)
        {
            return NameTaken();
        }

        // MySQL reports 0 changed rows for an identical update: check the row exists instead
        if (changed == 0 && (await GetAsync(publicId, ct)).IsFailure)
        {
            return NotFound;
        }

        audit.Record(AuditActions.ReportSaved, "REPORT", details: new { action = "UPDATE", name, table });
        return await GetAsync(publicId, ct);
    }

    public async Task<Result<bool>> DeleteAsync(Guid publicId, CancellationToken ct)
    {
        await using var connection = await OpenAsync(ct);
        var deleted = await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM saved_reports WHERE public_id = @pid AND owner_user_id = @me",
            new { pid = PublicId.ToBytes(publicId), me = user.UserId }, cancellationToken: ct));
        if (deleted == 0)
        {
            return NotFound;
        }

        audit.Record(AuditActions.ReportSaved, "REPORT", details: new { action = "DELETE", publicId });
        return true;
    }

    public async Task<Result<SavedReportView>> GetAsync(Guid publicId, CancellationToken ct)
    {
        await using var connection = await OpenAsync(ct);
        var row = await connection.QuerySingleOrDefaultAsync<Row>(new CommandDefinition(
            $"{Select} WHERE public_id = @pid AND owner_user_id = @me",
            new { pid = PublicId.ToBytes(publicId), me = user.UserId }, cancellationToken: ct));
        return row is null ? NotFound : View(row);
    }

    /// <summary>Name 1–120 characters, a known table, filters an object the table understands, known columns.</summary>
    private static Result<(string Name, int Table, string Filters, string Columns)> Validate(SaveReportRequest req)
    {
        var errors = new FieldErrors();
        var name = req.Name?.Trim() ?? string.Empty;
        if (name.Length is 0 or > 120)
        {
            errors.Add("name", "Emri duhet të ketë 1 deri në 120 shkronja.");
        }

        if (req.Table is not { } table || !ReportTables.IsKnown(table))
        {
            errors.Add("table", "Tabela duhet të jetë 1, 3, 5 ose 6.");
            return errors.ToError("Raporti nuk mund të ruhet.");
        }

        var filtersJson = req.Filters is { ValueKind: JsonValueKind.Object } f ? f.GetRawText() : "{}";
        var values = ReportFilterValues.Parse(filtersJson);
        var check = values is null
            ? (Error?)Error.Validation("report.filter_invalid", "Filtrat nuk janë të vlefshëm.")
            : table is ReportTables.Active or ReportTables.Closed
                ? CaseReportBuilder.Criteria(table, values).Error
                : StatisticsBuilder.Query(table, values).Error;
        if (check is not null)
        {
            errors.Add("filters", check.Message);
        }

        var known = table switch
        {
            ReportTables.Active => ReportColumns.Active.Select(c => c.Key),
            ReportTables.Closed => ReportColumns.Closed.Select(c => c.Key),
            _ => ["court", .. Statistics.StatisticsMeasures.Counts.Select(m => m.Code)],
        };
        var columns = req.Columns ?? [];
        if (columns.Any(c => !known.Contains(c)) || columns.Distinct().Count() != columns.Count)
        {
            errors.Add("columns", "Kolonat nuk janë të vlefshme.");
        }

        return errors.Any
            ? errors.ToError("Raporti nuk mund të ruhet.")
            : (name, table, filtersJson, JsonSerializer.Serialize(columns));
    }

    private static Error NameTaken() =>
        Error.Conflict("report.saved_name_taken", "Keni tashmë një raport me këtë emër.");

    private const string Select =
        "SELECT public_id AS PublicId, name AS Name, report_table AS ReportTable, filters AS Filters, columns AS Columns, updated_at AS UpdatedAt FROM saved_reports";

    /// <summary>
    /// The request's own connection and transaction (task 051 fix): a separate connection deadlocked on the user's row —
    /// the first request of the hour updates users.last_login_at inside the request transaction, and the INSERT's
    /// foreign key to users then waited for that lock while the request waited for the INSERT. Outside a request
    /// transaction (none active): an own connection.
    /// </summary>
    private async Task<Session> OpenAsync(CancellationToken ct)
    {
        if (unitOfWork.HasActiveTransaction)
        {
            await using var probe = unitOfWork.CreateCommand("SELECT 1");
            return new Session(unitOfWork.Connection, probe.Transaction, owned: false);
        }

        var connection = new MySqlConnection(persistence.ConnectionString);
        await connection.OpenAsync(ct);
        return new Session(connection, null, owned: true);
    }

    /// <summary>A connection (+ the request transaction) that is closed at the end only when this class opened it.</summary>
    private sealed class Session(MySqlConnection connection, MySqlTransaction? transaction, bool owned) : IAsyncDisposable
    {
        public Task<IEnumerable<T>> QueryAsync<T>(CommandDefinition c) =>
            connection.QueryAsync<T>(With(c));

        public Task<T?> QuerySingleOrDefaultAsync<T>(CommandDefinition c) =>
            connection.QuerySingleOrDefaultAsync<T>(With(c));

        public Task<T?> ExecuteScalarAsync<T>(CommandDefinition c) =>
            connection.ExecuteScalarAsync<T>(With(c));

        public Task<int> ExecuteAsync(CommandDefinition c) =>
            connection.ExecuteAsync(With(c));

        private CommandDefinition With(CommandDefinition c) =>
            new(c.CommandText, c.Parameters, transaction, cancellationToken: c.CancellationToken);

        public ValueTask DisposeAsync() => owned ? connection.DisposeAsync() : ValueTask.CompletedTask;
    }

    private static SavedReportView View(Row r)
    {
        using var filters = JsonDocument.Parse(r.Filters);
        return new SavedReportView(PublicId.FromBytes(r.PublicId), r.Name, r.ReportTable, ReportTables.Name(r.ReportTable),
            filters.RootElement.Clone(), JsonSerializer.Deserialize<List<string>>(r.Columns) ?? [],
            DateTime.SpecifyKind(r.UpdatedAt, DateTimeKind.Utc));
    }

    private sealed class Row
    {
        public byte[] PublicId { get; set; } = [];
        public string Name { get; set; } = string.Empty;
        public int ReportTable { get; set; }
        public string Filters { get; set; } = "{}";
        public string Columns { get; set; } = "[]";
        public DateTime UpdatedAt { get; set; }
    }
}
