using System.Text.RegularExpressions;

namespace Ash.Infrastructure.Persistence;

/// <summary>
/// The search text for MATCH … AGAINST (… IN BOOLEAN MODE) over the ngram FULLTEXT indexes (script 014).
/// Every word of at least <see cref="MinWordLength"/> letters or digits is required ("+word"); the ngram parser turns
/// each word into a phrase of its 2-character tokens, so a word matches anywhere inside a name or number ("640" finds
/// "2026-686640/1"). Anything else (punctuation, boolean operators) separates words, so the user cannot inject
/// operators. Accents and case are folded by the columns' utf8mb4_0900_ai_ci collation.
/// </summary>
public static partial class NgramSearch
{
    /// <summary>The ngram token size (server default ngram_token_size = 2): shorter words cannot match.</summary>
    public const int MinWordLength = 2;

    /// <summary>"+word1 +word2", or an empty string when q has no word to search for (then nothing matches).</summary>
    public static string BooleanQuery(string q) =>
        string.Join(' ', WordRegex().Matches(q).Select(m => m.Value).Where(w => w.Length >= MinWordLength).Select(w => $"+{w}"));

    [GeneratedRegex(@"[\p{L}\p{N}]+")]
    private static partial Regex WordRegex();
}
