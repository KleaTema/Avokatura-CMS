using Ash.Infrastructure.Persistence;
using Ash.Modules.Audit.Contracts;
using Ash.Modules.Reference.Contracts;
using Ash.Modules.Reference.Infrastructure;
using Ash.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Ash.Modules.Reference.Application.Admin;

public sealed class UpdateCourtHandler(ReferenceDbContext db, IAuditLog audit, IReferenceCache cache, IUnitOfWork unitOfWork)
{
    public async Task<Result<CourtAdminResponse>> Handle(ushort id, UpdateCourtRequest req, CancellationToken ct)
    {
        var row = await db.Courts.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (row is null)
        {
            return CourtAdminSupport.NotFound();
        }

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

        if (await db.Courts.AnyAsync(x => x.Name == name && x.Id != id, ct))
        {
            return CourtAdminSupport.NameExists();
        }

        var oldValues = CourtAdminSupport.Snapshot(row);
        row.Name = name;
        row.CourtLevelId = levelId.Value;
        row.City = CourtAdminSupport.CleanCity(req.City);
        row.IsForeign = req.IsForeign;
        await db.SaveChangesAsync(ct);

        CourtAdminSupport.AuditChange(audit, row, "COURT_UPDATED", oldValues, CourtAdminSupport.Snapshot(row));
        unitOfWork.OnCommitted(() => cache.Invalidate());
        return CourtAdminSupport.ToResponse(row, levelCode);
    }
}
