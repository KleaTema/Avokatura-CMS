namespace Ash.Modules.Cases.Application.RegistrationSearch.Filters;

/// <summary>The judge of a level; the UI lists the judges of the chosen court.</summary>
internal sealed class JudgeSearchFilter : IdFilter
{
    public override string Name => "judgeId";
    public override int Order => 40;
    public override RegistrationFilterInput Input => RegistrationFilterInput.Judge;
    public override string? DependsOn => "courtId";
    protected override string Column => "judge_id";
    protected override string InvalidMessage => "Gjyqtari nuk është i vlefshëm.";
}
