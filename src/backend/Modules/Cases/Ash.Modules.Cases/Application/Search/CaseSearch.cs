using Ash.Infrastructure.Persistence;
using Ash.Modules.Cases.Infrastructure;
using Ash.Modules.Identity.Contracts;
using Ash.Modules.Reference.Contracts;
using Microsoft.EntityFrameworkCore;
using MySql.Data.MySqlClient;

namespace Ash.Modules.Cases.Application.Search;

/// <summary>
/// A search text resolved once per request: the ngram BOOLEAN words (NgramSearch: every word of ≥ 2 letters required,
/// substring, accent-insensitive) and the judges and institutions whose names match — asked of the modules that own
/// them (Reference, Identity), never read from their tables here.
/// </summary>
public sealed record CaseSearchTerms(string Q, string Words, IReadOnlyList<uint> JudgeIds, IReadOnlyList<long> InstitutionIds)
{
    public bool IsEmpty => Words.Length == 0;
}

/// <summary>
/// THE case search (task 047): the one predicate behind the case list's ?q= and the header search (GET /api/search),
/// so both return the same cases for the same text. A case matches when the text is found in its case code, act number
/// or summons number, its object, a (not deleted) party's name or NIPT, its judge's name, or the name of an institution
/// that is its party or is linked to it — each source on its own ngram FULLTEXT index (script 014), joined by UNION.
/// </summary>
public sealed class CaseSearch(IReferenceData reference, IInstitutionDirectory institutions)
{
    /// <summary>At most this many judges / institutions per text (a 2-letter text may match many).</summary>
    public const int NameMatchLimit = 200;

    public async Task<CaseSearchTerms> ResolveAsync(string q, CancellationToken ct)
    {
        var words = NgramSearch.BooleanQuery(q);
        if (words.Length == 0)
        {
            return new CaseSearchTerms(q, words, [], []);
        }

        return new CaseSearchTerms(q, words,
            await reference.SearchJudgeIdsAsync(words, NameMatchLimit, ct),
            await institutions.SearchIdsAsync(words, NameMatchLimit, ct));
    }

    /// <summary>
    /// The ids of every matching case (no scope: callers compose it into their own scoped query, never materialized).
    /// The judge and institution ids are numbers from the database, written into the SQL as such.
    /// </summary>
    public static IQueryable<ulong> Ids(CasesDbContext db, CaseSearchTerms terms)
    {
        if (terms.IsEmpty)
        {
            return db.Database.SqlQueryRaw<ulong>("SELECT id AS Value FROM cases WHERE FALSE");
        }

        var sql = """
            SELECT id AS Value FROM cases WHERE MATCH(case_code, act_number, summons_number) AGAINST (@search_words IN BOOLEAN MODE)
            UNION SELECT id AS Value FROM cases WHERE MATCH(object_text) AGAINST (@search_words IN BOOLEAN MODE)
            UNION SELECT case_id AS Value FROM case_parties WHERE deleted_at IS NULL AND MATCH(display_name, identifier) AGAINST (@search_words IN BOOLEAN MODE)
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

        return db.Database.SqlQueryRaw<ulong>(sql, new MySqlParameter("@search_words", terms.Words));
    }
}
