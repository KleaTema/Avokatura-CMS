using Ash.Modules.Reference.Contracts;

namespace Ash.Modules.Cases.Application.RegistrationSearch.Filters;

/// <summary>The court level (shkalla) of a level, by code; it narrows the courts offered (task 053j: shkalla → gjykata → gjyqtari).</summary>
internal sealed class CourtLevelSearchFilter : CodeFilter
{
    public override string Name => "courtLevel";
    public override int Order => 25;
    public override RegistrationFilterInput Input => RegistrationFilterInput.CourtLevel;
    protected override string InvalidMessage => "Shkalla nuk është e vlefshme.";

    protected override async Task<byte?> ResolveAsync(string code, IReferenceData reference, CancellationToken ct) =>
        (await reference.GetCourtLevelByCode(code, ct))?.Id;

    protected override string Predicate(string id) => $"a.court_level_id = {id}";
}
