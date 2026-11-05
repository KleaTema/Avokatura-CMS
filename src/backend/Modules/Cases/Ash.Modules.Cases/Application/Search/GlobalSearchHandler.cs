using System.Globalization;
using System.Text.RegularExpressions;
using Ash.Modules.Cases.Application.ListCases;
using Ash.Modules.Cases.Infrastructure;
using Ash.Modules.Identity.Contracts;
using Ash.Modules.Reference.Contracts;
using Ash.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;
using MySql.Data.MySqlClient;

namespace Ash.Modules.Cases.Application.Search;

/// <summary>Where the text was found. Order = rank (exact codes first).</summary>
public static class MatchedOn
{
    public const string Code = "CODE";
    public const string Act = "ACT";
    public const string Party = "PARTY";
    public const string Object = "OBJECT";
    public const string Judge = "JUDGE";
    public const string Institution = "INSTITUTION";
}

/// <summary>A result of GET /api/search. Public ids only.</summary>
public sealed record SearchResultItem(
    Guid CasePublicId,
    string CaseCode,
    string Status,
    bool IsArchived,
    string CourtName,
    string? PrimaryLawyerName,
    /// <summary>CODE | ACT | PARTY | OBJECT | JUDGE | INSTITUTION</summary>
    string MatchedOn,
    /// <summary>"Pala: Petrit Hoxha", "Gjyqtari: …", "Nr. i aktit: …", or ~80 characters of the object around the match.</summary>
    string? Snippet);

/// <summary>Items: the best <c>limit</c>. TotalApprox: every matching case the user may see, counted up to 1,000 (Capped).</summary>
public sealed record SearchResponse(IReadOnlyList<SearchResultItem> Items, int TotalApprox, bool Capped);

/// <summary>
/// GET /api/search?q=&amp;limit= (task 047): the header typeahead. The cases are exactly those of the case list's ?q=
/// (CaseSearch — same predicate, ngram BOOLEAN, never NATURAL LANGUAGE MODE) within the user's access scope, every
/// status, archived included. Ranked: 1 exact case or dossier code, 2 exact act or summons number, 3 a code / act /
/// summons containing the text (prefix or inside), 4 a party (name or NIPT), 5 the object, 6 the judge, 7 an
/// institution that is a party or linked; then the newest registered. Levels of one dossier stay together, the latest
/// first. Every query carries the read time limit (QueryTimeLimitInterceptor).
/// </summary>
internal sealed partial class GlobalSearchHandler(
    CasesDbContext db,
    CaseListQueryBuilder builder,
    CaseSearch caseSearch,
    IReferenceData reference,
    IUserDirectory users,
    IInstitutionDirectory institutions)
{
    public const int DefaultLimit = 8;
    public const int MaxLimit = 20;
    public const int TotalCap = 1000;

    /// <summary>Ranked among the newest this many matches plus every exact / prefix code match.</summary>
    public const int Candidates = 200;

    private const int SnippetLength = 80;

    public async Task<Result<SearchResponse>> Handle(string? q, int? limit, CancellationToken ct)
    {
        var text = q?.Trim() ?? string.Empty;
        if (text.Length is < 2 or > 100)
        {
            return Error.ValidationFields("Kërkimi nuk është i vlefshëm.",
                new Dictionary<string, string[]> { ["q"] = ["Shkruani nga 2 deri në 100 karaktere për të kërkuar."] });
        }

        var take = Math.Clamp(limit ?? DefaultLimit, 1, MaxLimit);
        var terms = await caseSearch.ResolveAsync(text, ct);
        if (terms.IsEmpty)
        {
            return new SearchResponse([], 0, false);
        }

        // exactly the case list's ?q= over every status, within the user's scope
        var filter = new CaseListFilter(CaseListTab.All, CaseListFilter.AllStatuses, null, null, null, null, null, null, null, text)
        {
            Search = terms,
        };
        var scoped = builder.Filtered(filter);
        var counted = await scoped.Select(c => c.Id).Take(TotalCap + 1).CountAsync(ct);

        var newest = await scoped.OrderByDescending(c => c.RegisteredAt).ThenByDescending(c => c.Id)
            .Select(c => c.Id).Take(Candidates).ToListAsync(ct);
        // exact and prefix code / act / summons matches are always ranked, however old (ix_cases_act, ix_cases_summons)
        var codeLike = $"{EscapeLike(text)}%";
        var byCode = await scoped
            .Where(c => EF.Functions.Like(c.CaseCode, codeLike) || EF.Functions.Like(c.ActNumber, codeLike)
                        || EF.Functions.Like(c.SummonsNumber, codeLike)
                        || db.Dossiers.Any(d => d.Id == c.DossierId && EF.Functions.Like(d.UniqueCode, codeLike)))
            .Select(c => c.Id).Take(50).ToListAsync(ct);
        var ids = newest.Concat(byCode).Distinct().ToList();
        if (ids.Count == 0)
        {
            return new SearchResponse([], 0, false);
        }

        var rows = await (
                from c in db.Cases.AsNoTracking()
                join d in db.Dossiers.AsNoTracking() on c.DossierId equals d.Id
                where ids.Contains(c.Id)
                select new Row(c.Id, c.PublicId, c.CaseCode, d.UniqueCode, c.ActNumber, c.SummonsNumber, c.ObjectText, c.JudgeId,
                    c.DossierId, c.LevelSeq, c.LifecycleStatus, c.CourtId, c.RegisteredAt, d.ArchivedAt != null))
            .ToListAsync(ct);

        var numberHits = await IdsAsync("SELECT id FROM cases WHERE id IN ({ids}) AND MATCH(case_code, act_number, summons_number) AGAINST (@w IN BOOLEAN MODE)", ids, terms, ct);
        var objectHits = await IdsAsync("SELECT id FROM cases WHERE id IN ({ids}) AND MATCH(object_text) AGAINST (@w IN BOOLEAN MODE)", ids, terms, ct);
        var parties = await PartyHitsAsync(ids, terms, ct);
        var institutionCases = terms.InstitutionIds.Count == 0 ? [] : await InstitutionHitsAsync(ids, terms, ct);

        var ranked = new List<(Row Row, int Rank, string On, string? Snippet)>();
        foreach (var r in rows)
        {
            var hit = await ClassifyAsync(r, text, terms, numberHits, objectHits, parties, institutionCases, ct);
            if (hit is not null)
            {
                ranked.Add((r, hit.Value.Rank, hit.Value.On, hit.Value.Snippet));
            }
        }

        // best rank, newest registered; then each dossier's levels together, the latest level first
        var ordered = ranked.OrderBy(x => x.Rank).ThenByDescending(x => x.Row.RegisteredAt).ThenByDescending(x => x.Row.Id).ToList();
        var page = ordered
            .GroupBy(x => x.Row.DossierId)
            .SelectMany(g => g.OrderByDescending(x => x.Row.LevelSeq))
            .Take(take)
            .ToList();

        var caseIds = page.Select(x => x.Row.Id).ToList();
        var primaries = await db.CaseAssignments.AsNoTracking()
            .Where(a => caseIds.Contains(a.CaseId) && a.CaseRole == CaseAssignmentRow.PrimaryLawyer && a.EndedAt == null)
            .Select(a => new { a.CaseId, a.UserId })
            .ToListAsync(ct);
        var names = await users.GetNamesAsync(primaries.Select(p => (long)p.UserId).Distinct().ToList(), ct);
        var lawyerOf = primaries.Where(p => names.ContainsKey(p.UserId)).ToDictionary(p => p.CaseId, p => names[p.UserId]);

        var items = new List<SearchResultItem>(page.Count);
        foreach (var x in page)
        {
            items.Add(new SearchResultItem(x.Row.PublicId, x.Row.CaseCode, x.Row.Status, x.Row.Archived,
                (await reference.GetCourt(x.Row.CourtId, ct))?.Name ?? string.Empty, lawyerOf.GetValueOrDefault(x.Row.Id), x.On, x.Snippet));
        }

        return new SearchResponse(items, Math.Min(counted, TotalCap), counted > TotalCap);
    }

    private sealed record Row(
        ulong Id, Guid PublicId, string CaseCode, string UniqueCode, string ActNumber, string SummonsNumber, string ObjectText,
        uint? JudgeId, ulong DossierId, ushort LevelSeq, string Status, ushort CourtId, DateTime RegisteredAt, bool Archived);

    private async Task<(int Rank, string On, string? Snippet)?> ClassifyAsync(
        Row r, string q, CaseSearchTerms terms, HashSet<ulong> numberHits, HashSet<ulong> objectHits,
        Dictionary<ulong, string> parties, Dictionary<ulong, long> institutionCases, CancellationToken ct)
    {
        static bool Same(string a, string b) => string.Equals(a.Trim(), b, StringComparison.OrdinalIgnoreCase);
        static bool Starts(string a, string b) => a.StartsWith(b, StringComparison.OrdinalIgnoreCase);

        if (Same(r.CaseCode, q) || Same(r.UniqueCode, q))
        {
            return (1, MatchedOn.Code, null);
        }

        if (Same(r.ActNumber, q))
        {
            return (2, MatchedOn.Act, $"Nr. i aktit: {r.ActNumber}");
        }

        if (Same(r.SummonsNumber, q))
        {
            return (2, MatchedOn.Act, $"Nr. i fletëthirrjes: {r.SummonsNumber}");
        }

        if (Starts(r.CaseCode, q) || Starts(r.UniqueCode, q) || Starts(r.ActNumber, q) || Starts(r.SummonsNumber, q) || numberHits.Contains(r.Id))
        {
            // inside a number too ("640" in 2026-686640/1): which number holds it
            if (Contains(r.CaseCode, q) || Contains(r.UniqueCode, q) || !(Contains(r.ActNumber, q) || Contains(r.SummonsNumber, q)))
            {
                return (3, MatchedOn.Code, null);
            }

            return Contains(r.ActNumber, q)
                ? (3, MatchedOn.Act, $"Nr. i aktit: {r.ActNumber}")
                : (3, MatchedOn.Act, $"Nr. i fletëthirrjes: {r.SummonsNumber}");
        }

        if (parties.TryGetValue(r.Id, out var party))
        {
            return (4, MatchedOn.Party, $"Pala: {party}");
        }

        if (objectHits.Contains(r.Id))
        {
            return (5, MatchedOn.Object, ObjectSnippet(r.ObjectText, q));
        }

        if (r.JudgeId is { } judgeId && terms.JudgeIds.Contains(judgeId) && await reference.GetJudge(judgeId, ct) is { } judge)
        {
            return (6, MatchedOn.Judge, $"Gjyqtari: {judge.FirstName} {judge.LastName}");
        }

        if (institutionCases.TryGetValue(r.Id, out var institutionId))
        {
            var name = (await institutions.GetByIdsAsync([institutionId], ct)).FirstOrDefault()?.Name;
            return (7, MatchedOn.Institution, name is null ? null : $"Institucioni: {name}");
        }

        return null;
    }

    /// <summary>Accent- and case-insensitive "contains" (the columns' ai_ci collation in C#).</summary>
    private static bool Contains(string text, string q) =>
        CultureInfo.InvariantCulture.CompareInfo.IndexOf(text, q, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) >= 0;

    /// <summary>About 80 characters of the object around the first word of q found in it, "…" where cut.</summary>
    public static string ObjectSnippet(string objectText, string q)
    {
        var text = WhitespaceRegex().Replace(objectText, " ").Trim();
        var at = -1;
        foreach (var word in WordRegex().Matches(q).Select(m => m.Value).Where(w => w.Length >= 2))
        {
            at = CultureInfo.InvariantCulture.CompareInfo.IndexOf(text, word, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace);
            if (at >= 0)
            {
                break;
            }
        }

        if (text.Length <= SnippetLength)
        {
            return text;
        }

        var start = Math.Clamp(at < 0 ? 0 : at - 30, 0, text.Length - SnippetLength);
        var part = text.Substring(start, SnippetLength).Trim();
        return $"{(start > 0 ? "…" : "")}{part}{(start + SnippetLength < text.Length ? "…" : "")}";
    }

    private async Task<HashSet<ulong>> IdsAsync(string sql, List<ulong> ids, CaseSearchTerms terms, CancellationToken ct)
    {
        var rows = await db.Database
            .SqlQueryRaw<ulong>(sql.Replace("SELECT id FROM", "SELECT id AS Value FROM").Replace("{ids}", string.Join(',', ids)),
                new MySqlParameter("@w", terms.Words))
            .ToListAsync(ct);
        return rows.ToHashSet();
    }

    private sealed record PartyHit(ulong CaseId, string DisplayName);

    /// <summary>The first matching (not deleted) party of each candidate: its name for the snippet.</summary>
    private async Task<Dictionary<ulong, string>> PartyHitsAsync(List<ulong> ids, CaseSearchTerms terms, CancellationToken ct)
    {
        // the ids are numbers from the database; the text goes in as a parameter
        var sql = "SELECT case_id AS CaseId, display_name AS DisplayName FROM case_parties WHERE deleted_at IS NULL "
                  + $"AND case_id IN ({string.Join(',', ids)}) AND MATCH(display_name, identifier) AGAINST (@w IN BOOLEAN MODE) ORDER BY id";
        var hits = await db.Database.SqlQueryRaw<PartyHit>(sql, new MySqlParameter("@w", terms.Words)).ToListAsync(ct);
        return hits.GroupBy(h => h.CaseId).ToDictionary(g => g.Key, g => g.First().DisplayName);
    }

    private sealed record InstitutionHit(ulong CaseId, uint InstitutionId);

    /// <summary>Candidates with a matching institution as a party or a link: the case → that institution.</summary>
    private async Task<Dictionary<ulong, long>> InstitutionHitsAsync(List<ulong> ids, CaseSearchTerms terms, CancellationToken ct)
    {
        var caseIds = string.Join(',', ids);
        var institutionIds = string.Join(',', terms.InstitutionIds);
        // numbers from the database only
        var sql = $"SELECT case_id AS CaseId, institution_id AS InstitutionId FROM case_parties WHERE deleted_at IS NULL AND case_id IN ({caseIds}) AND institution_id IN ({institutionIds}) "
                  + $"UNION SELECT case_id, institution_id FROM case_institutions WHERE ended_at IS NULL AND case_id IN ({caseIds}) AND institution_id IN ({institutionIds})";
        var hits = await db.Database.SqlQueryRaw<InstitutionHit>(sql).ToListAsync(ct);
        return hits.GroupBy(h => h.CaseId).ToDictionary(g => g.Key, g => (long)g.First().InstitutionId);
    }

    private static string EscapeLike(string text) => text.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();

    [GeneratedRegex(@"[\p{L}\p{N}]+")]
    private static partial Regex WordRegex();
}
