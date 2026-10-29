using Ash.Infrastructure.Persistence;
using Ash.Modules.Audit.Contracts;
using Ash.Modules.Reference.Contracts;
using Ash.Modules.Reference.Infrastructure;
using Ash.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Ash.Modules.Reference.Application.Admin;

public sealed class UpdateDocumentTypeHandler(ReferenceDbContext db, IAuditLog audit, IReferenceCache cache, IUnitOfWork unitOfWork)
{
    public async Task<Result<DocumentTypeAdminResponse>> Handle(string code, UpdateDocumentTypeRequest req, CancellationToken ct)
    {
        var row = await db.DocumentTypes.SingleOrDefaultAsync(x => x.Code == code, ct);
        if (row is null)
        {
            return Error.NotFound("document_type.not_found", "Lloji i dokumentit nuk u gjet.");
        }

        var invalid = DocumentTypeAdminSupport.ValidateUpdate(req.Name, req.SortOrder);
        if (invalid is not null)
        {
            return invalid;
        }

        var sortOrder = DocumentTypeAdminSupport.ToDbSortOrder(req.SortOrder);
        var oldValues = DocumentTypeAdminSupport.Snapshot(row);
        row.Name = req.Name.Trim();
        row.SortOrder = sortOrder;
        row.IsOther = req.IsOther;
        await db.SaveChangesAsync(ct);

        DocumentTypeAdminSupport.AuditChange(audit, row, "DOCUMENT_TYPE_UPDATED", oldValues,
            DocumentTypeAdminSupport.Snapshot(row));
        unitOfWork.OnCommitted(() => cache.Invalidate());
        return DocumentTypeAdminSupport.ToResponse(row);
    }
}
