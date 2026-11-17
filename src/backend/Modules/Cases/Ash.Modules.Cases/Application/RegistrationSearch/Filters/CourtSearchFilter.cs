namespace Ash.Modules.Cases.Application.RegistrationSearch.Filters;

/// <summary>The court of a level.</summary>
internal sealed class CourtSearchFilter : IdFilter
{
    public override string Name => "courtId";
    public override int Order => 30;
    public override RegistrationFilterInput Input => RegistrationFilterInput.Court;
    public override string? DependsOn => "courtLevel";
    protected override string Column => "court_id";
    protected override string InvalidMessage => "Gjykata nuk është e vlefshme.";
}
