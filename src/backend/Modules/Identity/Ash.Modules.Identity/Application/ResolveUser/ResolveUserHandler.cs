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
        UserRecord? user;
        var email = req.Email?.Trim().ToLowerInvariant();
        if (!string.IsNullOrEmpty(req.Subject))
        {
            var subject = req.Subject;
            user = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.ExternalSubject == subject, ct);
            if (user is null && !string.IsNullOrEmpty(email))
            {
                // First login: the admin created the user without a subject; link it by email.
                var candidate = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Email == email, ct);
                if (candidate is { IsActive: true, ExternalSubject: null })
                {
                    var linked = await db.Users
                        .Where(u => u.Id == candidate.Id && u.ExternalSubject == null)
                        .ExecuteUpdateAsync(s => s.SetProperty(u => u.ExternalSubject, subject), ct);
                    if (linked == 1)
                    {
                        candidate.ExternalSubject = subject;
                        user = candidate;
                    }
                }
            }
        }
        else if (!string.IsNullOrEmpty(email))
        {
            user = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Email == email, ct);
        }
        else
        {
            return Error.NotFound("user.unknown", "Përdoruesi nuk u identifikua.");
        }

        if (user is null || !user.IsActive)
        {
            return Error.NotFound("user.unknown", "Përdoruesi nuk është i autorizuar.");
        }

        var now = clock.UtcNow;
        // TODO(prod-auth): in OIDC mode write LOGIN only when the token's auth_time claim is newer than the stored last login, instead of the 1-hour interval.
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
