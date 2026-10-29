using Ash.Infrastructure.Persistence;
using Ash.Modules.Audit.Contracts;
using Ash.Modules.Reference.Contracts;
using Ash.Modules.Reference.Infrastructure;
using Ash.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Ash.Modules.Reference.Application.Admin;

public sealed class SetDocumentTypeActiveHandler(ReferenceDbContext db, IAuditLog audit, IReferenceCache cache, IUnitOfWork unitOfWork)
{
    public async Task<Result<DocumentTypeAdminResponse>> Handle(string code, bool active, CancellationToken ct)
    {
        var row = await db.DocumentTypes.SingleOrDefaultAsync(x => x.Code == code, ct);
        if (row is null)
        {
            return Error.NotFound("document_type.not_found", "Lloji i dokumentit nuk u gjet.");
        }

        var oldValues = DocumentTypeAdminSupport.Snapshot(row);
        row.IsActive = active;
        await db.SaveChangesAsync(ct);

        DocumentTypeAdminSupport.AuditChange(audit, row, active ? "DOCUMENT_TYPE_ACTIVATED" : "DOCUMENT_TYPE_DEACTIVATED", oldValues,
            DocumentTypeAdminSupport.Snapshot(row));
        unitOfWork.OnCommitted(() => cache.Invalidate());
        return DocumentTypeAdminSupport.ToResponse(row);
    }
}
