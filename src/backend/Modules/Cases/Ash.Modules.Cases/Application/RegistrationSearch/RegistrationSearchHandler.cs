using Ash.Infrastructure.Persistence;
using Ash.Modules.Cases.Contracts;
using Ash.Modules.Cases.Infrastructure;
using Ash.Modules.Reference.Contracts;
using Ash.SharedKernel.Paging;
using Ash.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;
using MySql.Data.MySqlClient;

namespace Ash.Modules.Cases.Application.RegistrationSearch;

/// <summary>status: OPEN (not closed) or CLOSED; none = every status.</summary>
public sealed record RegistrationSearchQuery(string? Q, int? CourtId, string? After, int? Limit, string? Status = null);

/// <summary>
/// One dossier, shown by its latest level. Registration fields only: no object text, documents or lawyers.
/// CanContinue is false (with the reason) when the level is not closed in the system or the dossier is archived.
/// </summary>
public sealed record RegistrationSearchItem(
    Guid CasePublicId,
    string CaseCode,
    string Status,
    string ActNumber,
    string CourtName,
    IReadOnlyList<string> PartyNames,
    int PartyCount,
    DateTime? ClosedAt,
    bool CanContinue,
    string? Reason);

/// <summary>
/// Protokoll looks for the existing dispute a new summons belongs to (task 018). Matches anywhere inside the case code,
/// act number or summons number of any level, or a party name / identifier (NIPT) — ngram FULLTEXT, script 014.
/// Every status (a closed level is the one to continue). Keyset paging on (closed_at or registered_at) desc, id desc.
/// The scope comes from ICaseAccessService.GetRegistrationLookupScope — a lookup, not case.view.
/// </summary>
internal sealed class RegistrationSearchHandler(
    CasesDbContext db, ParallelReads parallel, ICaseAccessService access, IReferenceData reference)
{
    public const int DefaultLimit = 20;
    public const int MaxLimit = 50;
    public const int MaxPartyNames = 5;
    public const string OpenStatus = "OPEN";

    // TODO(client-q1): search fields to be confirmed by the client.
    public async Task<Result<KeysetPage<RegistrationSearchItem>>> Handle(RegistrationSearchQuery query, CancellationToken ct)
    {
        if (access.GetRegistrationLookupScope() == RegistrationLookupScope.None)
        {
            return Error.Forbidden("case.forbidden", "Nuk keni leje për këtë veprim.");
        }

        var q = query.Q?.Trim() ?? string.Empty;
        var errors = new FieldErrors();
        if (q.Length is < 2 or > 100)
        {
            errors.Add("q", "Shkruani nga 2 deri në 100 karaktere për të kërkuar.");
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
        var words = NgramSearch.BooleanQuery(q);
        if (words.Length == 0)
        {
            return new KeysetPage<RegistrationSearchItem>([], null, 0);
        }

        int? courtId = query.CourtId;
        DateTime? afterKey = null;
        ulong? afterId = null;
        if (Cursor.TryDecode(query.After, out var sortKey, out var id) && long.TryParse(sortKey, out var ticks))
        {
            afterKey = new DateTime(ticks, DateTimeKind.Utc);
            afterId = (ulong)id;
        }

        // the total (same filters, no cursor) on its own connection, beside the page query
        var total = parallel.RunAsync<CasesDbContext, long>(
            (other, token) => other.Database
                .SqlQueryRaw<long>($"SELECT COUNT(*) AS Value FROM cases c WHERE {MatchingDossiers}", Parameters(words, courtId, status))
                .SingleAsync(token),
            ct);

        // Latest level per dossier; the match may be on any level of the dossier. All in SQL, so a short query that
        // matches thousands of dossiers still returns one page.
        var caseIds = await db.Database.SqlQueryRaw<ulong>($"""
            SELECT c.id AS Value
            FROM cases c
            WHERE {MatchingDossiers}
              AND (@after_key IS NULL
                   OR COALESCE(c.closed_at, c.registered_at) < @after_key
                   OR (COALESCE(c.closed_at, c.registered_at) = @after_key AND c.id < @after_id))
            ORDER BY COALESCE(c.closed_at, c.registered_at) DESC, c.id DESC
            LIMIT @take
            """,
            [
                .. Parameters(words, courtId, status),
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
                names.Take(MaxPartyNames).ToList(), names.Count, c.ClosedAt, canContinue, reason));
        }

        string? next = null;
        if (hasMore)
        {
            var last = cases[caseIds[^1]];
            next = Cursor.Encode((last.ClosedAt ?? last.RegisteredAt).Ticks.ToString(), (long)last.Id);
        }

        return new KeysetPage<RegistrationSearchItem>(items, next, (int)await total);
    }

    /// <summary>
    /// The latest level (c) of every dossier with a match on ANY of its levels, substring over the ngram indexes
    /// (script 014): case code / act number / summons number, or a party's name or NIPT. Every word of q in the
    /// same source. Parameters: @words, @court.
    /// </summary>
    private const string MatchingDossiers = """
        c.level_seq = (SELECT MAX(l.level_seq) FROM cases l WHERE l.dossier_id = c.dossier_id)
          AND (@court IS NULL OR c.court_id = @court)
          AND (@status IS NULL OR (@status = 'CLOSED') = (c.lifecycle_status = 'CLOSED'))
          AND c.dossier_id IN (
                SELECT a.dossier_id FROM cases a
                WHERE MATCH(a.case_code, a.act_number, a.summons_number) AGAINST (@words IN BOOLEAN MODE)
                UNION
                SELECT a.dossier_id FROM case_parties p JOIN cases a ON a.id = p.case_id
                WHERE p.deleted_at IS NULL AND MATCH(p.display_name, p.identifier) AGAINST (@words IN BOOLEAN MODE))
        """;

    /// <summary>New parameter objects per command: one MySqlParameter cannot belong to two commands.</summary>
    private static object[] Parameters(string words, int? courtId, string? status) =>
    [
        new MySqlParameter("@words", words),
        new MySqlParameter("@court", (object?)courtId ?? DBNull.Value),
        new MySqlParameter("@status", (object?)status ?? DBNull.Value),
    ];
}
