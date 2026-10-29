using Ash.Infrastructure.Persistence;
using Ash.Modules.Audit.Contracts;
using Ash.Modules.Reference.Contracts;
using Ash.Modules.Reference.Infrastructure;
using Ash.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Ash.Modules.Reference.Application.Admin;

public sealed class SetHearingTypeActiveHandler(ReferenceDbContext db, IAuditLog audit, IReferenceCache cache, IUnitOfWork unitOfWork)
{
    public async Task<Result<HearingTypeAdminResponse>> Handle(string code, bool active, CancellationToken ct)
    {
        var row = await db.HearingTypes.SingleOrDefaultAsync(x => x.Code == code, ct);
        if (row is null)
        {
            return Error.NotFound("hearing_type.not_found", "Lloji i seancës nuk u gjet.");
        }

        var oldValues = HearingTypeAdminSupport.Snapshot(row);
        row.IsActive = active;
        await db.SaveChangesAsync(ct);

        HearingTypeAdminSupport.AuditChange(audit, row, active ? "HEARING_TYPE_ACTIVATED" : "HEARING_TYPE_DEACTIVATED", oldValues,
            HearingTypeAdminSupport.Snapshot(row));
        unitOfWork.OnCommitted(() => cache.Invalidate());
        return HearingTypeAdminSupport.ToResponse(row);
    }
}
