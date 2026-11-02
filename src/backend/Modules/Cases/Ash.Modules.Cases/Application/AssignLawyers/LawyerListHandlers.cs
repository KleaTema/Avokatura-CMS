using Ash.Modules.Cases.Contracts;
using Ash.Modules.Cases.Infrastructure;
using Ash.Modules.Cases.Infrastructure.Access;
using Ash.SharedKernel.Identity;
using Ash.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Ash.Modules.Cases.Application.AssignLawyers;

/// <summary>
/// GET /api/cases/{id}/assignable-lawyers: the lawyers the current user may assign in this case, least loaded first.
/// No view → 404; neither case.assign_lawyer (REGISTERED) nor case.transfer (ACTIVE) in the case's effective
/// permissions → 403.
/// </summary>
internal sealed class AssignableLawyersHandler(CasesDbContext db, ICaseAccessService access, OfficeLawyers lawyers)
{
    public async Task<Result<IReadOnlyList<AssignableLawyer>>> Handle(Guid casePublicId, CancellationToken ct)
    {
        var c = await db.Cases.AsNoTracking()
            .Where(x => x.PublicId == casePublicId)
            .Select(x => new { x.Id, x.OfficeId })
            .SingleOrDefaultAsync(ct);
        if (c is null || !await access.CanViewAsync((long)c.Id, ct))
        {
            return AssignLawyersHandler.NotFound();
        }

        // the picker of both the assignment (REGISTERED) and the transfer (ACTIVE)
        var effective = await access.GetEffectivePermissionsAsync((long)c.Id, ct);
        if (!effective.Contains(OfficeLawyers.AssignPermission) && !effective.Contains(CaseAccessService.TransferPermission))
        {
            return AssignLawyersHandler.Forbidden();
        }

        var candidates = await lawyers.ListAsync(c.OfficeId, includeSelf: true, ct);
        var workload = await lawyers.WorkloadAsync(candidates.Select(x => x.UserId).ToList(), ct);
        var none = new LawyerWorkload(0, 0);
        return candidates
            .Select(x => new AssignableLawyer(x.PublicId, x.FullName, x.IsSelf, workload.GetValueOrDefault(x.UserId, none)))
            .OrderBy(x => x.Workload.ActivePrimary)
            .ThenBy(x => x.FullName, OfficeLawyers.NameOrder)
            .ToList();
    }
}

/// <summary>
/// GET /api/cases/office-lawyers (case.view_office): the same people as the picker offers for the user's own office,
/// without workload, sorted by name — the lawyer filter of the case list.
/// </summary>
internal sealed class OfficeLawyersHandler(OfficeLawyers lawyers, ICurrentUser user)
{
    public async Task<IReadOnlyList<OfficeLawyer>> Handle(CancellationToken ct)
    {
        if (user.OfficeId is not { } officeId)
        {
            return [];
        }

        var candidates = await lawyers.ListAsync(officeId, user.HasPermission(OfficeLawyers.AssignPermission), ct);
        return candidates
            .Select(x => new OfficeLawyer(x.PublicId, x.FullName, x.IsSelf))
            .OrderBy(x => x.FullName, OfficeLawyers.NameOrder)
            .ToList();
    }
}
