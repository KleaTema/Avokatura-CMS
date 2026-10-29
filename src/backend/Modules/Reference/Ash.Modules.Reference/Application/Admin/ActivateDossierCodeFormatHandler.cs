using Ash.Infrastructure.Persistence;
using Ash.Modules.Audit.Contracts;
using Ash.Modules.Reference.Contracts;
using Ash.Modules.Reference.Infrastructure;
using Ash.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Ash.Modules.Reference.Application.Admin;

public sealed class ActivateDossierCodeFormatHandler(ReferenceDbContext db, IAuditLog audit, IReferenceCache cache, IUnitOfWork unitOfWork)
{
    public async Task<Result<DossierCodeFormatAdminResponse>> Handle(byte id, CancellationToken ct)
    {
        var row = await db.DossierCodeFormats.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (row is null)
        {
            return Error.NotFound("dossier_code_format.not_found", "Formati i kodit nuk u gjet.");
        }

        var old = DossierCodeFormatAdminSupport.Snapshot(row);
        var activeIds = await db.DossierCodeFormats
            .Where(x => x.IsActive && x.Id != id)
            .Select(x => x.Id)
            .ToListAsync(ct);

        if (activeIds.Count > 0)
        {
            await db.DossierCodeFormats
                .Where(x => activeIds.Contains(x.Id))
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.IsActive, false), ct);
        }

        row.IsActive = true;
        await db.SaveChangesAsync(ct);

        var newValues = DossierCodeFormatAdminSupport.Snapshot(row);
        DossierCodeFormatAdminSupport.AuditChange(audit, row, "DOSSIER_CODE_FORMAT_ACTIVATED", old, newValues);
        unitOfWork.OnCommitted(() => cache.Invalidate());
        return DossierCodeFormatAdminSupport.ToResponse(row);
    }
}
