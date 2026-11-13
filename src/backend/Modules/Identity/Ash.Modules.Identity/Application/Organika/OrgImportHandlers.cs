using System.Text.Json;
using Ash.Modules.Audit.Contracts;
using Ash.Modules.Identity.Infrastructure;
using Ash.SharedKernel.Identity;
using Ash.SharedKernel.Results;
using Ash.SharedKernel.Time;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace Ash.Modules.Identity.Application.Organika;

/// <summary>What a row would do. CHANGED and UNCHANGED are "matched" (the e-mail is a user).</summary>
public static class OrgRowStatus
{
    public const string Changed = "CHANGED";
    public const string Unchanged = "UNCHANGED";
    public const string EmailNotFound = "EMAIL_NOT_FOUND";
    public const string Duplicate = "DUPLICATE";
    public const string ManagerNotFound = "MANAGER_NOT_FOUND";
    public const string Invalid = "INVALID";
}

public sealed record OrgPreviewRow(
    int Line, string? Email, string Status, string? UserName,
    string? OldPosition, string? NewPosition, string? OldUnit, string? NewUnit, string? OldManager, string? NewManager,
    string? Message);

public sealed record OrgSummary(int Total, int Matched, int Changed, int Unchanged, int EmailNotFound, int Duplicate, int ManagerNotFound, int Invalid);

/// <summary>An import: its preview (computed now, against the current users) and whether it was applied.</summary>
public sealed record OrgImportView(
    Guid PublicId, string FileName, string UploadedBy, DateTime UploadedAt, string? AppliedBy, DateTime? AppliedAt,
    int? AppliedCount, OrgSummary Summary, IReadOnlyList<OrgPreviewRow> Rows);

/// <summary>One import of the list (no rows).</summary>
public sealed record OrgImportListItem(
    Guid PublicId, string FileName, string UploadedBy, DateTime UploadedAt, string? AppliedBy, DateTime? AppliedAt, int? AppliedCount,
    OrgSummary Summary);

/// <summary>
/// "Organika" (task 053g, client decision E; Administrimi, admin.org): the HR sheet gives each user's position, unit
/// and manager. Upload → the server reads and validates the sheet and shows the preview WITHOUT applying anything; the
/// import (the file, by, at, summary) is kept. "Apliko" computes the preview again against the current users and
/// writes the CHANGED rows (users.position, org_unit, manager_user_id) in the request's ONE transaction, with a
/// user_org_history row each; an import is applied once. Rows: an e-mail twice in the sheet → DUPLICATE (all of
/// them, none applied); an unknown e-mail → EMAIL_NOT_FOUND; an unknown manager → MANAGER_NOT_FOUND; no e-mail or
/// no position, or text too long → INVALID. TODO(akshi-q-hrmis): an integration with the public administration's HR
/// system (HRMIS) would keep the positions up to date without the sheet.
/// </summary>
public sealed class OrgImportHandlers(IdentityDbContext db, ICurrentUser user, IAuditLog audit, IClock clock)
{
    public const long MaxFileBytes = 2 * 1024 * 1024;
    public const int MaxRows = 5000;
    public const int MaxText = 200;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<Result<OrgImportView>> UploadAsync(HttpRequest request, CancellationToken ct)
    {
        if (!request.HasFormContentType)
        {
            return Error.Validation("organika.file_required", "Zgjidhni skedarin (.xlsx ose .csv).");
        }

        var form = await request.ReadFormAsync(ct);
        var file = form.Files.FirstOrDefault();
        if (file is null || file.Length == 0)
        {
            return Error.Validation("organika.file_required", "Zgjidhni skedarin (.xlsx ose .csv).");
        }

        if (file.Length > MaxFileBytes)
        {
            return Error.Validation("organika.file_too_large", "Skedari mund të jetë deri në 2 MB.");
        }

        byte[] content;
        await using (var stream = file.OpenReadStream())
        using (var memory = new MemoryStream())
        {
            await stream.CopyToAsync(memory, ct);
            content = memory.ToArray();
        }

        var fileName = Path.GetFileName(file.FileName);
        var (rows, error) = OrgSheet.Read(fileName, content);
        if (error is not null)
        {
            return Error.Validation("organika.file_invalid", error);
        }

        if (rows!.Count > MaxRows)
        {
            return Error.Validation("organika.too_many_rows", $"Skedari mund të ketë deri në {MaxRows} rreshta.");
        }

        var preview = await PreviewAsync(rows, ct);
        var record = new OrgImportRecord
        {
            PublicId = Guid.NewGuid(),
            FileName = fileName.Length <= 255 ? fileName : fileName[^255..],
            ContentType = fileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase) ? "text/csv" : OrgSheet.XlsxContentType,
            FileContent = content,
            Summary = JsonSerializer.Serialize(Summarize(preview), Json),
            UploadedBy = (uint)user.UserId,
            UploadedAt = clock.UtcNow,
        };
        db.OrgImports.Add(record);
        await db.SaveChangesAsync(ct);
        audit.Record(AuditActions.AdminChange, "ORG_IMPORT", (long)record.Id,
            details: new { Action = "ORG_IMPORT_UPLOADED", record.FileName, Summary = Summarize(preview) });
        return await ViewAsync(record, preview, ct);
    }

    public async Task<IReadOnlyList<OrgImportListItem>> ListAsync(CancellationToken ct)
    {
        var rows = await db.OrgImports.AsNoTracking()
            .OrderByDescending(i => i.UploadedAt).ThenByDescending(i => i.Id)
            .Take(100)
            .Select(i => new { i.PublicId, i.FileName, i.UploadedBy, i.UploadedAt, i.AppliedBy, i.AppliedAt, i.AppliedCount, i.Summary })
            .ToListAsync(ct);
        var names = await NamesAsync(rows.SelectMany(r => new[] { r.UploadedBy, r.AppliedBy ?? 0 }), ct);
        return rows.Select(r => new OrgImportListItem(r.PublicId, r.FileName, names.GetValueOrDefault(r.UploadedBy) ?? string.Empty,
                r.UploadedAt, r.AppliedBy is { } a ? names.GetValueOrDefault(a) : null, r.AppliedAt, (int?)r.AppliedCount,
                JsonSerializer.Deserialize<OrgSummary>(r.Summary, Json)!))
            .ToList();
    }

    /// <summary>The import with its preview computed NOW (an applied import shows what is left to change: usually nothing).</summary>
    public async Task<Result<OrgImportView>> GetAsync(Guid publicId, CancellationToken ct)
    {
        var record = await db.OrgImports.AsNoTracking().SingleOrDefaultAsync(i => i.PublicId == publicId, ct);
        if (record is null)
        {
            return NotFound();
        }

        var (rows, _) = OrgSheet.Read(record.FileName, record.FileContent);
        return await ViewAsync(record, await PreviewAsync(rows ?? [], ct), ct);
    }

    /// <summary>"Apliko": the CHANGED rows of a fresh preview, in this request's transaction; 409 when already applied.</summary>
    public async Task<Result<OrgImportView>> ApplyAsync(Guid publicId, CancellationToken ct)
    {
        var record = await db.OrgImports.SingleOrDefaultAsync(i => i.PublicId == publicId, ct);
        if (record is null)
        {
            return NotFound();
        }

        if (record.AppliedAt is not null)
        {
            return Error.Conflict("organika.applied", "Ky import është aplikuar tashmë.");
        }

        var (rows, _) = OrgSheet.Read(record.FileName, record.FileContent);
        var preview = await PreviewAsync(rows ?? [], ct);
        var changed = preview.Where(p => p.Row.Status == OrgRowStatus.Changed).ToList();
        var userIds = changed.Select(p => p.UserId!.Value).ToList();
        var users = await db.Users.Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, ct);
        var now = clock.UtcNow;
        var by = (uint)user.UserId;
        foreach (var p in changed)
        {
            var u = users[p.UserId!.Value];
            db.UserOrgHistory.Add(new UserOrgHistoryRecord
            {
                UserId = u.Id,
                ImportId = record.Id,
                OldPosition = u.Position,
                NewPosition = p.Position,
                OldOrgUnit = u.OrgUnit,
                NewOrgUnit = p.Unit,
                OldManagerUserId = u.ManagerUserId,
                NewManagerUserId = p.ManagerId,
                ChangedBy = by,
                ChangedAt = now,
            });
            u.Position = p.Position;
            u.OrgUnit = p.Unit;
            u.ManagerUserId = p.ManagerId;
        }

        record.AppliedAt = now;
        record.AppliedBy = by;
        record.AppliedCount = (uint)changed.Count;
        await db.SaveChangesAsync(ct);
        audit.Record(AuditActions.AdminChange, "ORG_IMPORT", (long)record.Id,
            details: new { Action = "ORG_IMPORT_APPLIED", record.FileName, Changed = changed.Count });
        return await ViewAsync(record, preview, ct);
    }

    private static Error NotFound() => Error.NotFound("organika.not_found", "Importi nuk u gjet.");

    /// <summary>A row of the preview with what "Apliko" needs (internal ids, never sent out).</summary>
    private sealed record Planned(OrgPreviewRow Row, uint? UserId, string? Position, string? Unit, uint? ManagerId);

    private async Task<List<Planned>> PreviewAsync(IReadOnlyList<OrgSheetRow> rows, CancellationToken ct)
    {
        static string Key(string email) => email.Trim().ToLowerInvariant();
        var emails = rows.SelectMany(r => new[] { r.Email, r.ManagerEmail }).OfType<string>().Select(Key).Distinct().ToList();
        var users = await db.Users.AsNoTracking()
            .Where(u => emails.Contains(u.Email))
            .Select(u => new { u.Id, u.Email, u.FirstName, u.LastName, u.Position, u.OrgUnit, u.ManagerUserId })
            .ToListAsync(ct);
        var byEmail = users.ToDictionary(u => Key(u.Email));
        var managerIds = users.Where(u => u.ManagerUserId != null).Select(u => u.ManagerUserId!.Value).Distinct().ToList();
        var managerNames = await NamesAsync(managerIds, ct);
        var duplicates = rows.Where(r => r.Email is not null).GroupBy(r => Key(r.Email!)).Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet();

        var result = new List<Planned>(rows.Count);
        foreach (var r in rows)
        {
            OrgPreviewRow Row(string status, string? message, string? userName = null) =>
                new(r.Line, r.Email, status, userName, null, r.Position, null, r.Unit, null, r.ManagerEmail, message);

            if (r.Email is null || r.Position is null)
            {
                result.Add(new Planned(Row(OrgRowStatus.Invalid, "Mungon e-maili ose pozicioni."), null, null, null, null));
                continue;
            }

            if (new[] { r.Position, r.Unit }.Any(t => t is { Length: > MaxText }))
            {
                result.Add(new Planned(Row(OrgRowStatus.Invalid, $"Pozicioni dhe njësia mund të kenë deri në {MaxText} karaktere."), null, null, null, null));
                continue;
            }

            if (duplicates.Contains(Key(r.Email)))
            {
                result.Add(new Planned(Row(OrgRowStatus.Duplicate, "Ky e-mail është më shumë se një herë në skedar."), null, null, null, null));
                continue;
            }

            if (!byEmail.TryGetValue(Key(r.Email), out var u))
            {
                result.Add(new Planned(Row(OrgRowStatus.EmailNotFound, "Nuk ka përdorues me këtë e-mail."), null, null, null, null));
                continue;
            }

            var name = $"{u.FirstName} {u.LastName}";
            uint? managerId = null;
            string? managerName = null;
            if (r.ManagerEmail is { } managerEmail)
            {
                if (!byEmail.TryGetValue(Key(managerEmail), out var manager) || manager.Id == u.Id)
                {
                    result.Add(new Planned(Row(OrgRowStatus.ManagerNotFound, "Eprori nuk është përdorues (ose është vetë personi).", name),
                        null, null, null, null));
                    continue;
                }

                managerId = manager.Id;
                managerName = $"{manager.FirstName} {manager.LastName}";
            }

            var oldManager = u.ManagerUserId is { } m ? managerNames.GetValueOrDefault(m) : null;
            var same = u.Position == r.Position && u.OrgUnit == r.Unit && u.ManagerUserId == managerId;
            result.Add(new Planned(
                new OrgPreviewRow(r.Line, r.Email, same ? OrgRowStatus.Unchanged : OrgRowStatus.Changed, name,
                    u.Position, r.Position, u.OrgUnit, r.Unit, oldManager, managerName, null),
                u.Id, r.Position, r.Unit, managerId));
        }

        return result;
    }

    private static OrgSummary Summarize(IReadOnlyList<Planned> preview)
    {
        int Count(string status) => preview.Count(p => p.Row.Status == status);
        var changed = Count(OrgRowStatus.Changed);
        var unchanged = Count(OrgRowStatus.Unchanged);
        return new OrgSummary(preview.Count, changed + unchanged, changed, unchanged, Count(OrgRowStatus.EmailNotFound),
            Count(OrgRowStatus.Duplicate), Count(OrgRowStatus.ManagerNotFound), Count(OrgRowStatus.Invalid));
    }

    private async Task<OrgImportView> ViewAsync(OrgImportRecord record, IReadOnlyList<Planned> preview, CancellationToken ct)
    {
        var names = await NamesAsync([record.UploadedBy, record.AppliedBy ?? 0], ct);
        return new OrgImportView(record.PublicId, record.FileName, names.GetValueOrDefault(record.UploadedBy) ?? string.Empty,
            record.UploadedAt, record.AppliedBy is { } a ? names.GetValueOrDefault(a) : null, record.AppliedAt, (int?)record.AppliedCount,
            Summarize(preview), preview.Select(p => p.Row).ToList());
    }

    private async Task<Dictionary<uint, string>> NamesAsync(IEnumerable<uint> ids, CancellationToken ct)
    {
        var list = ids.Where(id => id != 0).Distinct().ToList();
        return await db.Users.AsNoTracking().Where(u => list.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.FirstName + " " + u.LastName, ct);
    }
}
