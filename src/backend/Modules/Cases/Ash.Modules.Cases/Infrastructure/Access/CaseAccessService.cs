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

        var permissions = rows.Where(r => r.Permission is not null).Select(r => r.Permission!).ToHashSet();
        return new CaseAccess(canView, permissions);
    }

    private sealed record CaseAccess(bool CanView, HashSet<string> Permissions)
    {
        public static CaseAccess None { get; } = new(false, []);
    }
}
