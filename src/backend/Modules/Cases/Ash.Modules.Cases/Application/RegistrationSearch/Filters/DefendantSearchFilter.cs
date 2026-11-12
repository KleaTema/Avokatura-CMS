namespace Ash.Modules.Cases.Application.RegistrationSearch.Filters;

/// <summary>I padituri / person i tretë: a party in the DEFENDANT_OR_THIRD report group (name or NIPT).</summary>
internal sealed class DefendantSearchFilter : PartyFilter
{
    public override string Name => "defendant";
    public override int Order => 60;
    protected override string ReportGroup => "DEFENDANT_OR_THIRD";
}
