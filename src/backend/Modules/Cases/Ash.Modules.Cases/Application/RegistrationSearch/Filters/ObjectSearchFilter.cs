namespace Ash.Modules.Cases.Application.RegistrationSearch.Filters;

/// <summary>Words of the object of the dispute (ngram index ftn_cases_object, script 014).</summary>
internal sealed class ObjectSearchFilter : TextFilter
{
    public override string Name => "object";
    public override int Order => 70;

    protected override string Predicate(string words, string like) => $"MATCH(a.object_text) AGAINST ({words} IN BOOLEAN MODE)";
}
