using Ash.Modules.Audit.Contracts;
using Ash.Modules.Reference.Infrastructure;
using Ash.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Ash.Modules.Reference.Application.Admin;

public sealed record CreateCourtRequest(string Name, string CourtLevel, string? City, bool IsForeign);

public sealed record UpdateCourtRequest(string Name, string CourtLevel, string? City, bool IsForeign);

public sealed record CourtAdminResponse(int Id, string Name, string CourtLevel, string? City, bool IsForeign, bool IsActive);

public static class CourtAdminSupport
{
    public static Error? Validate(string? name, string? courtLevel, string? city)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 250)
        {
            return Error.Validation("court.name_invalid",
                "Emri i gjykatës është i detyrueshëm (deri në 250 karaktere).");
        }

        if (string.IsNullOrWhiteSpace(courtLevel))
        {
            return Error.Validation("court.level_invalid", "Shkalla e gjykimit është e detyrueshme.");
        }

        if (city is not null && city.Trim().Length > 100)
        {
            return Error.Validation("court.city_invalid", "Qyteti mund të ketë deri në 100 karaktere.");
        }

        return null;
    }

    public static Error UnknownLevel() =>
        Error.Validation("court.level_invalid", "Shkalla e gjykimit nuk ekziston.");

    public static Error NameExists() =>
        Error.Validation("court.name_exists", "Një gjykatë me këtë emër ekziston tashmë.");

    public static Error NotFound() =>
        Error.NotFound("court.not_found", "Gjykata nuk u gjet.");

    public static async Task<byte?> FindLevelId(ReferenceDbContext db, string code, CancellationToken ct) =>
        await db.CourtLevels.AsNoTracking()
            .Where(x => x.Code == code)
            .Select(x => (byte?)x.Id)
            .SingleOrDefaultAsync(ct);

    public static async Task<string> LevelCode(ReferenceDbContext db, byte levelId, CancellationToken ct) =>
        await db.CourtLevels.AsNoTracking().Where(x => x.Id == levelId).Select(x => x.Code).SingleAsync(ct);

    public static string? CleanCity(string? city) => string.IsNullOrWhiteSpace(city) ? null : city.Trim();

    public static CourtAdminResponse ToResponse(CourtRecord record, string levelCode) =>
        new(record.Id, record.Name, levelCode, record.City, record.IsForeign, record.IsActive);

    public static object Snapshot(CourtRecord record) => new
    {
        Name = record.Name,
        CourtLevelId = record.CourtLevelId,
        City = record.City,
        IsForeign = record.IsForeign,
        IsActive = record.IsActive
    };

    public static void AuditChange(IAuditLog audit, CourtRecord record, string action, object? oldValues, object? newValues) =>
        audit.Record(AuditActions.AdminChange, "COURT", record.Id,
            details: new { action, old = oldValues, @new = newValues });
}
