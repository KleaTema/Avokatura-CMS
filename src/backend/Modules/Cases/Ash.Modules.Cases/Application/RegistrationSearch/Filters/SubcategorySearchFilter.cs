using Ash.Modules.Reference.Contracts;

namespace Ash.Modules.Cases.Application.RegistrationSearch.Filters;

/// <summary>The subcategory (lloji) of a level, by code; the UI offers the subcategories of the chosen category.</summary>
internal sealed class SubcategorySearchFilter : CodeFilter
{
    public override string Name => "subcategory";
    public override int Order => 90;
    public override RegistrationFilterInput Input => RegistrationFilterInput.Subcategory;
    public override string? DependsOn => "category";
    protected override string InvalidMessage => "Nënkategoria nuk është e vlefshme.";

    protected override async Task<byte?> ResolveAsync(string code, IReferenceData reference, CancellationToken ct) =>
        (await reference.GetCaseSubcategory(code, ct))?.Id;

    protected override string Predicate(string id) => $"a.subcategory_id = {id}";
}
