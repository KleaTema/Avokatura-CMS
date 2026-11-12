using System.Linq.Expressions;
using Ash.Modules.Cases.Contracts;
using Ash.SharedKernel.Identity;

namespace Ash.Modules.Cases.Infrastructure.Access;

/// <summary>
/// The ONE place deciding which access branches apply to a user (<see cref="For"/> → <see cref="CaseScopeSpec"/>), and
/// its EF rendering for case lists. Rule: view_all OR (view_office AND same office) OR the user's access row
/// (v_case_access) OR (registered_by = me AND REGISTERED). The reports (task 042) render the same spec as SQL.
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
    public const string RegisteredStatus = CaseScopeSpec.Registered;
    public const string InstitutionUserRole = "INSTITUTION_USER";
    public const string InstitutionJuristRole = CaseScopeSpec.InstitutionJurist;

    /// <summary>Which branches apply to the user — decided by permissions and the kind of user.</summary>
    public static CaseScopeSpec For(ICurrentUser user)
    {
        long? linked = null, jurist = null;
        if (user.InstitutionId is { } institution)
        {
            if (user.Roles.Contains(InstitutionUserRole))
            {
                linked = institution;
            }

            jurist = institution;
        }

        return new CaseScopeSpec(
            user.HasPermission(ViewAllPermission),
            user.UserId,
            user.HasPermission(ViewOfficePermission) ? user.OfficeId : null,
            linked,
            jurist);
    }

    public static IQueryable<Case> Apply(IQueryable<Case> cases, CasesDbContext db, ICurrentUser user) =>
        Apply(cases, db, For(user));

    public static IQueryable<Case> Apply(IQueryable<Case> cases, CasesDbContext db, CaseScopeSpec spec)
    {
        if (spec.All)
        {
            return cases;
        }

        var userId = (uint)spec.UserId;
        var lawyerRoles = CaseScopeSpec.LawyerRoles.ToArray();
        // REGISTERED cases the user registered (the Protokoll, until the Kryetar assigns them)
        Expression<Func<Case, bool>> scope = c => c.RegisteredBy == userId && c.LifecycleStatus == RegisteredStatus;

        // branch 1: a current lawyer / assistant assignment (anyone may hold one)
        scope = Or(scope, c => db.CaseAssignments.Any(a =>
            a.CaseId == c.Id && a.UserId == userId && a.EndedAt == null && lawyerRoles.Contains(a.CaseRole)));

        if (spec.LinkedInstitutionId is { } linked)
        {
            // branch 2: an active link of the user's institution (the user is active: resolved as such)
            var institutionId = (uint)linked;
            scope = Or(scope, c => db.CaseInstitutions.Any(i =>
                i.CaseId == c.Id && i.InstitutionId == institutionId && i.EndedAt == null));
        }

        if (spec.JuristInstitutionId is { } juristInstitution)
        {
            // branch 3: a current jurist assignment on a case linked to the jurist's institution
            var institutionId = (uint)juristInstitution;
            scope = Or(scope, c => db.CaseAssignments.Any(a =>
                a.CaseId == c.Id && a.UserId == userId && a.EndedAt == null && a.CaseRole == CaseAssignmentRow.InstitutionJurist
                && db.CaseInstitutions.Any(i => i.CaseId == c.Id && i.InstitutionId == institutionId && i.EndedAt == null)));
        }

        if (spec.OfficeId is { } office)
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

/// <summary><see cref="ICaseScope"/> for other modules (the reports).</summary>
internal sealed class CaseScope(ICurrentUser user) : ICaseScope
{
    public CaseScopeSpec Current() => CaseScopeQuery.For(user);
}
