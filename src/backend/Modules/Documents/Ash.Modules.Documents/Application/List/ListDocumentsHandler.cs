using System.Globalization;
using Ash.Modules.Cases.Contracts;
using Ash.Modules.Documents.Infrastructure;
using Ash.Modules.Identity.Contracts;
using Ash.Modules.Reference.Contracts;
using Ash.SharedKernel.Paging;
using Ash.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Ash.Modules.Documents.Application.List;

/// <summary>Query string of GET /api/cases/{id}/documents: type = document type code, level = n of X/n.</summary>
public sealed record ListDocumentsQuery(string? Type, int? Level, string? After, int? Limit);

public sealed record DocumentTypeName(string Code, string Name);

/// <summary>A row of the dossier timeline. Public ids only; no storage key, no internal ids.</summary>
public sealed record DocumentListItem(
    Guid PublicId,
    string Title,
    DocumentTypeName DocumentType,
    string? OtherTypeText,
    /// <summary>The level it was uploaded to: its case code "X/n".</summary>
    string Level,
    string? UploadedBy,
    DateTime UploadedAt,
    long SizeBytes,
    string ContentType,
    string ScanStatus,
    /// <summary>CLEAN and a type the browser may show (pdf, png, jpg): the preview endpoint accepts it.</summary>
    bool CanPreview);

/// <summary>
/// GET /api/cases/{id}/documents (task 034): the dossier's documents as ONE timeline, newest first — every level, for
/// a user who may view this level (ICaseAccessService.CanViewDossierDocumentsAsync explains why). Keyset paging on
/// (uploaded_at DESC, id DESC) over ix_documents_timeline (dossier_id, deleted_at, uploaded_at); deleted ones never.
/// </summary>
internal sealed class ListDocumentsHandler(DocumentsDbContext db, ICaseActivity cases, IReferenceData reference, IUserDirectory users)
{
    public const int DefaultLimit = 50;
    public const int MaxLimit = 100;

    /// <summary>The types a browser may show inline (with the preview's sandbox CSP).</summary>
    public static readonly IReadOnlySet<string> PreviewContentTypes =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "application/pdf", "image/png", "image/jpeg" };

    public async Task<Result<KeysetPage<DocumentListItem>>> Handle(Guid casePublicId, ListDocumentsQuery query, CancellationToken ct)
    {
        var c = await cases.FindViewableAsync(casePublicId, ct);
        if (c is null)
        {
            return Error.NotFound("case.not_found", "Çështja nuk u gjet.");
        }

        var levels = await cases.ListLevelsAsync(c.DossierId, ct);
        var types = await reference.ListDocumentTypesAsync(ct);
        var errors = new FieldErrors();

        byte? typeId = null;
        if (!string.IsNullOrWhiteSpace(query.Type))
        {
            var type = types.FirstOrDefault(t => string.Equals(t.Code, query.Type.Trim(), StringComparison.OrdinalIgnoreCase));
            if (type is null)
            {
                errors.Add("type", "Lloji i dokumentit nuk ekziston.");
            }
            else
            {
                typeId = type.Id;
            }
        }

        ulong? levelCaseId = null;
        if (query.Level is { } levelSeq)
        {
            var level = levels.FirstOrDefault(l => l.LevelSeq == levelSeq);
            if (level is null)
            {
                errors.Add("level", "Dosja nuk ka këtë shkallë.");
            }
            else
            {
                levelCaseId = (ulong)level.CaseId;
            }
        }

        DateTime? afterAt = null;
        ulong afterId = 0;
        if (query.After is { } cursor)
        {
            if (Cursor.TryDecode(cursor, out var sortKey, out var id) && id > 0
                && long.TryParse(sortKey, NumberStyles.None, CultureInfo.InvariantCulture, out var ticks) && ticks > 0)
            {
                afterAt = new DateTime(ticks, DateTimeKind.Utc);
                afterId = (ulong)id;
            }
            else
            {
                errors.Add("after", "Kursori nuk vlen; filloni nga faqja e parë.");
            }
        }

        if (errors.Any)
        {
            return errors.ToError("Kërkimi nuk është i vlefshëm.");
        }

        var dossierId = (ulong)c.DossierId;
        var documents = db.Documents.AsNoTracking().Where(d => d.DossierId == dossierId && d.DeletedAt == null);
        if (typeId is { } t)
        {
            documents = documents.Where(d => d.DocumentTypeId == t);
        }

        if (levelCaseId is { } levelId)
        {
            documents = documents.Where(d => d.CaseId == levelId);
        }

        if (afterAt is { } at)
        {
            documents = documents.Where(d => d.UploadedAt < at || (d.UploadedAt == at && d.Id < afterId));
        }

        var limit = Math.Clamp(query.Limit ?? DefaultLimit, 1, MaxLimit);
        var rows = await documents.OrderByDescending(d => d.UploadedAt).ThenByDescending(d => d.Id).Take(limit + 1).ToListAsync(ct);
        var hasMore = rows.Count > limit;
        rows = rows.Take(limit).ToList();

        var names = await users.GetNamesAsync(rows.Select(r => (long)r.UploadedBy).Distinct().ToList(), ct);
        var codes = levels.ToDictionary(l => (ulong)l.CaseId, l => l.CaseCode);
        var typeById = types.ToDictionary(x => x.Id);
        var items = rows.Select(r => new DocumentListItem(
                r.PublicId,
                r.Title,
                typeById.TryGetValue(r.DocumentTypeId, out var type) ? new DocumentTypeName(type.Code, type.Name) : new DocumentTypeName(string.Empty, string.Empty),
                r.OtherTypeText,
                r.CaseId is { } caseId && codes.TryGetValue(caseId, out var code) ? code : string.Empty,
                names.GetValueOrDefault(r.UploadedBy),
                DateTime.SpecifyKind(r.UploadedAt, DateTimeKind.Utc),
                (long)r.SizeBytes,
                r.ContentType,
                r.ScanStatus,
                r.ScanStatus == DocumentRow.Clean && PreviewContentTypes.Contains(r.ContentType)))
            .ToList();

        var next = hasMore
            ? Cursor.Encode(rows[^1].UploadedAt.Ticks.ToString(CultureInfo.InvariantCulture), (long)rows[^1].Id)
            : null;
        return new KeysetPage<DocumentListItem>(items, next);
    }
}
