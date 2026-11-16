using Ash.Modules.Audit.Contracts;
using Ash.Modules.Identity.Infrastructure;
using Ash.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Ash.Modules.Identity.Application.Users;

/// <summary>One sign-in identity of a user, as the administrator sees it (task 051).</summary>
public sealed record UserIdentityView(Guid PublicId, string Provider, string? EmailAtLink, DateTime LinkedAt, DateTime? LastLoginAt);

/// <summary>
/// Task 051: the administrator lists a user's linked identities (one per provider at which they signed in) and
/// unlinks one ("Shkëput", audited IDENTITY_UNLINKED). Unlinked, the next login at that provider links again by
/// e-mail — or, if the e-mail no longer matches an active user, is refused. The subject is never shown.
/// </summary>
public sealed class UserIdentityHandlers(IdentityDbContext db, IAuditLog audit, IMemoryCache cache)
{
    private static readonly Error UserNotFound = Error.NotFound("user.not_found", "Përdoruesi nuk u gjet.");

    public async Task<Result<IReadOnlyList<UserIdentityView>>> ListAsync(Guid userPublicId, CancellationToken ct)
    {
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.PublicId == userPublicId, ct);
        if (user is null)
        {
            return UserNotFound;
        }

        return await db.UserIdentities.AsNoTracking()
            .Where(i => i.UserId == user.Id)
            .OrderBy(i => i.ProviderName).ThenBy(i => i.LinkedAt)
            .Select(i => new UserIdentityView(i.PublicId, i.ProviderName, i.EmailAtLink, i.LinkedAt, i.LastLoginAt))
            .ToListAsync(ct);
    }

    public async Task<Result<bool>> UnlinkAsync(Guid userPublicId, Guid identityPublicId, CancellationToken ct)
    {
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.PublicId == userPublicId, ct);
        if (user is null)
        {
            return UserNotFound;
        }

        var identity = await db.UserIdentities.SingleOrDefaultAsync(i => i.PublicId == identityPublicId && i.UserId == user.Id, ct);
        if (identity is null)
        {
            return Error.NotFound("identity.not_found", "Identiteti nuk u gjet.");
        }

        db.UserIdentities.Remove(identity);
        await db.SaveChangesAsync(ct);
        // the user's resolved access is cached: forget it so the change counts from the next request
        cache.Remove($"identity:access:{user.Id}");
        audit.Record(AuditActions.IdentityUnlinked, "USER", user.Id,
            details: new { provider = identity.ProviderName, emailAtLink = identity.EmailAtLink });
        return true;
    }
}
