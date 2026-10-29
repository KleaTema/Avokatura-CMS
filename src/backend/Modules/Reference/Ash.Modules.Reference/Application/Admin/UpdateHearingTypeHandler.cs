using Ash.Infrastructure.Persistence;
using Ash.Modules.Audit.Contracts;
using Ash.Modules.Reference.Contracts;
using Ash.Modules.Reference.Infrastructure;
using Ash.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Ash.Modules.Reference.Application.Admin;

public sealed class UpdateHearingTypeHandler(ReferenceDbContext db, IAuditLog audit, IReferenceCache cache, IUnitOfWork unitOfWork)
{
    public async Task<Result<HearingTypeAdminResponse>> Handle(string code, UpdateHearingTypeRequest req, CancellationToken ct)
    {
        var row = await db.HearingTypes.SingleOrDefaultAsync(x => x.Code == code, ct);
        if (row is null)
        {
            return Error.NotFound("hearing_type.not_found", "Lloji i seancës nuk u gjet.");
        }

        var invalid = HearingTypeAdminSupport.ValidateUpdate(req.Name, req.SortOrder);
        if (invalid is not null)
        {
            return invalid;
        }

        var sortOrder = HearingTypeAdminSupport.ToDbSortOrder(req.SortOrder);
        var oldValues = HearingTypeAdminSupport.Snapshot(row);
        row.Name = req.Name.Trim();
        row.SortOrder = sortOrder;
        row.IsOther = req.IsOther;
        await db.SaveChangesAsync(ct);

        HearingTypeAdminSupport.AuditChange(audit, row, "HEARING_TYPE_UPDATED", oldValues,
            HearingTypeAdminSupport.Snapshot(row));
        unitOfWork.OnCommitted(() => cache.Invalidate());
        return HearingTypeAdminSupport.ToResponse(row);
    }
}
