using Ash.Modules.Audit.Contracts;
using Ash.Modules.Identity.Infrastructure;
using Ash.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Ash.Modules.Identity.Application.Offices;

/// <summary>Name, type and parent are editable; the code is not.</summary>
public sealed class UpdateOfficeHandler(IdentityDbContext db, IAuditLog audit)
{
    public async Task<Result<OfficeAdminResponse>> Handle(Guid publicId, UpdateOfficeRequest req, CancellationToken ct)
    {
        var office = await db.Offices.SingleOrDefaultAsync(o => o.PublicId == publicId, ct);
        if (office is null)
        {
            return OfficeAdminSupport.NotFound();
        }

        var invalid = OfficeAdminSupport.ValidateFields(req.Name, req.OfficeType);
        if (invalid is not null)
        {
            return invalid;
        }

        var parent = await OfficeAdminSupport.ResolveParentAsync(db, req.ParentOfficeId, office.Id, ct);
        if (parent.IsFailure)
        {
            return parent.Error!;
        }

        var oldValues = OfficeAdminSupport.Snapshot(office);
        office.Name = req.Name.Trim();
        office.OfficeType = req.OfficeType.Trim();
        office.ParentOfficeId = parent.Value;
        await db.SaveChangesAsync(ct);

        OfficeAdminSupport.AuditChange(audit, office, "OFFICE_UPDATED", oldValues, OfficeAdminSupport.Snapshot(office));
        return await OfficeAdminSupport.ToResponseAsync(db, office, ct);
    }
}
