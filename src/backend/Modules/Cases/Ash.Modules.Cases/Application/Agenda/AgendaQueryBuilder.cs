using Ash.Modules.Cases.Infrastructure;
using Ash.Modules.Cases.Infrastructure.Access;
using Ash.SharedKernel.Identity;
using Microsoft.EntityFrameworkCore;

namespace Ash.Modules.Cases.Application.Agenda;

/// <summary>
/// Builds the agenda query (task 031). Never v_user_agenda: it sits on the UNION view v_case_access, which cannot use an
/// index for one user. Instead each scope has its own index-friendly path, the filters, then the access scope ON TOP
/// (defence in depth), sorted by time and case code. Public so tests can EXPLAIN the real SQL.
/// </summary>
public sealed class AgendaQueryBuilder(CasesDbContext db, CaseAccessService access, ICurrentUser user)
{
    /// <summary>Roles that hold case assignments (their agenda = the cases they are assigned to).</summary>
    public static readonly string[] AssignableRoles = ["STATE_LAWYER", "LEGAL_ASSISTANT", "INSTITUTION_JURIST"];
    public const string InstitutionUserRole = "INSTITUTION_USER";

    public IQueryable<AgendaRow> Rows(AgendaFilter f)
    {
        var from = f.FromUtc;
        var to = f.ToUtc;
        var hearings = db.CaseHearings.AsNoTracking().Where(h => h.ScheduledAt >= from && h.ScheduledAt < to);
        if (!f.IncludeCancelled)
        {
            hearings = hearings.Where(h => h.Status != CaseHearing.Cancelled);
        }

        if (f.Scope == AgendaScope.Own)
        {
            hearings = OwnCases(hearings);
        }

        // closed cases too: their past hearings still belong in the range
        var cases = db.Cases.AsNoTracking();
        if (f.OfficeId is { } office)
        {
            cases = cases.Where(c => c.OfficeId == office);
        }

        if (f.LawyerUserId is { } lawyer)
        {
            // any current role of that lawyer in the case
            cases = cases.Where(c => db.CaseAssignments.Any(a => a.CaseId == c.Id && a.UserId == lawyer && a.EndedAt == null));
        }

        cases = access.ApplyScope(cases);

        return from h in hearings
               join c in cases on h.CaseId equals c.Id
               orderby h.ScheduledAt, c.CaseCode, h.Id
               select new AgendaRow(h.Id, h.PublicId, c.Id, c.PublicId, c.CaseCode, h.ScheduledAt, h.HearingTypeId,
                   h.OtherTypeText, h.Status, c.CourtId);
    }

    /// <summary>
    /// The hearings of the user's own cases: a CURRENT assignment (ix_ca_user_current) and, for an institution user,
    /// an active link of their institution (ix_ci_institution); then case_hearings by (case_id, scheduled_at).
    /// </summary>
    private IQueryable<CaseHearing> OwnCases(IQueryable<CaseHearing> hearings)
    {
        var userId = (uint)user.UserId;
        IQueryable<ulong>? caseIds = null;
        if (user.Roles.Any(r => AssignableRoles.Contains(r)))
        {
            caseIds = db.CaseAssignments.Where(a => a.UserId == userId && a.EndedAt == null).Select(a => a.CaseId);
        }

        if (user.Roles.Contains(InstitutionUserRole) && user.InstitutionId is { } institution)
        {
            var institutionId = (uint)institution;
            var linked = db.CaseInstitutions.Where(i => i.InstitutionId == institutionId && i.EndedAt == null).Select(i => i.CaseId);
            caseIds = caseIds is null ? linked : caseIds.Union(linked);
        }

        return caseIds is null ? hearings.Where(_ => false) : hearings.Where(h => caseIds.Contains(h.CaseId));
    }
}
