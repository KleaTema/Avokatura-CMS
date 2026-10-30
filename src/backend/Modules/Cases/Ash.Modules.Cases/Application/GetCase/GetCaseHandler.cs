using Ash.Modules.Audit.Contracts;
using Ash.Modules.Cases.Application.Parties;
using Ash.Modules.Cases.Contracts;
using Ash.Modules.Cases.Infrastructure;
using Ash.Modules.Documents.Contracts;
using Ash.Modules.Identity.Contracts;
using Ash.Modules.Reference.Contracts;
using Ash.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Ash.Modules.Cases.Application.GetCase;

/// <summary>
/// GET /api/cases/{id}: everything the case page shows. No view → 404. Names come from Identity (one batch call) and
/// Reference (cached); the documents count from Documents. Writes CASE_VIEW once.
/// </summary>
internal sealed class GetCaseHandler(
    CasesDbContext db,
    ICaseAccessService access,
    PartyViews partyViews,
    IReferenceData reference,
    IUserDirectory users,
    IInstitutionDirectory institutions,
    IDocumentCounts documents,
    IAuditLog audit)
{
    public const string JuristRole = "INSTITUTION_JURIST";

    public async Task<Result<GetCaseResponse>> Handle(Guid casePublicId, CancellationToken ct)
    {
        var c = await db.Cases.AsNoTracking().SingleOrDefaultAsync(x => x.PublicId == casePublicId, ct);
        if (c is null || !await access.CanViewAsync((long)c.Id, ct))
        {
            return Error.NotFound("case.not_found", "Çështja nuk u gjet.");
        }

        var caseId = (long)c.Id;
        var team = await db.CaseAssignments.AsNoTracking()
            .Where(a => a.CaseId == c.Id && a.EndedAt == null)
            .OrderBy(a => a.AssignedAt).ThenBy(a => a.Id)
            .ToListAsync(ct);
        var links = await db.CaseInstitutions.AsNoTracking()
            .Where(l => l.CaseId == c.Id && l.EndedAt == null)
            .OrderBy(l => l.StartedAt).ThenBy(l => l.Id)
            .ToListAsync(ct);
        var parties = await db.CaseParties.AsNoTracking()
            .Where(p => p.CaseId == c.Id && p.DeletedAt == null)
            .ToListAsync(ct);
        var hearingsCount = await db.CaseHearings.CountAsync(h => h.CaseId == c.Id, ct);
        var decision = await db.CaseDecisions.AsNoTracking().SingleOrDefaultAsync(d => d.CaseId == c.Id, ct);

        // one call to Identity for every person on the page
        var people = await users.GetRefsAsync(team.Select(a => (long)a.UserId).Append(c.RegisteredBy).ToList(), ct);
        var linked = (await institutions.GetByIdsAsync(links.Select(l => (long)l.InstitutionId).ToList(), ct))
            .ToDictionary(i => i.Id);

        var permissions = await access.GetEffectivePermissionsAsync(caseId, ct);
        var response = new GetCaseResponse(
            new CaseHeader(c.PublicId, c.CaseCode, c.LevelSeq, c.LifecycleStatus, c.Priority, c.IsSuspended, c.RowVersion),
            await DataAsync(c, people, ct),
            team.Where(a => people.ContainsKey(a.UserId))
                .Select(a => new CaseTeamMember(a.CaseRole, people[a.UserId].PublicId, people[a.UserId].Name, a.AssignedAt))
                .ToList(),
            links.Where(l => linked.ContainsKey(l.InstitutionId))
                .Select(l => new CaseInstitutionView(linked[l.InstitutionId].PublicId, linked[l.InstitutionId].Name, l.LinkMode,
                    team.Where(a => a.CaseRole == JuristRole
                                    && people.TryGetValue(a.UserId, out var p) && p.InstitutionId == l.InstitutionId)
                        .Select(a => people[a.UserId].Name)
                        .ToList()))
                .ToList(),
            await partyViews.ToResponsesAsync(parties, ct),
            hearingsCount,
            await documents.CountForCaseAsync(caseId, ct),
            decision is null ? null : await DecisionAsync(decision, ct),
            await LevelsAsync(c, ct),
            permissions.Order(StringComparer.Ordinal).ToList());

        audit.Record(AuditActions.CaseView, "CASE", caseId, caseId);
        return response;
    }

    private async Task<CaseData> DataAsync(Case c, IReadOnlyDictionary<long, UserRef> people, CancellationToken ct)
    {
        var level = await reference.GetCourtLevel(c.CourtLevelId, ct);
        var court = await reference.GetCourt(c.CourtId, ct);
        var judge = c.JudgeId is { } judgeId ? await reference.GetJudge(judgeId, ct) : null;
        var summonsType = c.SummonsTypeId is { } typeId
            ? (await reference.ListSummonsTypesAsync(ct)).FirstOrDefault(t => t.Id == typeId)
            : null;
        var category = c.CategoryId is { } categoryId
            ? (await reference.ListCaseCategoriesAsync(ct)).FirstOrDefault(x => x.Id == categoryId)
            : null;
        var subcategory = c.SubcategoryId is { } subcategoryId
            ? (await reference.ListCaseSubcategoriesAsync(ct)).FirstOrDefault(x => x.Id == subcategoryId)
            : null;
        var registeredBy = people.GetValueOrDefault(c.RegisteredBy);

        return new CaseData(
            c.SummonsNumber,
            summonsType is null ? null : new CodeName(summonsType.Code, summonsType.Name),
            c.ActNumber,
            c.ActDate,
            c.ReceivedDate,
            level is null ? null : new CodeName(level.Code, level.Name),
            court?.Name ?? string.Empty,
            judge is null ? null : $"{judge.FirstName} {judge.LastName}",
            c.ObjectText,
            category is null ? null : new CodeName(category.Code, category.Name),
            subcategory is null ? null : new CodeName(subcategory.Code, subcategory.Name),
            c.ClaimAmount is null && c.ClaimAreaM2 is null ? null : new ClaimView(c.ClaimAmount, c.ClaimCurrency, c.ClaimAreaM2),
            c.HasSecurityMeasure,
            c.NextHearingAt,
            c.RegisteredAt,
            registeredBy is null ? null : new UserView(registeredBy.PublicId, registeredBy.Name),
            c.ClosedAt);
    }

    private async Task<CaseDecisionSummary> DecisionAsync(CaseDecision d, CancellationToken ct)
    {
        var result = (await reference.ListDecisionResultsAsync(ct)).FirstOrDefault(r => r.Id == d.ResultId);
        return new CaseDecisionSummary(new CodeName(result?.Code ?? string.Empty, result?.Name ?? string.Empty),
            d.ResultOtherText, d.DecisionNumber, d.DecisionDate, d.DecisionReceived, d.IsAppealed, d.IsRecursed, d.IsFinal);
    }

    /// <summary>Every level of the dossier; a level the user cannot view shows only its code, court level and status.</summary>
    private async Task<IReadOnlyList<CaseLevelView>> LevelsAsync(Case c, CancellationToken ct)
    {
        var levels = await db.Cases.AsNoTracking()
            .Where(x => x.DossierId == c.DossierId)
            .OrderBy(x => x.LevelSeq)
            .Select(x => new { x.Id, x.PublicId, x.CaseCode, x.LevelSeq, x.CourtLevelId, x.LifecycleStatus })
            .ToListAsync(ct);
        var result = new List<CaseLevelView>(levels.Count);
        foreach (var level in levels)
        {
            var canOpen = await access.CanViewAsync((long)level.Id, ct);
            var courtLevel = await reference.GetCourtLevel(level.CourtLevelId, ct);
            result.Add(new CaseLevelView(level.CaseCode, level.LevelSeq, courtLevel?.Name ?? string.Empty, level.LifecycleStatus,
                canOpen ? level.PublicId : null, canOpen));
        }

        return result;
    }
}
