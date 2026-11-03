using Ash.AspNetCore.Http;
using Ash.Infrastructure.Storage;
using Ash.Modules.Audit.Contracts;
using Ash.Modules.Cases.Contracts;
using Ash.Modules.Documents.Application.List;
using Ash.Modules.Documents.Infrastructure;
using Ash.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Ash.Modules.Documents.Application.Download;

public enum DownloadMode
{
    /// <summary>Content-Disposition: attachment — the browser saves the file.</summary>
    Download,

    /// <summary>Content-Disposition: inline with a sandbox CSP — pdf, png, jpg only.</summary>
    Preview,
}

/// <summary>
/// GET /api/documents/{docId}/download and …/preview (task 034), THROUGH THE API: the access check and the audit
/// happen at download time, and a link cannot be forwarded. Only CLEAN documents; a deleted one is 404.
/// Audit DOCUMENT_DOWNLOAD on EVERY call (each Range request of a PDF viewer too), before streaming starts.
/// TODO(client-q-deleted-docs): access for Monitor/Admin to deleted documents (none for now).
/// </summary>
internal sealed class DownloadDocumentHandler(
    DocumentsDbContext db, ICaseAccessService access, IFileStorage storage, IAuditLog audit)
{
    /// <summary>
    /// The preview is shown by the browser itself: no scripts, no plugins, no network (sandbox, default-src 'none');
    /// a PDF viewer's own images and styles still work.
    /// </summary>
    public const string PreviewPolicy = "sandbox; default-src 'none'; img-src 'self' data:; style-src 'unsafe-inline'";

    public async Task<Result<StoredFileResult>> Handle(Guid documentPublicId, DownloadMode mode, string? range, CancellationToken ct)
    {
        var row = await db.Documents.AsNoTracking().SingleOrDefaultAsync(d => d.PublicId == documentPublicId, ct);
        if (row is null || row.DeletedAt is not null || !await access.CanViewDossierDocumentsAsync((long)row.DossierId, ct))
        {
            return NotFound();
        }

        switch (row.ScanStatus)
        {
            case DocumentRow.Pending:
                return Error.Conflict("document.pending", "Dokumenti po kontrollohet ende");
            case DocumentRow.Infected or DocumentRow.Failed:
                return Error.Conflict("document.rejected", "Dokumenti u refuzua nga kontrolli antivirus");
        }

        var preview = mode == DownloadMode.Preview;
        if (preview && !ListDocumentsHandler.PreviewContentTypes.Contains(row.ContentType))
        {
            return Error.Validation("document.preview_unsupported", "Ky lloj dokumenti nuk shfaqet në shfletues; shkarkojeni.");
        }

        audit.Record(AuditActions.DocumentDownload, "DOCUMENT", (long)row.Id, (long?)row.CaseId,
            details: new { DocumentPublicId = row.PublicId, Mode = preview ? "preview" : "download", Range = range });

        var headers = preview ? new Dictionary<string, string> { ["Content-Security-Policy"] = PreviewPolicy } : null;
        return StoredFileResult.FromStorage(storage, row.StorageBucket, row.StorageKey, (long)row.SizeBytes, row.ContentType,
            row.OriginalFileName, inline: preview, headers: headers);
    }

    internal static Error NotFound() => Error.NotFound("document.not_found", "Dokumenti nuk u gjet.");
}
