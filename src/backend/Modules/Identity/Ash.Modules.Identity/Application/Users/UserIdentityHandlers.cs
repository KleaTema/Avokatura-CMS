using Ash.Modules.Audit.Contracts;
using Ash.Modules.Identity.Infrastructure;
using Ash.SharedKernel.Identity;
using Ash.SharedKernel.Results;
using Ash.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;

namespace Ash.Modules.Identity.Application.Users;

/// <summary>One sign-in identity of a user, as the administrator sees it (task 051). Blocked: "Shkëputur" (since).</summary>
public sealed record UserIdentityView(
    Guid PublicId, string Provider, string? EmailAtLink, DateTime LinkedAt, DateTime? LastLoginAt, DateTime? BlockedAt);

/// <summary>"Lejo sërish": why the administrator lifts the block (required, audited).</summary>
public sealed record AllowIdentityRequest(string? Reason);

/// <summary>
/// Task 051: the administrator lists a user's sign-in identities and
/// <list type="bullet">
/// <item>"Shkëput" — BLOCKS that one account (decision 16.11.2026): from the next request it gets 401 and it is never
/// re-linked by e-mail; the user's other accounts keep working. Audited IDENTITY_UNLINKED. To shut the person out
/// completely, deactivate the user.</item>
/// <item>"Lejo sërish" — lifts the block with a reason (the tombstone is removed; the next login links again by
/// e-mail). Audited IDENTITY_ALLOWED.</item>
/// </list>
/// The subject is never shown.
/// </summary>
public sealed class UserIdentityHandlers(IdentityDbContext db, IAuditLog audit, ICurrentUser admin, IClock clock)
{
    private static readonly Error UserNotFound = Error.NotFound("user.not_found", "Përdoruesi nuk u gjet.");
    private static readonly Error IdentityNotFound = Error.NotFound("identity.not_found", "Identiteti nuk u gjet.");

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
            .Select(i => new UserIdentityView(i.PublicId, i.ProviderName, i.EmailAtLink, i.LinkedAt, i.LastLoginAt, i.BlockedAt))
            .ToListAsync(ct);
    }

    public async Task<Result<bool>> UnlinkAsync(Guid userPublicId, Guid identityPublicId, CancellationToken ct)
    {
        var (user, identity, error) = await FindAsync(userPublicId, identityPublicId, ct);
        if (error is not null)
        {
            return error;
        }

        if (identity!.BlockedAt is null)
        {
            identity.BlockedAt = clock.UtcNow;
            identity.BlockedBy = (uint)admin.UserId;
            await db.SaveChangesAsync(ct);
            audit.Record(AuditActions.IdentityUnlinked, "USER", user!.Id,
                details: new { provider = identity.ProviderName, emailAtLink = identity.EmailAtLink });
        }

        return true;
    }

    public async Task<Result<bool>> AllowAsync(Guid userPublicId, Guid identityPublicId, AllowIdentityRequest req, CancellationToken ct)
    {
        var reason = req.Reason?.Trim() ?? string.Empty;
        if (reason.Length is < 5 or > 500)
        {
            return Error.ValidationFields("Arsyeja kërkohet.",
                new Dictionary<string, string[]> { ["reason"] = ["Shkruani arsyen (5 deri në 500 shkronja)."] });
        }

        var (user, identity, error) = await FindAsync(userPublicId, identityPublicId, ct);
        if (error is not null)
        {
            return error;
        }

        if (identity!.BlockedAt is null)
        {
            return Error.Conflict("identity.not_blocked", "Kjo llogari nuk është e shkëputur.");
        }

        // the tombstone goes: the next login at this provider links again by e-mail (an active user, case insensitive)
        db.UserIdentities.Remove(identity);
        await db.SaveChangesAsync(ct);
        audit.Record(AuditActions.IdentityAllowed, "USER", user!.Id,
            details: new { provider = identity.ProviderName, emailAtLink = identity.EmailAtLink, reason });
        return true;
    }

    private async Task<(UserRecord? User, UserIdentityRecord? Identity, Error? Error)> FindAsync(
        Guid userPublicId, Guid identityPublicId, CancellationToken ct)
    {
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.PublicId == userPublicId, ct);
        if (user is null)
        {
            return (null, null, UserNotFound);
        }

        var identity = await db.UserIdentities.SingleOrDefaultAsync(i => i.PublicId == identityPublicId && i.UserId == user.Id, ct);
        return identity is null ? (user, null, IdentityNotFound) : (user, identity, null);
    }
}
