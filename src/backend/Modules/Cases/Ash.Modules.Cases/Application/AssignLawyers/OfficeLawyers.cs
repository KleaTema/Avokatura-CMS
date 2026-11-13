using Ash.Modules.Cases.Infrastructure;
using Ash.Modules.Identity.Contracts;
using Ash.SharedKernel.Identity;
using Microsoft.EntityFrameworkCore;

namespace Ash.Modules.Cases.Application.AssignLawyers;

/// <summary>
/// A lawyer the Kryetar may pick, and for which position (task 053i: primary only with STATE_LAWYER, secondary only
/// with SECONDARY_LAWYER). The internal id is for the assignment row only, never sent out.
/// </summary>
internal sealed record LawyerCandidate(long UserId, Guid PublicId, string FullName, bool IsSelf, bool CanBePrimary, bool CanBeSecondary);

/// <summary>
/// Who can be assigned in a case of an office: the active users of that office with STATE_LAWYER ("Avokat Shteti
/// (primar)") or SECONDARY_LAWYER ("Avokat sekondar"), each marked with the positions their roles allow (users come
/// from Identity.Contracts — Cases does not read Identity tables), plus the current user (the Kryetar, a Monitor) only
/// when they hold one of these roles themselves (task 053i; on a supervised case of another office too). The same list
/// is offered by the picker and checked by the assignment and the transfer.
/// TODO(client-q-cross-office): lawyers of other offices are not offered (default).
/// </summary>
internal sealed class OfficeLawyers(CasesDbContext db, IUserDirectory users, ICurrentUser user)
{
    public const string LawyerRole = "STATE_LAWYER";
    public const string SecondaryRole = "SECONDARY_LAWYER";
    public const string AssignPermission = "case.assign_lawyer";

    public const string NotPrimaryMessage = "Ky përdorues nuk ka rolin Avokat Shteti (primar).";
    public const string NotSecondaryMessage = "Ky përdorues nuk ka rolin Avokat sekondar.";

    public static readonly StringComparer NameOrder = StringComparer.InvariantCultureIgnoreCase;

    public async Task<List<LawyerCandidate>> ListAsync(long officeId, bool includeSelf, CancellationToken ct)
    {
        var primaries = (await users.GetOfficeUsersByRoleAsync(officeId, LawyerRole, ct)).ToDictionary(u => u.Id);
        var secondaries = (await users.GetOfficeUsersByRoleAsync(officeId, SecondaryRole, ct)).ToDictionary(u => u.Id);
        var result = primaries.Values.Concat(secondaries.Values).DistinctBy(u => u.Id)
            .Select(u => new LawyerCandidate(u.Id, u.PublicId, u.FullName, u.Id == user.UserId,
                primaries.ContainsKey(u.Id), secondaries.ContainsKey(u.Id)))
            .ToList();
        var selfPrimary = user.Roles.Contains(LawyerRole);
        var selfSecondary = user.Roles.Contains(SecondaryRole);
        if (includeSelf && !result.Any(c => c.IsSelf) && (selfPrimary || selfSecondary))
        {
            var self = (await users.GetRefsAsync([user.UserId], ct))[user.UserId];
            result.Add(new LawyerCandidate(user.UserId, self.PublicId, self.Name, true, selfPrimary, selfSecondary));
        }

        return result;
    }

    /// <summary>
    /// The 400 field message for a person picked for a position: not offered at all, or offered without the role the
    /// position needs. Null when the pick is fine.
    /// </summary>
    public static string? PositionError(LawyerCandidate? candidate, bool primary) =>
        candidate is null ? "Ky avokat nuk mund të caktohet në këtë çështje."
        : primary && !candidate.CanBePrimary ? NotPrimaryMessage
        : !primary && !candidate.CanBeSecondary ? NotSecondaryMessage
        : null;

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
