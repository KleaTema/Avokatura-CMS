using System.Text.Json;
using System.Text.Json.Serialization;
using Ash.Modules.Audit.Contracts;
using Ash.Modules.Identity.Application.GetMe;
using Ash.Modules.Identity.Infrastructure;
using Ash.Modules.Notifications.Contracts;
using Ash.SharedKernel.Identity;
using Ash.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Ash.Modules.Identity.Application.Profile;

public sealed record RoleRef(string Code, string Name);

/// <summary>
/// The own profile (task 048). FirstName, LastName and Email are read-only here: in production they come from the
/// government identity provider (and the admin users page), so the profile never changes them.
/// </summary>
public sealed record ProfileResponse(
    Guid PublicId,
    string FirstName,
    string LastName,
    string Email,
    NamedRef? Office,
    NamedRef? Institution,
    IReadOnlyList<RoleRef> Roles,
    string? HeadOf,
    DateTime? LastLoginAt,
    string? Phone,
    IReadOnlyList<NotificationPreference> Notifications);

/// <summary>
/// PUT /api/identity/profile. Only the phone is editable — TODO(client-q3): which fields per role are editable.
/// Any other property in the body is refused (400), not silently ignored, so a client never thinks it saved a name.
/// </summary>
public sealed record UpdateProfileRequest(string? Phone)
{
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Other { get; init; }
}

public sealed class ProfileHandlers(
    IdentityDbContext db, ICurrentUser user, INotificationPreferences preferences, IAuditLog audit)
{
    public async Task<Result<ProfileResponse>> GetAsync(CancellationToken ct)
    {
        var id = (uint)user.UserId;
        var row = await db.Users.AsNoTracking()
            .Where(u => u.Id == id)
            .Select(u => new
            {
                u.PublicId,
                u.FirstName,
                u.LastName,
                u.Email,
                u.Phone,
                u.LastLoginAt,
                Office = db.Offices.Where(o => o.Id == u.OfficeId).Select(o => new NamedRef(o.PublicId, o.Name)).FirstOrDefault(),
                Institution = db.Institutions.Where(i => i.Id == u.InstitutionId).Select(i => new NamedRef(i.PublicId, i.Name)).FirstOrDefault(),
                HeadOf = db.Offices.Where(o => o.HeadUserId == u.Id).Select(o => o.Name).FirstOrDefault(),
            })
            .SingleOrDefaultAsync(ct);
        if (row is null)
        {
            return Error.NotFound("user.not_found", "Përdoruesi nuk u gjet.");
        }

        var roles = await (from ur in db.UserRoles.AsNoTracking()
                           join r in db.Roles.AsNoTracking() on ur.RoleId equals r.Id
                           where ur.UserId == id
                           orderby r.Id
                           select new RoleRef(r.Code, r.Name)).ToListAsync(ct);
        var notifications = await preferences.GetAsync(id, ct);
        return new ProfileResponse(row.PublicId, row.FirstName, row.LastName, row.Email, row.Office, row.Institution,
            roles, row.HeadOf, row.LastLoginAt, row.Phone, notifications);
    }

    public async Task<Result<ProfileResponse>> UpdateAsync(UpdateProfileRequest req, CancellationToken ct)
    {
        if (req.Other is { Count: > 0 })
        {
            return Error.Validation("profile.field_read_only",
                $"Vetëm telefoni mund të ndryshohet në profil ({string.Join(", ", req.Other.Keys)} jo).");
        }

        string? phone = null;
        if (!string.IsNullOrWhiteSpace(req.Phone))
        {
            phone = PhoneNumber.Normalize(req.Phone);
            if (phone is null)
            {
                return Error.Validation("profile.phone_invalid",
                    "Numri i telefonit nuk është i vlefshëm. Shembuj: 069 123 4567, +355 69 123 4567, +355 4 222 3333.");
            }
        }

        var id = (uint)user.UserId;
        var row = await db.Users.SingleAsync(u => u.Id == id, ct);
        var old = row.Phone;
        if (old != phone)
        {
            row.Phone = phone;
            await db.SaveChangesAsync(ct);
            // the phone masked: the audit log is read by admins and kept for years
            audit.Record(AuditActions.ProfileUpdated, "USER", id, details: new
            {
                old = new { Phone = PhoneNumber.Mask(old) },
                @new = new { Phone = PhoneNumber.Mask(phone) },
            });
        }

        return await GetAsync(ct);
    }

    public async Task<Result<IReadOnlyList<NotificationPreference>>> SetNotificationsAsync(
        Dictionary<string, bool> choices, CancellationToken ct)
    {
        var refused = await preferences.SetAsync(user.UserId, choices, ct);
        if (refused.Count > 0)
        {
            return Error.Validation("profile.notification_mandatory",
                $"Këto njoftime janë të detyrueshme ose të panjohura dhe nuk mund të çaktivizohen: {string.Join(", ", refused)}.");
        }

        return Result.Success(await preferences.GetAsync(user.UserId, ct));
    }
}
