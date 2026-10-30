using Ash.Modules.Cases.Contracts;
using Ash.SharedKernel.Identity;
using Ash.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Ash.Modules.Cases.Infrastructure.Access;

/// <summary>Scoped: one query per (user, case) per request, then answered from memory.</summary>
public sealed class CaseAccessService(CasesDbContext db, ICurrentUser user) : ICaseAccessService
{
    private readonly Dictionary<(long UserId, long CaseId), Task<CaseAccess>> _cache = [];

    public IQueryable<Case> ApplyScope(IQueryable<Case> cases) => CaseScopeQuery.Apply(cases, db, user);

    public async Task<bool> CanViewAsync(long caseId, CancellationToken cancellationToken = default) =>
        (await GetAsync(caseId, cancellationToken)).CanView;

    public async Task<bool> HasAsync(long caseId, string permissionCode, CancellationToken cancellationToken = default)
    {
        var access = await GetAsync(caseId, cancellationToken);
        return access.CanView && access.Permissions.Contains(permissionCode);
    }

    public async Task RequireAsync(long caseId, string permissionCode, CancellationToken cancellationToken = default)
    {
        var access = await GetAsync(caseId, cancellationToken);
        if (!access.CanView)
        {
            throw new CaseAccessException(Error.NotFound("case.not_found", "Çështja nuk u gjet."));
        }

        if (!access.Permissions.Contains(permissionCode))
        {
            throw new CaseAccessException(Error.Forbidden("case.forbidden", "Nuk keni leje për këtë veprim."));
        }
    }

    public async Task<IReadOnlySet<string>> GetEffectivePermissionsAsync(long caseId, CancellationToken cancellationToken = default) =>
        (await GetAsync(caseId, cancellationToken)).Permissions;

    public const string LinkPreviousPermission = "case.link_previous";
    public const string ViewPermission = "case.view";
    public const string EditRegistrationPermission = "case.edit_registration";

    /// <summary>The registering office's Protokoll corrects the registration while the case waits for a lawyer.</summary>
    private static readonly string[] RegistrationDeskActions = [EditRegistrationPermission, "party.add", "party.edit", "party.delete"];

    /// <summary>
    /// What still applies to a CLOSED case, if the user holds it (the next level continues the work):
    /// view, and the case chat (lawyers discuss the appeal after closing).
    /// Tasks 036/037 add the decision actions here (record the reasoned decision and its document after closing) —
    /// nowhere else. Every other write action is off for a CLOSED case.
    /// </summary>
    public static readonly IReadOnlySet<string> ActionsAllowedWhenClosed = new HashSet<string>
    {
        ViewPermission,
        "chat.case",
    };

    /// <summary>The Kryetar of the case's office (system permissions held for their own office only).</summary>
    private static readonly string[] OfficeHeadActions = ["case.assign_lawyer", "case.transfer", "case.set_priority_any"];

    public RegistrationLookupScope GetRegistrationLookupScope() =>
        // All offices: confirmed by the client (Q1a) — a next-level summons may arrive at another office.
        user.HasPermission(LinkPreviousPermission) ? RegistrationLookupScope.AllOffices : RegistrationLookupScope.None;

    private Task<CaseAccess> GetAsync(long caseId, CancellationToken cancellationToken)
    {
        var key = (user.UserId, caseId);
        if (!_cache.TryGetValue(key, out var task))
        {
            task = LoadAsync(caseId, cancellationToken);
            _cache[key] = task;
        }

        return task;
    }

    private async Task<CaseAccess> LoadAsync(long caseId, CancellationToken cancellationToken)
    {
        var id = (ulong)caseId;
        var userId = (uint)user.UserId;

        var rows = await (
            from c in db.Cases.Where(c => c.Id == id)
            join p in db.CasePermissions.Where(p => p.UserId == userId) on c.Id equals p.CaseId into ps
            from p in ps.DefaultIfEmpty()
            select new
            {
                c.OfficeId,
                c.LifecycleStatus,
                c.RegisteredBy,
                HasProfile = db.CaseAccess.Any(a => a.UserId == userId && a.CaseId == c.Id),
                Permission = p == null ? null : p.PermissionCode
            }).ToListAsync(cancellationToken);

        if (rows.Count == 0)
        {
            return CaseAccess.None;
        }

        // Same rule as CaseScopeQuery.Apply, evaluated on the one loaded row.
        var first = rows[0];
        var canView = first.HasProfile
            || user.HasPermission(CaseScopeQuery.ViewAllPermission)
            || (user.HasPermission(CaseScopeQuery.ViewOfficePermission) && user.OfficeId == first.OfficeId)
            || (first.RegisteredBy == userId && first.LifecycleStatus == CaseScopeQuery.RegisteredStatus);

        if (!canView)
        {
            return CaseAccess.None;
        }

        return new CaseAccess(true, Effective(rows.Select(r => r.Permission), first.OfficeId, first.LifecycleStatus));
    }

    /// <summary>
    /// Case permissions ∪ system permissions that apply to this case in its current state. A CLOSED case keeps only
    /// what is in <see cref="ActionsAllowedWhenClosed"/>.
    /// </summary>
    private HashSet<string> Effective(IEnumerable<string?> casePermissions, ushort officeId, string status)
    {
        var effective = casePermissions.Where(p => p is not null).Select(p => p!).ToHashSet();
        effective.Add(ViewPermission); // viewing through the office or view_all is still viewing
        if (status == Case.Closed)
        {
            effective.IntersectWith(ActionsAllowedWhenClosed);
            return effective;
        }

        var ownOffice = user.OfficeId == officeId;
        if (ownOffice && status == Case.Registered && user.HasPermission(EditRegistrationPermission))
        {
            effective.UnionWith(RegistrationDeskActions);
        }

        if (ownOffice)
        {
            effective.UnionWith(OfficeHeadActions.Where(user.HasPermission));
        }

        return effective;
    }

    private sealed record CaseAccess(bool CanView, IReadOnlySet<string> Permissions)
    {
        public static CaseAccess None { get; } = new(false, new HashSet<string>());
    }
}
