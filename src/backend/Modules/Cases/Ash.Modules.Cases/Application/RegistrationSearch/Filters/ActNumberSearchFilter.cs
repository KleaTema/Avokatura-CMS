namespace Ash.Modules.Cases.Application.RegistrationSearch.Filters;

/// <summary>The act number (Nr. i aktit), anywhere inside cases.act_number.</summary>
internal sealed class ActNumberSearchFilter : TextFilter
{
    public override string Name => "actNumber";
    public override int Order => 20;

    protected override string Predicate(string words, string like) =>
        $"MATCH(a.case_code, a.act_number, a.summons_number) AGAINST ({words} IN BOOLEAN MODE) AND a.act_number LIKE {like}";
}
