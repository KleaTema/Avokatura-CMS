using Ash.Infrastructure.Persistence;
using Ash.Modules.Cases.Contracts;
using Ash.Modules.Cases.Infrastructure;
using Ash.Modules.Identity.Contracts;
using Ash.Modules.Reference.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Ash.Modules.Cases.Application.Search;

/// <summary>
/// A search text resolved once per request: its terms (<see cref="TextSearch"/>: every term a substring, from 1
/// character, accent-insensitive; the ngram index narrows when a term has ≥ 2 letters / digits) and the judges and
/// institutions whose names match — asked of the modules that own them (Reference, Identity), never read from their
/// tables here.
/// </summary>
public sealed record CaseSearchTerms(string Q, TextSearch Search, IReadOnlyList<uint> JudgeIds, IReadOnlyList<long> InstitutionIds)
{
    public bool IsEmpty => Search.IsEmpty;

    /// <summary>The ngram BOOLEAN words ("" for a 1-character text).</summary>
    public string Words => Search.Words;
}

/// <summary>
/// THE case search (task 047; live search): the one predicate behind the case list's ?q=, the header search
/// (GET /api/search) and the reports, so all return the same cases for the same text. A case matches when every term
/// is found in its case code, act number or summons number, its object, a (not deleted) party's name or NIPT, its
/// judge's name, or the name of an institution that is its party or is linked to it — each source on its own ngram
/// FULLTEXT index (script 014) + the escaped LIKE, joined by UNION.
/// </summary>
public sealed class CaseSearch(IReferenceData reference, IInstitutionDirectory institutions)
{
    /// <summary>At most this many judges / institutions per text (a 1-letter text may match many).</summary>
    public const int NameMatchLimit = 200;

    public async Task<CaseSearchTerms> ResolveAsync(string q, CancellationToken ct)
    {
        var search = TextSearch.Parse(q);
        if (search.IsEmpty)
        {
            return new CaseSearchTerms(search.Text, search, [], []);
        }

        return new CaseSearchTerms(search.Text, search,
            await reference.SearchJudgeIdsAsync(search.Text, NameMatchLimit, ct),
            await institutions.SearchIdsAsync(search.Text, NameMatchLimit, ct));
    }

    /// <summary>
    /// The ids of every matching case (no scope: callers compose it into their own scoped query, never materialized).
    /// </summary>
    public static IQueryable<ulong> Ids(CasesDbContext db, CaseSearchTerms terms) =>
        db.Database.SqlQueryRaw<ulong>(terms.IsEmpty ? "SELECT id AS Value FROM cases WHERE FALSE" : IdsSql(terms));

    /// <summary>
    /// The id query of <see cref="Ids"/> as text, self-contained (the values are escaped literals, no parameters): the
    /// reports compose it into their own SQL (task 042). The judge and institution ids are numbers from the database.
    /// </summary>
    public static string IdsSql(CaseSearchTerms terms)
    {
        var s = terms.Search;
        var sql = $"""
            SELECT id AS Value FROM cases WHERE {s.InlinePredicate("case_code", "act_number", "summons_number")}
            UNION SELECT id AS Value FROM cases WHERE {s.InlinePredicate("object_text")}
            UNION SELECT case_id AS Value FROM case_parties WHERE deleted_at IS NULL AND {s.InlinePredicate("display_name", "identifier")}
            """;
        if (terms.JudgeIds.Count > 0)
        {
            // ix_cases_judge
            sql += $"\nUNION SELECT id AS Value FROM cases WHERE judge_id IN ({string.Join(',', terms.JudgeIds)})";
        }

        if (terms.InstitutionIds.Count > 0)
        {
            var ids = string.Join(',', terms.InstitutionIds);
            // ix_cp_institution, ix_ci_institution
            sql += $"\nUNION SELECT case_id AS Value FROM case_parties WHERE deleted_at IS NULL AND institution_id IN ({ids})";
            sql += $"\nUNION SELECT case_id AS Value FROM case_institutions WHERE ended_at IS NULL AND institution_id IN ({ids})";
        }

        return sql;
    }
}

/// <summary><see cref="ICaseSearchSql"/>: the same search as the case list and the header search.</summary>
internal sealed class CaseSearchSqlProvider(CaseSearch search) : ICaseSearchSql
{
    public async Task<CaseSearchSql?> ResolveAsync(string q, CancellationToken ct = default)
    {
        var terms = await search.ResolveAsync(q.Trim(), ct);
        return terms.IsEmpty ? null : new CaseSearchSql(CaseSearch.IdsSql(terms));
    }
}
