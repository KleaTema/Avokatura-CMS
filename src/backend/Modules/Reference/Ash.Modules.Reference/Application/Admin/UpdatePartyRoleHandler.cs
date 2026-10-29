using Ash.Infrastructure.Persistence;
using Ash.Modules.Audit.Contracts;
using Ash.Modules.Reference.Contracts;
using Ash.Modules.Reference.Infrastructure;
using Ash.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Ash.Modules.Reference.Application.Admin;

public sealed class UpdatePartyRoleHandler(ReferenceDbContext db, IAuditLog audit, IReferenceCache cache, IUnitOfWork unitOfWork)
{
    public async Task<Result<PartyRoleAdminResponse>> Handle(string code, UpdatePartyRoleRequest req, CancellationToken ct)
    {
        var row = await db.PartyRoles.SingleOrDefaultAsync(x => x.Code == code, ct);
        if (row is null)
        {
            return Error.NotFound("party_role.not_found", "Lloji i palës nuk u gjet.");
        }

        var invalid = PartyRoleAdminSupport.ValidateUpdate(req.Name, req.SortOrder);
        if (invalid is not null)
        {
            return invalid;
        }

        var sortOrder = PartyRoleAdminSupport.ToDbSortOrder(req.SortOrder);
        var oldValues = PartyRoleAdminSupport.Snapshot(row);
        row.Name = req.Name.Trim();
        row.SortOrder = sortOrder;
        await db.SaveChangesAsync(ct);

        PartyRoleAdminSupport.AuditChange(audit, row, "PARTY_ROLE_UPDATED", oldValues,
            PartyRoleAdminSupport.Snapshot(row));
        unitOfWork.OnCommitted(() => cache.Invalidate());
        return PartyRoleAdminSupport.ToResponse(row);
    }
}
