namespace Ash.Modules.Reference.Contracts;

/// <summary>
/// Cached read access to the lookup lists. List methods and Get methods return inactive rows too
/// (old cases still need their names); callers that fill pickers must filter on IsActive.
/// </summary>
public interface IReferenceData
{
    Task<IReadOnlyList<CourtLevelDto>> ListCourtLevelsAsync(CancellationToken ct = default);
    Task<IReadOnlyList<CourtDto>> ListCourtsAsync(CancellationToken ct = default);
    Task<IReadOnlyList<JudgeDto>> ListJudgesAsync(CancellationToken ct = default);
    Task<IReadOnlyList<PartyRoleDto>> ListPartyRolesAsync(CancellationToken ct = default);
    Task<IReadOnlyList<SummonsTypeDto>> ListSummonsTypesAsync(CancellationToken ct = default);
    Task<IReadOnlyList<HearingTypeDto>> ListHearingTypesAsync(CancellationToken ct = default);
    Task<IReadOnlyList<DocumentTypeDto>> ListDocumentTypesAsync(CancellationToken ct = default);
    Task<IReadOnlyList<DecisionResultDto>> ListDecisionResultsAsync(CancellationToken ct = default);
    Task<IReadOnlyList<CaseCategoryDto>> ListCaseCategoriesAsync(CancellationToken ct = default);
    Task<IReadOnlyList<CaseSubcategoryDto>> ListCaseSubcategoriesAsync(CancellationToken ct = default);
    Task<IReadOnlyList<AllowedFileTypeDto>> ListAllowedFileTypesAsync(CancellationToken ct = default);

    Task<CourtLevelDto?> GetCourtLevel(byte id, CancellationToken ct = default);
    Task<CourtLevelDto?> GetCourtLevelByCode(string code, CancellationToken ct = default);
    Task<CourtDto?> GetCourt(ushort id, CancellationToken ct = default);
    Task<JudgeDto?> GetJudge(uint id, CancellationToken ct = default);
    Task<PartyRoleDto?> GetPartyRole(string code, CancellationToken ct = default);
    Task<SummonsTypeDto?> GetSummonsType(string code, CancellationToken ct = default);
    Task<HearingTypeDto?> GetHearingType(string code, CancellationToken ct = default);
    Task<DocumentTypeDto?> GetDocumentType(string code, CancellationToken ct = default);
    Task<DecisionResultDto?> GetResultByCode(string code, CancellationToken ct = default);
    Task<CaseCategoryDto?> GetCaseCategory(string code, CancellationToken ct = default);
    Task<CaseSubcategoryDto?> GetCaseSubcategory(string code, CancellationToken ct = default);
}
