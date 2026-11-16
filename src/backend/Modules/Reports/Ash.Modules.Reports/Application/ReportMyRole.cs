using Ash.Modules.Reports.Infrastructure;
using Ash.SharedKernel.Ids;
using MySql.Data.MySqlClient;

namespace Ash.Modules.Reports.Application;

/// <summary>
/// Task 053h / 053i: the "Roli im" column of Tabela 1 / 3 — the reader's own role in each case (Avokat primar, Avokat
/// sekondar, Ndihmës ligjor, Jurist): the current assignment, else the latest one (a closed case, a later transfer).
/// Empty for a case the reader sees without a role (the Kryetar's office, the Monitor). One query per page / batch.
/// </summary>
public static class ReportMyRole
{
    public const string ColumnKey = "myRole";

    private static readonly Dictionary<string, string> Names = new()
    {
        ["PRIMARY_LAWYER"] = "Avokat primar",
        ["SECONDARY_LAWYER"] = "Avokat sekondar",
        ["LEGAL_ASSISTANT"] = "Ndihmës ligjor",
        ["INSTITUTION_JURIST"] = "Jurist i institucionit",
    };

    public static async Task<IReadOnlyDictionary<Guid, string>> ForAsync(
        ReportingDb db, MySqlConnection connection, IReadOnlyCollection<ulong> ids, long userId, bool export, CancellationToken ct)
    {
        if (ids.Count == 0)
        {
            return new Dictionary<Guid, string>();
        }

        var rows = await db.QueryAsync<(byte[] PublicId, string Role)>(connection, """
            SELECT c.public_id, a.case_role
              FROM case_assignments a JOIN cases c ON c.id = a.case_id
             WHERE a.case_id IN @ids AND a.user_id = @me
             ORDER BY a.case_id, (a.ended_at IS NULL) DESC, a.id DESC
            """, new { ids, me = userId }, export, ct);
        var result = new Dictionary<Guid, string>();
        foreach (var (publicId, role) in rows)
        {
            result.TryAdd(PublicId.FromBytes(publicId), Names.GetValueOrDefault(role, role));
        }

        return result;
    }
}
