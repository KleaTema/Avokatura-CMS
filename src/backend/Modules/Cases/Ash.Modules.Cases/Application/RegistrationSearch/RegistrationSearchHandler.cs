using Ash.Infrastructure.Persistence;
using Ash.Modules.Cases.Application.RegistrationSearch.Filters;
using Ash.Modules.Cases.Contracts;
using Ash.Modules.Cases.Infrastructure;
using Ash.Modules.Reference.Contracts;
using Ash.SharedKernel.Paging;
using Ash.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;
using MySql.Data.MySqlClient;

namespace Ash.Modules.Cases.Application.RegistrationSearch;

/// <summary>
/// Filters: the values of <see cref="RegistrationSearchFilters"/> by name (code, actNumber, courtId, …).
/// status: OPEN (not closed) or CLOSED on the latest level; none = every status.
/// </summary>
public sealed record RegistrationSearchQuery(
    IReadOnlyList<KeyValuePair<string, string>> Filters, string? After, int? Limit, string? Status = null);

/// <summary>
/// One dossier, shown by its latest level. Registration fields plus a short snippet of the object (task 053b); the
/// whole object is in the audited preview. CanContinue is false (with the reason) when the level is not closed in
/// the system or the dossier is archived.
/// </summary>
public sealed record RegistrationSearchItem(
    Guid CasePublicId,
    string CaseCode,
    string Status,
    string ActNumber,
    string CourtName,
    string ObjectSnippet,
    IReadOnlyList<string> PartyNames,
    int PartyCount,
    DateTime? ClosedAt,
    bool CanContinue,
    string? Reason);

/// <summary>
/// Protokoll looks for the existing dispute a new summons belongs to (task 018; fields: client decision B.2). Every
/// filter given must match, each on ANY level of the dossier; the dossier is shown by its latest level. Every status
/// (a closed level is the one to continue). Keyset paging on (closed_at or registered_at) desc, id desc.
/// The scope comes from ICaseAccessService.GetRegistrationLookupScope — a lookup, not case.view.
/// </summary>
internal sealed class RegistrationSearchHandler(
    CasesDbContext db, ParallelReads parallel, ICaseAccessService access, IReferenceData reference)
{
    public const int DefaultLimit = 20;
    public const int MaxLimit = 50;
    public const int MaxPartyNames = 5;
    public const int SnippetLength = 120;
    public const string OpenStatus = "OPEN";

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

        if (query.Filters.Count == 0)
        {
            errors.Add("filters", "Plotësoni të paktën një fushë për të kërkuar.");
        }

        var status = string.IsNullOrWhiteSpace(query.Status) ? null : query.Status.Trim().ToUpperInvariant();
        if (status is not (null or OpenStatus or Case.Closed))
        {
            errors.Add("status", "Statusi duhet të jetë OPEN ose CLOSED.");
        }

        if (errors.Any)
        {
            return errors.ToError("Kërkimi nuk është i vlefshëm.");
        }

        var limit = Math.Clamp(query.Limit ?? DefaultLimit, 1, MaxLimit);
        DateTime? afterKey = null;
        ulong? afterId = null;
        if (Cursor.TryDecode(query.After, out var sortKey, out var id) && long.TryParse(sortKey, out var ticks))
        {
            afterKey = new DateTime(ticks, DateTimeKind.Utc);
            afterId = (ulong)id;
        }

        // The SQL text is made of the filters' fixed predicates only; every value is a parameter.
        var matching = MatchingDossiers(bound);
        var countSql = $"SELECT COUNT(*) AS Value FROM cases c WHERE {matching}";
        var pageSql = $"""
            SELECT c.id AS Value
            FROM cases c
            WHERE {matching}
              AND (@after_key IS NULL
                   OR COALESCE(c.closed_at, c.registered_at) < @after_key
                   OR (COALESCE(c.closed_at, c.registered_at) = @after_key AND c.id < @after_id))
            ORDER BY COALESCE(c.closed_at, c.registered_at) DESC, c.id DESC
            LIMIT @take
            """;

        // the total (same filters, no cursor) on its own connection, beside the page query
        var total = parallel.RunAsync<CasesDbContext, long>(
            (other, token) => other.Database.SqlQueryRaw<long>(countSql, Parameters(bound, status)).SingleAsync(token),
            ct);

        // Latest level per dossier; each filter may match on any level of the dossier. All in SQL, so a short query
        // that matches thousands of dossiers still returns one page.
        var caseIds = await db.Database.SqlQueryRaw<ulong>(pageSql,
            [
                .. Parameters(bound, status),
                new MySqlParameter("@after_key", (object?)afterKey ?? DBNull.Value),
                new MySqlParameter("@after_id", (object?)afterId ?? DBNull.Value),
                new MySqlParameter("@take", limit + 1),
            ]).ToListAsync(ct);

        var hasMore = caseIds.Count > limit;
        caseIds = caseIds.Take(limit).ToList();
        var cases = await db.Cases.AsNoTracking().Where(c => caseIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, ct);
        var dossierIds = cases.Values.Select(c => c.DossierId).Distinct().ToList();
        var archived = await db.Dossiers.AsNoTracking()
            .Where(d => dossierIds.Contains(d.Id))
            .ToDictionaryAsync(d => d.Id, d => d.ArchivedAt != null, ct);
        var partyRows = await db.CaseParties.AsNoTracking()
            .Where(p => caseIds.Contains(p.CaseId) && p.DeletedAt == null)
            .OrderBy(p => p.Id)
            .Select(p => new { p.CaseId, p.DisplayName })
            .ToListAsync(ct);

        var items = new List<RegistrationSearchItem>();
        foreach (var caseId in caseIds)
        {
            var c = cases[caseId];
            var names = partyRows.Where(p => p.CaseId == caseId).Select(p => p.DisplayName).ToList();
            var court = await reference.GetCourt(c.CourtId, ct);
            var (canContinue, reason) = (c.LifecycleStatus, archived[c.DossierId]) switch
            {
                (_, true) => (false, "Dosja është arkivuar"),
                (Case.Closed, false) => (true, (string?)null),
                _ => (false, "E pambyllur në sistem") // Q1 extension: shown, but cannot be continued yet
            };
            items.Add(new RegistrationSearchItem(c.PublicId, c.CaseCode, c.LifecycleStatus, c.ActNumber, court?.Name ?? string.Empty,
                Snippet(c.ObjectText), names.Take(MaxPartyNames).ToList(), names.Count, c.ClosedAt, canContinue, reason));
        }

        string? next = null;
        if (hasMore)
        {
            var last = cases[caseIds[^1]];
            next = Cursor.Encode((last.ClosedAt ?? last.RegisteredAt).Ticks.ToString(), (long)last.Id);
        }

        return new KeysetPage<RegistrationSearchItem>(items, next, (int)await total);
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

    /// <summary>
    /// The latest level (c) of every dossier where each filter matches some level (a) of it. Parameters: the
    /// filters' own, and @status.
    /// </summary>
    private static string MatchingDossiers(IEnumerable<FilterSql> filters) =>
        """
        c.level_seq = (SELECT MAX(l.level_seq) FROM cases l WHERE l.dossier_id = c.dossier_id)
          AND (@status IS NULL OR (@status = 'CLOSED') = (c.lifecycle_status = 'CLOSED'))
        """ + string.Concat(filters.Select(f => $"\n  AND c.dossier_id IN (SELECT a.dossier_id FROM cases a WHERE {f.Predicate})"));

    /// <summary>New parameter objects per command: one MySqlParameter cannot belong to two commands.</summary>
    private static object[] Parameters(IEnumerable<FilterSql> filters, string? status) =>
    [
        .. filters.SelectMany(f => f.Parameters).Select(p => new MySqlParameter(p.Key, p.Value)),
        new MySqlParameter("@status", (object?)status ?? DBNull.Value),
    ];
}
