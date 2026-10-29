using Ash.Infrastructure.Persistence;
using Ash.Modules.Audit.Contracts;
using Ash.Modules.Reference.Contracts;
using Ash.Modules.Reference.Infrastructure;
using Ash.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Ash.Modules.Reference.Application.Admin;

public sealed class CreateDocumentTypeHandler(ReferenceDbContext db, IAuditLog audit, IReferenceCache cache, IUnitOfWork unitOfWork)
{
    public async Task<Result<DocumentTypeAdminResponse>> Handle(CreateDocumentTypeRequest req, CancellationToken ct)
    {
        var invalid = DocumentTypeAdminSupport.ValidateCreate(req.Code, req.Name, req.SortOrder);
        if (invalid is not null)
        {
            return invalid;
        }

        var code = req.Code.Trim();
        var name = req.Name.Trim();

        var sortOrder = DocumentTypeAdminSupport.ToDbSortOrder(req.SortOrder);

        if (await db.DocumentTypes.AnyAsync(x => x.Code == code, ct))
        {
            return Error.Validation("document_type.code_exists", "Ky kod ekziston tashmë.");
        }

        var row = new DocumentTypeRecord
        {
            Code = code,
            Name = name,
            IsOther = req.IsOther,
            SortOrder = sortOrder,
            IsActive = true
        };

        db.DocumentTypes.Add(row);
        await db.SaveChangesAsync(ct);

        DocumentTypeAdminSupport.AuditChange(audit, row, "DOCUMENT_TYPE_CREATED", null,
            DocumentTypeAdminSupport.Snapshot(row));
        unitOfWork.OnCommitted(() => cache.Invalidate());
        return DocumentTypeAdminSupport.ToResponse(row);
    }
}
