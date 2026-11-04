using Ash.Modules.Documents.Contracts;
using Ash.Modules.Documents.Infrastructure;
using Ash.Modules.Reference.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Ash.Modules.Documents.Application;

/// <summary><see cref="IDocumentService"/>: what other modules may ask about documents.</summary>
internal sealed class DocumentService(DocumentsDbContext db, IReferenceData reference) : IDocumentService
{
    public async Task<DocumentInfo?> GetAsync(Guid publicId, CancellationToken ct = default) =>
        await InfoAsync(await db.Documents.AsNoTracking().SingleOrDefaultAsync(d => d.PublicId == publicId, ct), ct);

    public async Task<DocumentInfo?> GetByIdAsync(long id, CancellationToken ct = default)
    {
        var key = (ulong)id;
        return await InfoAsync(await db.Documents.AsNoTracking().SingleOrDefaultAsync(d => d.Id == key, ct), ct);
    }

    private async Task<DocumentInfo?> InfoAsync(DocumentRow? row, CancellationToken ct)
    {
        if (row is null)
        {
            return null;
        }

        var type = (await reference.ListDocumentTypesAsync(ct)).FirstOrDefault(t => t.Id == row.DocumentTypeId);
        return new DocumentInfo((long)row.Id, row.PublicId, (long)row.DossierId, (long?)row.CaseId, row.Title,
            type?.Code ?? string.Empty, row.ScanStatus, row.DeletedAt is not null);
    }

    public async Task<IReadOnlyDictionary<long, int>> CountsByCaseAsync(IReadOnlyCollection<long> caseIds, CancellationToken ct = default)
    {
        var ids = caseIds.Select(id => (ulong?)id).ToList();
        var counts = await db.Documents.AsNoTracking()
            .Where(d => ids.Contains(d.CaseId) && d.DeletedAt == null)
            .GroupBy(d => d.CaseId)
            .Select(g => new { CaseId = g.Key, Count = g.Count() })
            .ToListAsync(ct);
        return counts.ToDictionary(x => (long)x.CaseId!.Value, x => x.Count);
    }

    public Task<bool> IsCleanAsync(Guid publicId, CancellationToken ct = default) =>
        db.Documents.AsNoTracking().AnyAsync(d => d.PublicId == publicId && d.DeletedAt == null && d.ScanStatus == DocumentRow.Clean, ct);

    // ix_documents_scan (scan_status, uploaded_at)
    public Task<int> CountFailedScansAsync(CancellationToken ct = default) =>
        db.Documents.AsNoTracking().CountAsync(d => d.ScanStatus == DocumentRow.Failed && d.DeletedAt == null, ct);
}
