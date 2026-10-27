using Ash.Modules.Audit.Contracts;
using Ash.Modules.Identity.Infrastructure;
using Ash.SharedKernel.Results;
using Ash.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Ash.Modules.Identity.Application.ResolveUser;

public sealed class ResolveUserHandler(IdentityDbContext db, IMemoryCache cache, IClock clock, IAuditLog audit)
{
    private static readonly TimeSpan RolesCacheDuration = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan LastLoginInterval = TimeSpan.FromHours(1);

    public async Task<Result<ResolveUserResponse>> Handle(ResolveUserRequest req, CancellationToken ct)
    {
        var query = db.Users.AsNoTracking();
        if (!string.IsNullOrEmpty(req.Subject))
        {
            var subject = req.Subject;
            query = query.Where(u => u.ExternalSubject == subject);
        }
        else if (!string.IsNullOrEmpty(req.Email))
        {
            var email = req.Email;
            query = query.Where(u => u.Email == email);
        }
        else
        {
            return Error.NotFound("user.unknown", "Përdoruesi nuk u identifikua.");
        }

        var user = await query.SingleOrDefaultAsync(ct);
        if (user is null || !user.IsActive)
        {
            return Error.NotFound("user.unknown", "Përdoruesi nuk është i autorizuar.");
        }

        var now = clock.UtcNow;
        var isLogin = user.LastLoginAt is null || now - user.LastLoginAt.Value >= LastLoginInterval;
        if (isLogin)
        {
            await db.Users.Where(u => u.Id == user.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(u => u.LastLoginAt, now), ct);
        }

        var access = await cache.GetOrCreateAsync($"identity:access:{user.Id}", async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = RolesCacheDuration;
            var roles = await db.Database
                .SqlQuery<string>($"""
                    SELECT r.code AS Value FROM user_roles ur
                    JOIN roles r ON r.id = ur.role_id
                    WHERE ur.user_id = {user.Id}
                    """)
                .ToListAsync(ct);
            var permissions = await db.Database
                .SqlQuery<string>($"""
                    SELECT DISTINCT p.code AS Value FROM user_roles ur
                    JOIN role_permissions rp ON rp.role_id = ur.role_id
                    JOIN permissions p ON p.id = rp.permission_id
                    WHERE ur.user_id = {user.Id} AND p.scope = 'SYSTEM'
                    """)
                .ToListAsync(ct);
            return (Roles: (IReadOnlyCollection<string>)roles, Permissions: (IReadOnlySet<string>)permissions.ToHashSet());
        });

        var displayName = $"{user.FirstName} {user.LastName}".Trim();
        if (isLogin)
        {
            audit.Record(AuditActions.Login, "USER", user.Id,
                actor: new AuditActor(user.Id, displayName, string.Join(',', access.Roles)));
        }

        return new ResolveUserResponse(
            user.Id, user.PublicId, displayName, user.OfficeId, user.InstitutionId, access.Roles, access.Permissions);
    }
}
