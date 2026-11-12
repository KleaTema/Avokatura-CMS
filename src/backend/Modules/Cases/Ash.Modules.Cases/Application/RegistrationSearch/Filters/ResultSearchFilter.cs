using Ash.Modules.Reference.Contracts;

namespace Ash.Modules.Cases.Application.RegistrationSearch.Filters;

/// <summary>The decision result recorded on a level (Rezultati i çështjes), by code.</summary>
internal sealed class ResultSearchFilter : CodeFilter
{
    public override string Name => "result";
    public override int Order => 100;
    public override RegistrationFilterInput Input => RegistrationFilterInput.DecisionResult;
    protected override string InvalidMessage => "Rezultati nuk është i vlefshëm.";

    protected override async Task<byte?> ResolveAsync(string code, IReferenceData reference, CancellationToken ct) =>
        (await reference.GetResultByCode(code, ct))?.Id;

    protected override string Predicate(string id) => $"a.id IN (SELECT d.case_id FROM case_decisions d WHERE d.result_id = {id})";
}
