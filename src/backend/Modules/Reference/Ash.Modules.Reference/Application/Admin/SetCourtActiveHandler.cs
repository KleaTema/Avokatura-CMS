using Ash.Infrastructure.Persistence;
using Ash.Modules.Audit.Contracts;
using Ash.Modules.Reference.Contracts;
using Ash.Modules.Reference.Infrastructure;
using Ash.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Ash.Modules.Reference.Application.Admin;

/// <summary>Only the court row changes; its judges keep their own is_active.</summary>
public sealed class SetCourtActiveHandler(ReferenceDbContext db, IAuditLog audit, IReferenceCache cache, IUnitOfWork unitOfWork)
{
    public async Task<Result<CourtAdminResponse>> Handle(ushort id, bool active, CancellationToken ct)
    {
        var row = await db.Courts.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (row is null)
        {
            return CourtAdminSupport.NotFound();
        }

        var oldValues = CourtAdminSupport.Snapshot(row);
        row.IsActive = active;
        await db.SaveChangesAsync(ct);

        CourtAdminSupport.AuditChange(audit, row, active ? "COURT_ACTIVATED" : "COURT_DEACTIVATED", oldValues,
            CourtAdminSupport.Snapshot(row));
        unitOfWork.OnCommitted(() => cache.Invalidate());
        return CourtAdminSupport.ToResponse(row, await CourtAdminSupport.LevelCode(db, row.CourtLevelId, ct));
    }
}
