using Ash.Infrastructure.Persistence;
using Ash.Modules.Audit.Contracts;
using Ash.Modules.Reference.Contracts;
using Ash.Modules.Reference.Infrastructure;
using Ash.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Ash.Modules.Reference.Application.Admin;

public sealed class SetDecisionResultActiveHandler(ReferenceDbContext db, IAuditLog audit, IReferenceCache cache, IUnitOfWork unitOfWork)
{
    public async Task<Result<DecisionResultAdminResponse>> Handle(string code, bool active, CancellationToken ct)
    {
        var row = await db.DecisionResults.SingleOrDefaultAsync(x => x.Code == code, ct);
        if (row is null)
        {
            return Error.NotFound("decision_result.not_found", "Rezultati i vendimit nuk u gjet.");
        }

        var oldValues = DecisionResultAdminSupport.Snapshot(row);
        row.IsActive = active;
        await db.SaveChangesAsync(ct);

        DecisionResultAdminSupport.AuditChange(audit, row, active ? "DECISION_RESULT_ACTIVATED" : "DECISION_RESULT_DEACTIVATED", oldValues,
            DecisionResultAdminSupport.Snapshot(row));
        unitOfWork.OnCommitted(() => cache.Invalidate());
        return DecisionResultAdminSupport.ToResponse(row);
    }
}
