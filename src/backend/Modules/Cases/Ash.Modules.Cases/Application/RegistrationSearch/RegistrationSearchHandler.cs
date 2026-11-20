using Ash.Infrastructure.Persistence;
using Ash.Modules.Audit.Contracts;
using Ash.Modules.Cases.Application.RegistrationSearch.Filters;
using Ash.Modules.Cases.Contracts;
using Ash.Modules.Cases.Infrastructure;
using Ash.Modules.Identity.Contracts;
using Ash.Modules.Reference.Contracts;
using Ash.Modules.Reports.Contracts;
using Ash.SharedKernel.Paging;
using Ash.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;
using MySql.Data.MySqlClient;

namespace Ash.Modules.Cases.Application.RegistrationSearch;

/// <summary>
/// Filters: the values of <see cref="RegistrationSearchFilters"/> by name (code, actNumber, courtLevel, courtId, …).
/// More: the "+ Shto filtër" values (the report builder's JSON object, ICaseFilterCatalog). Status: see
/// <see cref="RegistrationSearchStatus"/>; none = <see cref="RegistrationSearchStatus.Continuable"/>.
/// </summary>
public sealed record RegistrationSearchQuery(
    IReadOnlyList<KeyValuePair<string, string>> Filters, string? After, int? Limit, string? Status = null, string? More = null);

/// <summary>The status choice of "Vazhdo çështje ekzistuese" (task 053j).</summary>
public static class RegistrationSearchStatus
{
    /// <summary>"Mund të vazhdohen" (the default): the latest level CLOSED, its decision not final, the dossier not archived.</summary>
    public const string Continuable = "CONTINUABLE";
    public const string All = "ALL";
    /// <summary>The latest level not closed in the system.</summary>
    public const string Open = "OPEN";
    public const string Closed = "CLOSED";

    public static readonly IReadOnlyList<string> Values = [Continuable, All, Open, Closed];

    /// <summary>Task 054c: the status choice with its names — the ONE source of the page's select (no list in the page).</summary>
    public static readonly IReadOnlyList<Ash.SharedKernel.Filters.FilterOption> Options =
    [
        new(Continuable, "Mund të vazhdohen"), new(All, "Të gjitha"), new(Open, "Të hapura"), new(Closed, "Të mbyllura"),
    ];
}

/// <summary>
/// The SQL of the lookup (public for the EXPLAIN tests, as task 019's): fixed text only, every value a parameter
/// (@after_key, @after_id, @take, the filters' own).
/// </summary>
public static class RegistrationSearchSql
{
    /// <summary>CONTINUABLE / CLOSED page by closing time (ix_cases_status_closed); ALL / OPEN by id (the primary key).</summary>
    public static bool ByClosing(string status) => status is RegistrationSearchStatus.Continuable or RegistrationSearchStatus.Closed;

    /// <summary>
    /// FROM … WHERE: the latest level (c) of every dossier — no later level exists (uq_cases_dossier_level) — in the
    /// chosen status, where each filter predicate matches some level (a) of the dossier and the "+ Shto filtër" SQL
    /// (<paramref name="more"/>) matches c (and d, its decision).
    /// </summary>
    public static string Matching(IEnumerable<string> filterPredicates, string status, string more)
    {
        var statusSql = status switch
        {
            RegistrationSearchStatus.Continuable => """
                AND c.lifecycle_status = 'CLOSED' AND (d.is_final IS NULL OR d.is_final = 0)
                AND NOT EXISTS (SELECT 1 FROM dossiers ds WHERE ds.id = c.dossier_id AND ds.archived_at IS NOT NULL)
                """,
            RegistrationSearchStatus.Closed => "AND c.lifecycle_status = 'CLOSED'",
            RegistrationSearchStatus.Open => "AND c.lifecycle_status <> 'CLOSED'",
            _ => string.Empty,
        };
        return $"""
            FROM cases c LEFT JOIN case_decisions d ON d.case_id = c.id
            WHERE NOT EXISTS (SELECT 1 FROM cases nx WHERE nx.dossier_id = c.dossier_id AND nx.level_seq > c.level_seq)
            {statusSql}
            """
            + string.Concat(filterPredicates.Select(p => $"\n  AND c.dossier_id IN (SELECT a.dossier_id FROM cases a WHERE {p})"))
            + more;
    }

    /// <summary>One page of ids after the cursor, newest first.</summary>
    public static string Page(string matching, string status)
    {
        var keyset = ByClosing(status)
            ? """
              AND (@after_id IS NULL OR c.closed_at < @after_key OR (c.closed_at = @after_key AND c.id < @after_id))
              ORDER BY c.closed_at DESC, c.id DESC
              """
            : """
              AND (@after_id IS NULL OR c.id < @after_id)
              ORDER BY c.id DESC
              """;
        return $"SELECT c.id AS Value {matching}\n{keyset}\nLIMIT @take";
    }
}

/// <summary>
/// One dossier, shown by its latest level, with the registration-level data only (task 053j b): code, level, court,
/// judge, act number, parties (plaintiffs / defendants by party_roles.report_group), object snippet, category and type,
/// result, decision date, final, status, office. The whole object is in the audited preview. CanContinue is false (with
/// the reason) when the level is not closed in the system or the dossier is archived.
/// </summary>
public sealed record RegistrationSearchItem(
    Guid CasePublicId,
    string CaseCode,
    string Status,
    string CourtLevelName,
    string CourtName,
    string JudgeName,
    string ActNumber,
    string ObjectSnippet,
    IReadOnlyList<string> Plaintiffs,
    IReadOnlyList<string> Defendants,
    int PartyCount,
    string? CategoryName,
    string? SubcategoryName,
    string? ResultName,
    DateOnly? DecisionDate,
    bool? IsFinal,
    string OfficeName,
    DateTime RegisteredAt,
    DateTime? ClosedAt,
    bool CanContinue,
    string? Reason);

/// <summary>
/// "Vazhdo çështje ekzistuese" (tasks 018, 053b, 053j): the Protokoll — a CENTRAL role — sees every dossier of the
/// country, by its latest level, in a table (default "Mund të vazhdohen"). Every filter given must match, each on ANY
/// level of the dossier; the "+ Shto filtër" ones (the report builder's registry) on the shown latest level. Keyset
/// paging: CONTINUABLE / CLOSED on (closed_at, id) desc — ix_cases_status_closed; ALL / OPEN on id desc — the primary
/// key. The total beside it, on its own connection. The scope comes from ICaseAccessService.GetRegistrationLookupScope
/// — a lookup, not case.view — and every request is audited (REGISTRATION_LOOKUP, the filters used).
/// </summary>
internal sealed class RegistrationSearchHandler(
    CasesDbContext db,
    ParallelReads parallel,
    ICaseAccessService access,
    IReferenceData reference,
    IOfficeDirectory offices,
    ICaseFilterCatalog catalog,
    IAuditLog audit)
{
    public const int DefaultLimit = 10;
    public const int MaxLimit = 50;
    public const int MaxPartyNames = 5;
    public const int SnippetLength = 120;
    public const string OpenStatus = RegistrationSearchStatus.Open;

    public async Task<Result<KeysetPage<RegistrationSearchItem>>> Handle(RegistrationSearchQuery query, CancellationToken ct)
    {
        if (access.GetRegistrationLookupScope() == RegistrationLookupScope.None)
        {
            return Error.Forbidden("case.forbidden", "Nuk keni leje për këtë veprim.");
        }

        var errors = new FieldErrors();
        var bound = new List<FilterSql>();
        foreach (var (name, value) in query.Filters)
        {
            var filter = RegistrationSearchFilters.All.Single(f => f.Name == name);
            var binding = await filter.BindAsync(value, reference, ct);
            if (binding.Error is not null)
            {
                errors.Add(filter.Name, binding.Error);
            }
            else
            {
                bound.Add(binding.Sql!);
            }
        }

        var status = string.IsNullOrWhiteSpace(query.Status) ? RegistrationSearchStatus.Continuable : query.Status.Trim().ToUpperInvariant();
        if (!RegistrationSearchStatus.Values.Contains(status))
        {
            errors.Add("status", "Statusi duhet të jetë CONTINUABLE, ALL, OPEN ose CLOSED.");
        }

        var more = catalog.BindLookup(query.More);
        if (more.IsFailure)
        {
            errors.Add("more", more.Error!.Message);
        }

        if (errors.Any)
        {
            return errors.ToError("Kërkimi nuk është i vlefshëm.");
        }

        var byClosing = RegistrationSearchSql.ByClosing(status);
        var limit = Math.Clamp(query.Limit ?? DefaultLimit, 1, MaxLimit);
        DateTime? afterKey = null;
        ulong? afterId = null;
        if (Cursor.TryDecode(query.After, out var sortKey, out var id))
        {
            afterId = (ulong)id;
            if (byClosing && long.TryParse(sortKey, out var ticks))
            {
                afterKey = new DateTime(ticks, DateTimeKind.Utc);
            }
        }

        // The SQL text is made of fixed predicates only (the filters', the catalog's); every value is a parameter.
        var matching = RegistrationSearchSql.Matching(bound.Select(f => f.Predicate), status, more.Value.Sql);
        var countSql = $"SELECT COUNT(*) AS Value {matching}";
        var pageSql = RegistrationSearchSql.Page(matching, status);

        var total = parallel.RunAsync<CasesDbContext, long>(
            (other, token) => other.Database.SqlQueryRaw<long>(countSql, Parameters(bound, more.Value)).SingleAsync(token),
            ct);
        var caseIds = await db.Database.SqlQueryRaw<ulong>(pageSql,
            [
                .. Parameters(bound, more.Value),
                new MySqlParameter("@after_key", (object?)afterKey ?? DBNull.Value),
                new MySqlParameter("@after_id", (object?)afterId ?? DBNull.Value),
                new MySqlParameter("@take", limit + 1),
            ]).ToListAsync(ct);

        var hasMore = caseIds.Count > limit;
        caseIds = caseIds.Take(limit).ToList();
        var items = await ItemsAsync(caseIds, ct);

        string? next = null;
        if (hasMore)
        {
            var lastId = caseIds[^1];
            var last = await db.Cases.AsNoTracking().Where(c => c.Id == lastId).Select(c => new { c.Id, c.ClosedAt }).SingleAsync(ct);
            next = Cursor.Encode(byClosing ? last.ClosedAt!.Value.Ticks.ToString() : "id", (long)last.Id);
        }

        // task 053j: the country-wide lookup is audited with the filters used (names and values; paging included)
        audit.Record(AuditActions.RegistrationLookup, details: new
        {
            Status = status,
            Filters = query.Filters.ToDictionary(f => f.Key, f => f.Value),
            More = string.IsNullOrWhiteSpace(query.More) ? null : query.More,
            Page = query.After is null ? 1 : (int?)null,
        });
        return new KeysetPage<RegistrationSearchItem>(items, next, (int)await total);
    }

    private async Task<List<RegistrationSearchItem>> ItemsAsync(IReadOnlyList<ulong> caseIds, CancellationToken ct)
    {
        var cases = await db.Cases.AsNoTracking().Where(c => caseIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, ct);
        var dossierIds = cases.Values.Select(c => c.DossierId).Distinct().ToList();
        var archived = await db.Dossiers.AsNoTracking()
            .Where(d => dossierIds.Contains(d.Id))
            .ToDictionaryAsync(d => d.Id, d => d.ArchivedAt != null, ct);
        var decisions = await db.CaseDecisions.AsNoTracking()
            .Where(d => caseIds.Contains(d.CaseId))
            .ToDictionaryAsync(d => d.CaseId, ct);
        var partyRows = await db.CaseParties.AsNoTracking()
            .Where(p => caseIds.Contains(p.CaseId) && p.DeletedAt == null)
            .OrderBy(p => p.Id)
            .Select(p => new { p.CaseId, p.DisplayName, p.PartyRoleId })
            .ToListAsync(ct);
        var roles = (await reference.ListPartyRolesAsync(ct)).ToDictionary(r => r.Id);
        var categories = (await reference.ListCaseCategoriesAsync(ct)).ToDictionary(x => x.Id, x => x.Name);
        var subcategories = (await reference.ListCaseSubcategoriesAsync(ct)).ToDictionary(x => x.Id, x => x.Name);
        var results = (await reference.ListDecisionResultsAsync(ct)).ToDictionary(x => x.Id, x => x.Name);
        var officeNames = new Dictionary<ushort, string>();

        var items = new List<RegistrationSearchItem>(caseIds.Count);
        foreach (var caseId in caseIds)
        {
            var c = cases[caseId];
            var parties = partyRows.Where(p => p.CaseId == caseId).ToList();
            List<string> Group(string group) => parties
                .Where(p => roles.TryGetValue(p.PartyRoleId, out var r) && r.ReportGroup == group)
                .Select(p => p.DisplayName).Take(MaxPartyNames).ToList();
            var judge = c.JudgeId is { } judgeId ? await reference.GetJudge(judgeId, ct) : null;
            if (!officeNames.TryGetValue(c.OfficeId, out var officeName))
            {
                officeName = (await offices.GetByIdAsync(c.OfficeId, ct))?.Name ?? string.Empty;
                officeNames[c.OfficeId] = officeName;
            }

            var decision = decisions.GetValueOrDefault(caseId);
            var (canContinue, reason) = (c.LifecycleStatus, archived[c.DossierId]) switch
            {
                (_, true) => (false, "Dosja është arkivuar"),
                (Case.Closed, false) => (true, (string?)null),
                _ => (false, "E pambyllur në sistem") // Q1 extension: shown, but cannot be continued yet
            };
            items.Add(new RegistrationSearchItem(
                c.PublicId, c.CaseCode, c.LifecycleStatus,
                (await reference.GetCourtLevel(c.CourtLevelId, ct))?.Name ?? string.Empty,
                (await reference.GetCourt(c.CourtId, ct))?.Name ?? string.Empty,
                judge is null ? string.Empty : $"{judge.FirstName} {judge.LastName}",
                c.ActNumber, Snippet(c.ObjectText), Group("PLAINTIFF"), Group("DEFENDANT_OR_THIRD"), parties.Count,
                c.CategoryId is { } cat ? categories.GetValueOrDefault(cat) : null,
                c.SubcategoryId is { } sub ? subcategories.GetValueOrDefault(sub) : null,
                decision is null ? null : results.GetValueOrDefault(decision.ResultId),
                decision?.DecisionDate, decision?.IsFinal,
                officeName, c.RegisteredAt, c.ClosedAt, canContinue, reason));
        }

        return items;
    }

    /// <summary>The object on one line, cut at a word to at most <see cref="SnippetLength"/> characters plus "…".</summary>
    internal static string Snippet(string objectText)
    {
        var text = string.Join(' ', objectText.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (text.Length <= SnippetLength)
        {
            return text;
        }

        var cut = text.LastIndexOf(' ', SnippetLength);
        return text[..(cut > SnippetLength / 2 ? cut : SnippetLength)].TrimEnd() + "…";
    }

    /// <summary>New parameter objects per command: one MySqlParameter cannot belong to two commands.</summary>
    private static object[] Parameters(IEnumerable<FilterSql> filters, CaseFilterSql more) =>
    [
        .. filters.SelectMany(f => f.Parameters).Select(p => new MySqlParameter(p.Key, p.Value)),
        .. more.Parameters.Select(p => new MySqlParameter(p.Key, p.Value ?? DBNull.Value)),
    ];
}
