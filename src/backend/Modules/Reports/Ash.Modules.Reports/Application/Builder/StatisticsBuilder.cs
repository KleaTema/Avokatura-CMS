using System.Globalization;
using Ash.Modules.Reports.Application.Statistics;
using Ash.Modules.Reports.Infrastructure;
using Ash.SharedKernel.Ids;
using Ash.SharedKernel.Results;
using Ash.SharedKernel.Time;
using Dapper;

namespace Ash.Modules.Reports.Application.Builder;

/// <summary>
/// Task 053h: the filter builder of Tabela 5 / 6. The filters are the statistics' own (year, zyra → avokati, shkalla →
/// gjykata, ministria → institucioni in Tabela 6); the options are limited by the reader (ReportAudience): a Kryetar's
/// own office, a lawyer / assistant's own lawyers, an institution's own institution (fixed and read-only). The values
/// become a <see cref="StatisticsQuery"/>, so StatisticsScope still forces and checks every one of them.
/// </summary>
public sealed class StatisticsBuilder(ReportingDb db, ReportAudienceResolver audiences, IClock clock)
{
    private static readonly string[] Keys = ["year", "office", "lawyer", "courtLevel", "court", "ministry", "institution"];

    /// <summary>The filter values as a statistics query; 400 for an unknown key or a value that cannot be read.</summary>
    public static Result<StatisticsQuery> Query(int table, ReportFilterValues values)
    {
        if (values.Keys.FirstOrDefault(k => !Keys.Contains(k)) is { } unknown)
        {
            return Error.Validation("report.filter_unknown", $"Filtër i panjohur: {unknown}.");
        }

        static Result<Guid?> Id(ReportFilterValues v, string key, string label) =>
            v.Text(key) is not { } text ? (Guid?)null
            : Guid.TryParse(text, out var id) ? id
            : Error.Validation("report.filter_invalid", $"Vlera e filtrit \"{label}\" nuk është e vlefshme.");

        int? year = null;
        if (values.Text("year") is { } yearText)
        {
            if (!int.TryParse(yearText, NumberStyles.None, CultureInfo.InvariantCulture, out var y))
            {
                return Error.Validation("report.filter_invalid", "Vlera e filtrit \"Viti\" nuk është e vlefshme.");
            }

            year = y;
        }

        int? court = null;
        if (values.Text("court") is { } courtText)
        {
            if (!int.TryParse(courtText, NumberStyles.None, CultureInfo.InvariantCulture, out var c))
            {
                return Error.Validation("report.filter_invalid", "Vlera e filtrit \"Gjykata\" nuk është e vlefshme.");
            }

            court = c;
        }

        var office = Id(values, "office", "Zyra");
        var lawyer = Id(values, "lawyer", "Avokati");
        var ministry = Id(values, "ministry", "Ministria");
        var institution = Id(values, "institution", "Institucioni");
        foreach (var r in new[] { office, lawyer, ministry, institution })
        {
            if (r.IsFailure)
            {
                return r.Error!;
            }
        }

        // Tabela 6: an institution narrows more than its ministry — the institution wins when both are chosen
        return new StatisticsQuery(year, table, office.Value, lawyer.Value,
            institution.Value, institution.Value is null ? ministry.Value : null,
            values.Text("courtLevel"), court);
    }

    public async Task<Result<ReportFiltersResponse>> MetaAsync(int table, ReportFilterValues values, CancellationToken ct)
    {
        var resolved = await audiences.ResolveAsync(ct);
        if (resolved.IsFailure || resolved.Value.StatisticsDenied)
        {
            return ReportAudience.StatisticsForbidden;
        }

        var check = Query(table, values);
        if (check.IsFailure)
        {
            return check.Error!;
        }

        var audience = resolved.Value;
        await using var connection = await db.OpenAsync(ct);
        var p = new DynamicParameters();
        var scoped = $"FROM cases c WHERE {audience.CasesSql(p)}";

        async Task<List<ReportFilterOption>> Options(string sql, DynamicParameters parameters) =>
            (await db.QueryAsync<OptionDb>(connection, sql, parameters, false, ct))
            .Select(r => new ReportFilterOption(
                r.Bytes is { Length: 16 } bytes ? PublicId.FromBytes(bytes).ToString() : r.Value ?? string.Empty, r.Label))
            .ToList();

        // the years: from the oldest case of the reader's scope to this year, newest first
        var thisYear = TiraneTime.Today(clock).Year;
        var oldest = await db.ScalarAsync<int?>(connection, $"SELECT MIN(YEAR(c.registered_at)) {scoped}", p, false, ct) ?? thisYear;
        var years = Enumerable.Range(Math.Max(2000, Math.Min(oldest, thisYear)), thisYear - Math.Max(2000, Math.Min(oldest, thisYear)) + 1)
            .Reverse()
            .Select(y => new ReportFilterOption(y.ToString(CultureInfo.InvariantCulture), y.ToString(CultureInfo.InvariantCulture)))
            .ToList();

        // the offices: a Kryetar his own; everyone else the offices of the cases they see
        var offices = audience.Kind == ReportAudienceKind.Head
            ? await Options("SELECT o.public_id AS Bytes, o.name AS Label FROM offices o WHERE o.id = @own",
                new DynamicParameters(new { own = audience.OfficeId }))
            : await Options($"SELECT o.public_id AS Bytes, o.name AS Label FROM offices o WHERE o.id IN (SELECT c.office_id {scoped}) ORDER BY o.name", p);

        // the lawyers: a lawyer / assistant his own lawyers; else whoever was ever primary on the cases of the scope
        // (under the chosen office)
        var lp = new DynamicParameters(p);
        var officeLimit = string.Empty;
        if (values.Text("office") is { } officeText && Guid.TryParse(officeText, out var officeId))
        {
            lp.Add("bf_office", PublicId.ToBytes(officeId));
            officeLimit = " AND c.office_id = (SELECT o.id FROM offices o WHERE o.public_id = @bf_office)";
        }

        var lawyers = audience.Kind == ReportAudienceKind.Lawyers
            ? await Options(
                "SELECT u.public_id AS Bytes, CONCAT(u.first_name, ' ', u.last_name) AS Label FROM users u WHERE u.id IN @mine ORDER BY Label",
                new DynamicParameters(new { mine = audience.LawyerIds.DefaultIfEmpty(0L).ToList() }))
            : await Options($"""
                SELECT u.public_id AS Bytes, CONCAT(u.first_name, ' ', u.last_name) AS Label FROM users u
                 WHERE u.id IN (SELECT a.user_id FROM case_assignments a WHERE a.case_role = 'PRIMARY_LAWYER'
                                 AND a.case_id IN (SELECT c.id {scoped}{officeLimit}))
                 ORDER BY Label
                """, lp);

        var levels = await Options(
            $"SELECT cl.code AS Value, cl.name AS Label FROM court_levels cl WHERE cl.id IN (SELECT c.court_level_id {scoped}) ORDER BY cl.id", p);
        var cp = new DynamicParameters(p);
        var levelLimit = string.Empty;
        if (values.Text("courtLevel") is { } level)
        {
            cp.Add("bf_level", level);
            levelLimit = " AND c.court_level_id = (SELECT cl.id FROM court_levels cl WHERE cl.code = @bf_level)";
        }

        var courts = await Options(
            $"SELECT CAST(ct.id AS CHAR) AS Value, ct.name AS Label FROM courts ct WHERE ct.id IN (SELECT c.court_id {scoped}{levelLimit}) ORDER BY ct.name", cp);

        var filters = new List<ReportFilterMeta>
        {
            new("year", "Viti", ReportFilterKinds.Select, null, true, true, false, years),
            new("office", "Zyra", ReportFilterKinds.Select, null, true, false, false, offices),
            new("lawyer", "Avokati", ReportFilterKinds.Select, "office", table == ReportTables.Statistics, false, false, lawyers),
            new("courtLevel", "Shkalla", ReportFilterKinds.Select, null, false, false, false, levels),
            new("court", "Gjykata", ReportFilterKinds.Select, "courtLevel", false, false, false, courts),
        };

        var fixedValues = new Dictionary<string, string>();
        if (table == ReportTables.Institutional)
        {
            if (audience.Kind == ReportAudienceKind.Institution)
            {
                // fixed to the reader's institution, read-only (a ministry: with its agencies, as Tabela 6 by ministry)
                var own = await db.QueryAsync<OptionDb>(connection,
                    "SELECT i.public_id AS Bytes, i.name AS Label FROM institutions i WHERE i.id = @id",
                    new { id = audience.InstitutionId }, false, ct);
                var option = own.Select(r => new ReportFilterOption(PublicId.FromBytes(r.Bytes!).ToString(), r.Label)).ToList();
                var key = audience.InstitutionWithChildren ? "ministry" : "institution";
                filters.Add(new ReportFilterMeta(key, key == "ministry" ? "Ministria" : "Institucioni", ReportFilterKinds.Select,
                    null, true, true, true, option));
                if (option.Count == 1)
                {
                    fixedValues[key] = option[0].Value;
                }
            }
            else
            {
                var ministries = await Options("""
                    SELECT i.public_id AS Bytes, i.name AS Label FROM institutions i WHERE i.institution_type = 'MINISTRY' AND i.is_active = 1
                     ORDER BY i.name
                    """, new DynamicParameters());
                var ip = new DynamicParameters();
                var ministryLimit = string.Empty;
                if (values.Text("ministry") is { } ministryText && Guid.TryParse(ministryText, out var ministryId))
                {
                    ip.Add("bf_ministry", PublicId.ToBytes(ministryId));
                    ministryLimit = " AND (i.id = (SELECT im.id FROM institutions im WHERE im.public_id = @bf_ministry)" +
                                    " OR i.parent_institution_id = (SELECT im.id FROM institutions im WHERE im.public_id = @bf_ministry))";
                }

                var institutions = await Options(
                    $"SELECT i.public_id AS Bytes, i.name AS Label FROM institutions i WHERE i.is_active = 1{ministryLimit} ORDER BY i.name", ip);
                filters.Add(new("ministry", "Ministria", ReportFilterKinds.Select, null, true, false, false, ministries));
                filters.Add(new("institution", "Institucioni", ReportFilterKinds.Select, "ministry", true, false, false, institutions));
            }
        }

        var columns = new List<ReportColumnMeta> { new("court", "Gjykata", nameof(ReportColumnKind.Text), false) };
        columns.AddRange(StatisticsMeasures.Counts.Select(m => new ReportColumnMeta(m.Code, m.Name, nameof(ReportColumnKind.Integer), true)));
        return new ReportFiltersResponse(table, ReportTables.Name(table), filters, columns, fixedValues);
    }

    private sealed class OptionDb
    {
        public string? Value { get; set; }
        public byte[]? Bytes { get; set; }
        public string Label { get; set; } = string.Empty;
    }
}
