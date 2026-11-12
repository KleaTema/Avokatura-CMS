using Ash.Modules.Identity.Contracts;
using Ash.Modules.Reports.Infrastructure;
using Ash.SharedKernel.Ids;
using MySql.Data.MySqlClient;

namespace Ash.Modules.Reports.Application;

/// <summary>The party columns of both reports, as the views write them, for a page of cases in ONE grouped query.</summary>
public static class ReportParties
{
    public sealed record Parties(string? Plaintiffs, string? Defendants);

    /// <summary>
    /// Paditësi: the PLAINTIFF names "; "-separated; I padituri / Person i tretë: the DEFENDANT_OR_THIRD names with their
    /// role in brackets — both in party order, removed parties left out (as v_report_active_cases). GROUP_CONCAT skips the
    /// NULLs of the other group, so one pass over the page's parties fills both.
    /// </summary>
    public static async Task<IReadOnlyDictionary<ulong, Parties>> ForAsync(
        ReportingDb db, MySqlConnection connection, IReadOnlyList<ulong> ids, bool export, CancellationToken ct)
    {
        var rows = await db.QueryAsync<PartyDb>(connection, """
            SELECT p.case_id AS CaseId,
                   GROUP_CONCAT(CASE WHEN r.report_group = 'PLAINTIFF' THEN p.display_name END ORDER BY p.id SEPARATOR '; ') AS Plaintiffs,
                   GROUP_CONCAT(CASE WHEN r.report_group = 'DEFENDANT_OR_THIRD' THEN CONCAT(p.display_name, ' (', r.name, ')') END
                                ORDER BY p.id SEPARATOR '; ') AS Defendants
              FROM case_parties p JOIN party_roles r ON r.id = p.party_role_id
             WHERE p.case_id IN @ids AND p.deleted_at IS NULL
             GROUP BY p.case_id
            """, new { ids }, export, ct);
        return rows.ToDictionary(r => r.CaseId, r => new Parties(r.Plaintiffs, r.Defendants));
    }

    private sealed class PartyDb
    {
        public ulong CaseId { get; set; }
        public string? Plaintiffs { get; set; }
        public string? Defendants { get; set; }
    }
}

/// <summary>Readable names for the "Filtrat" sheet and the audit row (an unknown value is shown as given).</summary>
public static class ReportNames
{
    public static async Task<string> OfficeAsync(ReportingDb db, MySqlConnection connection, Guid office, CancellationToken ct) =>
        (await db.QueryAsync<string>(connection, "SELECT name FROM offices WHERE public_id = @id",
            new { id = PublicId.ToBytes(office) }, false, ct)).FirstOrDefault() ?? office.ToString();

    public static async Task<string> UserAsync(IUserDirectory users, Guid user, CancellationToken ct) =>
        await users.GetUser(user, ct) is { } u ? $"{u.FirstName} {u.LastName}" : user.ToString();

    /// <summary>name of the row of a lookup table (fixed table and column names from the code, never from input).</summary>
    public static async Task<string> LookupAsync(ReportingDb db, MySqlConnection connection, string table, string column, object value, CancellationToken ct) =>
        (await db.QueryAsync<string>(connection, $"SELECT name FROM {table} WHERE {column} = @value", new { value }, false, ct))
        .FirstOrDefault() ?? value.ToString() ?? string.Empty;
}
