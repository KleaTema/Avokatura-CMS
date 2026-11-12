namespace Ash.Modules.Cases.Application.RegistrationSearch.Filters;

/// <summary>The unique code or the case code ("640" finds 2026-686640/1): inside cases.case_code.</summary>
internal sealed class CodeSearchFilter : TextFilter
{
    public override string Name => "code";
    public override int Order => 10;

    // the ngram index narrows the rows; LIKE keeps the match inside the case code (not the act or summons number)
    protected override string Predicate(string words, string like) =>
        $"MATCH(a.case_code, a.act_number, a.summons_number) AGAINST ({words} IN BOOLEAN MODE) AND a.case_code LIKE {like}";
}
