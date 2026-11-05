using System.Linq.Expressions;
using Ash.SharedKernel.Identity;

namespace Ash.Modules.Cases.Infrastructure.Access;

/// <summary>
/// Filter for case lists. Rule: view_all OR (view_office AND same office) OR the user's access row (v_case_access)
/// OR (registered_by = me AND REGISTERED). Everything translates to SQL.
/// Composed per user so only the branches that apply reach SQL (no constant "@flag OR …" terms): a view_all user
/// gets no filter at all. Case lists apply it ON TOP of their own index-friendly predicate (defence in depth).
/// <para>
/// The access row is written out per user from the THREE branches of v_case_access (script 008) instead of an EXISTS
/// on the view (task 046): the view is a UNION ALL whose institution branch joins users, user_roles and roles, and
/// MySQL evaluated all of it for every candidate case — 366 ms just to count a lawyer's 12,000 cases. Here a lawyer
/// gets only the assignment branch (ix_ca_case_current), an institution user only the link branch (ix_ci_case), a
/// jurist only the jurist branch. KEEP IT IN STEP WITH v_case_access (CaseScopeQueryTests compares them).
/// </para>
/// </summary>
public static class CaseScopeQuery
{
    public const string ViewAllPermission = "case.view_all";
    public const string ViewOfficePermission = "case.view_office";
    public const string RegisteredStatus = "REGISTERED";
    public const string InstitutionUserRole = "INSTITUTION_USER";
    public const string InstitutionJuristRole = "INSTITUTION_JURIST";

    /// <summary>The lawyer roles of v_case_access's first branch.</summary>
    private static readonly string[] LawyerRoles =
        [CaseAssignmentRow.PrimaryLawyer, CaseAssignmentRow.SecondaryLawyer, CaseAssignmentRow.LegalAssistant];

    public static IQueryable<Case> Apply(IQueryable<Case> cases, CasesDbContext db, ICurrentUser user)
    {
        if (user.HasPermission(ViewAllPermission))
        {
            return cases;
        }

        var userId = (uint)user.UserId;
        // REGISTERED cases the user registered (the Protokoll, until the Kryetar assigns them)
        Expression<Func<Case, bool>> scope = c => c.RegisteredBy == userId && c.LifecycleStatus == RegisteredStatus;

        // branch 1: a current lawyer / assistant assignment (anyone may hold one)
        scope = Or(scope, c => db.CaseAssignments.Any(a =>
            a.CaseId == c.Id && a.UserId == userId && a.EndedAt == null && LawyerRoles.Contains(a.CaseRole)));

        if (user.InstitutionId is { } institution)
        {
            var institutionId = (uint)institution;
            if (user.Roles.Contains(InstitutionUserRole))
            {
                // branch 2: an active link of the user's institution (the user is active: resolved as such)
                scope = Or(scope, c => db.CaseInstitutions.Any(i =>
                    i.CaseId == c.Id && i.InstitutionId == institutionId && i.EndedAt == null));
            }

            // branch 3: a current jurist assignment on a case linked to the jurist's institution
            scope = Or(scope, c => db.CaseAssignments.Any(a =>
                a.CaseId == c.Id && a.UserId == userId && a.EndedAt == null && a.CaseRole == CaseAssignmentRow.InstitutionJurist
                && db.CaseInstitutions.Any(i => i.CaseId == c.Id && i.InstitutionId == institutionId && i.EndedAt == null)));
        }

        if (user.HasPermission(ViewOfficePermission) && user.OfficeId is { } office)
        {
            var officeId = (ushort)office;
            scope = Or(c => c.OfficeId == officeId, scope);
        }

        return cases.Where(scope);
    }

    /// <summary>left OR right over one parameter (EF translates the combined tree like a written lambda).</summary>
    private static Expression<Func<Case, bool>> Or(Expression<Func<Case, bool>> left, Expression<Func<Case, bool>> right)
    {
        var parameter = left.Parameters[0];
        var body = new Rebind(right.Parameters[0], parameter).Visit(right.Body)!;
        return Expression.Lambda<Func<Case, bool>>(Expression.OrElse(left.Body, body), parameter);
    }

    private sealed class Rebind(ParameterExpression from, ParameterExpression to) : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node) => node == from ? to : node;
    }
}
