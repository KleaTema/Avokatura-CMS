using Ash.Infrastructure.Persistence;
using Ash.Modules.Audit.Contracts;
using Ash.Modules.Reference.Contracts;
using Ash.Modules.Reference.Infrastructure;
using Ash.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Ash.Modules.Reference.Application.Admin;

public sealed class CreateSummonsTypeHandler(ReferenceDbContext db, IAuditLog audit, IReferenceCache cache, IUnitOfWork unitOfWork)
{
    public async Task<Result<SummonsTypeAdminResponse>> Handle(CreateSummonsTypeRequest req, CancellationToken ct)
    {
        var invalid = SummonsTypeAdminSupport.ValidateCreate(req.Code, req.Name);
        if (invalid is not null)
        {
            return invalid;
        }

        var code = req.Code.Trim();
        var name = req.Name.Trim();

        if (await db.SummonsTypes.AnyAsync(x => x.Code == code, ct))
        {
            return Error.Validation("summons_type.code_exists", "Ky kod ekziston tashmë.");
        }

        var row = new SummonsTypeRecord
        {
            Code = code,
            Name = name,
            IsActive = true
        };

        db.SummonsTypes.Add(row);
        await db.SaveChangesAsync(ct);

        SummonsTypeAdminSupport.AuditChange(audit, row, "SUMMONS_TYPE_CREATED", null,
            SummonsTypeAdminSupport.Snapshot(row));
        unitOfWork.OnCommitted(() => cache.Invalidate());
        return SummonsTypeAdminSupport.ToResponse(row);
    }
}
