using Ash.Infrastructure.Persistence;
using Ash.Modules.Audit.Contracts;
using Ash.Modules.Reference.Contracts;
using Ash.Modules.Reference.Infrastructure;
using Ash.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Ash.Modules.Reference.Application.Admin;

public sealed class CreateDecisionResultHandler(ReferenceDbContext db, IAuditLog audit, IReferenceCache cache, IUnitOfWork unitOfWork)
{
    public async Task<Result<DecisionResultAdminResponse>> Handle(CreateDecisionResultRequest req, CancellationToken ct)
    {
        var invalid = DecisionResultAdminSupport.ValidateCreate(req.Code, req.Name, req.OutcomeGroup, req.SendsBackForRetrial, req.SortOrder);
        if (invalid is not null)
        {
            return invalid;
        }

        var code = req.Code.Trim();
        var name = req.Name.Trim();

        var sortOrder = DecisionResultAdminSupport.ToDbSortOrder(req.SortOrder);

        if (await db.DecisionResults.AnyAsync(x => x.Code == code, ct))
        {
            return Error.Validation("decision_result.code_exists", "Ky kod ekziston tashmë.");
        }

        var row = new DecisionResultRecord
        {
            Code = code,
            Name = name,
            OutcomeGroup = req.OutcomeGroup.Trim(),
            SendsBackForRetrial = req.SendsBackForRetrial!.Value,
            IsOther = req.IsOther,
            SortOrder = sortOrder,
            IsActive = true
        };

        db.DecisionResults.Add(row);
        await db.SaveChangesAsync(ct);

        DecisionResultAdminSupport.AuditChange(audit, row, "DECISION_RESULT_CREATED", null,
            DecisionResultAdminSupport.Snapshot(row));
        unitOfWork.OnCommitted(() => cache.Invalidate());
        return DecisionResultAdminSupport.ToResponse(row);
    }
}
