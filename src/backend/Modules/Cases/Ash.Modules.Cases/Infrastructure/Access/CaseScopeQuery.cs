using Ash.SharedKernel.Identity;

namespace Ash.Modules.Cases.Infrastructure.Access;

/// <summary>
/// Filter for case lists. Rule: view_all OR (view_office AND same office) OR a row in v_case_access
/// OR (registered_by = me AND REGISTERED). Everything translates to SQL.
/// </summary>
public static class CaseScopeQuery
{
    public const string ViewAllPermission = "case.view_all";
    public const string ViewOfficePermission = "case.view_office";
    public const string RegisteredStatus = "REGISTERED";

    public static IQueryable<Case> Apply(IQueryable<Case> cases, CasesDbContext db, ICurrentUser user)
    {
        var userId = (uint)user.UserId;
        var viewAll = user.HasPermission(ViewAllPermission);
        var viewOffice = user.HasPermission(ViewOfficePermission) && user.OfficeId is not null;
        var officeId = (ushort)(user.OfficeId ?? 0);

        return cases.Where(c =>
            viewAll
            || (viewOffice && c.OfficeId == officeId)
            || db.CaseAccess.Any(a => a.UserId == userId && a.CaseId == c.Id)
            || (c.RegisteredBy == userId && c.LifecycleStatus == RegisteredStatus));
    }
}
