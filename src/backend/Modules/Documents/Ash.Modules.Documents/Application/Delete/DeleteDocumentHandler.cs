using Ash.Modules.Audit.Contracts;
using Ash.Modules.Cases.Contracts;
using Ash.Modules.Documents.Application.Download;
using Ash.Modules.Documents.Infrastructure;
using Ash.SharedKernel.Identity;
using Ash.SharedKernel.Results;
using Ash.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;

namespace Ash.Modules.Documents.Application.Delete;

/// <summary>DELETE /api/documents/{docId} body: why the document is removed (required, at most 300 characters).</summary>
public sealed record DeleteDocumentRequest(string? Reason);

/// <summary>
/// DELETE /api/documents/{docId} (task 034): hides a wrong upload. SOFT: deleted_at, deleted_by, delete_reason — the
/// row and the stored object stay (legal records; the bucket's retention keeps the object). document.delete in the
/// effective permissions of the level the document was uploaded to (the primary lawyer); a CLOSED level → refused.
/// A document that is the decision or the security measure's record → 409. History DOCUMENT_REMOVED, audit.
/// TODO(client-q-uploader-delete): may the uploader remove their own upload shortly after (default: no).
/// TODO: physical removal before the retention ends = an admin-only governance-mode bypass with audit; not built.
/// </summary>
internal sealed class DeleteDocumentHandler(
    DocumentsDbContext db, ICaseAccessService access, ICaseActivity cases, IAuditLog audit, ICurrentUser user, IClock clock)
{
    public const string DeletePermission = "document.delete";
    public const int MaxReasonLength = 300;

    public async Task<Result<DeleteDocumentResponse>> Handle(Guid documentPublicId, DeleteDocumentRequest req, CancellationToken ct)
    {
        var reason = req.Reason;
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length > MaxReasonLength)
        {
            var errors = new FieldErrors();
            errors.Add("reason", string.IsNullOrWhiteSpace(reason)
                ? "Shkruani arsyen e heqjes."
                : $"Arsyeja mund të ketë deri në {MaxReasonLength} karaktere.");
            return errors.ToError("Të dhënat nuk janë të vlefshme.");
        }

        var row = await db.Documents.SingleOrDefaultAsync(d => d.PublicId == documentPublicId && d.DeletedAt == null, ct);
        if (row is null || !await access.CanViewDossierDocumentsAsync((long)row.DossierId, ct))
        {
            return DownloadDocumentHandler.NotFound();
        }

        // the action belongs to the level the document was uploaded to
        var level = row.CaseId is { } caseId ? await cases.FindAsync((long)caseId, ct) : null;
        if (level is { IsArchived: true })
        {
            return DossierErrors.Archived;
        }

        var permissions = level is null ? new HashSet<string>() : await access.GetEffectivePermissionsAsync(level.Id, ct);
        if (!permissions.Contains(DeletePermission))
        {
            return level is { IsClosed: true }
                ? Error.Conflict("case.closed", "Çështja është mbyllur; dokumentet e saj nuk hiqen më.")
                : Error.Forbidden("document.delete_forbidden", "Nuk keni leje të hiqni dokumente në këtë çështje.");
        }

        if (await cases.FindDocumentLinkAsync((long)row.Id, ct) is { } link)
        {
            var what = link == DocumentLink.Decision ? "vendimin" : "masën e sigurimit";
            return Error.Conflict("document.linked", $"Dokumenti është i lidhur me {what} dhe nuk mund të hiqet.");
        }

        var cleanReason = reason.Trim();
        row.DeletedAt = clock.UtcNow;
        row.DeletedBy = (uint)user.UserId;
        row.DeleteReason = cleanReason;
        await db.SaveChangesAsync(ct);

        var summary = $"Dokumenti u hoq: {row.Title}. Arsyeja: {cleanReason}";
        await cases.AddHistoryAsync((long)row.DossierId, level!.Id, HistoryEventType.DocumentRemoved,
            summary.Length <= 500 ? summary : summary[..497] + "...", new { row.Title, Reason = cleanReason }, ct);
        audit.Record(AuditActions.DocumentDelete, "DOCUMENT", (long)row.Id, level.Id,
            details: new { DocumentPublicId = row.PublicId, Reason = cleanReason });
        return new DeleteDocumentResponse(row.PublicId);
    }
}

public sealed record DeleteDocumentResponse(Guid DocumentPublicId);
