using Ash.Modules.Audit.Contracts;
using Ash.Modules.Reference.Infrastructure;
using Ash.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Ash.Modules.Reference.Application.Admin;

public sealed record CreateJudgeRequest(string FirstName, string LastName, int? CourtId, string? ExternalRef);

public sealed record UpdateJudgeRequest(string FirstName, string LastName, int? CourtId, string? ExternalRef);

public sealed record JudgeAdminResponse(
    long Id, string FirstName, string LastName, int? CourtId, string? CourtName, string? ExternalRef, bool IsActive);

public sealed record ListJudgesAdminQuery(string? Q, bool? Active, string? After, int? Limit);

public static class JudgeAdminSupport
{
    public static Error? Validate(string? firstName, string? lastName, string? externalRef)
    {
        if (string.IsNullOrWhiteSpace(firstName) || firstName.Trim().Length > 100)
        {
            return Error.Validation("judge.first_name_invalid", "Emri është i detyrueshëm (deri në 100 karaktere).");
        }

        if (string.IsNullOrWhiteSpace(lastName) || lastName.Trim().Length > 100)
        {
            return Error.Validation("judge.last_name_invalid", "Mbiemri është i detyrueshëm (deri në 100 karaktere).");
        }

        if (externalRef is not null && externalRef.Trim().Length > 50)
        {
            return Error.Validation("judge.external_ref_invalid", "Referenca e jashtme mund të ketë deri në 50 karaktere.");
        }

        return null;
    }

    /// <summary>The court must exist and be active when set.</summary>
    public static async Task<Error?> ValidateCourt(ReferenceDbContext db, int? courtId, CancellationToken ct)
    {
        if (courtId is null)
        {
            return null;
        }

        var active = await db.Courts.AsNoTracking()
            .Where(x => x.Id == courtId.Value)
            .Select(x => (bool?)x.IsActive)
            .SingleOrDefaultAsync(ct);
        return active == true
            ? null
            : Error.Validation("judge.court_invalid", "Gjykata nuk ekziston ose nuk është aktive.");
    }

    public static async Task<Error?> ValidateExternalRefUnique(ReferenceDbContext db, string? externalRef, uint? exceptId, CancellationToken ct)
    {
        if (externalRef is null)
        {
            return null;
        }

        var taken = await db.Judges.AnyAsync(x => x.ExternalRef == externalRef && (exceptId == null || x.Id != exceptId), ct);
        return taken
            ? Error.Validation("judge.external_ref_exists", "Kjo referencë e jashtme ekziston tashmë.")
            : null;
    }

    public static Error NotFound() => Error.NotFound("judge.not_found", "Gjyqtari nuk u gjet.");

    public static string? CleanExternalRef(string? externalRef) =>
        string.IsNullOrWhiteSpace(externalRef) ? null : externalRef.Trim();

    public static async Task<JudgeAdminResponse> ToResponse(ReferenceDbContext db, JudgeRecord record, CancellationToken ct)
    {
        var courtName = record.CourtId is { } courtId
            ? await db.Courts.AsNoTracking().Where(x => x.Id == courtId).Select(x => x.Name).SingleOrDefaultAsync(ct)
            : null;
        return new JudgeAdminResponse(
            record.Id, record.FirstName, record.LastName, record.CourtId, courtName, record.ExternalRef, record.IsActive);
    }

    public static object Snapshot(JudgeRecord record) => new
    {
        FirstName = record.FirstName,
        LastName = record.LastName,
        CourtId = record.CourtId,
        ExternalRef = record.ExternalRef,
        IsActive = record.IsActive
    };

    public static void AuditChange(IAuditLog audit, JudgeRecord record, string action, object? oldValues, object? newValues) =>
        audit.Record(AuditActions.AdminChange, "JUDGE", record.Id,
            details: new { action, old = oldValues, @new = newValues });
}
