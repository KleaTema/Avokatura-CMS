using Ash.Modules.Documents.Contracts;
using Ash.Modules.Documents.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Ash.Modules.Documents.Application;

public sealed class DocumentCounts(DocumentsDbContext db) : IDocumentCounts
{
    public Task<int> CountForCaseAsync(long caseId, CancellationToken ct = default)
    {
        var id = (ulong)caseId;
        return db.Documents.AsNoTracking().CountAsync(d => d.CaseId == id && d.DeletedAt == null, ct);
    }
}
