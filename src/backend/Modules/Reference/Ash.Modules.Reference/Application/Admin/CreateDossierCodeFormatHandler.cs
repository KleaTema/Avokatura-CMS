using Ash.Infrastructure.Persistence;
using Ash.Modules.Audit.Contracts;
using Ash.Modules.Reference.Contracts;
using Ash.Modules.Reference.Infrastructure;
using Ash.SharedKernel.Results;
using Ash.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;

namespace Ash.Modules.Reference.Application.Admin;

public sealed class CreateDossierCodeFormatHandler(
    ReferenceDbContext db, IAuditLog audit, IReferenceCache cache, IUnitOfWork unitOfWork, IClock clock)
{
    public async Task<Result<DossierCodeFormatAdminResponse>> Handle(CreateDossierCodeFormatRequest req, CancellationToken ct)
    {
        var template = req.Template.Trim();
        var invalid = await DossierCodeFormatAdminSupport.ValidateCreate(db, template, req.RandomDigits, ct);
        if (invalid is not null)
        {
            return invalid;
        }

        if (await db.DossierCodeFormats.AnyAsync(x => x.Template == template, ct))
        {
            return Error.Validation("dossier_code_format.exists", "Ky format kodi ekziston tashmë.");
        }

        var row = new DossierCodeFormatRecord
        {
            Template = template,
            RandomDigits = (byte)req.RandomDigits,
            IsActive = false,
            CreatedAt = clock.UtcNow,
            Note = req.Note?.Trim()
        };

        db.DossierCodeFormats.Add(row);
        await db.SaveChangesAsync(ct);

        DossierCodeFormatAdminSupport.AuditChange(audit, row, "DOSSIER_CODE_FORMAT_CREATED", null,
            DossierCodeFormatAdminSupport.Snapshot(row));
        unitOfWork.OnCommitted(() => cache.Invalidate());
        return DossierCodeFormatAdminSupport.ToResponse(row);
    }
}
