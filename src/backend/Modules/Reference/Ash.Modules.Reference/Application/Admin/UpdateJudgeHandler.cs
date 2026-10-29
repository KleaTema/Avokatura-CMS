using Ash.Infrastructure.Persistence;
using Ash.Modules.Audit.Contracts;
using Ash.Modules.Reference.Contracts;
using Ash.Modules.Reference.Infrastructure;
using Ash.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Ash.Modules.Reference.Application.Admin;

public sealed class UpdateJudgeHandler(ReferenceDbContext db, IAuditLog audit, IReferenceCache cache, IUnitOfWork unitOfWork)
{
    public async Task<Result<JudgeAdminResponse>> Handle(uint id, UpdateJudgeRequest req, CancellationToken ct)
    {
        var row = await db.Judges.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (row is null)
        {
            return JudgeAdminSupport.NotFound();
        }

        // an unchanged court is accepted even if it was deactivated later, so the judge stays editable
        var courtChanged = req.CourtId != row.CourtId;
        var invalid = JudgeAdminSupport.Validate(req.FirstName, req.LastName, req.ExternalRef)
                      ?? (courtChanged ? await JudgeAdminSupport.ValidateCourt(db, req.CourtId, ct) : null);
        if (invalid is not null)
        {
            return invalid;
        }

        var externalRef = JudgeAdminSupport.CleanExternalRef(req.ExternalRef);
        var duplicate = await JudgeAdminSupport.ValidateExternalRefUnique(db, externalRef, id, ct);
        if (duplicate is not null)
        {
            return duplicate;
        }

        var oldValues = JudgeAdminSupport.Snapshot(row);
        row.FirstName = req.FirstName.Trim();
        row.LastName = req.LastName.Trim();
        row.CourtId = (ushort?)req.CourtId;
        row.ExternalRef = externalRef;
        await db.SaveChangesAsync(ct);

        JudgeAdminSupport.AuditChange(audit, row, "JUDGE_UPDATED", oldValues, JudgeAdminSupport.Snapshot(row));
        unitOfWork.OnCommitted(() => cache.Invalidate());
        return await JudgeAdminSupport.ToResponse(db, row, ct);
    }
}
