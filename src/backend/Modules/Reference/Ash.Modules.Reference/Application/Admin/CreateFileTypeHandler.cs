using Ash.Infrastructure.Persistence;
using Ash.Modules.Audit.Contracts;
using Ash.Modules.Reference.Contracts;
using Ash.Modules.Reference.Infrastructure;
using Ash.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Ash.Modules.Reference.Application.Admin;

public sealed class CreateFileTypeHandler(ReferenceDbContext db, IAuditLog audit, IReferenceCache cache, IUnitOfWork unitOfWork)
{
    public async Task<Result<FileTypeAdminResponse>> Handle(CreateFileTypeRequest req, CancellationToken ct)
    {
        var extension = req.Extension.Trim();
        var contentType = req.ContentType.Trim();
        var magicHex = req.MagicHex.Trim();
        var appliesTo = req.AppliesTo.Trim();

        var invalid = FileTypeAdminSupport.ValidateCreate(extension, contentType, magicHex, req.MaxSizeBytes, appliesTo);
        if (invalid is not null)
        {
            return invalid;
        }

        if (await db.AllowedFileTypes.AnyAsync(x => x.Extension == extension && x.AppliesTo == appliesTo, ct))
        {
            return Error.Validation("file_type.exists", "Ky lloj skedari ekziston tashmë për këtë aplikim.");
        }

        var row = new AllowedFileTypeRecord
        {
            Extension = extension,
            ContentType = contentType,
            MagicHex = magicHex,
            MaxSizeBytes = (ulong)req.MaxSizeBytes,
            AppliesTo = appliesTo,
            IsActive = req.IsActive
        };

        db.AllowedFileTypes.Add(row);
        await db.SaveChangesAsync(ct);

        FileTypeAdminSupport.AuditChange(audit, row, "FILE_TYPE_CREATED", null, FileTypeAdminSupport.Snapshot(row));
        unitOfWork.OnCommitted(() => cache.Invalidate());
        return FileTypeAdminSupport.ToResponse(row);
    }
}
