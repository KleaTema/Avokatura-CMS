namespace Ash.Modules.Cases.Application.RegistrationSearch.Filters;

/// <summary>Text inside the object of the dispute (ngram index ftn_cases_object, script 014, + the contains rule).</summary>
internal sealed class ObjectSearchFilter : TextFilter
{
    public override string Name => "object";
    public override int Order => 70;

    protected override string Predicate(Ash.Infrastructure.Persistence.TextSearch search, string name) =>
        search.Predicate(name, "a.object_text");
}
