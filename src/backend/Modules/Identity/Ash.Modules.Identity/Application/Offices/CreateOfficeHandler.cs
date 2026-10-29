using Ash.Modules.Audit.Contracts;
using Ash.Modules.Identity.Infrastructure;
using Ash.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Ash.Modules.Identity.Application.Offices;

public sealed class CreateOfficeHandler(IdentityDbContext db, IAuditLog audit)
{
    public async Task<Result<OfficeAdminResponse>> Handle(CreateOfficeRequest req, CancellationToken ct)
    {
        var invalid = OfficeAdminSupport.ValidateCode(req.Code) ?? OfficeAdminSupport.ValidateFields(req.Name, req.OfficeType);
        if (invalid is not null)
        {
            return invalid;
        }

        var code = req.Code.Trim();
        if (await db.Offices.AnyAsync(o => o.Code == code, ct))
        {
            return Error.Validation("office.code_exists", "Ekziston tashmë një zyrë me këtë kod.");
        }

        var parent = await OfficeAdminSupport.ResolveParentAsync(db, req.ParentOfficeId, null, ct);
        if (parent.IsFailure)
        {
            return parent.Error!;
        }

        var office = new OfficeRecord
        {
            PublicId = Guid.NewGuid(),
            Code = code,
            Name = req.Name.Trim(),
            OfficeType = req.OfficeType.Trim(),
            ParentOfficeId = parent.Value,
            IsActive = true
        };
        db.Offices.Add(office);
        await db.SaveChangesAsync(ct);

        OfficeAdminSupport.AuditChange(audit, office, "OFFICE_CREATED", null, OfficeAdminSupport.Snapshot(office));
        return await OfficeAdminSupport.ToResponseAsync(db, office, ct);
    }
}
