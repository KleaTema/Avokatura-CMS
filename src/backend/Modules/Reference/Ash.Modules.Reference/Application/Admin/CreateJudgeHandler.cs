using Ash.Infrastructure.Persistence;
using Ash.Modules.Audit.Contracts;
using Ash.Modules.Reference.Contracts;
using Ash.Modules.Reference.Infrastructure;
using Ash.SharedKernel.Results;

namespace Ash.Modules.Reference.Application.Admin;

public sealed class CreateJudgeHandler(ReferenceDbContext db, IAuditLog audit, IReferenceCache cache, IUnitOfWork unitOfWork)
{
    public async Task<Result<JudgeAdminResponse>> Handle(CreateJudgeRequest req, CancellationToken ct)
    {
        var invalid = JudgeAdminSupport.Validate(req.FirstName, req.LastName, req.ExternalRef)
                      ?? await JudgeAdminSupport.ValidateCourt(db, req.CourtId, ct);
        if (invalid is not null)
        {
            return invalid;
        }

        var externalRef = JudgeAdminSupport.CleanExternalRef(req.ExternalRef);
        var duplicate = await JudgeAdminSupport.ValidateExternalRefUnique(db, externalRef, null, ct);
        if (duplicate is not null)
        {
            return duplicate;
        }

        var row = new JudgeRecord
        {
            FirstName = req.FirstName.Trim(),
            LastName = req.LastName.Trim(),
            CourtId = (ushort?)req.CourtId,
            ExternalRef = externalRef,
            IsActive = true
        };

        db.Judges.Add(row);
        await db.SaveChangesAsync(ct);

        JudgeAdminSupport.AuditChange(audit, row, "JUDGE_CREATED", null, JudgeAdminSupport.Snapshot(row));
        unitOfWork.OnCommitted(() => cache.Invalidate());
        return await JudgeAdminSupport.ToResponse(db, row, ct);
    }
}
