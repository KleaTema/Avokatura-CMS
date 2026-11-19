using System.Globalization;
using System.Text;
using Ash.SharedKernel.Ids;
using Ash.SharedKernel.Results;
using Ash.SharedKernel.Time;
using Dapper;

namespace Ash.Modules.Reports.Application.Builder;

/// <summary>
/// One filter of Tabela 1 / 3 (task 053h): how it narrows the cases (a predicate over alias c, and d = case_decisions in
/// Tabela 3) and, for a select, which options exist. Every value is a bound parameter; the SQL text is fixed per filter.
/// </summary>
internal sealed record CaseFilterDef(
    string Key,
    string Label,
    string Kind,
    string? Parent = null,
    bool IsDefault = false,
    string? Column = null,
    bool UtcDate = false,
    Func<string, string>? SelectWhere = null,
    Func<string, string>? Options = null,
    IReadOnlyDictionary<string, string>? Labels = null,
    bool PublicIdValue = false,
    bool IntValue = false);

/// <summary>
/// The filters of Tabela 1 (active) and Tabela 3 (closed): one per column of the table, plus the dependent pairs
/// (shkalla → gjykata → gjyqtari, zyra → avokati, ministria → institucioni, kategoria → lloji). Defined HERE ONLY: the
/// metadata endpoint, the report, the exports and the saved reports all read these definitions.
/// </summary>
internal static class CaseReportFilters
{
    private const string CurrentLawyer = "a.ended_at IS NULL AND a.case_role IN ('PRIMARY_LAWYER', 'SECONDARY_LAWYER')";
    private const string PrimaryAtClosing =
        "a.case_role = 'PRIMARY_LAWYER' AND a.assigned_at <= c2.closed_at AND (a.ended_at IS NULL OR a.ended_at > c2.closed_at)";

    public static readonly IReadOnlyDictionary<string, string> PriorityNames = new Dictionary<string, string>
    {
        ["NONE"] = "Pa prioritet",
        ["LOW"] = "I ulët",
        ["MEDIUM"] = "Mesatar",
        ["HIGH"] = "I lartë",
    };

    /// <summary>The institutions that are a party of / linked to the cases of <paramref name="scoped"/>.</summary>
    private static string InstitutionIds(string scoped) => $"""
        SELECT p.institution_id FROM case_parties p
         WHERE p.deleted_at IS NULL AND p.institution_id IS NOT NULL AND p.case_id IN (SELECT c.id {scoped})
        UNION
        SELECT ci.institution_id FROM case_institutions ci WHERE ci.ended_at IS NULL AND ci.case_id IN (SELECT c.id {scoped})
        """;

    // ---------------------------------------------------------------- shared by both tables

    private static CaseFilterDef Office(bool isDefault) => new("office", "Zyra", ReportFilterKinds.Select, IsDefault: isDefault,
        PublicIdValue: true,
        SelectWhere: p => $"c.office_id = (SELECT o.id FROM offices o WHERE o.public_id = {p})",
        Options: s => $"SELECT o.public_id AS Bytes, o.name AS Label FROM offices o WHERE o.id IN (SELECT c.office_id {s}) ORDER BY o.name");

    private static CaseFilterDef Level => new("courtLevel", "Shkalla", ReportFilterKinds.Select,
        SelectWhere: p => $"c.court_level_id = (SELECT cl.id FROM court_levels cl WHERE cl.code = {p})",
        Options: s => $"SELECT cl.code AS Value, cl.name AS Label FROM court_levels cl WHERE cl.id IN (SELECT c.court_level_id {s}) ORDER BY cl.id");

    private static CaseFilterDef Court(bool isDefault) => new("court", "Gjykata", ReportFilterKinds.Select, Parent: "courtLevel",
        IsDefault: isDefault, IntValue: true,
        SelectWhere: p => $"c.court_id = {p}",
        Options: s => $"SELECT CAST(ct.id AS CHAR) AS Value, ct.name AS Label FROM courts ct WHERE ct.id IN (SELECT c.court_id {s}) ORDER BY ct.name");

    private static CaseFilterDef Judge => new("judge", "Gjyqtari", ReportFilterKinds.Select, Parent: "court", IntValue: true,
        SelectWhere: p => $"c.judge_id = {p}",
        Options: s => $"""
            SELECT CAST(j.id AS CHAR) AS Value, CONCAT(j.first_name, ' ', j.last_name) AS Label
              FROM judges j WHERE j.id IN (SELECT c.judge_id {s}) ORDER BY j.last_name, j.first_name
            """);

    private static CaseFilterDef Ministry => new("ministry", "Ministria", ReportFilterKinds.Select, PublicIdValue: true,
        SelectWhere: p => ReportAudience.InstitutionSql("c.id", $"(SELECT im.id FROM institutions im WHERE im.public_id = {p})", "1"),
        Options: s => $"""
            SELECT i.public_id AS Bytes, i.name AS Label FROM institutions i
             WHERE i.institution_type = 'MINISTRY'
               AND (i.id IN ({InstitutionIds(s)})
                    OR i.id IN (SELECT ic.parent_institution_id FROM institutions ic WHERE ic.id IN ({InstitutionIds(s)})))
             ORDER BY i.name
            """);

    private static CaseFilterDef Institution => new("institution", "Institucioni", ReportFilterKinds.Select, Parent: "ministry",
        PublicIdValue: true,
        SelectWhere: p => ReportAudience.InstitutionSql("c.id", $"(SELECT ix.id FROM institutions ix WHERE ix.public_id = {p})", "0"),
        // under a chosen ministry: the ministry itself and its direct agencies (the parent's predicate is in s)
        Options: s => $"SELECT i.public_id AS Bytes, i.name AS Label FROM institutions i WHERE i.id IN ({InstitutionIds(s)}) /*MINISTRY*/ ORDER BY i.name");

    private static CaseFilterDef Category => new("category", "Kategoria", ReportFilterKinds.Select,
        SelectWhere: p => $"c.category_id = (SELECT cc.id FROM case_categories cc WHERE cc.code = {p})",
        Options: s => $"SELECT cc.code AS Value, cc.name AS Label FROM case_categories cc WHERE cc.id IN (SELECT c.category_id {s}) ORDER BY cc.name");

    private static CaseFilterDef Subcategory => new("subcategory", "Lloji", ReportFilterKinds.Select, Parent: "category",
        SelectWhere: p => $"c.subcategory_id IN (SELECT cs.id FROM case_subcategories cs WHERE cs.code = {p})",
        Options: s => $"SELECT cs.code AS Value, cs.name AS Label FROM case_subcategories cs WHERE cs.id IN (SELECT c.subcategory_id {s}) ORDER BY cs.name");

    private static CaseFilterDef Text(string key, string label, string column) =>
        new(key, label, ReportFilterKinds.Text, Column: column);

    private static CaseFilterDef Party(string key, string label, string group) =>
        new(key, label, ReportFilterKinds.Text, SelectWhere: p => $"""
            EXISTS (SELECT 1 FROM case_parties pt JOIN party_roles pr ON pr.id = pt.party_role_id
                     WHERE pt.case_id = c.id AND pt.deleted_at IS NULL AND pr.report_group = '{group}' AND pt.display_name LIKE {p})
            """);

    private static CaseFilterDef Range(string key, string label, string column) =>
        new(key, label, ReportFilterKinds.NumberRange, Column: column);

    private static CaseFilterDef YesNo(string key, string label, string column) =>
        new(key, label, ReportFilterKinds.YesNo, Column: column);

    // ---------------------------------------------------------------- Tabela 1

    public static readonly IReadOnlyList<CaseFilterDef> Active =
    [
        Office(isDefault: true),
        new("lawyer", "Avokati", ReportFilterKinds.Select, Parent: "office", IsDefault: true, PublicIdValue: true,
            // the current primary or secondary (as the report's column and the case list's filter)
            SelectWhere: p => $"EXISTS (SELECT 1 FROM case_assignments a JOIN users u ON u.id = a.user_id WHERE a.case_id = c.id AND {CurrentLawyer} AND u.public_id = {p})",
            Options: s => $"""
                SELECT u.public_id AS Bytes, CONCAT(u.first_name, ' ', u.last_name) AS Label FROM users u
                 WHERE u.id IN (SELECT a.user_id FROM case_assignments a WHERE {CurrentLawyer} AND a.case_id IN (SELECT c.id {s}))
                 ORDER BY u.first_name, u.last_name
                """),
        Level,
        Court(isDefault: true),
        Judge,
        Ministry,
        Institution,
        Category,
        Subcategory,
        Text("caseCode", "Kodi unik", "c.case_code"),
        Text("actNumber", "Numri i aktit", "c.act_number"),
        Party("plaintiffs", "Paditësi", "PLAINTIFF"),
        Party("defendants", "I padituri / Person i tretë", "DEFENDANT_OR_THIRD"),
        Text("objectText", "Objekti", "c.object_text"),
        new("nextHearingAt", "Seanca e radhës", ReportFilterKinds.DateRange, Column: "c.next_hearing_at", UtcDate: true),
        new("lastHearingDescription", "Përshkrimi i seancës së fundit", ReportFilterKinds.Text,
            SelectWhere: p => $"EXISTS (SELECT 1 FROM case_hearings h WHERE h.id = c.last_hearing_id AND h.description LIKE {p})"),
        YesNo("hasSecurityMeasure", "Masa e sigurimit", "c.has_security_measure"),
        Range("claimAmount", "Vlera e padisë", "c.claim_amount"),
        new("claimCurrency", "Monedha", ReportFilterKinds.Select,
            SelectWhere: p => $"c.claim_currency = {p}",
            Options: s => $"SELECT DISTINCT c.claim_currency AS Value, c.claim_currency AS Label {s} AND c.claim_currency IS NOT NULL ORDER BY Value"),
        Range("claimAreaM2", "Sipërfaqja (m²)", "c.claim_area_m2"),
        new("priority", "Prioriteti", ReportFilterKinds.Select, Labels: PriorityNames,
            SelectWhere: p => $"c.priority = {p}",
            Options: s => $"SELECT DISTINCT c.priority AS Value, c.priority AS Label {s} ORDER BY FIELD(Value, 'HIGH', 'MEDIUM', 'LOW', 'NONE')"),
        YesNo("isSuspended", "Pezulluar", "c.is_suspended"),
    ];

    // ---------------------------------------------------------------- Tabela 3

    public static readonly IReadOnlyList<CaseFilterDef> Closed =
    [
        new("closedAt", "Data e mbylljes", ReportFilterKinds.DateRange, IsDefault: true, Column: "c.closed_at", UtcDate: true),
        new("result", "Rezultati", ReportFilterKinds.Select, IsDefault: true,
            SelectWhere: p => $"d.result_id = (SELECT dr.id FROM decision_results dr WHERE dr.code = {p})",
            Options: s => $"SELECT dr.code AS Value, dr.name AS Label FROM decision_results dr WHERE dr.id IN (SELECT d.result_id {s}) ORDER BY dr.sort_order, dr.name"),
        Office(isDefault: false),
        new("lawyer", "Avokati", ReportFilterKinds.Select, Parent: "office", IsDefault: true, PublicIdValue: true,
            // the report's column: the primary lawyer at closing
            SelectWhere: p => $"""
                EXISTS (SELECT 1 FROM case_assignments a JOIN cases c2 ON c2.id = a.case_id JOIN users u ON u.id = a.user_id
                         WHERE a.case_id = c.id AND {PrimaryAtClosing} AND u.public_id = {p})
                """,
            Options: s => $"""
                SELECT u.public_id AS Bytes, CONCAT(u.first_name, ' ', u.last_name) AS Label FROM users u
                 WHERE u.id IN (SELECT a.user_id FROM case_assignments a JOIN cases c2 ON c2.id = a.case_id
                                 WHERE {PrimaryAtClosing} AND c2.id IN (SELECT c.id {s}))
                 ORDER BY u.first_name, u.last_name
                """),
        Level,
        Court(isDefault: false),
        Judge,
        Ministry,
        Institution,
        Category,
        Subcategory,
        Text("caseCode", "Kodi unik", "c.case_code"),
        Text("actNumber", "Numri i aktit", "c.act_number"),
        Party("plaintiffs", "Paditësi", "PLAINTIFF"),
        Party("defendants", "I padituri / Person i tretë", "DEFENDANT_OR_THIRD"),
        Text("objectText", "Objekti", "c.object_text"),
        Text("resultOtherText", "Rezultati (tjetër)", "d.result_other_text"),
        Text("decisionNumber", "Numri i vendimit", "d.decision_number"),
        new("decisionDate", "Data e vendimit", ReportFilterKinds.DateRange, Column: "d.decision_date"),
        Text("dispositive", "Dispozitivi", "d.dispositive"),
        new("currency", "Monedha", ReportFilterKinds.Select,
            SelectWhere: p => $"d.currency = {p}",
            Options: s => $"SELECT DISTINCT d.currency AS Value, d.currency AS Label {s} AND d.currency IS NOT NULL ORDER BY Value"),
        Range("wonAmount", "Fituar (vlera)", "d.won_amount"),
        Range("wonAreaM2", "Fituar (m²)", "d.won_area_m2"),
        Range("lostAmount", "Humbur (vlera)", "d.lost_amount"),
        Range("lostAreaM2", "Humbur (m²)", "d.lost_area_m2"),
        Range("preventedDamageAmount", "Dëm i parandaluar (vlera)", "d.prevented_damage_amount"),
        Range("preventedDamageAreaM2", "Dëm i parandaluar (m²)", "d.prevented_damage_area_m2"),
        Range("causedDamageAmount", "Dëm i shkaktuar (vlera)", "d.caused_damage_amount"),
        Range("causedDamageAreaM2", "Dëm i shkaktuar (m²)", "d.caused_damage_area_m2"),
        Range("courtCostsAmount", "Shpenzime gjyqësore", "d.court_costs_amount"),
        YesNo("hasSecurityMeasure", "Masa e sigurimit", "c.has_security_measure"),
        YesNo("isAppealed", "Ankimuar", "d.is_appealed"),
        YesNo("isRecursed", "Rekursuar", "d.is_recursed"),
        YesNo("isFinal", "Formë e prerë", "d.is_final"),
        YesNo("decisionReceived", "Vendimi i arsyetuar ardhur", "d.decision_received"),
    ];

    // ---------------------------------------------------------------- the "Vazhdo çështje ekzistuese" lookup (task 053j)

    /// <summary>The lookup's own filters (Cases module, task 053b): never offered again under "+ Shto filtër".</summary>
    private static readonly HashSet<string> LookupOwnKeys =
    [
        "caseCode", "actNumber", "courtLevel", "court", "judge", "plaintiffs", "defendants", "objectText", "category",
        "subcategory", "result",
    ];

    private static CaseFilterDef RegisteredAt =>
        new("registeredAt", "Data e regjistrimit", ReportFilterKinds.DateRange, Column: "c.registered_at", UtcDate: true);

    /// <summary>
    /// "+ Shto filtër" of the lookup: every column of Tabela 3 and Tabela 1 (once each; Tabela 3's meaning first: the
    /// lookup shows mostly closed levels, so "Avokati" is the primary lawyer at closing) plus the registration date —
    /// minus the lookup's own filters. Over alias c and d = LEFT JOIN case_decisions (no decision → no d match).
    /// </summary>
    public static readonly IReadOnlyList<CaseFilterDef> Lookup =
        Closed.Concat(Active.Where(a => Closed.All(c => c.Key != a.Key)))
            .Append(RegisteredAt)
            .Where(d => !LookupOwnKeys.Contains(d.Key))
            .Select(d => d with { IsDefault = false })
            .ToList();

    /// <summary>The lookup's rows: the latest level of every dossier of the country (alias c, d).</summary>
    public const string LookupFrom =
        "FROM cases c LEFT JOIN case_decisions d ON d.case_id = c.id " +
        "WHERE NOT EXISTS (SELECT 1 FROM cases nx WHERE nx.dossier_id = c.dossier_id AND nx.level_seq > c.level_seq)";

    public static IReadOnlyList<CaseFilterDef> For(int table) => table == ReportTables.Closed ? Closed : Active;

    /// <summary>The table's base (FROM … WHERE …) without the scope: Tabela 1 = not closed, Tabela 3 = closed with a decision.</summary>
    public static (string From, string Base) Base(int table) => table == ReportTables.Closed
        ? ("cases c JOIN case_decisions d ON d.case_id = c.id", "c.lifecycle_status = 'CLOSED'")
        : ("cases c", "c.lifecycle_status <> 'CLOSED'");

    /// <summary>
    /// The predicate of one filter for its chosen value ("" when not chosen), with its parameters; a validation error
    /// for a value that cannot be read (a date, a number, an id).
    /// </summary>
    public static Result<string> Predicate(CaseFilterDef def, ReportFilterValues values, DynamicParameters p)
    {
        var name = "@bf_" + def.Key;
        switch (def.Kind)
        {
            case ReportFilterKinds.Select:
            {
                if (values.Text(def.Key) is not { } value)
                {
                    return string.Empty;
                }

                if (def.PublicIdValue)
                {
                    if (!Guid.TryParse(value, out var id))
                    {
                        return Invalid(def);
                    }

                    p.Add(name[1..], PublicId.ToBytes(id));
                }
                else if (def.IntValue)
                {
                    if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var number))
                    {
                        return Invalid(def);
                    }

                    p.Add(name[1..], number);
                }
                else
                {
                    p.Add(name[1..], value);
                }

                return def.SelectWhere!(name);
            }

            case ReportFilterKinds.Text:
            {
                if (values.Text(def.Key) is not { } value)
                {
                    return string.Empty;
                }

                // a contained text from 1 character (live search); % _ \ typed by the user are literal
                p.Add(name[1..], Ash.Infrastructure.Persistence.TextSearch.LikePattern(value.Trim()));
                return def.SelectWhere is { } custom ? custom(name) : $"{def.Column} LIKE {name}";
            }

            case ReportFilterKinds.YesNo:
            {
                if (values.Bool(def.Key) is not { } yes)
                {
                    return string.Empty;
                }

                p.Add(name[1..], yes);
                return $"{def.Column} = {name}";
            }

            case ReportFilterKinds.DateRange:
            {
                var (fromText, toText) = values.Range(def.Key, "from", "to");
                var sql = new List<string>();
                if (fromText is not null)
                {
                    if (!DateOnly.TryParseExact(fromText, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var from))
                    {
                        return Invalid(def);
                    }

                    p.Add($"bf_{def.Key}_from", def.UtcDate ? TiraneTime.StartOfDayUtc(from) : from.ToDateTime(TimeOnly.MinValue));
                    sql.Add($"{def.Column} >= {name}_from");
                }

                if (toText is not null)
                {
                    if (!DateOnly.TryParseExact(toText, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var to))
                    {
                        return Invalid(def);
                    }

                    // the whole last day
                    var next = to.AddDays(1);
                    p.Add($"bf_{def.Key}_to", def.UtcDate ? TiraneTime.StartOfDayUtc(next) : next.ToDateTime(TimeOnly.MinValue));
                    sql.Add($"{def.Column} < {name}_to");
                }

                return string.Join(" AND ", sql);
            }

            case ReportFilterKinds.NumberRange:
            {
                var (minText, maxText) = values.Range(def.Key, "min", "max");
                var sql = new List<string>();
                foreach (var (text, suffix, op) in new[] { (minText, "min", ">="), (maxText, "max", "<=") })
                {
                    if (text is null)
                    {
                        continue;
                    }

                    if (!decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var number))
                    {
                        return Invalid(def);
                    }

                    p.Add($"bf_{def.Key}_{suffix}", number);
                    sql.Add($"{def.Column} {op} {name}_{suffix}");
                }

                return string.Join(" AND ", sql);
            }
        }

        return string.Empty;
    }

    private static Error Invalid(CaseFilterDef def) =>
        Error.Validation("report.filter_invalid", $"Vlera e filtrit \"{def.Label}\" nuk është e vlefshme.");

    /// <summary>"AND …" of every chosen filter (the filter's own text never reaches the SQL, only its parameter).</summary>
    public static Result<string> Where(int table, ReportFilterValues values, DynamicParameters p, IEnumerable<CaseFilterDef>? only = null) =>
        Where(For(table), values, p, only);

    /// <summary>"AND …" of every chosen filter of <paramref name="defs"/> (a table's, or the lookup's).</summary>
    public static Result<string> Where(
        IReadOnlyList<CaseFilterDef> defs, ReportFilterValues values, DynamicParameters p, IEnumerable<CaseFilterDef>? only = null)
    {
        var unknown = values.Keys.FirstOrDefault(k => defs.All(d => d.Key != k));
        if (unknown is not null)
        {
            return Error.Validation("report.filter_unknown", $"Filtër i panjohur: {unknown}.");
        }

        var sql = new StringBuilder();
        foreach (var def in only ?? defs)
        {
            var predicate = Predicate(def, values, p);
            if (predicate.IsFailure)
            {
                return predicate.Error!;
            }

            if (predicate.Value.Length > 0)
            {
                sql.Append(" AND (").Append(predicate.Value).Append(')');
            }
        }

        return sql.ToString();
    }

    /// <summary>The filter and its ancestors (parent, parent's parent): what limits its options.</summary>
    public static IEnumerable<CaseFilterDef> Ancestors(int table, CaseFilterDef def) => Ancestors(For(table), def);

    public static IEnumerable<CaseFilterDef> Ancestors(IReadOnlyList<CaseFilterDef> defs, CaseFilterDef def)
    {
        var current = def.Parent;
        while (current is not null && defs.FirstOrDefault(d => d.Key == current) is { } parent)
        {
            yield return parent;
            current = parent.Parent;
        }
    }
}
