using System.Text;
using Ash.Infrastructure.Storage;
using Ash.Modules.Audit.Contracts;
using Ash.Modules.Cases.Contracts;
using Ash.Modules.Documents.Infrastructure;
using Ash.Modules.Reference.Contracts;
using Ash.SharedKernel.Identity;
using Ash.SharedKernel.Results;
using Ash.SharedKernel.Time;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Net.Http.Headers;

namespace Ash.Modules.Documents.Application.Upload;

public sealed record DocumentWarning(string Code, string Message);

public sealed record UploadDocumentResponse(Guid DocumentPublicId, string ScanStatus, IReadOnlyList<DocumentWarning> Warnings);

/// <summary>
/// POST /api/cases/{id}/documents (task 033), multipart read with MultipartReader — no IFormFile, no temp file, the
/// file is never held in memory: fields first (documentTypeCode, otherTypeText?, title?), then the file. In order:
/// the case and document.upload → the fields → the extension → the magic bytes (peeked) → streamed to the quarantine
/// bucket (SHA-256 and size computed on the way; over the type's limit → aborted, 413) → the documents row PENDING,
/// DOCUMENT_UPLOADED, first data entry, audit. The scan job (ScanQueueJob) takes it from there.
/// </summary>
internal sealed class UploadDocumentHandler(
    DocumentsDbContext db,
    ICaseActivity cases,
    ICaseAccessService access,
    IReferenceData reference,
    IFileStorage storage,
    IAuditLog audit,
    ICurrentUser user,
    IClock clock,
    ILogger<UploadDocumentHandler> logger)
{
    public const string UploadPermission = "document.upload";

    /// <summary>The only type that may still be uploaded to a CLOSED case: the decision (task 036).</summary>
    public const string DecisionDocumentType = "VENDIM";
    public const int MaxTitleLength = 255;
    public const int MaxFileNameLength = 255;
    public const int MaxOtherTypeLength = 200;
    private const int MaxFieldBytes = 8 * 1024;
    private const long Margin = 1024 * 1024;
    private const string InvalidMessage = "Të dhënat e dokumentit nuk janë të vlefshme.";
    private const string TypeNotAllowed = "Lloji i skedarit nuk lejohet";

    public async Task<Result<UploadDocumentResponse>> Handle(Guid casePublicId, HttpRequest request, CancellationToken ct)
    {
        // 1. the case and the action, before a single byte of the body is read
        var c = await cases.FindViewableAsync(casePublicId, ct);
        if (c is null)
        {
            return Error.NotFound("case.not_found", "Çështja nuk u gjet.");
        }

        if (!(await access.GetEffectivePermissionsAsync(c.Id, ct)).Contains(UploadPermission))
        {
            // a CLOSED case keeps only CaseAccessService.ActionsAllowedWhenClosed (document.upload there: VENDIM only)
            return c.IsClosed
                ? Error.Conflict("case.closed", "Çështja është mbyllur; nuk ngarkohen më dokumente.")
                : Error.Forbidden("document.upload_forbidden", "Nuk keni leje të ngarkoni dokumente në këtë çështje.");
        }

        var types = (await reference.ListAllowedFileTypesAsync(ct))
            .Where(t => t.IsActive && t.AppliesTo is "DOCUMENT" or "BOTH")
            .ToList();
        SetRequestLimit(request, types);

        if (!MediaTypeHeaderValue.TryParse(request.ContentType, out var mediaType)
            || !mediaType.MediaType.Equals("multipart/form-data", StringComparison.OrdinalIgnoreCase)
            || HeaderUtilities.RemoveQuotes(mediaType.Boundary).Value is not { Length: > 0 } boundary)
        {
            return Error.Validation("document.not_multipart", "Dërgoni dokumentin si multipart/form-data.");
        }

        // 2. the fields, then the file section
        var reader = new MultipartReader(boundary, request.Body) { HeadersLengthLimit = 16 * 1024 };
        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        MultipartSection? file = null;
        string? rawFileName = null;
        while (await reader.ReadNextSectionAsync(ct) is { } section)
        {
            if (!ContentDispositionHeaderValue.TryParse(section.ContentDisposition, out var disposition))
            {
                return Error.Validation("document.invalid_part", "Pjesa e kërkesës nuk ka Content-Disposition.");
            }

            if (disposition.IsFileDisposition())
            {
                if (!fields.ContainsKey("documentTypeCode"))
                {
                    return Error.Validation("document.fields_after_file", "Fushat duhet të dërgohen para skedarit.");
                }

                file = section;
                rawFileName = HeaderUtilities.RemoveQuotes(disposition.FileNameStar).Value
                              ?? HeaderUtilities.RemoveQuotes(disposition.FileName).Value;
                break;
            }

            var name = HeaderUtilities.RemoveQuotes(disposition.Name).Value ?? string.Empty;
            var value = await ReadFieldAsync(section.Body, ct);
            if (value is null)
            {
                return Field(name, "Vlera është shumë e gjatë.");
            }

            fields[name] = value;
        }

        // 3. the fields BEFORE the file is read
        var errors = new FieldErrors();
        var typeCode = fields.GetValueOrDefault("documentTypeCode")?.Trim();
        var documentType = string.IsNullOrEmpty(typeCode) ? null : await reference.GetDocumentType(typeCode, ct);
        if (documentType is not { IsActive: true })
        {
            errors.Add("documentTypeCode", "Zgjidhni llojin e dokumentit.");
        }
        else if (c.IsClosed && documentType.Code != DecisionDocumentType)
        {
            // CaseAccessService keeps document.upload on a CLOSED case for the decision document only (task 036)
            // TODO(client-q-closed-uploads): who may upload the reasoned decision after closing
            return Error.Conflict("document.closed_only_decision", "Në një çështje të mbyllur ngarkohet vetëm vendimi.");
        }

        var otherText = fields.GetValueOrDefault("otherTypeText")?.Trim();
        if (documentType is { IsOther: true } && string.IsNullOrEmpty(otherText))
        {
            errors.Add("otherTypeText", "Plotësoni llojin e dokumentit.");
        }
        else if (otherText is { Length: > MaxOtherTypeLength })
        {
            errors.Add("otherTypeText", $"Lloji mund të ketë deri në {MaxOtherTypeLength} karaktere.");
        }

        var title = fields.GetValueOrDefault("title")?.Trim();
        if (title is { Length: > MaxTitleLength })
        {
            errors.Add("title", $"Titulli mund të ketë deri në {MaxTitleLength} karaktere.");
        }

        var fileName = CleanFileName(rawFileName);
        if (file is null)
        {
            errors.Add("file", "Zgjidhni skedarin.");
        }
        else if (fileName.Length == 0)
        {
            errors.Add("file", "Skedari nuk ka emër.");
        }
        else if (fileName.Length > MaxFileNameLength)
        {
            errors.Add("file", $"Emri i skedarit mund të ketë deri në {MaxFileNameLength} karaktere.");
        }

        if (errors.Any)
        {
            return errors.ToError(InvalidMessage);
        }

        // 4. the extension, then the magic bytes of the first bytes
        var extension = Path.GetExtension(fileName).TrimStart('.').ToLowerInvariant();
        var fileType = types.Where(t => t.Extension == extension).OrderBy(t => t.AppliesTo == "DOCUMENT" ? 0 : 1).FirstOrDefault();
        if (fileType is null)
        {
            return Error.Validation("document.type_not_allowed", TypeNotAllowed);
        }

        var magic = Convert.FromHexString(fileType.MagicHex);
        var prefix = await PeekAsync(file!.Body, magic.Length, ct);
        if (!prefix.AsSpan().SequenceEqual(magic))
        {
            return Error.Validation("document.type_not_allowed", TypeNotAllowed);
        }

        // 5. streamed to quarantine; never more than the type's limit
        var key = StorageKeys.New(clock);
        var quarantine = storage.Buckets.Quarantine;
        StoredFileInfo stored;
        try
        {
            stored = await storage.PutAsync(quarantine, key, new LimitedUploadStream(prefix, file.Body, (long)fileType.MaxSizeBytes),
                fileType.ContentType, ct);
        }
        catch (FileTooLargeException)
        {
            await DeleteQuietlyAsync(quarantine, key);
            return Error.TooLarge("document.too_large",
                $"Skedari kalon madhësinë maksimale prej {fileType.MaxSizeBytes / (1024 * 1024)} MB.");
        }

        // 6. the record, in the request transaction. If anything below fails, the transaction rolls back and the
        //    quarantine object is left to expire (the bucket's 1-day lifecycle rule) — no row ever points to it.
        var duplicate = await db.Documents.AsNoTracking()
            .Where(d => d.DossierId == (ulong)c.DossierId && d.Sha256 == stored.Sha256 && d.DeletedAt == null)
            .OrderByDescending(d => d.UploadedAt)
            .Select(d => new { d.UploadedAt, d.Title })
            .FirstOrDefaultAsync(ct);

        var row = new DocumentRow
        {
            PublicId = Guid.NewGuid(),
            DossierId = (ulong)c.DossierId,
            CaseId = (ulong)c.Id,
            DocumentTypeId = documentType!.Id,
            OtherTypeText = documentType.IsOther ? otherText : null,
            Title = string.IsNullOrEmpty(title) ? Path.GetFileNameWithoutExtension(fileName) : title,
            OriginalFileName = fileName,
            ContentType = fileType.ContentType,
            SizeBytes = (ulong)stored.Length,
            Sha256 = stored.Sha256,
            StorageBucket = quarantine,
            StorageKey = key,
            ScanStatus = DocumentRow.Pending,
            UploadedBy = (uint)user.UserId,
            UploadedAt = clock.UtcNow,
        };
        db.Documents.Add(row);
        await db.SaveChangesAsync(ct);

        var typeName = row.OtherTypeText is null ? documentType.Name : $"{documentType.Name}: {row.OtherTypeText}";
        await cases.AddHistoryAsync(c.DossierId, c.Id, HistoryEventType.DocumentUploaded,
            Truncate($"Dokumenti u ngarkua: {row.Title} ({typeName}, {FormatSize(stored.Length)})."),
            new { row.Title, DocumentType = typeName, row.SizeBytes }, ct);
        await cases.MarkFirstDataEntryAsync(c.Id, ct);
        audit.Record(AuditActions.DocumentUpload, "DOCUMENT", (long)row.Id, c.Id,
            details: new { DocumentPublicId = row.PublicId, row.SizeBytes, DocumentType = documentType.Code });

        var warnings = duplicate is null
            ? []
            : new List<DocumentWarning>
            {
                new("document.duplicate",
                    $"Ky dokument është ngarkuar më parë më {TiraneTime.DateOf(duplicate.UploadedAt):dd.MM.yyyy} si \"{duplicate.Title}\"."),
            };
        return new UploadDocumentResponse(row.PublicId, row.ScanStatus, warnings);
    }

    /// <summary>
    /// The request may be as large as the largest DOCUMENT/BOTH type plus 1 MB for the fields and multipart framing
    /// (Kestrel's default is 30 MB). From the cached reference data, so an admin change applies with the cache.
    /// TODO(akshi-q-proxy): the reverse proxy in front of the API at AKSHI must allow the same size.
    /// </summary>
    private static void SetRequestLimit(HttpRequest request, IReadOnlyList<AllowedFileTypeDto> types)
    {
        var feature = request.HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (feature is { IsReadOnly: false } && types.Count > 0)
        {
            feature.MaxRequestBodySize = (long)types.Max(t => t.MaxSizeBytes) + Margin;
        }
    }

    /// <summary>A form field as text; null when it is longer than 8 KB.</summary>
    private static async Task<string?> ReadFieldAsync(Stream body, CancellationToken ct)
    {
        var buffer = new byte[MaxFieldBytes + 1];
        var total = 0;
        int read;
        while (total < buffer.Length && (read = await body.ReadAsync(buffer.AsMemory(total), ct)) > 0)
        {
            total += read;
        }

        return total > MaxFieldBytes ? null : Encoding.UTF8.GetString(buffer, 0, total);
    }

    /// <summary>Up to <paramref name="count"/> first bytes (fewer for a shorter file).</summary>
    private static async Task<byte[]> PeekAsync(Stream body, int count, CancellationToken ct)
    {
        var buffer = new byte[count];
        var total = 0;
        int read;
        while (total < count && (read = await body.ReadAsync(buffer.AsMemory(total), ct)) > 0)
        {
            total += read;
        }

        return total == count ? buffer : buffer[..total];
    }

    /// <summary>The name without a path ("C:\x\a.pdf", "../a.pdf" → "a.pdf") and without control characters.</summary>
    internal static string CleanFileName(string? fileName)
    {
        var name = (fileName ?? string.Empty).Replace('\\', '/');
        name = name[(name.LastIndexOf('/') + 1)..];
        return new string(name.Where(ch => !char.IsControl(ch)).ToArray()).Trim();
    }

    private static string FormatSize(long bytes) =>
        bytes >= 1024 * 1024 ? $"{bytes / (1024.0 * 1024):0.0} MB" : $"{Math.Max(1, bytes / 1024)} KB";

    private static string Truncate(string summary) => summary.Length <= 500 ? summary : summary[..497] + "...";

    private async Task DeleteQuietlyAsync(string bucket, string key)
    {
        try
        {
            await storage.DeleteAsync(bucket, key, CancellationToken.None);
        }
        catch (Exception ex)
        {
            // the multipart upload was aborted, so normally nothing is there; the quarantine lifecycle cleans up the rest
            logger.LogWarning(ex, "Could not remove the quarantined upload {Key} after it exceeded its limit.", key);
        }
    }

    private static Error Field(string field, string message)
    {
        var errors = new FieldErrors();
        errors.Add(field, message);
        return errors.ToError(InvalidMessage);
    }
}
