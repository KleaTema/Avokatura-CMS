using Ash.Modules.Reference.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Ash.Modules.Reference.Infrastructure;

public sealed class ReferenceData(ReferenceDbContext db, ReferenceCache cache) : IReferenceData
{
    private Task<IReadOnlyList<T>> Cached<T>(string key, Func<CancellationToken, Task<List<T>>> load, CancellationToken ct) =>
        cache.GetOrCreateAsync<IReadOnlyList<T>>(key, async token => await load(token), ct);

    public Task<IReadOnlyList<CourtLevelDto>> ListCourtLevelsAsync(CancellationToken ct = default) =>
        Cached("court-levels", t => db.CourtLevels.AsNoTracking().OrderBy(x => x.SortOrder)
            .Select(x => new CourtLevelDto(x.Id, x.Code, x.Name, x.SortOrder, x.AllowsDelegation, x.AppealKind)).ToListAsync(t), ct);

    public Task<IReadOnlyList<CourtDto>> ListCourtsAsync(CancellationToken ct = default) =>
        Cached("courts", t => db.Courts.AsNoTracking().OrderBy(x => x.CourtLevelId).ThenBy(x => x.Name)
            .Select(x => new CourtDto(x.Id, x.Name, x.CourtLevelId, x.City, x.IsForeign, x.IsActive)).ToListAsync(t), ct);

    public Task<IReadOnlyList<JudgeDto>> ListJudgesAsync(CancellationToken ct = default) =>
        Cached("judges", t => db.Judges.AsNoTracking().OrderBy(x => x.LastName).ThenBy(x => x.FirstName)
            .Select(x => new JudgeDto(x.Id, x.FirstName, x.LastName, x.CourtId, x.IsActive)).ToListAsync(t), ct);

    public Task<IReadOnlyList<PartyRoleDto>> ListPartyRolesAsync(CancellationToken ct = default) =>
        Cached("party-roles", t => db.PartyRoles.AsNoTracking().OrderBy(x => x.SortOrder)
            .Select(x => new PartyRoleDto(x.Id, x.Code, x.Name, x.ReportGroup, x.SortOrder, x.IsActive)).ToListAsync(t), ct);

    public Task<IReadOnlyList<SummonsTypeDto>> ListSummonsTypesAsync(CancellationToken ct = default) =>
        Cached("summons-types", t => db.SummonsTypes.AsNoTracking().OrderBy(x => x.Name)
            .Select(x => new SummonsTypeDto(x.Id, x.Code, x.Name, x.IsActive)).ToListAsync(t), ct);

    public Task<IReadOnlyList<HearingTypeDto>> ListHearingTypesAsync(CancellationToken ct = default) =>
        Cached("hearing-types", t => db.HearingTypes.AsNoTracking().OrderBy(x => x.SortOrder)
            .Select(x => new HearingTypeDto(x.Id, x.Code, x.Name, x.IsOther, x.SortOrder, x.IsActive)).ToListAsync(t), ct);

    public Task<IReadOnlyList<DocumentTypeDto>> ListDocumentTypesAsync(CancellationToken ct = default) =>
        Cached("document-types", t => db.DocumentTypes.AsNoTracking().OrderBy(x => x.SortOrder)
            .Select(x => new DocumentTypeDto(x.Id, x.Code, x.Name, x.IsOther, x.SortOrder, x.IsActive)).ToListAsync(t), ct);

    public Task<IReadOnlyList<DecisionResultDto>> ListDecisionResultsAsync(CancellationToken ct = default) =>
        Cached("decision-results", t => db.DecisionResults.AsNoTracking().OrderBy(x => x.SortOrder)
            .Select(x => new DecisionResultDto(x.Id, x.Code, x.Name, x.OutcomeGroup, x.SendsBackForRetrial, x.IsOther, x.SortOrder, x.IsActive))
            .ToListAsync(t), ct);

    public Task<IReadOnlyList<CaseCategoryDto>> ListCaseCategoriesAsync(CancellationToken ct = default) =>
        Cached("case-categories", t => db.CaseCategories.AsNoTracking().OrderBy(x => x.SortOrder)
            .Select(x => new CaseCategoryDto(x.Id, x.Code, x.Name, x.IsDelegation, x.SortOrder)).ToListAsync(t), ct);

    public Task<IReadOnlyList<CaseSubcategoryDto>> ListCaseSubcategoriesAsync(CancellationToken ct = default) =>
        Cached("case-subcategories", t => db.CaseSubcategories.AsNoTracking().OrderBy(x => x.CategoryId).ThenBy(x => x.SortOrder)
            .Select(x => new CaseSubcategoryDto(x.Id, x.CategoryId, x.Code, x.Name, x.SortOrder)).ToListAsync(t), ct);

    public Task<IReadOnlyList<AllowedFileTypeDto>> ListAllowedFileTypesAsync(CancellationToken ct = default) =>
        Cached("allowed-file-types", t => db.AllowedFileTypes.AsNoTracking().OrderBy(x => x.Extension).ThenBy(x => x.AppliesTo)
            .Select(x => new AllowedFileTypeDto(x.Id, x.Extension, x.ContentType, x.MagicHex, x.MaxSizeBytes, x.AppliesTo, x.IsActive))
            .ToListAsync(t), ct);

    public async Task<CourtLevelDto?> GetCourtLevel(byte id, CancellationToken ct = default) =>
        (await ListCourtLevelsAsync(ct)).FirstOrDefault(x => x.Id == id);

    public async Task<CourtLevelDto?> GetCourtLevelByCode(string code, CancellationToken ct = default) =>
        (await ListCourtLevelsAsync(ct)).FirstOrDefault(x => SameCode(x.Code, code));

    public async Task<CourtDto?> GetCourt(ushort id, CancellationToken ct = default) =>
        (await ListCourtsAsync(ct)).FirstOrDefault(x => x.Id == id);

    public async Task<JudgeDto?> GetJudge(uint id, CancellationToken ct = default) =>
        (await ListJudgesAsync(ct)).FirstOrDefault(x => x.Id == id);

    public async Task<PartyRoleDto?> GetPartyRole(string code, CancellationToken ct = default) =>
        (await ListPartyRolesAsync(ct)).FirstOrDefault(x => SameCode(x.Code, code));

    public async Task<SummonsTypeDto?> GetSummonsType(string code, CancellationToken ct = default) =>
        (await ListSummonsTypesAsync(ct)).FirstOrDefault(x => SameCode(x.Code, code));

    public async Task<HearingTypeDto?> GetHearingType(string code, CancellationToken ct = default) =>
        (await ListHearingTypesAsync(ct)).FirstOrDefault(x => SameCode(x.Code, code));

    public async Task<DocumentTypeDto?> GetDocumentType(string code, CancellationToken ct = default) =>
        (await ListDocumentTypesAsync(ct)).FirstOrDefault(x => SameCode(x.Code, code));

    public async Task<DecisionResultDto?> GetResultByCode(string code, CancellationToken ct = default) =>
        (await ListDecisionResultsAsync(ct)).FirstOrDefault(x => SameCode(x.Code, code));

    public async Task<CaseCategoryDto?> GetCaseCategory(string code, CancellationToken ct = default) =>
        (await ListCaseCategoriesAsync(ct)).FirstOrDefault(x => SameCode(x.Code, code));

    public async Task<CaseSubcategoryDto?> GetCaseSubcategory(string code, CancellationToken ct = default) =>
        (await ListCaseSubcategoriesAsync(ct)).FirstOrDefault(x => SameCode(x.Code, code));

    private static bool SameCode(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
