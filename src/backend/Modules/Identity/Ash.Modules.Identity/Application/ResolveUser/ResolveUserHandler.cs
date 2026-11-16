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

    /// <summary>
    /// Production (task 051): the user of (provider, subject); the first login at a provider links an ACTIVE user by
    /// e-mail (case insensitive) and records IDENTITY_LINKED — users are never created here. Later logins use the
    /// identity only, so an e-mail change at the provider keeps the link. Development: the user by e-mail.
    /// </summary>
    public async Task<Result<ResolveUserResponse>> Handle(ResolveUserRequest req, CancellationToken ct)
    {
        UserRecord? user;
        var email = req.Email?.Trim().ToLowerInvariant();
        var now = clock.UtcNow;
        if (!string.IsNullOrEmpty(req.Provider))
        {
            if (string.IsNullOrEmpty(req.Subject))
            {
                return Unknown();
            }

            var (provider, subject) = (req.Provider, req.Subject);
            var identity = await db.UserIdentities.AsNoTracking()
                .SingleOrDefaultAsync(i => i.ProviderName == provider && i.Subject == subject, ct);
            if (identity is { BlockedAt: not null })
            {
                // "Shkëput" (task 051): this account is blocked — 401, and never re-linked by e-mail
                return Unknown();
            }

            if (identity is not null)
            {
                user = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == identity.UserId, ct);
                if (identity.LastLoginAt is null || now - identity.LastLoginAt.Value >= LastLoginInterval)
                {
                    await db.UserIdentities.Where(i => i.Id == identity.Id)
                        .ExecuteUpdateAsync(s => s.SetProperty(i => i.LastLoginAt, now), ct);
                }
            }
            else
            {
                user = string.IsNullOrEmpty(email)
                    ? null
                    : await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Email == email && u.IsActive, ct);
                // at most one identity per provider: another account of the same provider with the same e-mail is
                // refused until the administrator unlinks the old one ("Shkëput") — no silent takeover by e-mail
                if (user is not null && await db.UserIdentities.AnyAsync(i => i.UserId == user.Id && i.ProviderName == provider, ct))
                {
                    user = null;
                }

                if (user is not null && !await LinkAsync(user, provider, subject, email!, now, ct))
                {
                    // a parallel first login inserted the same identity: read it back
                    var linked = await db.UserIdentities.AsNoTracking()
                        .SingleOrDefaultAsync(i => i.ProviderName == provider && i.Subject == subject, ct);
                    user = linked?.UserId == user.Id ? user : null;
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
            return Unknown();
        }

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

    private static Error Unknown() => Error.NotFound("user.unknown", "Përdoruesi nuk është i autorizuar.");

    /// <summary>The first login at a provider: one identity row (unique per provider and subject), audited.</summary>
    private async Task<bool> LinkAsync(UserRecord user, string provider, string subject, string email, DateTime now, CancellationToken ct)
    {
        var identity = new UserIdentityRecord
        {
            PublicId = Guid.CreateVersion7(),
            UserId = user.Id,
            ProviderName = provider,
            Subject = subject,
            EmailAtLink = email,
            LinkedAt = now,
            LastLoginAt = now,
        };
        db.UserIdentities.Add(identity);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            db.Entry(identity).State = EntityState.Detached;
            return false;
        }

        audit.Record(AuditActions.IdentityLinked, "USER", user.Id,
            actor: new AuditActor(user.Id, $"{user.FirstName} {user.LastName}".Trim(), string.Empty),
            details: new { provider, email });
        return true;
    }
}
