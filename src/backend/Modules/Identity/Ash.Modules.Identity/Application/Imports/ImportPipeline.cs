using System.Text.Json;
using Ash.Modules.Audit.Contracts;
using Ash.Modules.Identity.Infrastructure;
using Ash.SharedKernel.Identity;
using Ash.SharedKernel.Results;
using Ash.SharedKernel.Time;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace Ash.Modules.Identity.Application.Imports;

/// <summary>What a row does: NEW / CHANGED are written by "Apliko"; UNCHANGED, EXISTS and ERROR are not.</summary>
public static class ImportStatus
{
    public const string New = "NEW";
    public const string Changed = "CHANGED";
    public const string Unchanged = "UNCHANGED";
    /// <summary>Users: the e-mail exists and the row does not ask to update it (perditeso ≠ PO).</summary>
    public const string Exists = "EXISTS";
    public const string Error = "ERROR";

    public static bool Writes(string status) => status is New or Changed;

    public static string Text(string status) => status switch
    {
        New => "I ri",
        Changed => "Ndryshon",
        Unchanged => "Pa ndryshim",
        Exists => "Ekziston",
        _ => "Gabim",
    };
}

/// <summary>A value the row changes: the column, what it is now, what it becomes.</summary>
public sealed record ImportChange(string Column, string? Old, string? New);

/// <summary>Why a cell (Column) — or the whole row (Column empty) — cannot be imported.</summary>
public sealed record ImportCellError(string Column, string Message);

/// <summary>One row of the preview / the result: Key = the row's identifier (the e-mail).</summary>
public sealed record ImportRowView(
    int Line, string? Key, string Status, string? Name, IReadOnlyList<ImportChange> Changes,
    IReadOnlyList<ImportCellError> Errors, string? Message);

public sealed record ImportSummary(int Total, int New, int Changed, int Unchanged, int Exists, int Errors);

/// <summary>An import: its preview (computed now) — or, once applied, the rows as "Apliko" left them.</summary>
public sealed record ImportView(
    Guid PublicId, string Kind, string FileName, string UploadedBy, DateTime UploadedAt, string? AppliedBy, DateTime? AppliedAt,
    int? AppliedCount, ImportSummary Summary, IReadOnlyList<ImportRowView> Rows);

public sealed record ImportListItem(
    Guid PublicId, string FileName, string UploadedBy, DateTime UploadedAt, string? AppliedBy, DateTime? AppliedAt, int? AppliedCount,
    ImportSummary Summary);

/// <summary>The definition as the page shows it (the same texts as the template's "Udhëzime" sheet).</summary>
public sealed record ImportDefinitionView(
    string Kind, string Title, string Purpose, IReadOnlyList<string> Formats, IReadOnlyList<ImportColumnView> Columns,
    IReadOnlyList<string> Rules, int MaxRows, int MaxFileMb, string TemplateFileName);

public sealed record ImportColumnView(string Name, string Requirement, string RequirementText, string Description, string Example);

/// <summary>A row of the preview with what "Apliko" needs (internal data, never sent out).</summary>
public sealed record ImportPlan(ImportRowView Row, object? Payload);

/// <summary>One import kind (Organika, users): its definition, the preview of its rows, and how "Apliko" writes them.</summary>
public interface IImportKind
{
    /// <summary>ORGANIKA | USERS (org_imports.kind).</summary>
    string Kind { get; }

    ImportDefinition Definition { get; }

    /// <summary>Every row's status against the CURRENT data, with the reason per cell. Never writes.</summary>
    Task<IReadOnlyList<ImportPlan>> PreviewAsync(IReadOnlyList<ImportRow> rows, CancellationToken ct);

    /// <summary>
    /// Writes the NEW / CHANGED rows in the request's transaction. An error stops the whole import (the request rolls
    /// back: nothing is written).
    /// </summary>
    Task<Result<int>> ApplyAsync(ulong importId, IReadOnlyList<ImportPlan> rows, CancellationToken ct);
}

/// <summary>
/// Task 053l — the ONE import pipeline (the 053g Organika design, now for every kind): upload a .xlsx / .csv (≤ 5 MB,
/// ≤ 5,000 rows) → the server reads and validates it with the kind's definition → the PREVIEW (new / changed /
/// unchanged / exists / error with the reason per cell) — nothing is written but the import record (the file, by, at,
/// summary) → "Apliko" recomputes the preview against the current data and writes in the request's ONE transaction →
/// the result report (.xlsx, every row with its status and message). Every step is audited.
/// </summary>
public sealed class ImportPipeline(IdentityDbContext db, ICurrentUser user, IAuditLog audit, IClock clock)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static ImportDefinitionView Describe(IImportKind kind)
    {
        var d = kind.Definition;
        return new ImportDefinitionView(d.Kind, d.Title, d.Purpose, ImportDefinition.Formats,
            d.Columns.Select(c => new ImportColumnView(c.Name, c.Requirement, ImportDefinition.RequirementText(c.Requirement), c.Description, c.Example))
                .ToList(),
            d.AllRules.ToList(), ImportDefinition.MaxRows, (int)(ImportDefinition.MaxFileBytes / 1024 / 1024), d.TemplateFileName);
    }

    public async Task<Result<ImportView>> UploadAsync(IImportKind kind, HttpRequest request, CancellationToken ct)
    {
        if (!request.HasFormContentType)
        {
            return FileRequired();
        }

        var form = await request.ReadFormAsync(ct);
        var file = form.Files.FirstOrDefault();
        if (file is null || file.Length == 0)
        {
            return FileRequired();
        }

        if (file.Length > ImportDefinition.MaxFileBytes)
        {
            return Error.Validation("import.file_too_large", $"Skedari mund të jetë deri në {ImportDefinition.MaxFileBytes / 1024 / 1024} MB.");
        }

        byte[] content;
        await using (var stream = file.OpenReadStream())
        using (var memory = new MemoryStream())
        {
            await stream.CopyToAsync(memory, ct);
            content = memory.ToArray();
        }

        var fileName = Path.GetFileName(file.FileName);
        var (rows, error) = kind.Definition.Read(fileName, content);
        if (error is not null)
        {
            return Error.Validation("import.file_invalid", error);
        }

        var preview = await kind.PreviewAsync(rows!, ct);
        var summary = Summarize(preview.Select(p => p.Row));
        var record = new OrgImportRecord
        {
            PublicId = Guid.NewGuid(),
            Kind = kind.Kind,
            FileName = fileName.Length <= 255 ? fileName : fileName[^255..],
            ContentType = fileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase) ? "text/csv" : ImportDefinition.XlsxContentType,
            FileContent = content,
            Summary = JsonSerializer.Serialize(summary, Json),
            UploadedBy = (uint)user.UserId,
            UploadedAt = clock.UtcNow,
        };
        db.OrgImports.Add(record);
        await db.SaveChangesAsync(ct);
        audit.Record(AuditActions.AdminChange, "IMPORT", (long)record.Id,
            details: new { Action = "IMPORT_UPLOADED", kind.Kind, record.FileName, Summary = summary });
        return await ViewAsync(record, preview.Select(p => p.Row).ToList(), ct);
    }

    public async Task<IReadOnlyList<ImportListItem>> ListAsync(IImportKind kind, CancellationToken ct)
    {
        var rows = await db.OrgImports.AsNoTracking()
            .Where(i => i.Kind == kind.Kind)
            .OrderByDescending(i => i.UploadedAt).ThenByDescending(i => i.Id)
            .Take(100)
            .Select(i => new { i.PublicId, i.FileName, i.UploadedBy, i.UploadedAt, i.AppliedBy, i.AppliedAt, i.AppliedCount, i.Summary })
            .ToListAsync(ct);
        var names = await NamesAsync(rows.SelectMany(r => new[] { r.UploadedBy, r.AppliedBy ?? 0 }), ct);
        return rows.Select(r => new ImportListItem(r.PublicId, r.FileName, names.GetValueOrDefault(r.UploadedBy) ?? string.Empty,
                r.UploadedAt, r.AppliedBy is { } a ? names.GetValueOrDefault(a) : null, r.AppliedAt, (int?)r.AppliedCount,
                ReadSummary(r.Summary)))
            .ToList();
    }

    /// <summary>Not applied: the preview computed NOW. Applied: the rows as "Apliko" left them.</summary>
    public async Task<Result<ImportView>> GetAsync(IImportKind kind, Guid publicId, CancellationToken ct)
    {
        var record = await FindAsync(kind, publicId, tracked: false, ct);
        if (record is null)
        {
            return NotFound();
        }

        return await ViewAsync(record, await RowsAsync(kind, record, ct), ct);
    }

    /// <summary>"Apliko": a fresh preview, its NEW / CHANGED rows written in ONE transaction; 409 when already applied.</summary>
    public async Task<Result<ImportView>> ApplyAsync(IImportKind kind, Guid publicId, CancellationToken ct)
    {
        var record = await FindAsync(kind, publicId, tracked: true, ct);
        if (record is null)
        {
            return NotFound();
        }

        if (record.AppliedAt is not null)
        {
            return Error.Conflict("import.applied", "Ky import është aplikuar tashmë.");
        }

        var (rows, _) = kind.Definition.Read(record.FileName, record.FileContent);
        var preview = await kind.PreviewAsync(rows ?? [], ct);
        var written = await kind.ApplyAsync(record.Id, preview, ct);
        if (written.IsFailure)
        {
            // the request rolls back (an error status): nothing of the import is written
            return written.Error!;
        }

        var result = preview.Select(p => p.Row with
        {
            Message = p.Row.Message ?? (ImportStatus.Writes(p.Row.Status) ? (p.Row.Status == ImportStatus.New ? "U krijua." : "U përditësua.") : null),
        }).ToList();
        record.AppliedAt = clock.UtcNow;
        record.AppliedBy = (uint)user.UserId;
        record.AppliedCount = (uint)written.Value;
        record.Result = JsonSerializer.Serialize(result, Json);
        await db.SaveChangesAsync(ct);
        audit.Record(AuditActions.AdminChange, "IMPORT", (long)record.Id,
            details: new { Action = "IMPORT_APPLIED", kind.Kind, record.FileName, Written = written.Value, Summary = Summarize(result) });
        return await ViewAsync(record, result, ct);
    }

    /// <summary>The result report: one row per sheet row with its status and message (the preview before "Apliko").</summary>
    public async Task<Result<(byte[] Content, string FileName)>> ReportAsync(IImportKind kind, Guid publicId, CancellationToken ct)
    {
        var record = await FindAsync(kind, publicId, tracked: false, ct);
        if (record is null)
        {
            return NotFound();
        }

        var rows = await RowsAsync(kind, record, ct);
        var key = kind.Definition.Columns[0].Name;
        var table = new List<string[]> { new[] { "Rreshti", key, "Emri", "Statusi", "Ndryshimet", "Mesazhi" } };
        table.AddRange(rows.Select(r => new[]
        {
            r.Line.ToString(), r.Key ?? string.Empty, r.Name ?? string.Empty, ImportStatus.Text(r.Status),
            string.Join("; ", r.Changes.Select(c => $"{c.Column}: {c.Old ?? "—"} → {c.New ?? "—"}")),
            string.Join("; ", r.Errors.Select(e => string.IsNullOrEmpty(e.Column) ? e.Message : $"{e.Column}: {e.Message}")
                .Concat(r.Message is { } m ? [m] : [])),
        }));
        var name = $"{Path.GetFileNameWithoutExtension(record.FileName)}-rezultati.xlsx";
        return (ImportDefinition.Workbook(("Rezultati", table)), name);
    }

    private async Task<IReadOnlyList<ImportRowView>> RowsAsync(IImportKind kind, OrgImportRecord record, CancellationToken ct)
    {
        if (record.Result is { } stored)
        {
            return JsonSerializer.Deserialize<List<ImportRowView>>(stored, Json)!;
        }

        var (rows, _) = kind.Definition.Read(record.FileName, record.FileContent);
        return (await kind.PreviewAsync(rows ?? [], ct)).Select(p => p.Row).ToList();
    }

    private Task<OrgImportRecord?> FindAsync(IImportKind kind, Guid publicId, bool tracked, CancellationToken ct)
    {
        var query = tracked ? db.OrgImports : db.OrgImports.AsNoTracking();
        return query.SingleOrDefaultAsync(i => i.PublicId == publicId && i.Kind == kind.Kind, ct);
    }

    public static ImportSummary Summarize(IEnumerable<ImportRowView> rows)
    {
        var list = rows.ToList();
        int Count(string status) => list.Count(r => r.Status == status);
        return new ImportSummary(list.Count, Count(ImportStatus.New), Count(ImportStatus.Changed), Count(ImportStatus.Unchanged),
            Count(ImportStatus.Exists), Count(ImportStatus.Error));
    }

    /// <summary>A summary stored by 053g (matched, emailNotFound …) reads as zeros where the names differ.</summary>
    private static ImportSummary ReadSummary(string json) =>
        JsonSerializer.Deserialize<ImportSummary>(json, Json) ?? new ImportSummary(0, 0, 0, 0, 0, 0);

    private static Error FileRequired() => Error.Validation("import.file_required", "Zgjidhni skedarin (.xlsx ose .csv).");

    private static Error NotFound() => Error.NotFound("import.not_found", "Importi nuk u gjet.");

    private async Task<ImportView> ViewAsync(OrgImportRecord record, IReadOnlyList<ImportRowView> rows, CancellationToken ct)
    {
        var names = await NamesAsync([record.UploadedBy, record.AppliedBy ?? 0], ct);
        return new ImportView(record.PublicId, record.Kind, record.FileName, names.GetValueOrDefault(record.UploadedBy) ?? string.Empty,
            record.UploadedAt, record.AppliedBy is { } a ? names.GetValueOrDefault(a) : null, record.AppliedAt, (int?)record.AppliedCount,
            Summarize(rows), rows);
    }

    private async Task<Dictionary<uint, string>> NamesAsync(IEnumerable<uint> ids, CancellationToken ct)
    {
        var list = ids.Where(id => id != 0).Distinct().ToList();
        return await db.Users.AsNoTracking().Where(u => list.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.FirstName + " " + u.LastName, ct);
    }
}
