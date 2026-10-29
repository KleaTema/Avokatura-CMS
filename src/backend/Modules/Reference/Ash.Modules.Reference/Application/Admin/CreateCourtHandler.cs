using Ash.Infrastructure.Persistence;
using Ash.Modules.Audit.Contracts;
using Ash.Modules.Reference.Contracts;
using Ash.Modules.Reference.Infrastructure;
using Ash.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Ash.Modules.Reference.Application.Admin;

public sealed class CreateCourtHandler(ReferenceDbContext db, IAuditLog audit, IReferenceCache cache, IUnitOfWork unitOfWork)
{
    public async Task<Result<CourtAdminResponse>> Handle(CreateCourtRequest req, CancellationToken ct)
    {
        var invalid = CourtAdminSupport.Validate(req.Name, req.CourtLevel, req.City);
        if (invalid is not null)
        {
            return invalid;
        }

        var name = req.Name.Trim();
        var levelCode = req.CourtLevel.Trim();

        var levelId = await CourtAdminSupport.FindLevelId(db, levelCode, ct);
        if (levelId is null)
        {
            return CourtAdminSupport.UnknownLevel();
        }

        if (await db.Courts.AnyAsync(x => x.Name == name, ct))
        {
            return CourtAdminSupport.NameExists();
        }

        var row = new CourtRecord
        {
            Name = name,
            CourtLevelId = levelId.Value,
            City = CourtAdminSupport.CleanCity(req.City),
            IsForeign = req.IsForeign,
            IsActive = true
        };

        db.Courts.Add(row);
        await db.SaveChangesAsync(ct);

        CourtAdminSupport.AuditChange(audit, row, "COURT_CREATED", null, CourtAdminSupport.Snapshot(row));
        unitOfWork.OnCommitted(() => cache.Invalidate());
        return CourtAdminSupport.ToResponse(row, levelCode);
    }
}
