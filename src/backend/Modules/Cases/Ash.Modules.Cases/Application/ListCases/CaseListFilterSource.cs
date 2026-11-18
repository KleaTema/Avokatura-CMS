using Ash.Modules.Cases.Infrastructure;
using Ash.Modules.Identity.Contracts;
using Ash.Modules.Reference.Contracts;
using Ash.SharedKernel.Filters;
using Ash.SharedKernel.Identity;
using Microsoft.EntityFrameworkCore;

namespace Ash.Modules.Cases.Application.ListCases;

/// <summary>
/// Task 053m — the filters of the case list (GET /api/filters/cases?tab=…): one per column of the list plus the case
/// facts the list filters on, each writing the query parameter GET /api/cases reads. Defaults (the page's main filters):
/// the search, the status, the court. The selects offer only what exists among the cases of the user's tab (its scope);
/// dependent ones name their parent value (shkalla → gjykata → gjyqtari, zyra → avokati).
/// </summary>
internal sealed class CaseListFilterSource(
    CaseListQueryBuilder builder,
    CasesDbContext db,
    IReferenceData reference,
    IOfficeDirectory offices,
    IUserDirectory users,
    IInstitutionDirectory institutions,
    ICurrentUser user) : ITableFilterSource
{
    public string Table => "cases";

    public string? Permission => null;

    public static readonly IReadOnlyList<FilterOption> Statuses =
    [
        new("OPEN", "Të hapura"), new("ALL", "Të gjitha"), new(Case.Registered, "Pa avokat"), new(Case.Active, "Aktive"),
        new(Case.Closed, "Të mbyllura"),
    ];

    public static readonly IReadOnlyList<FilterOption> Priorities =
        [new("HIGH", "I lartë"), new("MEDIUM", "Mesatar"), new("LOW", "I ulët"), new("NONE", "Pa prioritet")];

    public async Task<IReadOnlyList<TableFilter>> DescribeAsync(IReadOnlyDictionary<string, string> context, CancellationToken ct)
    {
        var tabs = CaseListTabs.For(user);
        var tab = context.TryGetValue("tab", out var code) && CaseListTabs.TryParse(code, out var parsed) && tabs.Contains(parsed)
            ? parsed
            : tabs.FirstOrDefault();
        // the cases of the tab, every status: what the selects may offer
        var scope = builder.Filtered(new CaseListFilter(tab, CaseListFilter.AllStatuses, null, null, null, null, null, null, null, null));

        var courtIds = await scope.Select(c => c.CourtId).Distinct().ToListAsync(ct);
        var judgeIds = await scope.Where(c => c.JudgeId != null).Select(c => c.JudgeId!.Value).Distinct().ToListAsync(ct);
        var officeIds = await scope.Select(c => c.OfficeId).Distinct().ToListAsync(ct);
        var categoryIds = await scope.Where(c => c.CategoryId != null).Select(c => c.CategoryId!.Value).Distinct().ToListAsync(ct);
        var lawyerRoles = new[] { CaseAssignmentRow.PrimaryLawyer, CaseAssignmentRow.SecondaryLawyer };
        var lawyerOffices = await (from a in db.CaseAssignments.AsNoTracking()
                                   join c in scope on a.CaseId equals c.Id
                                   where a.EndedAt == null && lawyerRoles.Contains(a.CaseRole)
                                   select new { a.UserId, c.OfficeId }).Distinct().ToListAsync(ct);
        var institutionIds = await (from p in db.CaseParties.AsNoTracking()
                                    join c in scope on p.CaseId equals c.Id
                                    where p.DeletedAt == null && p.InstitutionId != null
                                    select p.InstitutionId!.Value).Distinct().ToListAsync(ct);

        var levels = (await reference.ListCourtLevelsAsync(ct)).ToDictionary(l => l.Id);
        var courts = (await reference.ListCourtsAsync(ct)).Where(c => courtIds.Contains(c.Id)).OrderBy(c => c.Name).ToList();
        var judges = (await reference.ListJudgesAsync(ct)).Where(j => judgeIds.Contains(j.Id)).OrderBy(j => j.LastName).ThenBy(j => j.FirstName);
        var categories = (await reference.ListCaseCategoriesAsync(ct)).Where(c => categoryIds.Contains(c.Id)).OrderBy(c => c.Name);
        var officeInfos = new Dictionary<ushort, OfficeInfo>();
        foreach (var id in officeIds)
        {
            if (await offices.GetByIdAsync(id, ct) is { } office)
            {
                officeInfos[id] = office;
            }
        }

        var lawyerNames = await users.GetNamesAsync(lawyerOffices.Select(l => (long)l.UserId).Distinct().ToList(), ct);
        var lawyerIds = await users.GetPublicIdsAsync(lawyerNames.Keys.ToList(), ct);
        var institutionInfos = await institutions.GetByIdsAsync(institutionIds.Select(i => (long)i).ToList(), ct);

        // the office only for who sees every office (the Monitor), the lawyer for the Kryetar too (task 031)
        var byOffice = user.HasPermission("case.view_all");
        var byLawyer = byOffice || user.HasPermission("case.view_office");

        // the keys are the case list page's URL parameters (the dashboard links use them); the page maps them to the
        // API's (court → courtId, level → courtLevel, from / to → hearingFrom / hearingTo …)
        IEnumerable<TableFilter> all =
        [
            TableFilter.Of("q", "Kërko (kodi, nr. i aktit, pala, objekti)", FilterKinds.Text, isDefault: true),
            TableFilter.Of("status", "Statusi", FilterKinds.Select, isDefault: true, options: Statuses),
            TableFilter.Of("level", "Shkalla e gjykimit", FilterKinds.Select,
                options: courts.Select(c => c.CourtLevelId).Distinct().Where(levels.ContainsKey)
                    .Select(id => new FilterOption(levels[id].Code, levels[id].Name)).ToList()),
            TableFilter.Of("court", "Gjykata", FilterKinds.Select, isDefault: true, parent: "level",
                options: courts.Select(c => new FilterOption(c.Id.ToString(), c.Name,
                    levels.TryGetValue(c.CourtLevelId, out var level) ? level.Code : null)).ToList()),
            TableFilter.Of("judge", "Gjyqtari", FilterKinds.Select, parent: "court",
                options: judges.Select(j => new FilterOption(j.Id.ToString(), $"{j.FirstName} {j.LastName}", j.CourtId?.ToString())).ToList()),
            TableFilter.Of("code", "Kodi unik", FilterKinds.Text),
            TableFilter.Of("actNumber", "Nr. i aktit", FilterKinds.Text),
            TableFilter.Of("office", "Zyra", FilterKinds.Select,
                options: officeInfos.Values.OrderBy(o => o.Name).Select(o => new FilterOption(o.PublicId.ToString(), o.Name)).ToList()),
            TableFilter.Of("lawyer", "Avokati", FilterKinds.Select, parent: "office",
                options: lawyerOffices.GroupBy(l => l.UserId)
                    .Where(g => lawyerNames.ContainsKey(g.Key) && lawyerIds.ContainsKey(g.Key))
                    .OrderBy(g => lawyerNames[g.Key])
                    .Select(g => new FilterOption(lawyerIds[g.Key].ToString(), lawyerNames[g.Key],
                        officeInfos.TryGetValue(g.First().OfficeId, out var o) ? o.PublicId.ToString() : null))
                    .ToList()),
            TableFilter.Of("institution", "Institucioni (palë)", FilterKinds.Select,
                options: institutionInfos.OrderBy(i => i.Name).Select(i => new FilterOption(i.PublicId.ToString(), i.Name)).ToList()),
            TableFilter.Of("category", "Kategoria", FilterKinds.Select,
                options: categories.Select(c => new FilterOption(c.Code, c.Name)).ToList()),
            TableFilter.Of("priority", "Prioriteti", FilterKinds.Select, options: Priorities),
            TableFilter.Of("suspended", "Pezulluar", FilterKinds.YesNo),
            TableFilter.Range("hearing", "Seanca e radhës", FilterKinds.DateRange, "from", "to"),
            TableFilter.Range("registered", "Data e regjistrimit", FilterKinds.DateRange, "regFrom", "regTo"),
        ];
        return all.Where(f => (f.Key != "office" || byOffice) && (f.Key != "lawyer" || byLawyer)).ToList();
    }
}
