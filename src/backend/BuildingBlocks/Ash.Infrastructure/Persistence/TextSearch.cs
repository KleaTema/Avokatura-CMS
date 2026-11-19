using System.Text;
using System.Text.RegularExpressions;
using MySql.Data.MySqlClient;

namespace Ash.Infrastructure.Persistence;

/// <summary>
/// Live search (search-as-you-type): ONE rule for every server-side text search.
/// <list type="bullet">
/// <item>The text is split on whitespace into terms (at most <see cref="MaxTerms"/>); every term must appear, as a
/// contiguous substring ("contains"), in one of the searched columns — "2026-12" matches "…2026-12…", never "2026 … 12".</item>
/// <item>From 1 character. A term's letter/digit runs of ≥ <see cref="NgramSearch.MinWordLength"/> characters go to the
/// ngram FULLTEXT index (script 014) — it narrows the rows; the escaped <c>LIKE '%term%'</c> decides. A text with no
/// such run (a 1-character term) is matched by the LIKE alone, on the same columns, inside the caller's scope.</item>
/// <item>Accent- and case-insensitive through the columns' utf8mb4_0900_ai_ci collation ("pergjigje" finds
/// "Përgjigje", "c" finds "ç"), in the LIKE and in the index alike.</item>
/// <item>Nothing the user types is an operator: LIKE wildcards (% _ \) are escaped, and the FULLTEXT query is built from
/// letter/digit runs only (+ - * " ( ) ~ &lt; &gt; @ never reach it).</item>
/// </list>
/// </summary>
public sealed partial class TextSearch
{
    public const int MaxTerms = 8;
    public const int MaxLength = 100;

    private TextSearch(string text, IReadOnlyList<string> terms, string words)
    {
        Text = text;
        Terms = terms;
        Words = words;
    }

    /// <summary>The trimmed text.</summary>
    public string Text { get; }

    /// <summary>The whitespace-separated terms (distinct, case-insensitively).</summary>
    public IReadOnlyList<string> Terms { get; }

    /// <summary>The ngram BOOLEAN query ("+2026 +12"), or "" when no term has a run of ≥ 2 letters / digits.</summary>
    public string Words { get; }

    public bool IsEmpty => Terms.Count == 0;

    public static TextSearch Parse(string? q)
    {
        var text = (q ?? string.Empty).Trim();
        var terms = WhitespaceRegex().Split(text).Where(t => t.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase).Take(MaxTerms).ToList();
        return new TextSearch(text, terms, NgramSearch.BooleanQuery(string.Join(' ', terms)));
    }

    /// <summary>'%term%' with the LIKE wildcards and the escape character escaped (MySQL's default escape is \).</summary>
    public static string LikePattern(string term)
    {
        var builder = new StringBuilder(term.Length + 8).Append('%');
        foreach (var ch in term)
        {
            if (ch is '\\' or '%' or '_')
            {
                builder.Append('\\');
            }

            builder.Append(ch);
        }

        return builder.Append('%').ToString();
    }

    /// <summary>
    /// The predicate of one FULLTEXT index: <c>MATCH(columns) AGAINST (@name_w …) AND (c1 LIKE @name_l0 OR c2 LIKE
    /// @name_l0) AND …</c> — the MATCH only when there are ngram words. <paramref name="columns"/> are exactly the
    /// index's columns (MATCH requires it), optionally with a table alias ("p.display_name").
    /// Parameters: <see cref="Parameters"/> with the same name. Empty search: <c>FALSE</c>.
    /// </summary>
    public string Predicate(string name, params string[] columns) => Predicate(name, columns, columns);

    /// <summary>
    /// As <see cref="Predicate(string, string[])"/>, the index on <paramref name="indexColumns"/> narrowing and the LIKE
    /// on <paramref name="likeColumns"/> only (e.g. the 3-column number index, the match kept inside the case code).
    /// </summary>
    public string Predicate(string name, string[] indexColumns, string[] likeColumns)
    {
        if (IsEmpty)
        {
            return "FALSE";
        }

        var match = Words.Length > 0 ? $"MATCH({string.Join(", ", indexColumns)}) AGAINST (@{name}_w IN BOOLEAN MODE) AND " : string.Empty;
        return match + LikePredicate(name, likeColumns);
    }

    /// <summary>The LIKE-only predicate (columns without a FULLTEXT index, e.g. a single code column).</summary>
    public string LikePredicate(string name, params string[] columns) =>
        IsEmpty
            ? "FALSE"
            : string.Join(" AND ", Terms.Select((_, i) => "(" + string.Join(" OR ", columns.Select(c => $"{c} LIKE @{name}_l{i}")) + ")"));

    /// <summary>The parameters of <see cref="Predicate"/> / <see cref="LikePredicate"/> named <paramref name="name"/>.</summary>
    public IReadOnlyList<MySqlParameter> Parameters(string name)
    {
        var list = new List<MySqlParameter>(Terms.Count + 1) { new($"@{name}_w", Words) };
        list.AddRange(Terms.Select((t, i) => new MySqlParameter($"@{name}_l{i}", LikePattern(t))));
        return list;
    }

    /// <summary>
    /// The predicate with its values written in as SQL string literals — for the places that compose SQL text across
    /// modules (a contract carries the text, not parameters). Values are escaped for a single-quoted MySQL literal.
    /// </summary>
    public string InlinePredicate(params string[] columns)
    {
        if (IsEmpty)
        {
            return "FALSE";
        }

        var likes = Terms.Select(t => "(" + string.Join(" OR ", columns.Select(c => $"{c} LIKE {Literal(LikePattern(t))}")) + ")");
        var match = Words.Length > 0 ? $"MATCH({string.Join(", ", columns)}) AGAINST ({Literal(Words)} IN BOOLEAN MODE) AND " : string.Empty;
        return match + string.Join(" AND ", likes);
    }

    /// <summary>A single-quoted MySQL string literal (backslash and quote escaped).</summary>
    public static string Literal(string value) => "'" + value.Replace("\\", "\\\\").Replace("'", "\\'") + "'";

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();
}
