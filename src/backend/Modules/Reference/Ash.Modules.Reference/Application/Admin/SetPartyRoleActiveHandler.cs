using Ash.Infrastructure.Persistence;
using Ash.Modules.Audit.Contracts;
using Ash.Modules.Reference.Contracts;
using Ash.Modules.Reference.Infrastructure;
using Ash.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Ash.Modules.Reference.Application.Admin;

public sealed class SetPartyRoleActiveHandler(ReferenceDbContext db, IAuditLog audit, IReferenceCache cache, IUnitOfWork unitOfWork)
{
    public async Task<Result<PartyRoleAdminResponse>> Handle(string code, bool active, CancellationToken ct)
    {
        var row = await db.PartyRoles.SingleOrDefaultAsync(x => x.Code == code, ct);
        if (row is null)
        {
            return Error.NotFound("party_role.not_found", "Lloji i palës nuk u gjet.");
        }

        var oldValues = PartyRoleAdminSupport.Snapshot(row);
        row.IsActive = active;
        await db.SaveChangesAsync(ct);

        PartyRoleAdminSupport.AuditChange(audit, row, active ? "PARTY_ROLE_ACTIVATED" : "PARTY_ROLE_DEACTIVATED", oldValues,
            PartyRoleAdminSupport.Snapshot(row));
        unitOfWork.OnCommitted(() => cache.Invalidate());
        return PartyRoleAdminSupport.ToResponse(row);
    }
}
