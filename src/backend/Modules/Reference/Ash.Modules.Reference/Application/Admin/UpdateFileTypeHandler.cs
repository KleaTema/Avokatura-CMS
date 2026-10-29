using Ash.Infrastructure.Persistence;
using Ash.Modules.Audit.Contracts;
using Ash.Modules.Reference.Contracts;
using Ash.Modules.Reference.Infrastructure;
using Ash.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Ash.Modules.Reference.Application.Admin;

public sealed class UpdateFileTypeHandler(ReferenceDbContext db, IAuditLog audit, IReferenceCache cache, IUnitOfWork unitOfWork)
{
    public async Task<Result<FileTypeAdminResponse>> Handle(byte id, UpdateFileTypeRequest req, CancellationToken ct)
    {
        var row = await db.AllowedFileTypes.AsTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
        if (row is null)
        {
            return Error.NotFound("file_type.not_found", "Lloji i skedarit nuk u gjet.");
        }

        var invalid = FileTypeAdminSupport.ValidateUpdate(req.Extension, req.ContentType, req.MagicHex, req.MaxSizeBytes, req.AppliesTo);
        if (invalid is not null)
        {
            return invalid;
        }

        if (row.Extension != req.Extension.Trim() || row.AppliesTo != req.AppliesTo.Trim())
        {
            return Error.Validation("file_type.immutable_field", "Shtesa dhe 'applies_to' janë fushë të pandryshueshme.");
        }

        var oldValues = FileTypeAdminSupport.Snapshot(row);
        row.ContentType = req.ContentType.Trim();
        row.MagicHex = req.MagicHex.Trim();
        row.MaxSizeBytes = (ulong)req.MaxSizeBytes;
        row.IsActive = req.IsActive;
        await db.SaveChangesAsync(ct);

        FileTypeAdminSupport.AuditChange(audit, row, "FILE_TYPE_UPDATED", oldValues, FileTypeAdminSupport.Snapshot(row));
        unitOfWork.OnCommitted(() => cache.Invalidate());
        return FileTypeAdminSupport.ToResponse(row);
    }
}
