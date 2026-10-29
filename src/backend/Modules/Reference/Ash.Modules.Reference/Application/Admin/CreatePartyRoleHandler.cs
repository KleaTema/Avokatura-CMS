using Ash.Infrastructure.Persistence;
using Ash.Modules.Audit.Contracts;
using Ash.Modules.Reference.Contracts;
using Ash.Modules.Reference.Infrastructure;
using Ash.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Ash.Modules.Reference.Application.Admin;

public sealed class CreatePartyRoleHandler(ReferenceDbContext db, IAuditLog audit, IReferenceCache cache, IUnitOfWork unitOfWork)
{
    public async Task<Result<PartyRoleAdminResponse>> Handle(CreatePartyRoleRequest req, CancellationToken ct)
    {
        var invalid = PartyRoleAdminSupport.ValidateCreate(req.Code, req.Name, req.ReportGroup, req.SortOrder);
        if (invalid is not null)
        {
            return invalid;
        }

        var code = req.Code.Trim();
        var name = req.Name.Trim();

        var sortOrder = PartyRoleAdminSupport.ToDbSortOrder(req.SortOrder);

        if (await db.PartyRoles.AnyAsync(x => x.Code == code, ct))
        {
            return Error.Validation("party_role.code_exists", "Ky kod ekziston tashmë.");
        }

        var row = new PartyRoleRecord
        {
            Code = code,
            Name = name,
            ReportGroup = req.ReportGroup.Trim(),
            SortOrder = sortOrder,
            IsActive = true
        };

        db.PartyRoles.Add(row);
        await db.SaveChangesAsync(ct);

        PartyRoleAdminSupport.AuditChange(audit, row, "PARTY_ROLE_CREATED", null,
            PartyRoleAdminSupport.Snapshot(row));
        unitOfWork.OnCommitted(() => cache.Invalidate());
        return PartyRoleAdminSupport.ToResponse(row);
    }
}
