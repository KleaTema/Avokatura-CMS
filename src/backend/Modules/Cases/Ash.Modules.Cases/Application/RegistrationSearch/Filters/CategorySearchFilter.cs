using Ash.Modules.Reference.Contracts;

namespace Ash.Modules.Cases.Application.RegistrationSearch.Filters;

/// <summary>The category of a level (Ndjekje / Delegim / Relatim), by code.</summary>
internal sealed class CategorySearchFilter : CodeFilter
{
    public override string Name => "category";
    public override int Order => 80;
    public override RegistrationFilterInput Input => RegistrationFilterInput.Category;
    protected override string InvalidMessage => "Kategoria nuk është e vlefshme.";

    protected override async Task<byte?> ResolveAsync(string code, IReferenceData reference, CancellationToken ct) =>
        (await reference.GetCaseCategory(code, ct))?.Id;

    protected override string Predicate(string id) => $"a.category_id = {id}";
}
