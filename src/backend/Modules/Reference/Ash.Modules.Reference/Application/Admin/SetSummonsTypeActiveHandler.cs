using Ash.Infrastructure.Persistence;
using Ash.Modules.Audit.Contracts;
using Ash.Modules.Reference.Contracts;
using Ash.Modules.Reference.Infrastructure;
using Ash.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Ash.Modules.Reference.Application.Admin;

public sealed class SetSummonsTypeActiveHandler(ReferenceDbContext db, IAuditLog audit, IReferenceCache cache, IUnitOfWork unitOfWork)
{
    public async Task<Result<SummonsTypeAdminResponse>> Handle(string code, bool active, CancellationToken ct)
    {
        var row = await db.SummonsTypes.SingleOrDefaultAsync(x => x.Code == code, ct);
        if (row is null)
        {
            return Error.NotFound("summons_type.not_found", "Lloji i fletëthirrjes nuk u gjet.");
        }

        var oldValues = SummonsTypeAdminSupport.Snapshot(row);
        row.IsActive = active;
        await db.SaveChangesAsync(ct);

        SummonsTypeAdminSupport.AuditChange(audit, row, active ? "SUMMONS_TYPE_ACTIVATED" : "SUMMONS_TYPE_DEACTIVATED", oldValues,
            SummonsTypeAdminSupport.Snapshot(row));
        unitOfWork.OnCommitted(() => cache.Invalidate());
        return SummonsTypeAdminSupport.ToResponse(row);
    }
}
