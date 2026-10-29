using Ash.Modules.Audit.Contracts;
using Ash.Modules.Identity.Infrastructure;
using Ash.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Ash.Modules.Identity.Application.Institutions;

public sealed class UpdateInstitutionHandler(IdentityDbContext db, IAuditLog audit)
{
    public async Task<Result<InstitutionAdminResponse>> Handle(Guid publicId, UpdateInstitutionRequest req, CancellationToken ct)
    {
        var institution = await db.Institutions.SingleOrDefaultAsync(i => i.PublicId == publicId, ct);
        if (institution is null)
        {
            return InstitutionAdminSupport.NotFound();
        }

        var invalid = InstitutionAdminSupport.ValidateFields(req.Name, req.ShortName, req.InstitutionType, req.Nipt);
        if (invalid is not null)
        {
            return invalid;
        }

        var type = req.InstitutionType.Trim();
        var nipt = InstitutionAdminSupport.CleanNipt(req.Nipt);
        var duplicate = await InstitutionAdminSupport.ValidateNiptUnique(db, nipt, institution.Id, ct);
        if (duplicate is not null)
        {
            return duplicate;
        }

        var parent = await InstitutionAdminSupport.ResolveParentAsync(db, req.ParentInstitutionId, type, ct);
        if (parent.IsFailure)
        {
            return parent.Error!;
        }

        // institutions under it would end up with a parent that is not a ministry
        if (institution.InstitutionType == InstitutionAdminSupport.Ministry && type != InstitutionAdminSupport.Ministry
            && await db.Institutions.AnyAsync(i => i.ParentInstitutionId == institution.Id, ct))
        {
            return Error.Validation("institution.ministry_has_children",
                "Kjo ministri ka institucione në varësi; lloji i saj nuk mund të ndryshohet.");
        }

        var oldValues = InstitutionAdminSupport.Snapshot(institution);
        institution.Name = req.Name.Trim();
        institution.ShortName = InstitutionAdminSupport.CleanShortName(req.ShortName);
        institution.InstitutionType = type;
        institution.ParentInstitutionId = parent.Value;
        institution.Nipt = nipt;
        await db.SaveChangesAsync(ct);

        InstitutionAdminSupport.AuditChange(audit, institution, "INSTITUTION_UPDATED", oldValues, InstitutionAdminSupport.Snapshot(institution));
        return await InstitutionAdminSupport.ToResponseAsync(db, institution, ct);
    }
}
