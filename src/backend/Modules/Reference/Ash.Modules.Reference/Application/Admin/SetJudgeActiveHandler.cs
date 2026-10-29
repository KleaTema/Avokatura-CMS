using Ash.Infrastructure.Persistence;
using Ash.Modules.Audit.Contracts;
using Ash.Modules.Reference.Contracts;
using Ash.Modules.Reference.Infrastructure;
using Ash.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Ash.Modules.Reference.Application.Admin;

public sealed class SetJudgeActiveHandler(ReferenceDbContext db, IAuditLog audit, IReferenceCache cache, IUnitOfWork unitOfWork)
{
    public async Task<Result<JudgeAdminResponse>> Handle(uint id, bool active, CancellationToken ct)
    {
        var row = await db.Judges.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (row is null)
        {
            return JudgeAdminSupport.NotFound();
        }

        var oldValues = JudgeAdminSupport.Snapshot(row);
        row.IsActive = active;
        await db.SaveChangesAsync(ct);

        JudgeAdminSupport.AuditChange(audit, row, active ? "JUDGE_ACTIVATED" : "JUDGE_DEACTIVATED", oldValues,
            JudgeAdminSupport.Snapshot(row));
        unitOfWork.OnCommitted(() => cache.Invalidate());
        return await JudgeAdminSupport.ToResponse(db, row, ct);
    }
}
