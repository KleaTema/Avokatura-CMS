using System.Text.RegularExpressions;
using Ash.Modules.Audit.Contracts;
using Ash.Modules.Identity.Application.GetMe;
using Ash.Modules.Identity.Infrastructure;
using Ash.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Ash.Modules.Identity.Application.Institutions;

public sealed record CreateInstitutionRequest(
    string Name, string? ShortName, string InstitutionType, Guid? ParentInstitutionId, string? Nipt);

public sealed record UpdateInstitutionRequest(
    string Name, string? ShortName, string InstitutionType, Guid? ParentInstitutionId, string? Nipt);

public sealed record InstitutionAdminResponse(
    Guid PublicId,
    string Name,
    string? ShortName,
    string InstitutionType,
    NamedRef? Parent,
    string? Nipt,
    bool IsActive,
    int ActiveUsers);

public sealed record ListInstitutionsAdminQuery(string? Q, string? After, int? Limit);

public static partial class InstitutionAdminSupport
{
    public const string Ministry = "MINISTRY";

    private static readonly string[] InstitutionTypes = [Ministry, "AGENCY", "LOCAL_GOV", "INDEPENDENT", "OTHER"];

    public static Error? ValidateFields(string? name, string? shortName, string? institutionType, string? nipt)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 300)
        {
            return Error.Validation("institution.name_invalid", "Emri i institucionit është i detyrueshëm (deri në 300 karaktere).");
        }

        if (shortName is not null && shortName.Trim().Length > 60)
        {
            return Error.Validation("institution.short_name_invalid", "Emri i shkurtër mund të ketë deri në 60 karaktere.");
        }

        if (string.IsNullOrWhiteSpace(institutionType) || !InstitutionTypes.Contains(institutionType.Trim()))
        {
            return Error.Validation("institution.type_invalid",
                "Lloji i institucionit duhet të jetë MINISTRY, AGENCY, LOCAL_GOV, INDEPENDENT ose OTHER.");
        }

        var cleanNipt = CleanNipt(nipt);
        if (cleanNipt is not null && !NiptRegex().IsMatch(cleanNipt))
        {
            return Error.Validation("institution.nipt_invalid",
                "NIPT-i duhet të ketë formatin: një shkronjë, 8 shifra, një shkronjë (p.sh. K12345678A).");
        }

        return null;
    }

    /// <summary>NIPT is stored trimmed and upper-case; blank means none.</summary>
    public static string? CleanNipt(string? nipt) =>
        string.IsNullOrWhiteSpace(nipt) ? null : nipt.Trim().ToUpperInvariant();

    public static string? CleanShortName(string? shortName) =>
        string.IsNullOrWhiteSpace(shortName) ? null : shortName.Trim();

    public static async Task<Error?> ValidateNiptUnique(IdentityDbContext db, string? nipt, uint? exceptId, CancellationToken ct) =>
        nipt is not null && await db.Institutions.AnyAsync(i => i.Nipt == nipt && (exceptId == null || i.Id != exceptId), ct)
            ? Error.Validation("institution.nipt_exists", "Ekziston tashmë një institucion me këtë NIPT.")
            : null;

    // TODO(client-q-institution-hierarchy): statistics "per ministry" roll up exactly one level, so only
    // ministries can be parents and a ministry has no parent. Confirm with the client before allowing deeper trees.
    /// <summary>Resolves the parent: optional; when set it must be an active MINISTRY, and a MINISTRY has no parent.</summary>
    public static async Task<Result<uint?>> ResolveParentAsync(
        IdentityDbContext db, Guid? parentPublicId, string institutionType, CancellationToken ct)
    {
        if (parentPublicId is null)
        {
            return (uint?)null;
        }

        if (institutionType == Ministry)
        {
            return Error.Validation("institution.ministry_has_parent", "Një ministri nuk mund të ketë institucion mëmë.");
        }

        var parent = await db.Institutions.AsNoTracking()
            .Where(i => i.PublicId == parentPublicId.Value)
            .Select(i => new { i.Id, i.InstitutionType, i.IsActive })
            .SingleOrDefaultAsync(ct);
        if (parent is null || !parent.IsActive || parent.InstitutionType != Ministry)
        {
            return Error.Validation("institution.parent_not_ministry", "Institucioni mëmë duhet të jetë një ministri aktive.");
        }

        return (uint?)parent.Id;
    }

    public static Error NotFound() => Error.NotFound("institution.not_found", "Institucioni nuk u gjet.");

    /// <summary>
    /// Escape character for LIKE. Not '\': the provider writes ESCAPE '\' literally and MySQL reads \' as a quote.
    /// </summary>
    public const string LikeEscape = "!";

    /// <summary>"%text%" with the user's own %, _ and ! escaped, so they match literally.</summary>
    public static string LikeContains(string text) =>
        "%" + text.Replace("!", "!!").Replace("%", "!%").Replace("_", "!_") + "%";

    public static async Task<InstitutionAdminResponse> ToResponseAsync(IdentityDbContext db, InstitutionRecord institution, CancellationToken ct)
    {
        var parent = institution.ParentInstitutionId is { } parentId
            ? await db.Institutions.AsNoTracking().Where(i => i.Id == parentId)
                .Select(i => new NamedRef(i.PublicId, i.Name)).SingleOrDefaultAsync(ct)
            : null;
        var activeUsers = await db.Users.CountAsync(u => u.InstitutionId == institution.Id && u.IsActive, ct);
        return new InstitutionAdminResponse(institution.PublicId, institution.Name, institution.ShortName,
            institution.InstitutionType, parent, institution.Nipt, institution.IsActive, activeUsers);
    }

    public static object Snapshot(InstitutionRecord institution) => new
    {
        institution.Name,
        institution.ShortName,
        institution.InstitutionType,
        institution.ParentInstitutionId,
        institution.Nipt,
        institution.IsActive
    };

    public static void AuditChange(IAuditLog audit, InstitutionRecord institution, string action, object? oldValues, object? newValues) =>
        audit.Record(AuditActions.AdminChange, "INSTITUTION", institution.Id,
            details: new { action, old = oldValues, @new = newValues });

    // [0-9], not \d: \d also matches non-ASCII digits
    [GeneratedRegex("^[A-Z][0-9]{8}[A-Z]$")]
    private static partial Regex NiptRegex();
}
