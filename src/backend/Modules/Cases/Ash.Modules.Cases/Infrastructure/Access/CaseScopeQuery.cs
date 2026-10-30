using Ash.SharedKernel.Identity;

namespace Ash.Modules.Cases.Infrastructure.Access;

/// <summary>
/// Filter for case lists. Rule: view_all OR (view_office AND same office) OR a row in v_case_access
/// OR (registered_by = me AND REGISTERED). Everything translates to SQL.
/// Composed per user so only the branches that apply reach SQL (no constant "@flag OR …" terms): a view_all user
/// gets no filter at all. Case lists apply it ON TOP of their own index-friendly predicate (defence in depth).
/// </summary>
public static class CaseScopeQuery
{
    public const string ViewAllPermission = "case.view_all";
    public const string ViewOfficePermission = "case.view_office";
    public const string RegisteredStatus = "REGISTERED";

    public static IQueryable<Case> Apply(IQueryable<Case> cases, CasesDbContext db, ICurrentUser user)
    {
        if (user.HasPermission(ViewAllPermission))
        {
            return cases;
        }

        var userId = (uint)user.UserId;
        if (user.HasPermission(ViewOfficePermission) && user.OfficeId is { } office)
        {
            var officeId = (ushort)office;
            return cases.Where(c =>
                c.OfficeId == officeId
                || db.CaseAccess.Any(a => a.UserId == userId && a.CaseId == c.Id)
                || (c.RegisteredBy == userId && c.LifecycleStatus == RegisteredStatus));
        }

        return cases.Where(c =>
            db.CaseAccess.Any(a => a.UserId == userId && a.CaseId == c.Id)
            || (c.RegisteredBy == userId && c.LifecycleStatus == RegisteredStatus));
    }
}
