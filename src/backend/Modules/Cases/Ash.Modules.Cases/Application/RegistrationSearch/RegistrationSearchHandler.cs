using System.Text;
using Ash.Modules.Cases.Contracts;
using Ash.Modules.Cases.Infrastructure;
using Ash.Modules.Reference.Contracts;
using Ash.SharedKernel.Paging;
using Ash.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Ash.Modules.Cases.Application.RegistrationSearch;

public sealed record RegistrationSearchQuery(string? Q, int? CourtId, string? After, int? Limit);

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
/// Protokoll looks for the existing dispute a new summons belongs to (task 018). Matches the unique code or case
/// code (prefix), the act number of any level (prefix), a party name (FULLTEXT, every word as a prefix) or a party
/// identifier / NIPT (exact). Keyset paging on (closed_at or registered_at) desc, id desc.
/// The scope comes from ICaseAccessService.GetRegistrationLookupScope — a lookup, not case.view.
/// </summary>
internal sealed class RegistrationSearchHandler(CasesDbContext db, ICaseAccessService access, IReferenceData reference)
{
    public const int DefaultLimit = 20;
    public const int MaxLimit = 50;
    public const int MaxPartyNames = 5;

    // TODO(client-q1): search fields to be confirmed by the client.
    public async Task<Result<KeysetPage<RegistrationSearchItem>>> Handle(RegistrationSearchQuery query, CancellationToken ct)
    {
        if (access.GetRegistrationLookupScope() == RegistrationLookupScope.None)
        {
            return Error.Forbidden("case.forbidden", "Nuk keni leje për këtë veprim.");
        }

        var q = query.Q?.Trim() ?? string.Empty;
        if (q.Length is < 2 or > 100)
        {
            var errors = new FieldErrors();
            errors.Add("q", "Shkruani nga 2 deri në 100 karaktere për të kërkuar.");
            return errors.ToError("Kërkimi nuk është i vlefshëm.");
        }

        var limit = Math.Clamp(query.Limit ?? DefaultLimit, 1, MaxLimit);
        var prefix = LikePrefix(q);
        var words = BooleanPrefixWords(q);
        int? courtId = query.CourtId;
        DateTime? afterKey = null;
        ulong? afterId = null;
        if (Cursor.TryDecode(query.After, out var sortKey, out var id) && long.TryParse(sortKey, out var ticks))
        {
            afterKey = new DateTime(ticks, DateTimeKind.Utc);
            afterId = (ulong)id;
        }

        // Latest level per dossier; the match may be on any level of the dossier. All in SQL, so a short prefix
        // that matches thousands of dossiers still returns one page.
        var caseIds = await db.Database.SqlQuery<ulong>($"""
            SELECT c.id AS Value
            FROM cases c
            JOIN dossiers d ON d.id = c.dossier_id
            WHERE c.level_seq = (SELECT MAX(l.level_seq) FROM cases l WHERE l.dossier_id = c.dossier_id)
              AND ({courtId} IS NULL OR c.court_id = {courtId})
              AND (d.unique_code LIKE {prefix} ESCAPE '!'
                   OR EXISTS (SELECT 1 FROM cases a
                              WHERE a.dossier_id = c.dossier_id
                                AND (a.case_code LIKE {prefix} ESCAPE '!' OR a.act_number LIKE {prefix} ESCAPE '!'))
                   OR EXISTS (SELECT 1 FROM case_parties p JOIN cases a ON a.id = p.case_id
                              WHERE a.dossier_id = c.dossier_id AND p.deleted_at IS NULL
                                AND (p.identifier = {q}
                                     OR ({words} <> '' AND MATCH(p.display_name) AGAINST ({words} IN BOOLEAN MODE)))))
              AND ({afterKey} IS NULL
                   OR COALESCE(c.closed_at, c.registered_at) < {afterKey}
                   OR (COALESCE(c.closed_at, c.registered_at) = {afterKey} AND c.id < {afterId}))
            ORDER BY COALESCE(c.closed_at, c.registered_at) DESC, c.id DESC
            LIMIT {limit + 1}
            """).ToListAsync(ct);

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

        return new KeysetPage<RegistrationSearchItem>(items, next);
    }

    /// <summary>"q%" with the user's own %, _ and ! escaped (ESCAPE '!').</summary>
    private static string LikePrefix(string q) =>
        q.Replace("!", "!!").Replace("%", "!%").Replace("_", "!_") + "%";

    /// <summary>
    /// "+word1* +word2*" for MATCH … IN BOOLEAN MODE: every word required, as a prefix ('financ' finds 'Financave').
    /// Only letters and digits are kept, so the user cannot inject boolean operators.
    /// </summary>
    private static string BooleanPrefixWords(string q)
    {
        var words = new List<string>();
        var current = new StringBuilder();
        foreach (var ch in q + " ")
        {
            if (char.IsLetterOrDigit(ch))
            {
                current.Append(ch);
            }
            else if (current.Length > 0)
            {
                words.Add(current.ToString());
                current.Clear();
            }
        }

        return string.Join(' ', words.Where(w => w.Length >= 2).Select(w => $"+{w}*"));
    }
}
