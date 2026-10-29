using Ash.Modules.Audit.Contracts;
using Ash.Modules.Cases.Contracts;
using Ash.Modules.Identity.Application.GetMe;
using Ash.Modules.Identity.Infrastructure;
using Ash.SharedKernel.Identity;
using Ash.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Ash.Modules.Identity.Application.Users;

public sealed record UserResponse(
    Guid PublicId,
    string Email,
    string FirstName,
    string LastName,
    NamedRef? Office,
    NamedRef? Institution,
    IReadOnlyList<string> Roles,
    bool IsActive,
    DateTime? LastLoginAt,
    int CurrentAssignments);

/// <summary>Shared rules and helpers of the user administration use cases.</summary>
public sealed class UserAdminSupport(
    IdentityDbContext db, IMemoryCache cache, IAuditLog audit, ICaseAssignmentCounts assignments, ICurrentUser currentUser)
{
    public const string AdminRole = "ADMIN";

    public static readonly string[] InstitutionRoles = ["INSTITUTION_USER", "INSTITUTION_JURIST"];
    public static readonly string[] OfficeRoles = ["ADMIN", "PROTOCOL", "OFFICE_HEAD", "STATE_LAWYER", "LEGAL_ASSISTANT", "MONITOR"];

    public ICurrentUser Actor => currentUser;

    public static string NormalizeEmail(string? email) => (email ?? string.Empty).Trim().ToLowerInvariant();

    public const string OfficeScope = "OFFICE";
    public const string InstitutionScope = "INSTITUTION";

    /// <summary>The single rule deciding which kind of user a role may be granted to; null = grantable to nobody.</summary>
    public static string? ScopeOf(string role) =>
        InstitutionRoles.Contains(role) ? InstitutionScope
        : OfficeRoles.Contains(role) ? OfficeScope
        : null;

    public static Error? ValidateRole(string role, bool isInstitutionUser)
    {
        var scope = ScopeOf(role);
        if (isInstitutionUser && scope != InstitutionScope)
        {
            return Error.Validation("user.role_not_allowed",
                $"Roli {role} nuk lejohet për përdorues të institucionit.");
        }

        if (!isInstitutionUser && scope != OfficeScope)
        {
            return Error.Validation("user.role_not_allowed",
                $"Roli {role} nuk lejohet për përdorues të zyrës.");
        }

        return null;
    }

    public static Error? ValidateIdentity(string email, string? first, string? last)
    {
        if (string.IsNullOrWhiteSpace(first) || first.Trim().Length > 100)
        {
            return Error.Validation("user.first_name_invalid", "Emri është i detyrueshëm (deri në 100 shkronja).");
        }

        if (string.IsNullOrWhiteSpace(last) || last.Trim().Length > 100)
        {
            return Error.Validation("user.last_name_invalid", "Mbiemri është i detyrueshëm (deri në 100 shkronja).");
        }

        var at = email.IndexOf('@');
        if (email.Length is 0 or > 254 || at < 1 || at == email.Length - 1 || email.Contains(' ') || email.IndexOf('@', at + 1) >= 0)
        {
            return Error.Validation("user.email_invalid", "Email-i nuk është i vlefshëm.");
        }

        return null;
    }

    /// <summary>Exactly one of office / institution must be given; resolves public ids to internal ids.</summary>
    public async Task<Result<(ushort? OfficeId, uint? InstitutionId)>> ResolveOrganizationAsync(
        Guid? officePublicId, Guid? institutionPublicId, CancellationToken ct)
    {
        if (officePublicId is null == institutionPublicId is null)
        {
            return Error.Validation("user.organization_required",
                "Përdoruesi duhet t'i përkasë saktësisht një zyre ose një institucioni.");
        }

        if (officePublicId is { } officeGuid)
        {
            var id = await db.Offices.AsNoTracking().Where(o => o.PublicId == officeGuid)
                .Select(o => (ushort?)o.Id).SingleOrDefaultAsync(ct);
            return id is null
                ? Error.Validation("user.office_not_found", "Zyra nuk u gjet.")
                : (id, (uint?)null);
        }

        var institutionGuid = institutionPublicId!.Value;
        var instId = await db.Institutions.AsNoTracking().Where(i => i.PublicId == institutionGuid)
            .Select(i => (uint?)i.Id).SingleOrDefaultAsync(ct);
        return instId is null
            ? Error.Validation("user.institution_not_found", "Institucioni nuk u gjet.")
            : ((ushort?)null, instId);
    }

    public Task<byte?> FindRoleIdAsync(string code, CancellationToken ct) =>
        db.Roles.AsNoTracking().Where(r => r.Code == code).Select(r => (byte?)r.Id).SingleOrDefaultAsync(ct);

    public Task<List<string>> RoleCodesAsync(uint userId, CancellationToken ct) =>
        (from ur in db.UserRoles.AsNoTracking()
         join r in db.Roles.AsNoTracking() on ur.RoleId equals r.Id
         where ur.UserId == userId
         orderby r.Code
         select r.Code).ToListAsync(ct);

    /// <summary>Locks the active ADMIN users and returns their ids, so two concurrent requests cannot both remove the last one.</summary>
    public async Task<List<uint>> LockActiveAdminsAsync(CancellationToken ct) =>
        await db.Database.SqlQuery<uint>($"""
            SELECT u.id AS Value FROM users u
            JOIN user_roles ur ON ur.user_id = u.id
            JOIN roles r ON r.id = ur.role_id
            WHERE r.code = 'ADMIN' AND u.is_active = 1
            FOR UPDATE
            """).ToListAsync(ct);

    public async Task<UserResponse> ToResponseAsync(UserRecord user, CancellationToken ct)
    {
        var roles = await RoleCodesAsync(user.Id, ct);
        var counts = await assignments.CountCurrentAsync([user.Id], ct);
        var office = await db.Offices.AsNoTracking().Where(o => o.Id == user.OfficeId)
            .Select(o => new NamedRef(o.PublicId, o.Name)).FirstOrDefaultAsync(ct);
        var institution = await db.Institutions.AsNoTracking().Where(i => i.Id == user.InstitutionId)
            .Select(i => new NamedRef(i.PublicId, i.Name)).FirstOrDefaultAsync(ct);
        return new UserResponse(
            user.PublicId, user.Email, user.FirstName, user.LastName, office, institution, roles,
            user.IsActive, user.LastLoginAt, counts.GetValueOrDefault(user.Id));
    }

    /// <summary>
    /// A head of office cannot be deactivated, lose OFFICE_HEAD or move to another office: new cases are routed
    /// to the head, so the admin must first appoint another head.
    /// </summary>
    public async Task<Error?> HeadOfOfficeErrorAsync(uint userId, CancellationToken ct)
    {
        var officeName = await db.Offices.AsNoTracking()
            .Where(o => o.HeadUserId == userId)
            .Select(o => o.Name)
            .FirstOrDefaultAsync(ct);
        return officeName is null
            ? null
            : Error.Validation("user.is_office_head",
                $"Ky përdorues është Kryetar i zyrës {officeName}. Caktoni fillimisht një Kryetar tjetër.");
    }

    /// <summary>Role and status changes must apply immediately, so drop the 60-second access cache of the user.</summary>
    public void InvalidateAccess(uint userId) => cache.Remove($"identity:access:{userId}");

    public void AuditChange(uint userId, string action, object? oldValues, object? newValues) =>
        audit.Record(AuditActions.AdminChange, "USER", userId,
            details: new { action, old = oldValues, @new = newValues });
}
