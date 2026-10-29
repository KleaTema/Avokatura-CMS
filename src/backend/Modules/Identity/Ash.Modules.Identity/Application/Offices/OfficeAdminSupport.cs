using System.Text.RegularExpressions;
using Ash.Modules.Audit.Contracts;
using Ash.Modules.Identity.Application.GetMe;
using Ash.Modules.Identity.Infrastructure;
using Ash.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Ash.Modules.Identity.Application.Offices;

public sealed record CreateOfficeRequest(string Code, string Name, string OfficeType, Guid? ParentOfficeId);

/// <summary>The code is not here: it is a stable identifier (seeds, reports, integrations) and never changes.</summary>
public sealed record UpdateOfficeRequest(string Name, string OfficeType, Guid? ParentOfficeId);

public sealed record SetOfficeHeadRequest(Guid? UserPublicId);

public sealed record OfficeAdminResponse(
    Guid PublicId,
    string Code,
    string Name,
    string OfficeType,
    NamedRef? Parent,
    NamedRef? Head,
    bool IsActive,
    int ActiveUsers);

public static partial class OfficeAdminSupport
{
    public const string HeadRole = "OFFICE_HEAD";

    private static readonly string[] OfficeTypes = ["CENTRAL", "LOCAL", "REPRESENTATION"];

    public static Error? ValidateCode(string? code) =>
        string.IsNullOrWhiteSpace(code) || !CodeRegex().IsMatch(code.Trim())
            ? Error.Validation("office.code_invalid",
                "Kodi është i detyrueshëm: 2 deri në 10 shkronja të mëdha ose shifra, pa hapësira.")
            : null;

    public static Error? ValidateFields(string? name, string? officeType)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 200)
        {
            return Error.Validation("office.name_invalid", "Emri i zyrës është i detyrueshëm (deri në 200 karaktere).");
        }

        if (string.IsNullOrWhiteSpace(officeType) || !OfficeTypes.Contains(officeType.Trim()))
        {
            return Error.Validation("office.type_invalid", "Lloji i zyrës duhet të jetë CENTRAL, LOCAL ose REPRESENTATION.");
        }

        return null;
    }

    public static Error NotFound() => Error.NotFound("office.not_found", "Zyra nuk u gjet.");

    /// <summary>
    /// Resolves the parent's public id. It must exist and be active, and — for an existing office — must not
    /// be the office itself or one of its descendants (that would close a cycle A → B → A).
    /// </summary>
    public static async Task<Result<ushort?>> ResolveParentAsync(
        IdentityDbContext db, Guid? parentPublicId, ushort? officeId, CancellationToken ct)
    {
        if (parentPublicId is null)
        {
            return (ushort?)null;
        }

        var offices = await db.Offices.AsNoTracking()
            .Select(o => new { o.Id, o.PublicId, o.ParentOfficeId, o.IsActive })
            .ToListAsync(ct);
        var parent = offices.SingleOrDefault(o => o.PublicId == parentPublicId.Value);
        if (parent is null || !parent.IsActive)
        {
            return Error.Validation("office.parent_invalid", "Zyra mëmë nuk ekziston ose nuk është aktive.");
        }

        if (officeId is not null)
        {
            var parentOf = offices.ToDictionary(o => o.Id, o => o.ParentOfficeId);
            var visited = new HashSet<ushort>();
            for (ushort? current = parent.Id; current is not null && visited.Add(current.Value); current = parentOf.GetValueOrDefault(current.Value))
            {
                if (current == officeId)
                {
                    return Error.Validation("office.parent_cycle",
                        "Zyra mëmë nuk mund të jetë vetë zyra ose një nga zyrat që varen prej saj.");
                }
            }
        }

        return (ushort?)parent.Id;
    }

    public static async Task<OfficeAdminResponse> ToResponseAsync(IdentityDbContext db, OfficeRecord office, CancellationToken ct)
    {
        var parent = office.ParentOfficeId is { } parentId
            ? await db.Offices.AsNoTracking().Where(o => o.Id == parentId)
                .Select(o => new NamedRef(o.PublicId, o.Name)).SingleOrDefaultAsync(ct)
            : null;
        var head = office.HeadUserId is { } headId
            ? await db.Users.AsNoTracking().Where(u => u.Id == headId)
                .Select(u => new NamedRef(u.PublicId, u.FirstName + " " + u.LastName)).SingleOrDefaultAsync(ct)
            : null;
        var activeUsers = await db.Users.CountAsync(u => u.OfficeId == office.Id && u.IsActive, ct);
        return new OfficeAdminResponse(
            office.PublicId, office.Code, office.Name, office.OfficeType, parent, head, office.IsActive, activeUsers);
    }

    public static object Snapshot(OfficeRecord office) => new
    {
        office.Code,
        office.Name,
        office.OfficeType,
        office.ParentOfficeId,
        office.HeadUserId,
        office.IsActive
    };

    public static void AuditChange(IAuditLog audit, OfficeRecord office, string action, object? oldValues, object? newValues) =>
        audit.Record(AuditActions.AdminChange, "OFFICE", office.Id,
            details: new { action, old = oldValues, @new = newValues });

    [GeneratedRegex("^[A-Z0-9]{2,10}$")]
    private static partial Regex CodeRegex();
}
