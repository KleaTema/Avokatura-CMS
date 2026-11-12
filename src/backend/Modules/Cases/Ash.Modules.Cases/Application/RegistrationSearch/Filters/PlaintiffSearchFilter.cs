namespace Ash.Modules.Cases.Application.RegistrationSearch.Filters;

/// <summary>Paditësi: a party whose role is in the PLAINTIFF report group (name or NIPT).</summary>
internal sealed class PlaintiffSearchFilter : PartyFilter
{
    public override string Name => "plaintiff";
    public override int Order => 50;
    protected override string ReportGroup => "PLAINTIFF";
}
