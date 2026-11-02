using Ash.Modules.Cases.Infrastructure;
using Ash.Modules.Identity.Contracts;
using Ash.SharedKernel.Identity;
using Microsoft.EntityFrameworkCore;

namespace Ash.Modules.Cases.Application.AssignLawyers;

/// <summary>A lawyer the Kryetar may pick. The internal id is for the assignment row only, never sent out.</summary>
internal sealed record LawyerCandidate(long UserId, Guid PublicId, string FullName, bool IsSelf);

/// <summary>
/// Who can be assigned in a case of an office: the active STATE_LAWYERs of that office (users come from
/// Identity.Contracts — Cases does not read Identity tables), plus the current user when they hold case.assign_lawyer
/// there (the Kryetar may assign himself). The same list is offered by the picker and checked by the assignment.
/// TODO(client-q-cross-office): lawyers of other offices are not offered (default).
/// </summary>
internal sealed class OfficeLawyers(CasesDbContext db, IUserDirectory users, ICurrentUser user)
{
    public const string LawyerRole = "STATE_LAWYER";
    public const string AssignPermission = "case.assign_lawyer";

    public static readonly StringComparer NameOrder = StringComparer.InvariantCultureIgnoreCase;

    public async Task<List<LawyerCandidate>> ListAsync(long officeId, bool includeSelf, CancellationToken ct)
    {
        var lawyers = await users.GetOfficeUsersByRoleAsync(officeId, LawyerRole, ct);
        var result = lawyers.Select(u => new LawyerCandidate(u.Id, u.PublicId, u.FullName, u.Id == user.UserId)).ToList();
        if (includeSelf && !result.Any(c => c.IsSelf))
        {
            var self = (await users.GetRefsAsync([user.UserId], ct))[user.UserId];
            result.Add(new LawyerCandidate(user.UserId, self.PublicId, self.Name, true));
        }

        return result;
    }

    /// <summary>
    /// Current PRIMARY / SECONDARY assignments per user on cases that are not CLOSED, in one grouped query;
    /// users without any are missing from the result.
    /// </summary>
    public async Task<Dictionary<long, LawyerWorkload>> WorkloadAsync(IReadOnlyCollection<long> userIds, CancellationToken ct)
    {
        var ids = userIds.Select(id => (uint)id).ToList();
        var rows = await (
                from a in db.CaseAssignments.AsNoTracking()
                join c in db.Cases.AsNoTracking() on a.CaseId equals c.Id
                where a.EndedAt == null && ids.Contains(a.UserId) && c.LifecycleStatus != Case.Closed
                      && (a.CaseRole == CaseAssignmentRow.PrimaryLawyer || a.CaseRole == CaseAssignmentRow.SecondaryLawyer)
                group a by new { a.UserId, a.CaseRole } into g
                select new { g.Key.UserId, g.Key.CaseRole, Count = g.Count() })
            .ToListAsync(ct);

        return rows.GroupBy(r => (long)r.UserId).ToDictionary(g => g.Key, g => new LawyerWorkload(
            g.Where(r => r.CaseRole == CaseAssignmentRow.PrimaryLawyer).Sum(r => r.Count),
            g.Where(r => r.CaseRole == CaseAssignmentRow.SecondaryLawyer).Sum(r => r.Count)));
    }
}
