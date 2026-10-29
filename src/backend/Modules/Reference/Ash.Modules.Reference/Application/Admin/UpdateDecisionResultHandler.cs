using Ash.Infrastructure.Persistence;
using Ash.Modules.Audit.Contracts;
using Ash.Modules.Reference.Contracts;
using Ash.Modules.Reference.Infrastructure;
using Ash.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Ash.Modules.Reference.Application.Admin;

public sealed class UpdateDecisionResultHandler(ReferenceDbContext db, IAuditLog audit, IReferenceCache cache, IUnitOfWork unitOfWork)
{
    public async Task<Result<DecisionResultAdminResponse>> Handle(string code, UpdateDecisionResultRequest req, CancellationToken ct)
    {
        var row = await db.DecisionResults.SingleOrDefaultAsync(x => x.Code == code, ct);
        if (row is null)
        {
            return Error.NotFound("decision_result.not_found", "Rezultati i vendimit nuk u gjet.");
        }

        var invalid = DecisionResultAdminSupport.ValidateUpdate(req.Name, req.SortOrder);
        if (invalid is not null)
        {
            return invalid;
        }

        var sortOrder = DecisionResultAdminSupport.ToDbSortOrder(req.SortOrder);
        var oldValues = DecisionResultAdminSupport.Snapshot(row);
        row.Name = req.Name.Trim();
        row.SortOrder = sortOrder;
        row.IsOther = req.IsOther;
        await db.SaveChangesAsync(ct);

        DecisionResultAdminSupport.AuditChange(audit, row, "DECISION_RESULT_UPDATED", oldValues,
            DecisionResultAdminSupport.Snapshot(row));
        unitOfWork.OnCommitted(() => cache.Invalidate());
        return DecisionResultAdminSupport.ToResponse(row);
    }
}
