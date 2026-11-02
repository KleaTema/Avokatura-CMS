using Ash.Modules.Cases.Application.GetCase;

namespace Ash.Modules.Cases.Application.DelegateCase;

/// <summary>
/// The case after (un)delegating: the institution, its link mode now (DELEGATED | MONITORING), the category
/// (DELEGIM / RELATIM, or null after undelegating) and the new row version.
/// </summary>
public sealed record DelegationResponse(
    uint RowVersion, Guid InstitutionPublicId, string InstitutionName, string LinkMode, CodeName? Category);
