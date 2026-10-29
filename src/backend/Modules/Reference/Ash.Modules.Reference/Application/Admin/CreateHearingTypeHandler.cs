using Ash.Infrastructure.Persistence;
using Ash.Modules.Audit.Contracts;
using Ash.Modules.Reference.Contracts;
using Ash.Modules.Reference.Infrastructure;
using Ash.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Ash.Modules.Reference.Application.Admin;

public sealed class CreateHearingTypeHandler(ReferenceDbContext db, IAuditLog audit, IReferenceCache cache, IUnitOfWork unitOfWork)
{
    public async Task<Result<HearingTypeAdminResponse>> Handle(CreateHearingTypeRequest req, CancellationToken ct)
    {
        var invalid = HearingTypeAdminSupport.ValidateCreate(req.Code, req.Name, req.SortOrder);
        if (invalid is not null)
        {
            return invalid;
        }

        var code = req.Code.Trim();
        var name = req.Name.Trim();

        var sortOrder = HearingTypeAdminSupport.ToDbSortOrder(req.SortOrder);

        if (await db.HearingTypes.AnyAsync(x => x.Code == code, ct))
        {
            return Error.Validation("hearing_type.code_exists", "Ky kod ekziston tashmë.");
        }

        var row = new HearingTypeRecord
        {
            Code = code,
            Name = name,
            IsOther = req.IsOther,
            SortOrder = sortOrder,
            IsActive = true
        };

        db.HearingTypes.Add(row);
        await db.SaveChangesAsync(ct);

        HearingTypeAdminSupport.AuditChange(audit, row, "HEARING_TYPE_CREATED", null,
            HearingTypeAdminSupport.Snapshot(row));
        unitOfWork.OnCommitted(() => cache.Invalidate());
        return HearingTypeAdminSupport.ToResponse(row);
    }
}
